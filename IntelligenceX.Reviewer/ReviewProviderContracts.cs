using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewProviderContract {
    public ReviewProviderContract(ReviewProvider provider, string id, string displayName, IReadOnlyList<string> aliases,
        IReadOnlyList<string> supportedTransports, bool supportsUsageApi, bool supportsReasoningControls, bool supportsStreaming,
        bool requiresOpenAiAuthStore, int maxRecommendedRetryCount) {
        Provider = provider;
        Id = id;
        DisplayName = displayName;
        Aliases = aliases;
        SupportedTransports = supportedTransports;
        SupportsUsageApi = supportsUsageApi;
        SupportsReasoningControls = supportsReasoningControls;
        SupportsStreaming = supportsStreaming;
        RequiresOpenAiAuthStore = requiresOpenAiAuthStore;
        MaxRecommendedRetryCount = maxRecommendedRetryCount;
    }

    public ReviewProvider Provider { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> Aliases { get; }
    public IReadOnlyList<string> SupportedTransports { get; }
    public bool SupportsUsageApi { get; }
    public bool SupportsReasoningControls { get; }
    public bool SupportsStreaming { get; }
    public bool RequiresOpenAiAuthStore { get; }
    public int MaxRecommendedRetryCount { get; }
}

internal static class ReviewProviderContracts {
    private static readonly ReviewProviderContract OpenAi = new(
        ReviewProvider.OpenAI,
        "openai",
        "OpenAI",
        new[] { "codex", "chatgpt", "openai-codex" },
        new[] { "appserver", "native" },
        supportsUsageApi: true,
        supportsReasoningControls: true,
        supportsStreaming: true,
        requiresOpenAiAuthStore: true,
        maxRecommendedRetryCount: 5);

    private static readonly ReviewProviderContract Copilot = new(
        ReviewProvider.Copilot,
        "copilot",
        "Copilot",
        new[] { "copilot" },
        new[] { "cli", "direct" },
        supportsUsageApi: false,
        supportsReasoningControls: false,
        supportsStreaming: true,
        requiresOpenAiAuthStore: false,
        maxRecommendedRetryCount: 3);

    private static readonly ReviewProviderContract OpenAiCompatible = new(
        ReviewProvider.OpenAICompatible,
        "openai-compatible",
        "OpenAI Compatible",
        new[] { "openai-api", "ollama", "openrouter" },
        new[] { "http" },
        supportsUsageApi: false,
        supportsReasoningControls: false,
        supportsStreaming: false,
        requiresOpenAiAuthStore: false,
        maxRecommendedRetryCount: 3);

    private static readonly IReadOnlyDictionary<ReviewProvider, ReviewProviderContract> ByProvider =
        new Dictionary<ReviewProvider, ReviewProviderContract> {
            [ReviewProvider.OpenAI] = OpenAi,
            [ReviewProvider.Copilot] = Copilot,
            [ReviewProvider.OpenAICompatible] = OpenAiCompatible
        };

    private static readonly IReadOnlyDictionary<string, ReviewProvider> ByAlias = BuildAliasMap();

    public static ReviewProviderContract Get(ReviewProvider provider) {
        return ByProvider.TryGetValue(provider, out var contract)
            ? contract
            : throw new NotSupportedException($"Unsupported review provider '{provider}'.");
    }

    public static bool TryParseProviderAlias(string? value, out ReviewProvider provider) {
        provider = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        return ByAlias.TryGetValue(value.Trim(), out provider);
    }

    public static ReviewProvider ParseProviderOrDefault(string? value, ReviewProvider fallback) {
        return TryParseProviderAlias(value, out var provider) ? provider : fallback;
    }

    public static ReviewProvider ParseProviderOrThrow(string? value, string settingName) {
        if (TryParseProviderAlias(value, out var provider)) {
            return provider;
        }
        var configuredValue = string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
        throw new InvalidOperationException(
            $"Invalid {settingName} '{configuredValue}'. Supported values: {DescribeSupportedProviders()}.");
    }

    private static string DescribeSupportedProviders() {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in ByProvider.Values) {
            values.Add(contract.Id);
            foreach (var alias in contract.Aliases) {
                if (!string.IsNullOrWhiteSpace(alias)) {
                    values.Add(alias.Trim());
                }
            }
        }
        return string.Join(", ", values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, ReviewProvider> BuildAliasMap() {
        var map = new Dictionary<string, ReviewProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in ByProvider.Values) {
            map.TryAdd(contract.Id, contract.Provider);
            foreach (var alias in contract.Aliases) {
                if (!string.IsNullOrWhiteSpace(alias)) {
                    map.TryAdd(alias, contract.Provider);
                }
            }
        }
        return map;
    }
}
