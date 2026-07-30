import Foundation
import Observation

// MARK: - WS 事件

struct WSEvent: Decodable {
    let type: String
    let dataRaw: Any?
    let ts: Int64?

    enum CodingKeys: String, CodingKey {
        case type
        case dataRaw = "data"
        case ts
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        type = (try? c.decode(String.self, forKey: .type)) ?? ""
        let raw = try? c.decodeIfPresent(AnyDecodable.self, forKey: .dataRaw)
        dataRaw = raw?.value
        ts = c.decodeFlexibleInt64(forKey: .ts)
    }

    /// 将 data 转换为指定类型
    func decodeData<T: Decodable>(_ type: T.Type) -> T? {
        guard let raw = dataRaw,
              JSONSerialization.isValidJSONObject(raw),
              let data = try? JSONSerialization.data(withJSONObject: raw, options: []) else { return nil }
        return try? JSONDecoder().decode(T.self, from: data)
    }
}

// MARK: - WS 消息增量

/// 服务端经 /ws/app 推送的 AI 回复增量（SSE 经 server 中转）
struct WSStreamChunk: Equatable {
    let conversationId: String
    let delta: String
    let reasoningDelta: String?
    let finished: Bool
}

// MARK: - WSClient

/// 连服务端 /ws/app?token=<token>
/// 代次（generation）防 stale 回调；20s ping；指数退避重连（最大 60s）
@Observable
final class WSClient {
    static let shared = WSClient()

    // MARK: 对外状态

    var isConnected: Bool = false
    var lastError: String? = nil
    var connectCount: Int = 0
    var lastEventReceivedAt: String = ""

    // MARK: 事件回调

    /// AI 回复流式增量（核心：iOS 实时接收 Windows 端经 server 中转的 SSE）
    var onStreamChunk: ((WSStreamChunk) -> Void)?
    /// 新消息完成（Windows 端回推完整 assistant 消息）
    var onNewMessage: ((String, String, String) -> Void)?  // conversationId, role, content
    /// agent 在线状态
    var onAgentOnlineChanged: ((Bool) -> Void)?
    /// 会话更新（标题/最后消息时间）
    var onConversationUpdated: ((String) -> Void)?

    // MARK: 私有

    private var task: URLSessionWebSocketTask?
    private var session: URLSession?
    private var pingTimer: Timer?
    private var reconnectAttempts: Int = 0
    private var shouldRun: Bool = false
    private let store: SettingsStore

    /// 连接代次，识别过期回调
    private var generation: Int = 0

    init(store: SettingsStore = .shared) {
        self.store = store
    }

    // MARK: 连接管理

    func start() {
        shouldRun = true
        guard !isConnected else { return }
        if Thread.isMainThread {
            connect()
        } else {
            DispatchQueue.main.async { [weak self] in self?.connect() }
        }
    }

    func stop() {
        shouldRun = false
        if Thread.isMainThread {
            teardown()
        } else {
            DispatchQueue.main.async { [weak self] in self?.teardown() }
        }
    }

    private func teardown() {
        pingTimer?.invalidate()
        pingTimer = nil
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        session?.invalidateAndCancel()
        session = nil
        isConnected = false
    }

    /// 必须主线程调用
    private func connect() {
        assert(Thread.isMainThread, "connect() must be on main thread")
        guard shouldRun else { return }
        let cfg = store.config
        guard cfg.isLoggedIn, let token = cfg.token, !token.isEmpty else { return }
        guard let url = buildWSURL(serverURL: cfg.serverURL, token: token) else { return }

        teardown()
        generation += 1
        let gen = generation

        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 30
        config.waitsForConnectivity = true
        let s = URLSession(configuration: config)
        self.session = s
        let t = s.webSocketTask(with: url)
        self.task = t
        t.resume()

        isConnected = true
        reconnectAttempts = 0
        connectCount += 1
        lastError = nil

        receiveLoop(generation: gen)
        startPing()
    }

