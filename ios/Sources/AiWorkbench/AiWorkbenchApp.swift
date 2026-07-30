import SwiftUI
import Observation

// MARK: - 全局应用状态

@Observable
final class AppStateManager {
    var isLoggedIn: Bool = false
    var currentUser: String? = nil
    /// agent 在线状态镜像，供 UI 订阅
    var isAgentOnline: Bool = false

    let webSocketClient: WSClient = WSClient.shared
    var globalAlert: GlobalAlert?

    private let store = SettingsStore.shared

    init() {
        self.isLoggedIn = store.config.isLoggedIn
        self.currentUser = store.config.username
        // 桥接 agent 在线状态到 @Observable 属性
        webSocketClient.onAgentOnlineChanged = { [weak self] online in
            DispatchQueue.main.async { self?.isAgentOnline = online }
        }
    }

    func refreshAuthState() {
        isLoggedIn = store.config.isLoggedIn
        currentUser = store.config.username
    }

    func onLoginSuccess() async {
        refreshAuthState()
        webSocketClient.start()
    }

    func logout() {
        webSocketClient.stop()
        store.clearLogin()
        refreshAuthState()
    }

    func showGlobalAlert(title: String, message: String) {
        globalAlert = GlobalAlert(title: title, message: message)
    }
}

struct GlobalAlert: Identifiable {
    let id = UUID()
    let title: String
    let message: String
}

// MARK: - App 入口

@main
struct AiWorkbenchApp: App {
    @State private var appState = AppStateManager()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(appState)
                .preferredColorScheme(.dark)
                .alert(item: $appState.globalAlert) { alert in
                    Alert(title: Text(alert.title), message: Text(alert.message), dismissButton: .default(Text("好")))
                }
                .onChange(of: scenePhase) { _, phase in
                    guard phase == .active else { return }
                    // 回前台时若应连接但未连，自动重连
                    if appState.isLoggedIn && !appState.webSocketClient.isConnected {
                        appState.webSocketClient.start()
                    }
                }
        }
    }
}

// MARK: - RootView

struct RootView: View {
    @Environment(AppStateManager.self) private var appState

    var body: some View {
        if appState.isLoggedIn {
            MainView()
        } else {
            LoginView()
        }
    }
}
