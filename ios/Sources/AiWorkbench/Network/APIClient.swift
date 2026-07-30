import Foundation

// MARK: - API 错误

enum APIError: LocalizedError {
    case invalidURL
    case invalidResponse
    case networkError(String)
    case httpError(Int)
    case decodeError(String)
    case businessError(code: Int, message: String?)
    case emptyData
    case authRequired
    case agentOffline

    var errorDescription: String? {
        switch self {
        case .invalidURL:
            return "服务器地址无效，请在设置中检查服务器地址。"
        case .invalidResponse:
            return "服务器响应格式异常。"
        case .networkError(let msg):
            return msg
        case .httpError(let code):
            return "网络请求失败（HTTP \(code)）。"
        case .decodeError(let msg):
            return "数据解析失败：\(msg)"
        case .businessError(let code, let message):
            if code == 503 { return message ?? "Windows Agent 离线，请检查书房电脑。" }
            return message ?? "业务错误（code=\(code)）"
        case .emptyData:
            return "服务器未返回数据。"
        case .authRequired:
            return "登录已过期，请重新登录。"
        case .agentOffline:
            return "Windows Agent 离线。"
        }
    }
}

// MARK: - APIClient

/// REST 客户端单例。所有 /api/* 走此。
/// 自动附加 Authorization: Bearer <token>
final class APIClient {
    static let shared = APIClient()

    private let session: URLSession
    private let store: SettingsStore

    init(session: URLSession = .shared, store: SettingsStore = .shared) {
        self.session = session
        self.store = store
    }

    // MARK: - URL 构建

    /// 构建 REST URL: {serverURL}/api/{path}
    private func buildURL(path: String, queryItems: [URLQueryItem] = []) throws -> URL {
        let cfg = store.config
        let base = cfg.serverURL.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !base.isEmpty else { throw APIError.invalidURL }
        guard var components = URLComponents(string: base) else { throw APIError.invalidURL }

        var basePath = components.path ?? ""
        if basePath.hasSuffix("/") { basePath.removeLast() }
        let cleanPath = path.hasPrefix("/") ? String(path.dropFirst()) : path
        components.path = basePath + "/api/" + cleanPath
        if !queryItems.isEmpty {
            components.queryItems = queryItems
        }
        guard let url = components.url else { throw APIError.invalidURL }
        return url
    }

    // MARK: - 统一请求

    func get<T: Decodable>(path: String, queryItems: [URLQueryItem] = [], timeout: TimeInterval = 15) async throws -> APIResponse<T> {
        let url = try buildURL(path: path, queryItems: queryItems)
        var req = URLRequest(url: url)
        req.httpMethod = "GET"
        req.timeoutInterval = timeout
        return try await perform(req)
    }

    func post<T: Decodable>(path: String, body: [String: Any] = [:], timeout: TimeInterval = 15) async throws -> APIResponse<T> {
        let url = try buildURL(path: path)
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        req.timeoutInterval = timeout
        if !body.isEmpty {
            req.httpBody = try JSONSerialization.data(withJSONObject: body, options: [])
        }
        return try await perform(req)
    }

    func put<T: Decodable>(path: String, body: [String: Any] = [:], timeout: TimeInterval = 15) async throws -> APIResponse<T> {
        let url = try buildURL(path: path)
        var req = URLRequest(url: url)
        req.httpMethod = "PUT"
        req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        req.timeoutInterval = timeout
        if !body.isEmpty {
            req.httpBody = try JSONSerialization.data(withJSONObject: body, options: [])
        }
        return try await perform(req)
    }

