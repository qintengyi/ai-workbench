import Foundation

// MARK: - 会话

/// GET /api/conversations 返回项
/// 结构对齐 PROVIDER_SPEC §4: {id, title, providerId, modelId, createdAt, updatedAt}
struct Conversation: Codable, Identifiable, Equatable {
    let id: String
    var title: String?
    var providerId: String?
    var modelId: String?
    var createdAt: Int64?
    var updatedAt: Int64?
    var lastMessageAt: Int64?

    enum CodingKeys: String, CodingKey {
        case id, title
        case providerId = "provider_id"
        case modelId = "model_id"
        case createdAt = "created_at"
        case updatedAt = "updated_at"
        case lastMessageAt = "last_message_at"
    }

    init(id: String, title: String? = nil, providerId: String? = nil, modelId: String? = nil,
         createdAt: Int64? = nil, updatedAt: Int64? = nil, lastMessageAt: Int64? = nil) {
        self.id = id
        self.title = title
        self.providerId = providerId
        self.modelId = modelId
        self.createdAt = createdAt
        self.updatedAt = updatedAt
        self.lastMessageAt = lastMessageAt
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        // 兼容 Int/String，避免后端返 Int id 时落到随机 UUID 致列表点击失效
        if let s = try? c.decode(String.self, forKey: .id), !s.isEmpty {
            id = s
        } else {
            id = String((c.decodeFlexibleInt(forKey: .id) ?? 0))
        }
        title = try? c.decodeIfPresent(String.self, forKey: .title)
        providerId = try? c.decodeIfPresent(String.self, forKey: .providerId)
        modelId = try? c.decodeIfPresent(String.self, forKey: .modelId)
        createdAt = c.decodeFlexibleInt64(forKey: .createdAt)
        updatedAt = c.decodeFlexibleInt64(forKey: .updatedAt)
        lastMessageAt = c.decodeFlexibleInt64(forKey: .lastMessageAt)
    }
}

// MARK: - 新建会话请求

struct CreateConversationRequest: Encodable {
    let title: String?
    let providerId: String?
    let modelId: String?

    enum CodingKeys: String, CodingKey {
        case title
        case providerId = "provider_id"
        case modelId = "model_id"
    }
}
