using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AiWorkbench.Models;

namespace AiWorkbench.Services;

/// <summary>
/// Provider 配置 CRUD（本地 providers.json）。
/// 对应 PROVIDER_SPEC.md 第 1 节。
/// </summary>
public sealed class ProviderStore
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiWorkbench");

    private static string ConfigPath => Path.Combine(AppDir, "providers.json");

    public async Task<List<Provider>> LoadAsync()
    {
        Directory.CreateDirectory(AppDir);
        if (!File.Exists(ConfigPath))
        {
            var seed = SeedProviders();
            await SaveAsync(seed).ConfigureAwait(false);
            return seed;
        }
        try
        {
            var text = await File.ReadAllTextAsync(ConfigPath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<Provider>>(text, Provider.JsonOpts) ?? new();
        }
        catch
        {
            return SeedProviders();
        }
    }

    public async Task SaveAsync(List<Provider> providers)
    {
        Directory.CreateDirectory(AppDir);
        var text = JsonSerializer.Serialize(providers, Provider.JsonOpts);
        await File.WriteAllTextAsync(ConfigPath, text).ConfigureAwait(false);
    }

    public async Task UpsertAsync(Provider p)
    {
        var list = await LoadAsync().ConfigureAwait(false);
        var idx = list.FindIndex(x => x.Id == p.Id);
        if (idx >= 0) list[idx] = p;
        else list.Add(p);
        await SaveAsync(list).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id)
    {
        var list = await LoadAsync().ConfigureAwait(false);
        list.RemoveAll(x => x.Id == id);
        await SaveAsync(list).ConfigureAwait(false);
    }

    /// <summary>为主模型查找辅助 provider（第 10 条）。
    /// 优先 isAuxiliary + auxiliaryFor 匹配；否则取任意 supportsImages=true。</summary>
    public async Task<Provider?> FindAuxiliaryForAsync(string primaryId)
    {
        var list = await LoadAsync().ConfigureAwait(false);
        return list.FirstOrDefault(p => p.IsAuxiliary && p.AuxiliaryFor == primaryId)
            ?? list.FirstOrDefault(p => p.SupportsImages && p.Id != primaryId);
    }

    private static List<Provider> SeedProviders() => new()
    {
        new Provider
        {
            Id = "GLM-5.2",
            Name = "GLM-5.2",
            Vendor = "Buddy",
            ApiKey = "sk-xxxx",
            Url = "https://api.xiaoyyua.top/v1",
            MaxInputTokens = 1_000_000,
            MaxOutputTokens = 8192,
            SupportsToolCall = true,
            SupportsImages = true,
            SupportsReasoning = true,
            UseCustomProtocol = false,
            Reasoning = new Provider.ReasoningConfig
            {
                SupportedEfforts = new() { "xhigh", "high", "medium", "low" },
                DefaultEffort = "medium",
            },
            IsAuxiliary = false,
            AuxiliaryFor = null,
        },
    };
}