    private func reconnect() {
        guard shouldRun else { return }
        reconnectAttempts += 1
        let delay = min(pow(2.0, Double(reconnectAttempts)), 60.0)
        let attempts = reconnectAttempts
        let gen = generation
        print("[WSClient] reconnect in \(delay)s (attempt \(attempts))")
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self = self else { return }
            guard self.shouldRun else { return }
            guard self.generation == gen else {
                print("[WSClient] stale reconnect (gen \(gen), current \(self.generation)), skipping")
                return
            }
            self.connect()
        }
    }

    // MARK: URL 构建

    /// http(s)://host:port → ws(s)://host:port/ws/app?token=xxx
    private func buildWSURL(serverURL: String, token: String) -> URL? {
        var s = serverURL.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.hasPrefix("http://") { s = "ws://" + s.dropFirst(7) }
        else if s.hasPrefix("https://") { s = "wss://" + s.dropFirst(8) }
        else if !s.hasPrefix("ws://") && !s.hasPrefix("wss://") { s = "ws://" + s }
        if s.hasSuffix("/") { s.removeLast() }
        let encoded = token.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? token
        s += "/ws/app?token=\(encoded)"
        return URL(string: s)
    }

    // MARK: 接收循环

    private func receiveLoop(generation gen: Int) {
        task?.receive { [weak self] result in
            guard let self = self else { return }
            DispatchQueue.main.async {
                guard self.generation == gen else {
                    print("[WSClient] stale receive callback (gen \(gen), current \(self.generation)), ignoring")
                    return
                }
                switch result {
                case .failure(let error):
                    self.isConnected = false
                    if self.shouldRun {
                        self.lastError = error.localizedDescription
                        print("[WSClient] receive error: \(error.localizedDescription)")
                        self.reconnect()
                    }
                case .success(let msg):
                    switch msg {
                    case .data(let data):
                        self.handleData(data)
                    case .string(let str):
                        if let data = str.data(using: .utf8) {
                            self.handleData(data)
                        }
                    @unknown default:
                        break
                    }
                    self.receiveLoop(generation: gen)
                }
            }
        }
    }

    private func handleData(_ data: Data) {
        guard let event = try? JSONDecoder().decode(WSEvent.self, from: data) else {
            print("[WSClient] failed to decode event: \(String(data: data, encoding: .utf8) ?? "")")
            return
        }

        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        lastEventReceivedAt = formatter.string(from: Date())

        switch event.type {
        case "stream_chunk":
            // data: {conversation_id, delta, reasoning_delta?, finished}
            if let dict = event.dataRaw as? [String: Any] {
                // conversation_id flexible: 兼容 Int/String
                let convId: String
                if let s = dict["conversation_id"] as? String { convId = s }
                else { convId = String((dict["conversation_id"] as? Int) ?? 0) }
                let delta = dict["delta"] as? String ?? ""
                let reasoning = dict["reasoning_delta"] as? String
                let finished = (dict["finished"] as? Bool) ?? false
                onStreamChunk?(WSStreamChunk(
                    conversationId: convId,
                    delta: delta,
                    reasoningDelta: reasoning,
                    finished: finished
                ))
            }
        case "new_message":
            // data: {conversation_id, role, content}
            if let dict = event.dataRaw as? [String: Any] {
                let convId: String
                if let s = dict["conversation_id"] as? String { convId = s }
                else { convId = String((dict["conversation_id"] as? Int) ?? 0) }
                let role = dict["role"] as? String ?? ""
                let content = dict["content"] as? String ?? ""
                onNewMessage?(convId, role, content)
            }
        case "agent_online":
            onAgentOnlineChanged?(true)
        case "agent_offline":
            onAgentOnlineChanged?(false)
        case "conversation_updated":
            if let dict = event.dataRaw as? [String: Any] {
                let convId: String
                if let s = dict["conversation_id"] as? String { convId = s }
                else { convId = String((dict["conversation_id"] as? Int) ?? 0) }
                if !convId.isEmpty { onConversationUpdated?(convId) }
            }
        case "pong":
            break
        default:
            print("[WSClient] unknown event type: \(event.type)")
        }
    }

    // MARK: 发送

    private func send(_ dict: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: dict),
              let str = String(data: data, encoding: .utf8) else { return }
        task?.send(.string(str)) { error in
            if let e = error { print("[WSClient] send error: \(e.localizedDescription)") }
        }
    }

    private func startPing() {
        pingTimer?.invalidate()
        pingTimer = Timer.scheduledTimer(withTimeInterval: 20, repeats: true) { [weak self] _ in
            self?.send(["type": "ping"])
        }
    }
}