    private func perform<T: Decodable>(_ req: URLRequest) async throws -> APIResponse<T> {
        var req = req
        if let token = store.config.token, !token.isEmpty {
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        let (rawData, response): (Data, URLResponse)
        do {
            (rawData, response) = try await session.data(for: req)
        } catch {
            throw Self.mapNetworkError(error)
        }

        if let http = response as? HTTPURLResponse {
            if http.statusCode == 401 { throw APIError.authRequired }
            if !(200..<300).contains(http.statusCode) { throw APIError.httpError(http.statusCode) }
        }

        do {
            let decoded = try JSONDecoder().decode(APIResponse<T>.self, from: rawData)
            if decoded.code == 401 { throw APIError.authRequired }
            return decoded
        } catch let err as APIError {
            throw err
        } catch {
            let preview = String(data: rawData, encoding: .utf8) ?? "<non-utf8 \(rawData.count) bytes>"
            throw APIError.decodeError("\(error.localizedDescription) | body=\(preview.prefix(200))")
        }
    }

    private static func mapNetworkError(_ error: Error) -> APIError {
        let ns = error as NSError
        if ns.domain == NSURLErrorDomain {
            switch ns.code {
            case NSURLErrorTimedOut:
                return .networkError("连接超时，请检查服务器地址。")
            case NSURLErrorCannotConnectToHost, NSURLErrorNetworkConnectionLost:
                return .networkError("无法连接服务器，请确认服务端运行中且地址正确。")
            case NSURLErrorNotConnectedToInternet:
                return .networkError("当前网络不可用，请检查手机 Wi-Fi。")
            case NSURLErrorCannotFindHost:
                return .networkError("找不到服务器主机，请检查地址拼写。")
            default:
                break
            }
        }
        return .networkError("网络请求失败：\(error.localizedDescription)")
    }

    // MARK: - 认证

    /// POST /api/auth/login {username, password} → token
    func login(username: String, password: String, serverURL: String) async throws -> String {
        let trimmed = serverURL.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw APIError.invalidURL }
        var components: URLComponents
        if let c = URLComponents(string: trimmed) {
            if c.scheme == nil {
                guard let fixed = URLComponents(string: "http://\(trimmed)") else { throw APIError.invalidURL }
                components = fixed
            } else {
                components = c
            }
        } else {
            throw APIError.invalidURL
        }
        var basePath = components.path
        if basePath.hasSuffix("/") { basePath.removeLast() }
        components.path = basePath + "/api/auth/login"
        guard let url = components.url else { throw APIError.invalidURL }

        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        req.timeoutInterval = 15
        let body: [String: Any] = ["username": username, "password": password]
        req.httpBody = try JSONSerialization.data(withJSONObject: body, options: [])

        let (rawData, response): (Data, URLResponse)
        do {
            (rawData, response) = try await session.data(for: req)
        } catch {
            throw Self.mapNetworkError(error)
        }

        if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
            if let errResp = try? JSONDecoder().decode(APIResponse<EmptyData>.self, from: rawData) {
                throw APIError.businessError(code: errResp.code, message: errResp.msg)
            }
            throw APIError.httpError(http.statusCode)
        }

        do {
            let decoded = try JSONDecoder().decode(APIResponse<LoginData>.self, from: rawData)
            if !decoded.isSuccess {
                throw APIError.businessError(code: decoded.code, message: decoded.msg)
            }
            guard let token = decoded.data?.token, !token.isEmpty else {
                throw APIError.decodeError("登录响应中缺少 token")
            }
            return token
        } catch let err as APIError {
            throw err
        } catch {
            let preview = String(data: rawData, encoding: .utf8) ?? "<non-utf8>"
            throw APIError.decodeError("\(error.localizedDescription) | body=\(preview.prefix(200))")
        }
    }

    // MARK: - 会话

    func fetchConversations(limit: Int = 50, offset: Int = 0) async throws -> [Conversation] {
        let resp: APIResponse<[Conversation]> = try await get(
            path: "conversations",
            queryItems: [
                URLQueryItem(name: "limit", value: String(limit)),
                URLQueryItem(name: "offset", value: String(offset))
            ]
        )
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        return resp.data ?? []
    }

    func createConversation(title: String? = nil, providerId: String? = nil, modelId: String? = nil) async throws -> Conversation {
        var body: [String: Any] = [:]
        if let t = title { body["title"] = t }
        if let p = providerId { body["provider_id"] = p }
        if let m = modelId { body["model_id"] = m }
        let resp: APIResponse<Conversation> = try await post(path: "conversations", body: body)
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        guard let conv = resp.data else { throw APIError.emptyData }
        return conv
    }

    // MARK: - 消息

    func fetchMessages(conversationId: String, limit: Int = 50, before: Int64? = nil) async throws -> [Message] {
        var items = [URLQueryItem(name: "limit", value: String(limit))]
        if let b = before, b > 0 {
            items.append(URLQueryItem(name: "before", value: String(b)))
        }
        let resp: APIResponse<[Message]> = try await get(
            path: "conversations/\(conversationId)/messages",
            queryItems: items
        )
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        return resp.data ?? []
    }

    func sendMessage(conversationId: String, content: String, images: [String]? = nil) async throws -> SendMessageResult {
        var body: [String: Any] = ["content": content]
        if let imgs = images, !imgs.isEmpty { body["images"] = imgs }
        let resp: APIResponse<SendMessageResult> = try await post(
            path: "conversations/\(conversationId)/messages",
            body: body
        )
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        return resp.data ?? SendMessageResult(ok: false, queued: false, messageId: nil)
    }

    // MARK: - 文件

    func listFiles(path: String = "") async throws -> [FileNode] {
        var body: [String: Any] = [:]
        if !path.isEmpty { body["path"] = path }
        let resp: APIResponse<[FileNode]> = try await post(path: "files", body: body)
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        return resp.data ?? []
    }

    func readFile(path: String) async throws -> FileContent {
        let resp: APIResponse<FileContent> = try await post(path: "files/read", body: ["path": path])
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        guard let f = resp.data else { throw APIError.emptyData }
        return f
    }

    // MARK: - 健康检查

    /// 只判 HTTP 200，不 decode body（/api/status 返非空对象，EmptyData 仅匹配 {} 会失败）
    func checkHealth() async -> Bool {
        do {
            let url = try buildURL(path: "status")
            var req = URLRequest(url: url)
            req.httpMethod = "GET"
            req.timeoutInterval = 10
            if let token = store.config.token, !token.isEmpty {
                req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            }
            let (_, response) = try await session.data(for: req)
            if let http = response as? HTTPURLResponse {
                return http.statusCode == 200
            }
            return false
        } catch {
            return false
        }
    }
}

