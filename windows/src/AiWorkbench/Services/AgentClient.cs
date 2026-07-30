using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AiWorkbench.Models;

namespace AiWorkbench.Services;

/// <summary>
/// 被控端 WS 客户端：连 Server /ws/agent?token=AGENT_TOKEN，接收 iOS 指令执行。
/// 对应 ARCHITECTURE.md Windows 端职责 3。
/// 消息格式：{type, data, ts}，统一响应 {code, msg, data}。
/// 文件树结果回 {type:"command_result", request_id, data:{tree}}（让 Future 正确 resolve）。
/// </summary>
public sealed class AgentClient
{
    private readonly ProviderStore _providers;
    private readonly FileWorkspace _files;
    private readonly AiClient _ai;
    private readonly ImageRouter _router;

    private CancellationTokenSource? _loopCts;
    private ClientWebSocket? _ws;

    public string ServerUrl { get; set; } = "ws://127.0.0.1:10370/ws/agent";
    public string AgentToken { get; set; } = string.Empty;
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event EventHandler<string>? OnLog;
    public event EventHandler<JsonNode>? OnCommand;

    public AgentClient(ProviderStore providers, FileWorkspace files, AiClient ai, ImageRouter router)
    {
        _providers = providers;
        _files = files;
        _ai = ai;
        _router = router;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (!_loopCts.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(_loopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                OnLog?.Invoke(this, $"[agent] 连接异常: {ex.Message}");
            }
            try { await Task.Delay(3000, _loopCts.Token).ConfigureAwait(false); }
            catch { break; }
        }
    }

    public void Stop()
    {
        try { _loopCts?.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }
    }

    private async Task ConnectAndServeAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(AgentToken))
        {
            OnLog?.Invoke(this, "[agent] 未配置 AgentToken，跳过连接");
            return;
        }

        using var ws = new ClientWebSocket();
        _ws = ws;
        var uri = new UriBuilder(ServerUrl)
        {
            Query = $"token={Uri.EscapeDataString(AgentToken)}",
        }.Uri;
        await ws.ConnectAsync(uri, ct).ConfigureAwait(false);

        OnLog?.Invoke(this, "[agent] 已连接服务端");

        // 接收循环
        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult r;
            try
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            }
            catch { break; }

            if (r.MessageType == WebSocketMessageType.Close)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct).ConfigureAwait(false); } catch { }
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            if (!r.EndOfMessage) continue;

            var raw = sb.ToString();
            sb.Clear();
            // 后台处理，避免阻塞接收循环；用 ws 引用判断状态
            _ = HandleAndReplyAsync(ws, raw, ct);
        }
    }

    private async Task HandleAndReplyAsync(ClientWebSocket ws, string raw, CancellationToken ct)
    {
        try
        {
            var resp = await HandleAsync(raw).ConfigureAwait(false);
            if (resp is null) return;
            var respText = resp.ToJsonString();
            if (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(respText)),
                    WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke(this, $"[agent] 回复失败: {ex.Message}");
        }
    }

    private async Task<JsonObject?> HandleAsync(string raw)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(raw); }
        catch { return WrapResult(400, "invalid json", null, null, null); }
        if (msg is null) return null;

        var type = msg["type"]?.ToString() ?? "";
        var requestId = msg["request_id"]?.ToString();
        var data = msg["data"] as JsonObject ?? new JsonObject();

        try
        {
            object? resultData = type switch
            {
                "ping" => new { pong = true },
                "list_providers" => new { providers = await _providers.LoadAsync() },
                "list_files" => new { tree = _files.List(data["dir"]?.ToString()) },
                "read_file" => new { content = _files.ReadText(data["path"]?.ToString() ?? "") },
                "send_message" => await HandleSendMessageAsync(data),
                _ => new { error = $"unknown command: {type}" },
            };
            return WrapResult(0, "ok", resultData, requestId, type);
        }
        catch (Exception ex)
        {
            OnLog?.Invoke(this, $"[agent] 处理 {type} 失败: {ex.Message}");
            return WrapResult(500, ex.Message, null, requestId, type);
        }
    }

    /// <summary>iOS 远程发起消息：执行 AI 调用（含主辅切换），流式聚合为最终文本返回。</summary>
    private async Task<object> HandleSendMessageAsync(JsonObject data)
    {
        var providerId = data["providerId"]?.ToString() ?? "";
        var modelId = data["modelId"]?.ToString() ?? "";
        var content = data["content"]?.ToString() ?? "";
        var effort = data["effort"]?.ToString();
        // 远程 iOS 端可带 images base64 数组
        var images = new List<string>();
        if (data["images"] is JsonArray imgArr)
        {
            foreach (var img in imgArr)
            {
                var s = img?.ToString();
                if (!string.IsNullOrEmpty(s)) images.Add(s);
            }
        }

        var providers = await _providers.LoadAsync();
        var provider = providers.Find(p => p.Id == providerId)
            ?? throw new InvalidOperationException($"provider not found: {providerId}");

        var userMsg = new Message
        {
            Role = "user",
            Content = content,
        };
        foreach (var img in images) userMsg.Images.Add(img);

        // 第 10 条主辅切换（透明，不修改原 userMsg 语义）
        var prepared = await _router.PrepareForPrimaryAsync(provider, userMsg).ConfigureAwait(false);

        var history = new List<Message>
        {
            new() { Role = "system", Content = "你是 AI Workbench 助手。" },
            prepared,
        };

        var sb = new StringBuilder();
        var reasoning = new StringBuilder();
        await _ai.StreamChatAsync(provider, modelId, history, effort,
            onContent: c => { sb.Append(c); return Task.CompletedTask; },
            onReasoning: r => { reasoning.Append(r); return Task.CompletedTask; }
        ).ConfigureAwait(false);

        return new
        {
            content = sb.ToString(),
            reasoning = reasoning.ToString(),
            auxiliaryTrace = prepared.AuxiliaryTrace,
        };
    }

    private static JsonObject WrapResult(int code, string msg, object? data, string? requestId, string? type)
    {
        var obj = new JsonObject
        {
            ["type"] = "command_result",
            ["code"] = code,
            ["msg"] = msg,
        };
        if (data is not null)
        {
            obj["data"] = JsonSerializer.SerializeToNode(data, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }
        if (requestId is not null) obj["request_id"] = requestId;
        return obj;
    }
}
