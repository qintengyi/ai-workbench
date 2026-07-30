import SwiftUI

// MARK: - ConversationDetailView

/// 会话详情：远程操作 Windows 端对话，实时接收 WS 流式增量
struct ConversationDetailView: View {
    let conversationId: String

    @Environment(AppStateManager.self) private var appState
    @State private var messages: [Message] = []
    @State private var inputText: String = ""
    @State private var isLoading: Bool = false
    @State private var isSending: Bool = false
    @State private var errorMsg: String?
    @State private var streamingText: String = ""
    @State private var streamingReasoning: String = ""
    @State private var isStreaming: Bool = false
    /// 流超时看门狗 Task
    @State private var streamTimeoutTask: Task<Void, Never>?
    /// 乐观消息递减计数器（避免同秒 Date 重复）
    @State private var _optimId: Int = -1
    /// handleNewMessage debounce
    @State private var refreshDebounceTask: Task<Void, Never>?

    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 12) {
                        ForEach(messages) { msg in
                            MessageBubble(message: msg)
                                .id(msg.id)
                        }
                        if isStreaming || !streamingText.isEmpty {
                            StreamingBubble(text: streamingText, reasoning: streamingReasoning, active: isStreaming)
                                .id("streaming")
                        }
                    }
                    .padding(.horizontal, 12)
                    .padding(.vertical, 8)
                }
                .onChange(of: messages.count) { _, _ in
                    withAnimation { proxy.scrollTo(messages.last?.id ?? 0, anchor: .bottom) }
                }
                .onChange(of: streamingText) { _, _ in
                    withAnimation { proxy.scrollTo("streaming", anchor: .bottom) }
                }
            }

            Divider()
            inputBar
        }
        .navigationTitle("对话")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    Task { await refresh() }
                } label: {
                    Image(systemName: "arrow.clockwise")
                }
            }
        }
        .task {
            appState.webSocketClient.onStreamChunk = handleStream
            appState.webSocketClient.onNewMessage = handleNewMessage
            await refresh()
        }
        .onDisappear {
            appState.webSocketClient.onStreamChunk = nil
            appState.webSocketClient.onNewMessage = nil
            streamTimeoutTask?.cancel()
            refreshDebounceTask?.cancel()
        }
    }

    // MARK: - 输入栏

    private var inputBar: some View {
        HStack(spacing: 8) {
            TextField("输入消息…", text: $inputText, axis: .vertical)
                .textFieldStyle(.roundedBorder)
                .lineLimit(1...5)
                .submitLabel(.send)

            Button {
                Task { await send() }
            } label: {
                if isSending {
                    ProgressView().controlSize(.small).tint(.white).padding(8)
                        .background(Color.accentColor)
                        .clipShape(Circle())
                } else {
                    Image(systemName: "paperplane.fill")
                        .padding(8)
                        .background(Color.accentColor)
                        .foregroundStyle(.white)
                        .clipShape(Circle())
                }
            }
            .disabled(isSending || inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        }
        .padding(12)
    }

    // MARK: - 加载/刷新

    private func refresh() async {
        isLoading = true
        errorMsg = nil
        do {
            messages = try await APIClient.shared.fetchMessages(conversationId: conversationId)
        } catch let err as APIError {
            errorMsg = err.errorDescription
            if case .authRequired = err { appState.logout() }
        } catch {
            errorMsg = "加载失败：\(error.localizedDescription)"
        }
        isLoading = false
    }

    // MARK: - 发送

    private func send() async {
        let content = inputText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !content.isEmpty, !isSending else { return }
        isSending = true
        inputText = ""
        // 乐观插入用户消息：递减计数器避免同秒 Date 重复
        let optimistic = Message(id: _optimId, conversationId: conversationId, role: "user", content: content, ts: Int64(Date().timeIntervalSince1970))
        _optimId -= 1
        messages.append(optimistic)
        streamingText = ""
        streamingReasoning = ""
        isStreaming = true
        // 启动 30s 流超时看门狗
        streamTimeoutTask?.cancel()
        streamTimeoutTask = Task { [self] in
            try? await Task.sleep(nanoseconds: 30_000_000_000)
            guard !Task.isCancelled else { return }
            await MainActor.run {
                if self.isStreaming {
                    self.isStreaming = false
                    self.errorMsg = "响应超时"
                    self.streamingText = ""
                    self.streamingReasoning = ""
                }
            }
        }
        do {
            _ = try await APIClient.shared.sendMessage(conversationId: conversationId, content: content)
        } catch let err as APIError {
            isStreaming = false
            streamTimeoutTask?.cancel()
            errorMsg = err.errorDescription
            if case .authRequired = err { appState.logout() }
        } catch {
            isStreaming = false
            streamTimeoutTask?.cancel()
            errorMsg = "发送失败：\(error.localizedDescription)"
        }
        isSending = false
    }

    // MARK: - WS 回调

    private func handleStream(_ chunk: WSStreamChunk) {
        guard chunk.conversationId == conversationId else { return }
        if !chunk.delta.isEmpty { streamingText += chunk.delta }
        if let r = chunk.reasoningDelta, !r.isEmpty { streamingReasoning += r }
        isStreaming = !chunk.finished
        if chunk.finished {
            // 流结束：取消超时看门狗，把流文本暂存为占位 assistant 消息，等 new_message 事件刷新真实消息
            streamTimeoutTask?.cancel()
            if !streamingText.isEmpty {
                let placeholder = Message(
                    id: _optimId,
                    conversationId: conversationId,
                    role: "assistant",
                    content: streamingText,
                    reasoningContent: streamingReasoning.isEmpty ? nil : streamingReasoning,
                    ts: Int64(Date().timeIntervalSince1970)
                )
                _optimId -= 1
                messages.append(placeholder)
            }
            streamingText = ""
            streamingReasoning = ""
        }
    }

    private func handleNewMessage(convId: String, role: String, content: String) {
        guard convId == conversationId else { return }
        // debounce 300ms 合并多次 refresh
        refreshDebounceTask?.cancel()
        refreshDebounceTask = Task {
            try? await Task.sleep(nanoseconds: 300_000_000)
            guard !Task.isCancelled else { return }
            await refresh()
        }
    }
}

