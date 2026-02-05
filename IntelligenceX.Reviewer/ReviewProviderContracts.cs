using System;
using System.Collections.Generic;

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
        new[] { "codex" },
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

    private static readonly IReadOnlyDictionary<ReviewProvider, ReviewProviderContract> ByProvider =
        new Dictionary<ReviewProvider, ReviewProviderContract> {
            [ReviewProvider.OpenAI] = OpenAi,
            [ReviewProvider.Copilot] = Copilot
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
