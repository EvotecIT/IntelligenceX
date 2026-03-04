using System;
using System.IO;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed class ChatServiceToolingBootstrapCache {
    private const int PersistedSnapshotSchemaVersion = 1;
    private const string DefaultPersistedSnapshotFileName = "tooling-bootstrap-cache-v1.json";
    private static readonly JsonSerializerOptions PersistedSnapshotJson = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly object _sync = new();
    private readonly string _persistedSnapshotPath;
    private string _cacheKey = string.Empty;
    private ChatServiceToolingBootstrapSnapshot? _snapshot;
    private ChatServiceToolingBootstrapPersistedSnapshot? _persistedSnapshot;

    public ChatServiceToolingBootstrapCache(string? persistedSnapshotPath = null) {
        _persistedSnapshotPath = ResolvePersistedSnapshotPath(persistedSnapshotPath);
        _persistedSnapshot = LoadPersistedSnapshot(_persistedSnapshotPath);
    }

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

    public bool TryGetPersistedSnapshot(string cacheKey, out ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        var normalizedCacheKey = (cacheKey ?? string.Empty).Trim();
        if (normalizedCacheKey.Length == 0) {
            snapshot = null!;
            return false;
        }

        lock (_sync) {
            if (_persistedSnapshot is null
                || !string.Equals(_persistedSnapshot.CacheKey, normalizedCacheKey, StringComparison.Ordinal)) {
                snapshot = null!;
                return false;
            }

            snapshot = _persistedSnapshot;
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
            _persistedSnapshot = BuildPersistedSnapshot(normalizedCacheKey, snapshot);
            SavePersistedSnapshot(_persistedSnapshotPath, _persistedSnapshot);
        }
    }

    public void Clear() {
        lock (_sync) {
            _cacheKey = string.Empty;
            _snapshot = null;
            _persistedSnapshot = null;
            TryDeletePersistedSnapshot(_persistedSnapshotPath);
        }
    }

    private static ChatServiceToolingBootstrapPersistedSnapshot BuildPersistedSnapshot(
        string cacheKey,
        ChatServiceToolingBootstrapSnapshot snapshot) {
        return new ChatServiceToolingBootstrapPersistedSnapshot {
            SchemaVersion = PersistedSnapshotSchemaVersion,
            CacheKey = cacheKey,
            CachedAtUtc = DateTime.UtcNow,
            ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
            PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            StartupWarnings = snapshot.StartupWarnings ?? Array.Empty<string>(),
            StartupBootstrap = snapshot.StartupBootstrap ?? new SessionStartupBootstrapTelemetryDto(),
            PluginSearchPaths = snapshot.PluginSearchPaths ?? Array.Empty<string>(),
            RuntimePolicyDiagnostics = snapshot.RuntimePolicyDiagnostics,
            RoutingCatalogDiagnostics = snapshot.RoutingCatalogDiagnostics
        };
    }

    private static string ResolvePersistedSnapshotPath(string? path) {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.Length > 0) {
            try {
                return Path.GetFullPath(normalized);
            } catch {
                return normalized;
            }
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", DefaultPersistedSnapshotFileName);
    }

    private static ChatServiceToolingBootstrapPersistedSnapshot? LoadPersistedSnapshot(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return null;
            }

            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<ChatServiceToolingBootstrapPersistedSnapshot>(json, PersistedSnapshotJson);
            if (snapshot is null || snapshot.SchemaVersion != PersistedSnapshotSchemaVersion) {
                return null;
            }

            var normalizedCacheKey = (snapshot.CacheKey ?? string.Empty).Trim();
            if (normalizedCacheKey.Length == 0) {
                return null;
            }
            if (snapshot.RuntimePolicyDiagnostics is null || snapshot.RoutingCatalogDiagnostics is null) {
                return null;
            }

            return snapshot with {
                CacheKey = normalizedCacheKey,
                ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
                PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
                StartupWarnings = snapshot.StartupWarnings ?? Array.Empty<string>(),
                StartupBootstrap = snapshot.StartupBootstrap ?? new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = snapshot.PluginSearchPaths ?? Array.Empty<string>(),
                RuntimePolicyDiagnostics = snapshot.RuntimePolicyDiagnostics,
                RoutingCatalogDiagnostics = snapshot.RoutingCatalogDiagnostics
            };
        } catch {
            return null;
        }
    }

    private static void SavePersistedSnapshot(string path, ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        try {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(snapshot, PersistedSnapshotJson);
            File.WriteAllText(tempPath, json);
            if (File.Exists(path)) {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        } catch {
            // Persisted startup metadata cache is best-effort.
        }
    }

    private static void TryDeletePersistedSnapshot(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return;
            }

            File.Delete(path);
        } catch {
            // Best-effort cleanup.
        }
    }
}

internal sealed record ChatServiceToolingBootstrapSnapshot {
    public required ToolRegistry Registry { get; init; }
    public required ToolDefinitionDto[] ToolDefinitions { get; init; }
    public required IToolPack[] Packs { get; init; }
    public required ToolPackAvailabilityInfo[] PackAvailability { get; init; }
    public required string[] StartupWarnings { get; init; }
    public required SessionStartupBootstrapTelemetryDto StartupBootstrap { get; init; }
    public required string[] PluginSearchPaths { get; init; }
    public required ToolRuntimePolicyDiagnostics RuntimePolicyDiagnostics { get; init; }
    public required ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics { get; init; }
    public required ToolOrchestrationCatalog ToolOrchestrationCatalog { get; init; }
}

internal sealed record ChatServiceToolingBootstrapPersistedSnapshot {
    public int SchemaVersion { get; init; }
    public required string CacheKey { get; init; }
    public DateTime CachedAtUtc { get; init; }
    public required ToolDefinitionDto[] ToolDefinitions { get; init; }
    public required ToolPackAvailabilityInfo[] PackAvailability { get; init; }
    public required string[] StartupWarnings { get; init; }
    public required SessionStartupBootstrapTelemetryDto StartupBootstrap { get; init; }
    public required string[] PluginSearchPaths { get; init; }
    public required ToolRuntimePolicyDiagnostics RuntimePolicyDiagnostics { get; init; }
    public required ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics { get; init; }
}
