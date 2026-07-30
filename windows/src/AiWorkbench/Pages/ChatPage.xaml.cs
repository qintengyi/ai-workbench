using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiWorkbench.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace AiWorkbench.Pages;

/// <summary>对话主界面。第 10 条主辅图片切换对用户透明（UI 仅显示主模型回复）。</summary>
public sealed partial class ChatPage : Page
{
    public ObservableCollection<Message> Messages { get; } = new();
    public ObservableCollection<Provider> Providers { get; } = new();

    private CancellationTokenSource? _cts;
    private Provider? _current;
    private readonly ObservableCollection<string> _pendingImages = new();

    public ChatPage()
    {
        InitializeComponent();
        _ = LoadProvidersAsync();
    }

    private async Task LoadProvidersAsync()
    {
        Providers.Clear();
        var list = await MainWindow.ProviderStore.LoadAsync();
        foreach (var p in list) Providers.Add(p);
        if (Providers.Count > 0)
        {
            ProviderCombo.ItemsSource = Providers;
            ProviderCombo.DisplayMemberPath = "Name";
            ProviderCombo.SelectedIndex = 0;
        }
    }

    private void Provider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderCombo.SelectedItem is Provider p)
        {
            _current = p;
            ModelCombo.Items.Clear();
            ModelCombo.Items.Add(p.Id);
            ModelCombo.SelectedIndex = 0;
            // 思考强度：若允许关闭思考则前置 "off"
            var efforts = new System.Collections.Generic.List<string>();
            if (p.AllowDisableReasoning) efforts.Add("off");
            efforts.AddRange(p.Reasoning.SupportedEfforts);
            EffortCombo.ItemsSource = efforts;
            EffortCombo.SelectedItem = !string.IsNullOrEmpty(p.Reasoning.DefaultEffort)
                ? p.Reasoning.DefaultEffort
                : (efforts.Count > 0 ? efforts[efforts.Count - 1] : null);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
        => await SendAsync();

    private async void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter
            && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0)
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (_current is null) return;
        var text = InputBox.Text.Trim();
        if (text.Length == 0 && _pendingImages.Count == 0) return;

        var userMsg = new Message
        {
            Role = "user",
            Content = text,
            ConversationId = "local",
        };
        foreach (var img in _pendingImages) userMsg.Images.Add(img);
        _pendingImages.Clear();
        ImgCount.Text = string.Empty;
        Messages.Add(userMsg);
        InputBox.Text = string.Empty;

        var assistant = new Message
        {
            Role = "assistant",
            ConversationId = "local",
            Effort = EffortCombo.SelectedItem?.ToString(),
        };
        Messages.Add(assistant);

        SendBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        _cts = new CancellationTokenSource();

        try
        {
            // 第 10 条主辅切换：在调用前把图片识别为文字（透明）
            var prepared = await MainWindow.ImageRouter.PrepareForPrimaryAsync(_current, userMsg);
            // 把 system 与 user 历史一并发送（简化：单轮）
            var history = new System.Collections.Generic.List<Message>
            {
                new() { Role = "system", Content = "你是 AI Workbench 助手。" },
                prepared,
            };
            var modelId = ModelCombo.SelectedItem?.ToString() ?? _current.Id;
            var effort = assistant.Effort == "off" ? null : assistant.Effort;

            await MainWindow.AiClient.StreamChatAsync(_current, modelId, history, effort,
                onContent: c =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        assistant.Content += c;
                        BindBack(assistant);
                    });
                    return Task.CompletedTask;
                },
                onReasoning: r =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        assistant.ReasoningContent += r;
                        BindBack(assistant);
                    });
                    return Task.CompletedTask;
                },
                _cts.Token);

            // 把辅助调用审计合并到 assistant 消息上（便于 UI 展示）
            foreach (var t in prepared.AuxiliaryTrace) assistant.AuxiliaryTrace.Add(t);
            BindBack(assistant);
        }
        catch (OperationCanceledException)
        {
            assistant.Content += "\n[已取消]";
        }
        catch (Exception ex)
        {
            assistant.Content += $"\n[错误: {ex.Message}]";
        }
        finally
        {
            SendBtn.IsEnabled = true;
            CancelBtn.IsEnabled = false;
        }
    }

    private void BindBack(Message m)
    {
        var idx = Messages.IndexOf(m);
        if (idx >= 0)
        {
            Messages.RemoveAt(idx);
            Messages.Insert(idx, m);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
    }

    // ─── 图片附加：拖放 + 粘贴 + 文件选择 ───────────────────
    private void Input_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void Input_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var it in items)
        {
            if (it is StorageFile f) await AddImageAsync(f);
        }
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Current));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await AddImageAsync(file);
    }

    private async Task AddImageAsync(StorageFile file)
    {
        using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        var b64 = Convert.ToBase64String(ms.ToArray());
        _pendingImages.Add(b64);
        ImgCount.Text = $"已附加 {_pendingImages.Count} 张图片";
    }
}
