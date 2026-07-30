import Foundation

// MARK: - 文件节点

/// GET /api/files 返回 Windows E:\code 文件树节点
struct FileNode: Codable, Identifiable, Equatable {
    /// 节点唯一 id（用相对路径）
    var id: String { path }
    /// 相对路径，如 "ai-workbench/docs/ARCHITECTURE.md"
    let path: String
    /// 名称
    let name: String
    /// 是否目录
    var isDir: Bool
    /// 子节点（仅展开时填充）
    var children: [FileNode]?
    /// 字节数（文件）
    var size: Int64?
    /// 修改时间（秒级）
    var modifiedAt: Int64?

    enum CodingKeys: String, CodingKey {
        case path, name
        case isDir = "is_dir"
        case children
        case size
        case modifiedAt = "modified_at"
    }

    init(path: String, name: String, isDir: Bool, children: [FileNode]? = nil,
         size: Int64? = nil, modifiedAt: Int64? = nil) {
        self.path = path
        self.name = name
        self.isDir = isDir
        self.children = children
        self.size = size
        self.modifiedAt = modifiedAt
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        path = (try? c.decode(String.self, forKey: .path)) ?? ""
        name = (try? c.decode(String.self, forKey: .name)) ?? ""
        isDir = c.decodeFlexibleBool(forKey: .isDir) ?? false
        children = try? c.decodeIfPresent([FileNode].self, forKey: .children)
        size = c.decodeFlexibleInt64(forKey: .size)
        modifiedAt = c.decodeFlexibleInt64(forKey: .modifiedAt)
    }
}

// MARK: - 读取文件响应

struct FileContent: Decodable {
    let path: String
    let content: String
    let encoding: String?
    let size: Int64?

    enum CodingKeys: String, CodingKey {
        case path, content, encoding, size
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        path = (try? c.decode(String.self, forKey: .path)) ?? ""
        content = (try? c.decode(String.self, forKey: .content)) ?? ""
        encoding = try? c.decodeIfPresent(String.self, forKey: .encoding)
        size = c.decodeFlexibleInt64(forKey: .size)
    }
}

// MARK: - 浏览文件请求

struct ListFilesRequest: Encodable {
    let path: String
}
