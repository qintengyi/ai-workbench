using System.Threading;
using Microsoft.UI.Xaml;

namespace AiWorkbench;

/// <summary>App 入口。深色主题优先。</summary>
public partial class App : Application
{
    internal MainWindow? _window;
    private CancellationTokenSource? _agentCts;

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        StartAgentIfNeeded();
    }

    /// <summary>
    /// 启动被控端 WS（后台）。配置来源：
    /// 环境变量 AI_WORKBENCH_AGENT_TOKEN / AI_WORKBENCH_SERVER_URL（缺省 ws://127.0.0.1:10370/ws/agent）。
    /// 无 token 则不启动（AgentClient 内部也会跳过连接）。
    /// </summary>
    private void StartAgentIfNeeded()
    {
        var token = System.Environment.GetEnvironmentVariable("AI_WORKBENCH_AGENT_TOKEN");
        if (string.IsNullOrEmpty(token)) return;
        var url = System.Environment.GetEnvironmentVariable("AI_WORKBENCH_SERVER_URL");
        _agentCts = new CancellationTokenSource();
        var agent = MainWindow.AgentClient;
        agent.AgentToken = token;
        if (!string.IsNullOrEmpty(url)) agent.ServerUrl = url;
        _ = agent.RunAsync(_agentCts.Token);
    }

    public static App CurrentApp => (App)Current;
    public static MainWindow? MainWindow => ((App)Current)._window;
}
