using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AiWorkbench.Models;

namespace AiWorkbench.Services;

/// <summary>
/// OpenAI 兼容 AI 调用客户端。遵循 PROVIDER_SPEC.md 第 2 节发包特征。
/// UA: CodeBuddy-Code/5.3.5, stream SSE, reasoning_effort。
/// </summary>
public sealed class AiClient : IDisposable
{
    public const string UserAgent = "CodeBuddy-Code/5.3.5";

    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        MaxResponseContentBufferSize = int.MaxValue,
    })
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    public void Dispose() { /* 共享静态客户端，无资源释放 */ }

    /// <summary>
    /// 流式调用。逐 chunk 回调 content / reasoning。
    /// </summary>
    public async Task StreamChatAsync(
        Provider provider,
        string modelId,
        IReadOnlyList<Message> history,
        string? effort,
        Func<string, Task> onContent,
        Func<string, Task> onReasoning,
        CancellationToken ct = default)
    {
        var url = provider.Url.TrimEnd('/') + "/chat/completions";

        var body = BuildRequestBody(modelId, history, effort, stream: true);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        req.Headers.Accept.ParseAdd("text/event-stream");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data:")) continue;
            var payload = line.Substring(5).Trim();
            if (payload == "[DONE]") return;

            JsonNode? node;
            try { node = JsonNode.Parse(payload); }
            catch { continue; }
            if (node is null) continue;

            var delta = node["choices"]?[0]?["delta"];
            if (delta is null) continue;

            var content = delta["content"]?.ToString();
            if (!string.IsNullOrEmpty(content))
                await onContent(content).ConfigureAwait(false);

            var reasoning = delta["reasoning_content"]?.ToString()
                            ?? delta["reasoning"]?.ToString();
            if (!string.IsNullOrEmpty(reasoning))
                await onReasoning(reasoning).ConfigureAwait(false);
        }
    }

    /// <summary>非流式调用（辅助模型图片识别用，结果一次性返回）。</summary>
    public async Task<string> CompleteAsync(
        Provider provider,
        string modelId,
        IReadOnlyList<Message> history,
        string? effort = null,
        CancellationToken ct = default)
    {
        var url = provider.Url.TrimEnd('/') + "/chat/completions";
        var body = BuildRequestBody(modelId, history, effort, stream: false);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var node = JsonNode.Parse(json);
        return node?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
    }

    private static string BuildRequestBody(
        string modelId,
        IReadOnlyList<Message> history,
        string? effort,
        bool stream)
    {
        var arr = new JsonArray();
        foreach (var m in history)
        {
            var msg = new JsonObject
            {
                ["role"] = m.Role,
            };

            if (m.Images.Count > 0)
            {
                var contentArr = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = m.Content ?? string.Empty,
                    },
                };
                foreach (var img in m.Images)
                {
                    contentArr.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = $"data:image/png;base64,{img}",
                        },
                    });
                }
                msg["content"] = contentArr;
            }
            else
            {
                msg["content"] = m.Content ?? string.Empty;
            }

            arr.Add(msg);
        }

        var root = new JsonObject
        {
            ["model"] = modelId,
            ["messages"] = arr,
            ["stream"] = stream,
        };

        if (!string.IsNullOrEmpty(effort) && effort != "off")
            root["reasoning_effort"] = effort;

        if (stream)
        {
            root["stream_options"] = new JsonObject { ["include_usage"] = false };
        }

        return root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
