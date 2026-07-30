using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace AiWorkbench.Models;

/// <summary>单条消息。对应 PROVIDER_SPEC.md 第 4 节。INotifyPropertyChanged 支持 x:Bind OneWay 流式更新。</summary>
public class Message : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = string.Empty;

    private string _role = "user";
    public string Role
    {
        get => _role;
        set { if (_role != value) { _role = value; OnPropertyChanged(); } }
    }

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set { if (_content != value) { _content = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReasoning)); } }
    }

    /// <summary>附图 base64（不含 data: 前缀）。</summary>
    public List<string> Images { get; set; } = new();

    private string? _reasoningContent;
    /// <summary>思考链内容（reasoning_content）。</summary>
    public string? ReasoningContent
    {
        get => _reasoningContent;
        set { if (_reasoningContent != value) { _reasoningContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReasoning)); } }
    }

    /// <summary>本次调用使用的思考强度。</summary>
    public string? Effort { get; set; }

    public DateTimeOffset Ts { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>后台辅助调用审计（图片识别走辅助模型时记录）。</summary>
    public List<AuxiliaryTrace> AuxiliaryTrace { get; set; } = new();

    // ─── XAML 便利属性（返回 Visibility 便于 x:Bind）──────────
    public Visibility HasReasoning => string.IsNullOrEmpty(ReasoningContent) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HasAuxTrace => AuxiliaryTrace.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>外部触发辅助审计相关属性重新评估（List 内容变化不自动通知）。</summary>
    public void RaiseAuxTraceChanged()
    {
        OnPropertyChanged(nameof(HasAuxTrace));
    }
}

/// <summary>辅助调用审计条目。第 10 条主辅切换留痕。</summary>
public class AuxiliaryTrace
{
    /// <summary>辅助 provider id。</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>识别结果文字（注入主模型的内容）。</summary>
    public string Result { get; set; } = string.Empty;
    public DateTimeOffset Ts { get; set; } = DateTimeOffset.UtcNow;
}
