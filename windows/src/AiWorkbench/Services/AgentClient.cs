using System;
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
/// </summary>
public sealed class AgentClient
{
    private readonly ProviderStore _providers;
    private readonly FileWorkspace _files;
    private readonly AiClient _ai;
    private readonly ImageRouter _router;

    private CancellationTokenSource _cts = new();
    private ClientWebSocket? _ws;

    public string ServerUrl { get; set; } = "ws://127.0.0.1:10370/ws/agent";
    public string AgentToken { get; set; } = string.Empty;
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public event EventHandler<string>? OnLog;
    public event EventHandler<JsonNode>? OnCommand; // 上层可订阅（如刷新 UI 会话列表）

    public AgentClient(ProviderStore providers, FileWorkspace files, AiClient ai, ImageRouter router)
    {
        _providers = providers;
        _files = files;
        _ai = ai;
        _router = router;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await ConnectAndServeAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                OnLog?.Invoke(this, $"[agent] 连接异常: {ex.Message}");
            }
            try { await Task.Delay(3000, _cts.Token).ConfigureAwait(false); }
            catch { break; }
        }
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }
    }

    private async Task ConnectAndServeAsync(CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        _ws = ws;
        if (!string.IsNullOrEmpty(AgentToken))
        {
            var uri = new UriBuilder(ServerUrl)
            {
                Query = $"token={Uri.EscapeDataString(AgentToken)}",
            }.Uri;
            await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
        }
        else
        {
            OnLog?.Invoke(this, "[agent] 未配置 AgentToken，跳过连接");
            return;
        }

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
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct).ConfigureAwait(false);
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            if (!r.EndOfMessage) continue;

            var raw = sb.ToString();
            sb.Clear();
            _ = Task.Run(async () =>
            {
                var resp = await HandleAsync(raw).ConfigureAwait(false);
                if (resp is null) return;
                var respText = resp.ToJsonString();
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(respText)),
                        WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
            }, ct);
        }
    }

    private async Task<JsonObject?> HandleAsync(string raw)
    {
        JsonNode? msg;
        try { msg = JsonNode.Parse(raw); }
        catch { return WrapResult(400, "invalid json", null, null); }
        if (msg is null) return null;

        var type = msg["type"]?.ToString() ?? "";
        var requestId = msg["request_id"]?.ToString();
        var data = msg["data"] as JsonObject ?? new JsonObject();

        try
        {
            object? resultData = type switch
            {
                "ping" => new { pong = true },
                "list_providers" => await _providers.LoadAsync(),
                "list_files" => _files.List(data["dir"]?.ToString()),
                "read_file" => _files.ReadText(data["path"]?.ToString() ?? ""),
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

        var providers = await _providers.LoadAsync();
        var provider = providers.Find(p => p.Id == providerId)
            ?? throw new InvalidOperationException($"provider not found: {providerId}");

        var userMsg = new Message
        {
            Role = "user",
            Content = content,
        };

        // 第 10 条主辅切换
        userMsg = await _router.PrepareForPrimaryAsync(provider, userMsg);

        var history = new List<Message>
        {
            new() { Role = "system", Content = "你是 AI Workbench 助手。" },
            userMsg,
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
            auxiliaryTrace = userMsg.AuxiliaryTrace,
        };
    }

    private static JsonObject WrapResult(int code, string msg, object? data, string? requestId, string? type = null)
    {
        var obj = new JsonObject
        {
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
        if (type is not null) obj["type"] = "command_result";
        return obj;
    }
}
