import Foundation

// MARK: - 服务端配置

/// 本地保存的服务端配置（持久化到 UserDefaults）
struct ServerConfig: Codable, Equatable {
    /// 服务端地址，如 http://192.168.1.8 或 https://ai.example.com
    var serverURL: String
    /// 登录 token（HMAC-SHA256 自签）
    var token: String?
    /// 上次登录用户名
    var username: String?

    static let `default` = ServerConfig(
        serverURL: "http://192.168.1.8",
        token: nil,
        username: nil
    )

    var isLoggedIn: Bool {
        guard let t = token, !t.isEmpty else { return false }
        return true
    }
}
