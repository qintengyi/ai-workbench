import SwiftUI

// MARK: - ConversationsView

/// 会话列表（远程操作 Windows 端对话）
struct ConversationsView: View {
    @Environment(AppStateManager.self) private var appState
    @State private var conversations: [Conversation] = []
    @State private var isLoading: Bool = false
    @State private var errorMsg: String?

    var body: some View {
        NavigationStack {
            List {
                if isLoading && conversations.isEmpty {
                    HStack { Spacer(); ProgressView(); Spacer() }
                }
                ForEach(conversations) { conv in
                    NavigationLink(value: conv.id) {
                        ConversationRow(conv: conv)
                    }
                }
            }
            .navigationTitle("会话")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task { await createConversation() }
                    } label: {
                        Image(systemName: "plus")
                    }
                }
                ToolbarItem(placement: .topBarLeading) {
                    Button {
                        Task { await refresh() }
                    } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                }
            }
            .navigationDestination(for: String.self) { convId in
                ConversationDetailView(conversationId: convId)
            }
            .refreshable { await refresh() }
            .task {
                appState.webSocketClient.onConversationUpdated = { _ in
                    Task { await refresh() }
                }
                await refresh()
            }
            .onDisappear {
                if appState.webSocketClient.onConversationUpdated != nil {
                    appState.webSocketClient.onConversationUpdated = nil
                }
            }
            .overlay {
                if let err = errorMsg, conversations.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: "exclamationmark.triangle")
                            .font(.title)
                            .foregroundStyle(.secondary)
                        Text(err).font(.footnote).foregroundStyle(.secondary).multilineTextAlignment(.center)
                    }
                    .padding()
                } else if !isLoading && conversations.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: "bubble.left.and.bubble.right")
                            .font(.title)
                            .foregroundStyle(.secondary)
                        Text("暂无会话").font(.subheadline).foregroundStyle(.secondary)
                        Text("点右上 + 新建")
                            .font(.caption)
                            .foregroundStyle(.tertiary)
                    }
                }
            }
        }
    }

    private func refresh() async {
        isLoading = true
        errorMsg = nil
        do {
            conversations = try await APIClient.shared.fetchConversations()
        } catch let err as APIError {
            errorMsg = err.errorDescription
            if case .authRequired = err { appState.logout() }
        } catch {
            errorMsg = "加载失败：\(error.localizedDescription)"
        }
        isLoading = false
    }

    private func createConversation() async {
        do {
            _ = try await APIClient.shared.createConversation(title: "新会话 \(Date().formatted(.dateTime.hour().minute()))")
            await refresh()
        } catch let err as APIError {
            appState.showGlobalAlert(title: "新建失败", message: err.errorDescription ?? "")
        } catch {
            appState.showGlobalAlert(title: "新建失败", message: error.localizedDescription)
        }
    }
}

// MARK: - Row

struct ConversationRow: View {
    let conv: Conversation

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(conv.title ?? "未命名")
                    .font(.body.bold())
                    .lineLimit(1)
                Spacer()
                if let ts = conv.lastMessageAt, ts > 0 {
                    Text(formatTime(ts))
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
            if let p = conv.providerId, !p.isEmpty {
                Text("\(p) · \(conv.modelId ?? "")")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .padding(.vertical, 4)
    }

    private func formatTime(_ ts: Int64) -> String {
        let date = Date(timeIntervalSince1970: TimeInterval(ts))
        let f = DateFormatter()
        f.dateFormat = "MM-dd HH:mm"
        return f.string(from: date)
    }
}
