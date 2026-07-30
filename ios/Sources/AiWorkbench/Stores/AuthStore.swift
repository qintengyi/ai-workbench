import Foundation
import Observation

// MARK: - AuthStore

/// 鉴权状态（@Observable）。token + serverURL 持久化到 SettingsStore。
/// ponytail: 用存储属性镜像 SettingsStore（@Observable 宏只跟踪 stored property）；
/// 升级路径：iOS 17+ 可直接用 @AppStorage 或将 SettingsStore 改为 @Observable。
@Observable
final class AuthStore {
    static let shared = AuthStore()

    var serverURL: String
    var token: String?
    var username: String?

    var isLoggedIn: Bool {
        guard let t = token, !t.isEmpty else { return false }
        return true
    }

    private let store: SettingsStore

    init(store: SettingsStore = .shared) {
        self.store = store
        let cfg = store.config
        self.serverURL = cfg.serverURL
        self.token = cfg.token
        self.username = cfg.username
    }

    func saveLogin(token: String, username: String) {
        store.saveLogin(token: token, username: username)
        self.token = token
        self.username = username
    }

    func logout() {
        store.clearLogin()
        self.token = nil
        self.username = nil
    }

    func saveServerURL(_ url: String) {
        var c = store.config
        c.serverURL = url
        store.save(c)
        self.serverURL = url
    }
}
