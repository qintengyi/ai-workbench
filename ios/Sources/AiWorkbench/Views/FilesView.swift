import SwiftUI

// MARK: - FilesView

/// 浏览 Windows 端 E:\code 文件树、读文件
struct FilesView: View {
    @Environment(AppStateManager.self) private var appState
    @State private var rootNodes: [FileNode] = []
    @State private var currentPath: String = ""
    @State private var isLoading: Bool = false
    @State private var errorMsg: String?

    var body: some View {
        NavigationStack {
            List {
                if !currentPath.isEmpty {
                    Button {
                        navigateUp()
                    } label: {
                        Label("返回上级", systemImage: "arrow.up")
                            .foregroundStyle(Color.accentColor)
                    }
                }
                if isLoading && rootNodes.isEmpty {
                    HStack { Spacer(); ProgressView(); Spacer() }
                }
                ForEach(rootNodes) { node in
                    FileNodeRow(node: node, onOpen: { open(node) })
                }
            }
            .navigationTitle(navigationTitle)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task { await refresh() }
                    } label: { Image(systemName: "arrow.clockwise") }
                }
            }
            .refreshable { await refresh() }
            .task { await refresh() }
            .sheet(item: $showingFile) { node in
                FilePreviewSheet(node: node)
            }
            .overlay {
                if let err = errorMsg, rootNodes.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: "exclamationmark.triangle").font(.title).foregroundStyle(.secondary)
                        Text(err).font(.footnote).foregroundStyle(.secondary).multilineTextAlignment(.center)
                    }.padding()
                } else if !isLoading && rootNodes.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: "folder").font(.title).foregroundStyle(.secondary)
                        Text("空目录").font(.subheadline).foregroundStyle(.secondary)
                    }
                }
            }
        }
    }

    private var navigationTitle: String {
        if currentPath.isEmpty { return "E:\\code" }
        return currentPath
    }

    private func refresh() async {
        isLoading = true
        errorMsg = nil
        do {
            rootNodes = try await APIClient.shared.listFiles(path: currentPath)
        } catch let err as APIError {
            errorMsg = err.errorDescription
            if case .authRequired = err { appState.logout() }
        } catch {
            errorMsg = "加载失败：\(error.localizedDescription)"
        }
        isLoading = false
    }

    private func open(_ node: FileNode) {
        if node.isDir {
            currentPath = node.path
            Task { await refresh() }
        } else {
            // 文件 → 跳转预览（用 NavigationLink 不可行，需 push 详情）
            // 简化：直接弹 sheet
            showingFile = node
        }
    }

    private func navigateUp() {
        let parts = currentPath.split(separator: "/")
        if parts.isEmpty {
            currentPath = ""
        } else {
            currentPath = parts.dropLast().joined(separator: "/")
        }
        Task { await refresh() }
    }

    @State private var showingFile: FileNode?
}

// MARK: - Row

struct FileNodeRow: View {
    let node: FileNode
    let onOpen: () -> Void

    var body: some View {
        Button(action: onOpen) {
            HStack {
                Image(systemName: node.isDir ? "folder.fill" : fileIcon(node.name))
                    .foregroundStyle(node.isDir ? Color.accentColor : Color.secondary)
                    .font(.title3)
                VStack(alignment: .leading, spacing: 2) {
                    Text(node.name).font(.body).lineLimit(1)
                    if !node.isDir, let size = node.size {
                        Text(humanReadable(size))
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }
                }
                Spacer()
                if node.isDir { Image(systemName: "chevron.right").font(.caption).foregroundStyle(.tertiary) }
            }
        }
        .buttonStyle(.plain)
    }

    private func fileIcon(_ name: String) -> String {
        let ext = (name as NSString).pathExtension.lowercased()
        switch ext {
        case "md": return "doc.text"
        case "swift": return "swift"
        case "py": return "doc.text"
        case "json": return "curlybraces"
        case "yml", "yaml": return "doc.text"
        case "png", "jpg", "jpeg", "gif": return "photo"
        case "html", "htm": return "globe"
        default: return "doc"
        }
    }

    private func humanReadable(_ bytes: Int64) -> String {
        let f = ByteCountFormatter()
        f.allowedUnits = [.useKB, .useMB, .useGB]
        f.countStyle = .file
        return f.string(fromByteCount: bytes)
    }
}

// MARK: - 文件预览 Sheet

struct FilePreviewSheet: View {
    let node: FileNode
    @State private var content: String = ""
    @State private var isLoading: Bool = false
    @State private var errorMsg: String?

    var body: some View {
        NavigationStack {
            ScrollView {
                if isLoading {
                    HStack { Spacer(); ProgressView(); Spacer() }.padding()
                } else if let err = errorMsg {
                    Text(err).foregroundStyle(.red).padding()
                } else {
                    Text(content)
                        .font(.system(.footnote, design: .monospaced))
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding()
                        .textSelection(.enabled)
                }
            }
            .navigationTitle(node.name)
            .navigationBarTitleDisplayMode(.inline)
        }
        .task { await load() }
    }

    private func load() async {
        isLoading = true
        do {
            let f = try await APIClient.shared.readFile(path: node.path)
            content = f.content
        } catch let err as APIError {
            errorMsg = err.errorDescription
        } catch {
            errorMsg = "读取失败：\(error.localizedDescription)"
        }
        isLoading = false
    }
}
