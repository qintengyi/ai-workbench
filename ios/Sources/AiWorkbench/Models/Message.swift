import Foundation

// MARK: - 消息

/// GET /api/conversations/{id}/messages 返回项
/// 结构对齐 PROVIDER_SPEC §4:
/// {id, conversationId, role(user/assistant/system), content, images[],
///  reasoningContent, effort, ts, auxiliaryTrace[]}
struct Message: Codable, Identifiable, Equatable {
    let id: Int
    var conversationId: String?
    var role: String?
    var content: String?
    var images: [String]?
    var reasoningContent: String?
    var effort: String?
    var ts: Int64?
    var auxiliaryTrace: [AuxiliaryTrace]?

    enum CodingKeys: String, CodingKey {
        case id
        case conversationId = "conversation_id"
        case role, content, images
        case reasoningContent = "reasoning_content"
        case effort
        case ts
        case auxiliaryTrace = "auxiliary_trace"
    }

    init(id: Int, conversationId: String? = nil, role: String? = nil, content: String? = nil,
         images: [String]? = nil, reasoningContent: String? = nil, effort: String? = nil,
         ts: Int64? = nil, auxiliaryTrace: [AuxiliaryTrace]? = nil) {
        self.id = id
        self.conversationId = conversationId
        self.role = role
        self.content = content
        self.images = images
        self.reasoningContent = reasoningContent
        self.effort = effort
        self.ts = ts
        self.auxiliaryTrace = auxiliaryTrace
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.decodeFlexibleInt(forKey: .id) ?? 0
        conversationId = try? c.decodeIfPresent(String.self, forKey: .conversationId)
        role = try? c.decodeIfPresent(String.self, forKey: .role)
        content = try? c.decodeIfPresent(String.self, forKey: .content)
        images = try? c.decodeIfPresent([String].self, forKey: .images)
        reasoningContent = try? c.decodeIfPresent(String.self, forKey: .reasoningContent)
        effort = try? c.decodeIfPresent(String.self, forKey: .effort)
        ts = c.decodeFlexibleInt64(forKey: .ts)
        auxiliaryTrace = try? c.decodeIfPresent([AuxiliaryTrace].self, forKey: .auxiliaryTrace)
    }

    var isUser: Bool { (role ?? "").lowercased() == "user" }
    var isAssistant: Bool { (role ?? "").lowercased() == "assistant" }
    var isSystem: Bool { (role ?? "").lowercased() == "system" }
}

// MARK: - 辅助调用追踪

/// PROVIDER_SPEC §4: 记录后台辅助调用的模型 id + 识别结果
struct AuxiliaryTrace: Codable, Identifiable, Equatable {
    let id: Int
    var modelId: String?
    var result: String?

    enum CodingKeys: String, CodingKey {
        case id
        case modelId = "model_id"
        case result
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = c.decodeFlexibleInt(forKey: .id) ?? 0
        modelId = try? c.decodeIfPresent(String.self, forKey: .modelId)
        result = try? c.decodeIfPresent(String.self, forKey: .result)
    }
}

// MARK: - 发送消息请求

struct SendMessageRequest: Encodable {
    let content: String
    var images: [String]?

    init(content: String, images: [String]? = nil) {
        self.content = content
        self.images = images
    }
}

// MARK: - 发送消息响应

struct SendMessageResult: Decodable {
    let ok: Bool?
    let queued: Bool?
    let messageId: Int?

    enum CodingKeys: String, CodingKey {
        case ok, queued
        case messageId = "message_id"
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        ok = try? c.decodeIfPresent(Bool.self, forKey: .ok)
        queued = try? c.decodeIfPresent(Bool.self, forKey: .queued)
        messageId = c.decodeFlexibleInt(forKey: .messageId)
    }
}
