using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed class ChatServiceToolingBootstrapCache {
    private readonly object _sync = new();
    private string _cacheKey = string.Empty;
    private ChatServiceToolingBootstrapSnapshot? _snapshot;

    public bool TryGetSnapshot(string cacheKey, out ChatServiceToolingBootstrapSnapshot snapshot) {
        var normalizedCacheKey = (cacheKey ?? string.Empty).Trim();
        if (normalizedCacheKey.Length == 0) {
            snapshot = null!;
            return false;
        }

        lock (_sync) {
            if (_snapshot is null
                || !string.Equals(_cacheKey, normalizedCacheKey, StringComparison.Ordinal)) {
                snapshot = null!;
                return false;
            }

            snapshot = _snapshot;
            return true;
        }
    }

    public void StoreSnapshot(string cacheKey, ChatServiceToolingBootstrapSnapshot snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var normalizedCacheKey = (cacheKey ?? string.Empty).Trim();
        if (normalizedCacheKey.Length == 0) {
            throw new ArgumentException("Cache key cannot be empty.", nameof(cacheKey));
        }

        lock (_sync) {
            _cacheKey = normalizedCacheKey;
            _snapshot = snapshot;
        }
    }

    public void Clear() {
        lock (_sync) {
            _cacheKey = string.Empty;
            _snapshot = null;
        }
    }
}

internal sealed record ChatServiceToolingBootstrapSnapshot {
    public required ToolRegistry Registry { get; init; }
    public required IToolPack[] Packs { get; init; }
    public required ToolPackAvailabilityInfo[] PackAvailability { get; init; }
    public required string[] StartupWarnings { get; init; }
    public required SessionStartupBootstrapTelemetryDto StartupBootstrap { get; init; }
    public required string[] PluginSearchPaths { get; init; }
    public required ToolRuntimePolicyDiagnostics RuntimePolicyDiagnostics { get; init; }
    public required ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics { get; init; }
    public required ToolOrchestrationCatalog ToolOrchestrationCatalog { get; init; }
}
