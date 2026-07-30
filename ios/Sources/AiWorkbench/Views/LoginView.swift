import SwiftUI

// MARK: - LoginView

/// 登录服务端拿 token
struct LoginView: View {
    @Environment(AppStateManager.self) private var appState
    @State private var serverURL: String = SettingsStore.shared.config.serverURL
    @State private var username: String = SettingsStore.shared.config.username ?? ""
    @State private var password: String = ""
    @State private var isLoading: Bool = false
    @State private var errorMsg: String?

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 24) {
                    header

                    VStack(spacing: 16) {
                        LabeledField("服务器地址", text: $serverURL, placeholder: "http://192.168.1.8")
                            .keyboardType(.URL)
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()

                        LabeledField("用户名", text: $username, placeholder: "admin")
                            .textInputAutocapitalization(.never)
                            .autocorrectionDisabled()

                        SecureField("密码", text: $password)
                            .textFieldStyle(.roundedBorder)
                            .submitLabel(.go)
                            .onSubmit { login() }
                    }

                    if let err = errorMsg {
                        Text(err)
                            .font(.footnote)
                            foregroundStyle(.red)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }

                    Button(action: login) {
                        HStack {
                            if isLoading { ProgressView().controlSize(.small).tint(.white) }
                            Text(isLoading ? "登录中…" : "登录").bold()
                        }
                        .frame(maxWidth: .infinity, minHeight: 48)
                        .background(Color.accentColor)
                        .foregroundStyle(.white)
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                    }
                    .disabled(isLoading || username.isEmpty || password.isEmpty || serverURL.isEmpty)

                    Spacer(minLength: 0)
                }
                .padding(24)
            }
            .navigationTitle("AI 工作台")
            .navigationBarTitleDisplayMode(.inline)
        }
    }

    private var header: some View {
        VStack(spacing: 8) {
            Image(systemName: "cpu")
                .font(.system(size: 56))
                .foregroundStyle(.accent)
            Text("全场景 AI 办公工作台")
                .font(.title3.bold())
            Text("远程控制 Windows 端")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .padding(.top, 12)
    }

    private func login() {
        guard !isLoading else { return }
        isLoading = true
        errorMsg = nil
        let url = serverURL.trimmingCharacters(in: .whitespacesAndNewlines)
        let user = username.trimmingCharacters(in: .whitespacesAndNewlines)
        let pass = password
        Task {
            do {
                let token = try await APIClient.shared.login(username: user, password: pass, serverURL: url)
                SettingsStore.shared.saveLogin(token: token, username: user)
                await appState.onLoginSuccess()
            } catch let err as APIError {
                errorMsg = err.errorDescription
            } catch {
                errorMsg = "登录失败：\(error.localizedDescription)"
            }
            isLoading = false
        }
    }
}

// MARK: - 复用组件

struct LabeledField: View {
    let title: String
    @Binding var text: String
    let placeholder: String

    init(_ title: String, text: Binding<String>, placeholder: String = "") {
        self.title = title
        self._text = text
        self.placeholder = placeholder
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.subheadline).foregroundStyle(.secondary)
            TextField(placeholder, text: $text)
                .textFieldStyle(.roundedBorder)
        }
    }
}
