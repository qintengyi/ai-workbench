import SwiftUI
import Observation

// MARK: - 全局应用状态

@Observable
final class AppStateManager {
    var isLoggedIn: Bool = false
    var currentUser: String? = nil

    let webSocketClient: WSClient = WSClient.shared
    var globalAlert: GlobalAlert?

    private let store = SettingsStore.shared

    init() {
        self.isLoggedIn = store.config.isLoggedIn
        self.currentUser = store.config.username
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

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(appState)
                .preferredColorScheme(.dark)
                .alert(item: $appState.globalAlert) { alert in
                    Alert(title: Text(alert.title), message: Text(alert.message), dismissButton: .default(Text("好")))
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
