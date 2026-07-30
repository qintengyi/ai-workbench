using Microsoft.UI.Xaml;

namespace AiWorkbench;

/// <summary>App 入口。深色主题优先。</summary>
public partial class App : Application
{
    internal MainWindow? _window;

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    public static App CurrentApp => (App)Current;
    public static MainWindow? MainWindow => ((App)Current)._window;
}
