import SwiftUI

// MARK: - MainView

/// 主 TabView：会话 / 文件 / 设置（镜像 Windows 端布局）
struct MainView: View {
    @Environment(AppStateManager.self) private var appState

    var body: some View {
        TabView {
            ConversationsView()
                .tabItem {
                    Label("会话", systemImage: "bubble.left.and.bubble.right")
                }

            FilesView()
                .tabItem {
                    Label("文件", systemImage: "folder")
                }

            SettingsView()
                .tabItem {
                    Label("设置", systemImage: "gearshape")
                }
        }
        .overlay(alignment: .top) {
            AgentStatusBadge(online: appState.isAgentOnline)
                .padding(.top, 2)
        }
    }
}

// MARK: - Agent 在线徽标

private struct AgentStatusBadge: View {
    let online: Bool

    var body: some View {
        HStack(spacing: 4) {
            Circle()
                .fill(online ? Color.green : Color.gray)
                .frame(width: 7, height: 7)
            Text(online ? "Agent 在线" : "Agent 离线")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 2)
        .background(Color.secondary.opacity(0.1))
        .clipShape(Capsule())
    }
}
