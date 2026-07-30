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
/// AI 调用客户端。遵循 PROVIDER_SPEC.md 第 2 节发包特征 + ai_engine.py 验证特征。
/// UA: codebuddy/2.115.0（ai_engine.py 实测更稳，覆盖 SPEC 的 CodeBuddy-Code/5.3.5）。
/// 双格式：openai（/chat/completions + Bearer + reasoning_effort）+ anthropic（/v1/messages + x-api-key + thinking.budget_tokens）。
/// </summary>
public sealed class AiClient : IDisposable
{
    /// <summary>ai_engine.py 验证 UA，比 SPEC 的 CodeBuddy-Code/5.3.5 更稳。</summary>
    public const string UserAgent = "codebuddy/2.115.0";

    private static readonly HttpClient _http = new HttpClient(new HttpClientHandler())
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    public void Dispose() { /* 共享静态客户端，无资源释放 */ }

    private static bool IsAnthropic(Provider p) => string.Equals(p.ApiFormat, "anthropic", StringComparison.OrdinalIgnoreCase);

    /// <summary>流式调用。逐 chunk 回调 content / reasoning。</summary>
    public async Task StreamChatAsync(
        Provider provider,
        string modelId,
        IReadOnlyList<Message> history,
        string? effort,
        Func<string, Task> onContent,
        Func<string, Task> onReasoning,
        CancellationToken ct = default)
    {
        var (url, body) = BuildRequest(provider, modelId, history, effort, stream: true);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req, provider);
        if (IsAnthropic(provider)) req.Headers.Accept.ParseAdd("text/event-stream");
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

            if (IsAnthropic(provider))
            {
                // anthropic SSE: event: content_block_delta / message_delta
                var type = node["type"]?.ToString();
                if (type == "content_block_delta")
                {
                    var delta = node["delta"];
                    var dType = delta?["type"]?.ToString();
                    if (dType == "text_delta")
                    {
                        var text = delta?["text"]?.ToString();
                        if (!string.IsNullOrEmpty(text)) await onContent(text).ConfigureAwait(false);
                    }
                    else if (dType == "thinking_delta")
                    {
                        var think = delta?["thinking"]?.ToString();
                        if (!string.IsNullOrEmpty(think)) await onReasoning(think).ConfigureAwait(false);
                    }
                }
            }
            else
            {
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
    }

    /// <summary>非流式调用（辅助模型图片识别用，结果一次性返回）。</summary>
    public async Task<string> CompleteAsync(
        Provider provider,
        string modelId,
        IReadOnlyList<Message> history,
        string? effort = null,
        CancellationToken ct = default)
    {
        var (url, body) = BuildRequest(provider, modelId, history, effort, stream: false);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyHeaders(req, provider);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var node = JsonNode.Parse(json);
        if (node is null) return string.Empty;

        if (IsAnthropic(provider))
        {
            // anthropic 非流式：content 数组，type=text 的 text 字段拼接
            var contentArr = node["content"] as JsonArray;
            if (contentArr is null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var block in contentArr)
            {
                if (block?["type"]?.ToString() == "text")
                    sb.Append(block["text"]?.ToString());
            }
            return sb.ToString();
        }

        return node["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
    }

    private static void ApplyHeaders(HttpRequestMessage req, Provider provider)
    {
        req.Headers.UserAgent.ParseAdd(UserAgent);
        if (IsAnthropic(provider))
        {
            req.Headers.TryAddWithoutValidation("x-api-key", provider.ApiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }
    }

    private static (string url, string body) BuildRequest(
        Provider provider,
        string modelId,
        IReadOnlyList<Message> history,
        string? effort,
        bool stream)
    {
        var anthropic = IsAnthropic(provider);
        var baseUrl = provider.Url.TrimEnd('/');
        string url;
        JsonObject root;

        if (anthropic)
        {
            url = baseUrl + "/v1/messages";
            // anthropic: system 单独字段，messages 不含 system
            var systemSb = new StringBuilder();
            var userMsgs = new JsonArray();
            foreach (var m in history)
            {
                if (m.Role == "system") { systemSb.AppendLine(m.Content); continue; }
                userMsgs.Add(BuildAnthropicMessage(m));
            }
            root = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = userMsgs,
                ["stream"] = stream,
                ["max_tokens"] = provider.MaxOutputTokens > 0 ? provider.MaxOutputTokens : 8192,
            };
            if (systemSb.Length > 0) root["system"] = systemSb.ToString().TrimEnd();

            // anthropic 思考：thinking.budget_tokens（仅当 effort 非 null/off）
            if (!string.IsNullOrEmpty(effort) && effort != "off")
            {
                // budget_tokens 映射：low=2048 / medium=8192 / high=16384 / xhigh=24576
                var budget = effort switch
                {
                    "low" => 2048,
                    "medium" => 8192,
                    "high" => 16384,
                    "xhigh" => 24576,
                    _ => 8192,
                };
                root["thinking"] = new JsonObject { ["type"] = "enabled", ["budget_tokens"] = budget };
            }
        }
        else
        {
            url = baseUrl + "/chat/completions";
            var arr = new JsonArray();
            foreach (var m in history) arr.Add(BuildOpenAiMessage(m));

            root = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = arr,
                ["stream"] = stream,
                ["max_tokens"] = provider.MaxOutputTokens > 0 ? provider.MaxOutputTokens : 8192,
            };

            if (!string.IsNullOrEmpty(effort) && effort != "off")
                root["reasoning_effort"] = effort;

            if (stream) root["stream_options"] = new JsonObject { ["include_usage"] = false };
        }

        var body = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        return (url, body);
    }

    private static JsonObject BuildOpenAiMessage(Message m)
    {
        var msg = new JsonObject { ["role"] = m.Role };
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
                    ["image_url"] = new JsonObject { ["url"] = $"data:image/png;base64,{img}" },
                });
            }
            msg["content"] = contentArr;
        }
        else
        {
            msg["content"] = m.Content ?? string.Empty;
        }
        return msg;
    }

    private static JsonObject BuildAnthropicMessage(Message m)
    {
        var msg = new JsonObject { ["role"] = m.Role };
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
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = "image/png",
                        ["data"] = img,
                    },
                });
            }
            msg["content"] = contentArr;
        }
        else
        {
            msg["content"] = m.Content ?? string.Empty;
        }
        return msg;
    }
}
