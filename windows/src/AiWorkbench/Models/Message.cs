using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace AiWorkbench.Models;

/// <summary>单条消息。对应 PROVIDER_SPEC.md 第 4 节。</summary>
public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = string.Empty;
    public string Role { get; set; } = "user"; // user | assistant | system
    public string Content { get; set; } = string.Empty;

    /// <summary>附图 base64（不含 data: 前缀）。</summary>
    public List<string> Images { get; set; } = new();

    /// <summary>思考链内容（reasoning_content）。</summary>
    public string? ReasoningContent { get; set; }

    /// <summary>本次调用使用的思考强度。</summary>
    public string? Effort { get; set; }

    public DateTimeOffset Ts { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>后台辅助调用审计（图片识别走辅助模型时记录）。</summary>
    public List<AuxiliaryTrace> AuxiliaryTrace { get; set; } = new();

    // ─── XAML 便利属性（返回 Visibility 便于 x:Bind）──────────
    public Visibility HasReasoning => string.IsNullOrEmpty(ReasoningContent) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HasAuxTrace => AuxiliaryTrace.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
