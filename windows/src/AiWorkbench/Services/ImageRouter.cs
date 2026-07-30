using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AiWorkbench.Models;

namespace AiWorkbench.Services;

/// <summary>
/// 第 10 条核心：主辅模型图片自动切换。
/// 用户发图 + 主模型 supportsImages=false → 切辅助模型识别 → 注入主模型 → 切回。
/// 对用户透明。
/// </summary>
public sealed class ImageRouter
{
    private readonly AiClient _ai;
    private readonly ProviderStore _store;

    public ImageRouter(AiClient ai, ProviderStore store)
    {
        _ai = ai;
        _store = store;
    }

    /// <summary>
    /// 处理用户消息：若包含图片且主模型不支持图片，调辅助模型识别，
    /// 返回**新的** prepared message（含识别文字注入），并记录 AuxiliaryTrace。
    /// 原 userMsg 不被修改，UI 仍展示用户原始输入（第 10 条对用户透明）。
    /// 主模型支持图片时直接返回 userMsg 副本。
    /// </summary>
    public async Task<Message> PrepareForPrimaryAsync(
        Provider primary,
        Message userMsg,
        CancellationToken ct = default)
    {
        // 副本，避免污染 UI 展示用的原消息
        var prepared = new Message
        {
            Role = userMsg.Role,
            ConversationId = userMsg.ConversationId,
            Content = userMsg.Content,
        };
        foreach (var img in userMsg.Images) prepared.Images.Add(img);

        if (prepared.Images.Count == 0) return prepared;
        if (primary.SupportsImages) return prepared;

        var aux = await _store.FindAuxiliaryForAsync(primary.Id).ConfigureAwait(false);
        if (aux is null)
        {
            prepared.AuxiliaryTrace.Add(new AuxiliaryTrace
            {
                ProviderId = "(none)",
                Result = "[图片识别失败：未配置辅助视觉模型]",
            });
            prepared.Content += "\n\n[用户发送了图片，但当前未配置可识别图片的辅助模型。请在设置中添加 supportsImages=true 的 provider 并标记为辅助。]";
            prepared.Images.Clear();
            return prepared;
        }

        // 调辅助模型识别图片
        var auxMessages = new List<Message>
        {
            new()
            {
                Role = "system",
                Content = "你是图片识别助手。请用中文准确、详细描述用户发送的图片内容，便于另一文本模型理解。",
            },
            new()
            {
                Role = "user",
                // 用户只发图无文字时给默认提示词，避免空 content
                Content = string.IsNullOrWhiteSpace(prepared.Content) ? "请描述这张图片。" : prepared.Content,
                Images = new List<string>(prepared.Images),
            },
        };

        string description;
        try
        {
            description = await _ai.CompleteAsync(aux, aux.Id, auxMessages, null, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 辅助模型调用失败：降级为提示文字，不抛异常上抛（避免阻断主对话）
            prepared.AuxiliaryTrace.Add(new AuxiliaryTrace
            {
                ProviderId = aux.Id,
                Result = $"[图片识别失败：{ex.Message}]",
            });
            prepared.Content += $"\n\n[用户发送了图片，但辅助模型 {aux.Name} 识别失败：{ex.Message}]";
            prepared.Images.Clear();
            return prepared;
        }

        prepared.AuxiliaryTrace.Add(new AuxiliaryTrace
        {
            ProviderId = aux.Id,
            Result = description,
        });

        // 注入主模型：把图片描述作为用户文字，移除原始图片
        // 保留原 Content（用户原话），附加图片描述
        prepared.Content = string.IsNullOrWhiteSpace(prepared.Content)
            ? $"[用户发送了图片，辅助模型 {aux.Name} 已识别]\n图片描述：{description}"
            : $"[用户发送了图片，辅助模型 {aux.Name} 已识别]\n图片描述：{description}\n\n用户原话：{prepared.Content}";
        prepared.Images.Clear();
        return prepared;
    }
}