// MARK: - 气泡

struct MessageBubble: View {
    let message: Message

    var body: some View {
        HStack {
            if message.isUser { Spacer() }
            VStack(alignment: message.isUser ? .trailing : .leading, spacing: 4) {
                if let reasoning = message.reasoningContent, !reasoning.isEmpty {
                    Text(reasoning)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .padding(8)
                        .background(Color.secondary.opacity(0.15))
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                }
                if let content = message.content, !content.isEmpty {
                    Text(content)
                        .font(.body)
                        .foregroundStyle(message.isUser ? .white : .primary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 8)
                        .background(message.isUser ? Color.accentColor : Color.secondary.opacity(0.2))
                        .clipShape(RoundedRectangle(cornerRadius: 12))
                }
            }
            if !message.isUser { Spacer() }
        }
    }
}

struct StreamingBubble: View {
    let text: String
    let reasoning: String
    let active: Bool

    var body: some View {
        HStack {
            Spacer()
            VStack(alignment: .leading, spacing: 4) {
                if !reasoning.isEmpty {
                    Text(reasoning)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .padding(8)
                        .background(Color.secondary.opacity(0.15))
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                }
                HStack(alignment: .bottom, spacing: 4) {
                    Text(text.isEmpty ? "…" : text)
                        .font(.body)
                        .foregroundStyle(.primary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 8)
                        .background(Color.secondary.opacity(0.2))
                        .clipShape(RoundedRectangle(cornerRadius: 12))
                    if active {
                        ProgressView().controlSize(.small)
                    }
                }
            }
            .frame(maxWidth: 0.85, alignment: .leading)
        }
    }
}
