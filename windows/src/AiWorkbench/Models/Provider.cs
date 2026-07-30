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

    /// <summary>使用自定义协议（false = 标准 OpenAI /v1/chat/completions）。</summary>
    [JsonPropertyName("useCustomProtocol")]
    public bool UseCustomProtocol { get; set; }

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

        /// <summary>是否允许关闭思考（与上层 AllowDisableReasoning 同义，UI 暴露）。</summary>
        [JsonPropertyName("allowDisableReasoning")]
        public bool AllowDisableReasoning { get; set; } = true;
    }

    public static JsonSerializerOptions JsonOpts => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null,
    };
}
