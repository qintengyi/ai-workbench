import SwiftUI

// MARK: - SettingsView

/// 设置：服务端地址、Token、登出、WS 状态
struct SettingsView: View {
    @Environment(AppStateManager.self) private var appState
    @State private var serverURL: String = SettingsStore.shared.config.serverURL
    @State private var username: String = SettingsStore.shared.config.username ?? ""
    @State private var showingLogoutConfirm: Bool = false

    var body: some View {
        NavigationStack {
            Form {
                Section("服务端") {
                    LabeledRow(label: "地址", value: serverURL)
                    LabeledRow(label: "用户", value: username)
                    LabeledRow(label: "Token", value: maskToken(SettingsStore.shared.config.token ?? ""))
                }

                Section("WebSocket 连接") {
                    LabeledRow(label: "状态", value: appState.webSocketClient.isConnected ? "已连接" : "未连接")
                    LabeledRow(label: "连接次数", value: "\(appState.webSocketClient.connectCount)")
                    LabeledRow(label: "最近事件", value: appState.webSocketClient.lastEventReceivedAt.isEmpty ? "—" : appState.webSocketClient.lastEventReceivedAt)
                    if let err = appState.webSocketClient.lastError {
                        Text(err).font(.footnote).foregroundStyle(.red)
                    }
                }

                Section("关于") {
                    LabeledRow(label: "版本", value: "\(appVersion()) (\(buildVersion()))")
                    LabeledRow(label: "平台", value: "iOS 远程控制端")
                    LabeledRow(label: "协议", value: "REST /api/* + WS /ws/app")
                }

                Section {
                    Button(role: .destructive) {
                        showingLogoutConfirm = true
                    } label: {
                        HStack { Spacer(); Text("退出登录").bold(); Spacer() }
                    }
                }
            }
            .navigationTitle("设置")
            .navigationBarTitleDisplayMode(.inline)
            .confirmationDialog("确认退出登录？", isPresented: $showingLogoutConfirm, titleVisibility: .visible) {
                Button("退出", role: .destructive) {
                    appState.logout()
                }
                Button("取消", role: .cancel) {}
            }
        }
    }

    private func maskToken(_ t: String) -> String {
        guard t.count > 10 else { return String(repeating: "•", count: t.count) }
        let head = String(t.prefix(6))
        let tail = String(t.suffix(4))
        return "\(head)…\(tail)"
    }

    private func appVersion() -> String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0"
    }

    private func buildVersion() -> String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "1"
    }
}

// MARK: - 只读行

struct LabeledRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack {
            Text(label).foregroundStyle(.secondary)
            Spacer()
            Text(value).lineLimit(1).truncationMode(.middle)
        }
    }
}
