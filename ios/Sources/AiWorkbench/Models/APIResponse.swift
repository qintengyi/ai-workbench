import Foundation

// MARK: - 通用响应

/// 后端统一响应结构 {code, msg, data}
struct APIResponse<T: Decodable>: Decodable {
    let code: Int
    let msg: String?
    let data: T?

    var isSuccess: Bool { code == 200 }
}

/// 用于无返回数据的接口
struct EmptyData: Decodable {}

// MARK: - 登录响应

struct LoginData: Decodable {
    let token: String
    let expiresAt: Int64?

    enum CodingKeys: String, CodingKey {
        case token
        case expiresAt = "expires_at"
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        token = (try? c.decode(String.self, forKey: .token)) ?? ""
        expiresAt = c.decodeFlexibleInt64(forKey: .expiresAt)
    }
}

// MARK: - 宽松 JSON 解码

/// 服务端可能返回数字字段为字符串，统一用扩展解码
extension KeyedDecodingContainer {
    func decodeFlexibleInt(forKey key: Key) -> Int? {
        if let v = try? decodeIfPresent(Int.self, forKey: key) { return v }
        if let v = try? decodeIfPresent(Double.self, forKey: key) { return Int(v) }
        if let v = try? decodeIfPresent(String.self, forKey: key) {
            let s = v.replacingOccurrences(of: "%", with: "").trimmingCharacters(in: .whitespacesAndNewlines)
            if let n = Int(s) { return n }
            if let d = Double(s) { return Int(d) }
            return nil
        }
        return nil
    }

    func decodeFlexibleInt64(forKey key: Key) -> Int64? {
        if let v = try? decodeIfPresent(Int64.self, forKey: key) { return v }
        if let v = try? decodeIfPresent(Int.self, forKey: key) { return Int64(v) }
        if let v = try? decodeIfPresent(Double.self, forKey: key) { return Int64(v) }
        if let v = try? decodeIfPresent(String.self, forKey: key) {
            let s = v.trimmingCharacters(in: .whitespacesAndNewlines)
            if let n = Int64(s) { return n }
            if let d = Double(s) { return Int64(d) }
            return nil
        }
        return nil
    }

    func decodeFlexibleBool(forKey key: Key) -> Bool? {
        if let v = try? decodeIfPresent(Bool.self, forKey: key) { return v }
        if let v = try? decodeIfPresent(Int.self, forKey: key) { return v != 0 }
        if let v = try? decodeIfPresent(String.self, forKey: key) {
            let s = v.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
            if s == "true" || s == "1" || s == "yes" { return true }
            if s == "false" || s == "0" || s == "no" { return false }
            return nil
        }
        return nil
    }
}

// MARK: - AnyDecodable

/// 用于解析任意 JSON 值（WS data 字段）
struct AnyDecodable: Decodable {
    let value: Any?

    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() { value = nil }
        else if let v = try? c.decode(Bool.self) { value = v }
        else if let v = try? c.decode(Int64.self) { value = v }
        else if let v = try? c.decode(Double.self) { value = v }
        else if let v = try? c.decode(String.self) { value = v }
        else if let v = try? c.decode([AnyDecodable].self) { value = v.map { $0.value } }
        else if let v = try? c.decode([String: AnyDecodable].self) { value = v.mapValues { $0.value } }
        else { value = nil }
    }
}