// MARK: - SendMessageResult 默认值

extension SendMessageResult {
    init(ok: Bool?, queued: Bool?, messageId: Int?) {
        self.ok = ok
        self.queued = queued
        self.messageId = messageId
    }
}

// MARK: - Provider

/// GET/PUT /api/providers
struct Provider: Codable, Identifiable, Equatable {
    var id: String
    var name: String?
    var isEnabled: Bool?
    var isAuxiliary: Bool?
    var auxiliaryFor: String?
    var reasoningEffort: String?

    enum CodingKeys: String, CodingKey {
        case id, name
        case isEnabled = "is_enabled"
        case isAuxiliary = "is_auxiliary"
        case auxiliaryFor = "auxiliary_for"
        case reasoningEffort = "reasoning_effort"
    }

    init(id: String, name: String? = nil, isEnabled: Bool? = nil, isAuxiliary: Bool? = nil,
         auxiliaryFor: String? = nil, reasoningEffort: String? = nil) {
        self.id = id
        self.name = name
        self.isEnabled = isEnabled
        self.isAuxiliary = isAuxiliary
        self.auxiliaryFor = auxiliaryFor
        self.reasoningEffort = reasoningEffort
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        // id flexible: 兼容 Int/String
        if let s = try? c.decode(String.self, forKey: .id), !s.isEmpty {
            id = s
        } else {
            id = String((c.decodeFlexibleInt(forKey: .id) ?? 0))
        }
        name = try? c.decodeIfPresent(String.self, forKey: .name)
        isEnabled = c.decodeFlexibleBool(forKey: .isEnabled)
        isAuxiliary = c.decodeFlexibleBool(forKey: .isAuxiliary)
        auxiliaryFor = try? c.decodeIfPresent(String.self, forKey: .auxiliaryFor)
        reasoningEffort = try? c.decodeIfPresent(String.self, forKey: .reasoningEffort)
    }
}

extension APIClient {
    // MARK: - Provider 管理

    func fetchProviders() async throws -> [Provider] {
        let resp: APIResponse<[Provider]> = try await get(path: "providers")
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
        return resp.data ?? []
    }

    func updateProvider(_ provider: Provider) async throws {
        var body: [String: Any] = ["id": provider.id]
        if let n = provider.name { body["name"] = n }
        if let e = provider.isEnabled { body["is_enabled"] = e }
        if let a = provider.isAuxiliary { body["is_auxiliary"] = a }
        if let af = provider.auxiliaryFor { body["auxiliary_for"] = af }
        if let re = provider.reasoningEffort { body["reasoning_effort"] = re }
        let resp: APIResponse<EmptyData> = try await put(path: "providers", body: body)
        if !resp.isSuccess { throw APIError.businessError(code: resp.code, message: resp.msg) }
    }
}
