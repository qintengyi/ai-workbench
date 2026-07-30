import SwiftUI

// MARK: - MainView

/// 主 TabView：会话 / 文件 / 设置（镜像 Windows 端布局）
struct MainView: View {
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
    }
}
