using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AiWorkbench.Pages;
using AiWorkbench.Services;

namespace AiWorkbench;

/// <summary>主窗口：Mica 背景 + Fluent 导航。</summary>
public sealed partial class MainWindow : Window
{
    public static ProviderStore ProviderStore { get; } = new();
    public static FileWorkspace FileWorkspace { get; } = new();
    public static AiClient AiClient { get; } = new();
    public static ImageRouter ImageRouter { get; } = new(AiClient, ProviderStore);
    public static AgentClient AgentClient { get; } = new(ProviderStore, FileWorkspace, AiClient, ImageRouter);

    public MainWindow()
    {
        InitializeComponent();
        Title = "AI Workbench";
        ExtendsContentIntoTitleBar = true;
        TrySetSystemBackdrop();
        ContentFrame.Navigate(typeof(ChatPage));
    }

    private void TrySetSystemBackdrop()
    {
        if (MicaController.IsSupported())
            SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        else if (DesktopAcrylicController.IsSupported())
            SystemBackdrop = new DesktopAcrylicBackdrop();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "Chat";
        ContentFrame.Navigate(tag switch
        {
            "Files" => typeof(FilesPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(ChatPage),
        });
    }

    /// <summary>供子页面访问窗口实例。</summary>
    public static MainWindow Current => (MainWindow)App.CurrentApp._window!;
}

