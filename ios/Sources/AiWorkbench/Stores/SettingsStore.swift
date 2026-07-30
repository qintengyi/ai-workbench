import Foundation

// MARK: - SettingsStore

/// 管理 ServerConfig（serverURL/token/username），持久化到 UserDefaults
final class SettingsStore {
    static let shared = SettingsStore()
    private let key = "cn.aiworkbench.settings"
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    var config: ServerConfig {
        get {
            guard let data = defaults.data(forKey: key),
                  let decoded = try? JSONDecoder().decode(ServerConfig.self, from: data) else {
                return .default
            }
            return decoded
        }
        set {
            if let data = try? JSONEncoder().encode(newValue) {
                defaults.set(data, forKey: key)
            }
        }
    }

    func save(_ config: ServerConfig) {
        self.config = config
    }

    func saveLogin(token: String, username: String) {
        var c = config
        c.token = token
        c.username = username
        save(c)
    }

    func clearLogin() {
        var c = config
        c.token = nil
        c.username = nil
        save(c)
    }
}
