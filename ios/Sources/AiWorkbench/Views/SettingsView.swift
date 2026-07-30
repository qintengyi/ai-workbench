import SwiftUI

// MARK: - SettingsView

/// 设置：服务端地址、Token、登出、WS 状态
struct SettingsView: View {
    @Environment(AppStateManager.self) private var appState
    @State private var serverURL: String = SettingsStore.shared.config.serverURL
    @State private var username: String = SettingsStore.shared.config.username ?? ""
    @State private var showingLogoutConfirm: Bool = false
    @State private var showingSaveAlert: Bool = false

    var body: some View {
        NavigationStack {
            Form {
                Section("服务端") {
                    TextField("地址", text: $serverURL)
                        .textFieldStyle(.roundedBorder)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                    Button("保存并重连") {
                        saveServerURL()
                    }
                    .disabled(serverURL.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    LabeledRow(label: "用户", value: username)
                    LabeledRow(label: "Token", value: maskToken(SettingsStore.shared.config.token ?? ""))
                    NavigationLink("Provider 管理") {
                        ProviderManageView()
                    }
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
            .alert("已保存", isPresented: $showingSaveAlert) {
                Button("好", role: .cancel) {}
            }
        }
    }

    private func saveServerURL() {
        let trimmed = serverURL.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        var c = SettingsStore.shared.config
        c.serverURL = trimmed
        SettingsStore.shared.save(c)
        // 触发 WS 重连：先停再启
        let ws = appState.webSocketClient
        ws.stop()
        ws.start()
        showingSaveAlert = true
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

// MARK: - Provider 管理子页

struct ProviderManageView: View {
    @State private var providers: [Provider] = []
    @State private var isLoading: Bool = false
    @State private var errorMsg: String?

    var body: some View {
        Form {
            if isLoading && providers.isEmpty {
                HStack { Spacer(); ProgressView(); Spacer() }
            }
            ForEach($providers) { $p in
                Section {
                    LabeledRow(label: "ID", value: p.id)
                    if let n = p.name {
                        LabeledRow(label: "名称", value: n)
                    }
                    Toggle("启用", isOn: binding($p.isEnabled, default: false))
                    Toggle("辅助 Provider", isOn: binding($p.isAuxiliary, default: false))
                    if p.isAuxiliary == true {
                        TextField("auxiliary_for", text: binding($p.auxiliaryFor, default: ""))
                            .textFieldStyle(.roundedBorder)
                    }
                    TextField("reasoning effort", text: binding($p.reasoningEffort, default: ""))
                        .textFieldStyle(.roundedBorder)
                    Button("保存") { save(p) }
                }
            }
            if let err = errorMsg {
                Text(err).font(.footnote).foregroundStyle(.red)
            }
        }
        .navigationTitle("Provider 管理")
        .navigationBarTitleDisplayMode(.inline)
        .task { await refresh() }
    }

    /// 把 Optional<T> 暴露为非可选 Binding，方便表单控件
    private func binding<T>(_ source: Binding<T?>, default def: T) -> Binding<T> {
        Binding(
            get: { source.wrappedValue ?? def },
            set: { source.wrappedValue = $0 }
        )
    }

    private func refresh() async {
        isLoading = true
        errorMsg = nil
        do {
            providers = try await APIClient.shared.fetchProviders()
        } catch let err as APIError {
            errorMsg = err.errorDescription
        } catch {
            errorMsg = "加载失败：\(error.localizedDescription)"
        }
        isLoading = false
    }

    private func save(_ p: Provider) {
        Task {
            do {
                try await APIClient.shared.updateProvider(p)
            } catch let err as APIError {
                errorMsg = err.errorDescription
            } catch {
                errorMsg = "保存失败：\(error.localizedDescription)"
            }
        }
    }
}
