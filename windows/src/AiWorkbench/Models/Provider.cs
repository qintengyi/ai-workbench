using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace AiWorkbench.Models;

/// <summary>
/// AI Provider 配置。对应 PROVIDER_SPEC.md 第 1 节 models.json 格式。
/// </summary>
public class Provider
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("vendor")]
    public string Vendor { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("maxInputTokens")]
    public int MaxInputTokens { get; set; } = 128_000;

    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; set; } = 8192;

    /// <summary>工具调用支持（function calling）。</summary>
    [JsonPropertyName("supportsToolCall")]
    public bool SupportsToolCall { get; set; }

    /// <summary>图片输入支持。决定第 10 条主辅切换。</summary>
    [JsonPropertyName("supportsImages")]
    public bool SupportsImages { get; set; }

    /// <summary>仅思考模式（reasoning-only）。</summary>
    [JsonPropertyName("supportsReasoning")]
    public bool SupportsReasoning { get; set; }

    /// <summary>使用自定义协议（兼容旧字段；实际协议由 ApiFormat 决定）。</summary>
    [JsonPropertyName("useCustomProtocol")]
    public bool UseCustomProtocol { get; set; }

    /// <summary>
    /// 接口协议格式：openai（默认 /v1/chat/completions）或 anthropic（/v1/messages + x-api-key + thinking.budget_tokens）。
    /// 对齐 ai_engine.py 的 api_format 字段。PROVIDER_SPEC 第 9 条要求双格式。
    /// </summary>
    [JsonPropertyName("api_format")]
    public string ApiFormat { get; set; } = "openai";

    /// <summary>是否允许关闭思考。</summary>
    [JsonPropertyName("allowDisableReasoning")]
    public bool AllowDisableReasoning { get; set; } = true;

    /// <summary>思考模式（如 openai/o1-style）。</summary>
    [JsonPropertyName("reasoningMode")]
    public string ReasoningMode { get; set; } = "default";

    /// <summary>主辅标记：true 表示此 provider 是某主模型的视觉辅助。</summary>
    [JsonPropertyName("isAuxiliary")]
    public bool IsAuxiliary { get; set; }

    /// <summary>辅助模型所服务的主模型 id；非辅助时为 null。</summary>
    [JsonPropertyName("auxiliaryFor")]
    public string? AuxiliaryFor { get; set; }

    [JsonPropertyName("reasoning")]
    public ReasoningConfig Reasoning { get; set; } = new();

    public class ReasoningConfig
    {
        [JsonPropertyName("supportedEfforts")]
        public List<string> SupportedEfforts { get; set; } = new() { "high", "medium", "low" };

        [JsonPropertyName("defaultEffort")]
        public string DefaultEffort { get; set; } = "medium";
    }

    public static JsonSerializerOptions JsonOpts => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,
    };
}
