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
    private const int PersistedSnapshotSchemaVersion = 4;
    private const int PersistedDescriptorSnapshotSchemaVersion = 1;
    private const string DefaultPersistedSnapshotFileName = "tooling-bootstrap-cache-v1.json";
    private static readonly JsonSerializerOptions PersistedSnapshotJson = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly object _sync = new();
    private readonly string _persistedSnapshotPath;
    private string _cacheKey = string.Empty;
    private string _previewCacheKey = string.Empty;
    private ChatServiceToolingBootstrapSnapshot? _snapshot;
    private ChatServiceToolingBootstrapPersistedSnapshot? _persistedSnapshot;
    private string? _persistedSnapshotLoadWarning;

    public ChatServiceToolingBootstrapCache(string? persistedSnapshotPath = null) {
        _persistedSnapshotPath = ResolvePersistedSnapshotPath(persistedSnapshotPath);
        _persistedSnapshot = LoadPersistedSnapshot(_persistedSnapshotPath, out _persistedSnapshotLoadWarning);
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

    public bool TryGetSnapshotByPreviewCacheKey(string previewCacheKey, out ChatServiceToolingBootstrapSnapshot snapshot) {
        var normalizedPreviewCacheKey = NormalizePreviewCacheKey(previewCacheKey, cacheKey: null);
        if (normalizedPreviewCacheKey.Length == 0) {
            snapshot = null!;
            return false;
        }

        lock (_sync) {
            if (_snapshot is null
                || !string.Equals(_previewCacheKey, normalizedPreviewCacheKey, StringComparison.Ordinal)) {
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

    public bool TryGetPersistedPreviewSnapshot(string previewCacheKey, out ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        return TryGetPersistedPreviewSnapshot(previewCacheKey, expectedPreviewDiscoveryFingerprint: null, out snapshot);
    }

    public bool TryGetPersistedPreviewSnapshot(
        string previewCacheKey,
        string? expectedPreviewDiscoveryFingerprint,
        out ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        var normalizedPreviewCacheKey = NormalizePreviewCacheKey(previewCacheKey, cacheKey: null);
        if (normalizedPreviewCacheKey.Length == 0) {
            snapshot = null!;
            return false;
        }

        var normalizedExpectedPreviewDiscoveryFingerprint = (expectedPreviewDiscoveryFingerprint ?? string.Empty).Trim();

        lock (_sync) {
            if (_persistedSnapshot is null
                || !string.Equals(_persistedSnapshot.PreviewCacheKey, normalizedPreviewCacheKey, StringComparison.Ordinal)) {
                snapshot = null!;
                return false;
            }

            var normalizedPersistedPreviewDiscoveryFingerprint = (_persistedSnapshot.PreviewDiscoveryFingerprint ?? string.Empty).Trim();
            if (normalizedExpectedPreviewDiscoveryFingerprint.Length > 0
                && normalizedPersistedPreviewDiscoveryFingerprint.Length > 0
                && !string.Equals(
                    normalizedPersistedPreviewDiscoveryFingerprint,
                    normalizedExpectedPreviewDiscoveryFingerprint,
                    StringComparison.Ordinal)) {
                _persistedSnapshotLoadWarning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary("preview_fingerprint_mismatch");
                snapshot = null!;
                return false;
            }

            _persistedSnapshotLoadWarning = null;
            snapshot = _persistedSnapshot;
            return true;
        }
    }

    public bool TryGetPersistedSnapshotLoadWarning(out string warning) {
        lock (_sync) {
            if (string.IsNullOrWhiteSpace(_persistedSnapshotLoadWarning)) {
                warning = string.Empty;
                return false;
            }

            warning = _persistedSnapshotLoadWarning;
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
            _previewCacheKey = NormalizePreviewCacheKey(previewCacheKey: null, normalizedCacheKey);
            _snapshot = snapshot;
            _persistedSnapshot = BuildPersistedSnapshot(normalizedCacheKey, snapshot);
            _persistedSnapshotLoadWarning = null;
            SavePersistedSnapshot(_persistedSnapshotPath, _persistedSnapshot);
        }
    }

    public void Clear() {
        lock (_sync) {
            _cacheKey = string.Empty;
            _previewCacheKey = string.Empty;
            _snapshot = null;
            _persistedSnapshot = null;
            _persistedSnapshotLoadWarning = null;
            TryDeletePersistedSnapshot(_persistedSnapshotPath);
        }
    }

    private static ChatServiceToolingBootstrapPersistedSnapshot BuildPersistedSnapshot(
        string cacheKey,
        ChatServiceToolingBootstrapSnapshot snapshot) {
        var descriptorSnapshot = BuildDescriptorSnapshot(snapshot);
        return new ChatServiceToolingBootstrapPersistedSnapshot {
            SchemaVersion = PersistedSnapshotSchemaVersion,
            CacheKey = cacheKey,
            PreviewCacheKey = NormalizePreviewCacheKey(previewCacheKey: null, cacheKey),
            CachedAtUtc = DateTime.UtcNow,
            ToolDefinitions = descriptorSnapshot.ToolDefinitions,
            PackSummaries = descriptorSnapshot.PackSummaries,
            PackAvailability = descriptorSnapshot.PackAvailability,
            PluginAvailability = descriptorSnapshot.PluginAvailability,
            PluginCatalog = descriptorSnapshot.PluginCatalog,
            StartupWarnings = snapshot.StartupWarnings ?? Array.Empty<string>(),
            StartupBootstrap = snapshot.StartupBootstrap ?? new SessionStartupBootstrapTelemetryDto(),
            PluginSearchPaths = snapshot.PluginSearchPaths ?? Array.Empty<string>(),
            RuntimePolicyDiagnostics = descriptorSnapshot.RuntimePolicyDiagnostics,
            RoutingCatalogDiagnostics = descriptorSnapshot.RoutingCatalogDiagnostics,
            CapabilitySnapshot = descriptorSnapshot.CapabilitySnapshot!,
            PreviewDiscoveryFingerprint = descriptorSnapshot.PreviewDiscoveryFingerprint,
            DescriptorSnapshot = descriptorSnapshot
        };
    }

    private static ChatServiceToolingBootstrapDescriptorSnapshot BuildDescriptorSnapshot(
        ChatServiceToolingBootstrapSnapshot snapshot) {
        return new ChatServiceToolingBootstrapDescriptorSnapshot {
            SchemaVersion = PersistedDescriptorSnapshotSchemaVersion,
            PreviewDiscoveryFingerprint = BuildPreviewDiscoveryFingerprint(snapshot),
            ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
            PackSummaries = snapshot.PackSummaries ?? Array.Empty<ToolPackInfoDto>(),
            PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            PluginAvailability = snapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
            PluginCatalog = snapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>(),
            RuntimePolicyDiagnostics = snapshot.RuntimePolicyDiagnostics,
            RoutingCatalogDiagnostics = snapshot.RoutingCatalogDiagnostics,
            CapabilitySnapshot = snapshot.CapabilitySnapshot
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

    private static ChatServiceToolingBootstrapPersistedSnapshot? LoadPersistedSnapshot(string path, out string? warning) {
        warning = null;
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return null;
            }

            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<ChatServiceToolingBootstrapPersistedSnapshot>(json, PersistedSnapshotJson);
            if (snapshot is null) {
                warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary("deserialize_failed");
                return null;
            }

            if (snapshot.SchemaVersion != PersistedSnapshotSchemaVersion) {
                warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary(
                    "schema_mismatch",
                    expectedSchemaVersion: PersistedSnapshotSchemaVersion,
                    actualSchemaVersion: snapshot.SchemaVersion);
                return null;
            }

            var normalizedCacheKey = (snapshot.CacheKey ?? string.Empty).Trim();
            if (normalizedCacheKey.Length == 0) {
                warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary("missing_cache_key");
                return null;
            }
            var normalizedPreviewCacheKey = NormalizePreviewCacheKey(snapshot.PreviewCacheKey, normalizedCacheKey);
            if (normalizedPreviewCacheKey.Length == 0) {
                warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary("missing_preview_cache_key");
                return null;
            }

            if (!TryResolveDescriptorSnapshot(snapshot, out var descriptorSnapshot, out warning)) {
                return null;
            }

            return snapshot with {
                CacheKey = normalizedCacheKey,
                PreviewCacheKey = normalizedPreviewCacheKey,
                ToolDefinitions = descriptorSnapshot.ToolDefinitions,
                PackSummaries = descriptorSnapshot.PackSummaries,
                PackAvailability = descriptorSnapshot.PackAvailability,
                PluginAvailability = descriptorSnapshot.PluginAvailability,
                PluginCatalog = descriptorSnapshot.PluginCatalog,
                StartupWarnings = snapshot.StartupWarnings ?? Array.Empty<string>(),
                StartupBootstrap = snapshot.StartupBootstrap ?? new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = snapshot.PluginSearchPaths ?? Array.Empty<string>(),
                RuntimePolicyDiagnostics = descriptorSnapshot.RuntimePolicyDiagnostics,
                RoutingCatalogDiagnostics = descriptorSnapshot.RoutingCatalogDiagnostics,
                CapabilitySnapshot = descriptorSnapshot.CapabilitySnapshot!,
                PreviewDiscoveryFingerprint = descriptorSnapshot.PreviewDiscoveryFingerprint,
                DescriptorSnapshot = descriptorSnapshot
            };
        } catch (Exception ex) {
            warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary(
                "read_failed",
                detail: ex.GetType().Name);
            return null;
        }
    }

    private static bool TryResolveDescriptorSnapshot(
        ChatServiceToolingBootstrapPersistedSnapshot snapshot,
        out ChatServiceToolingBootstrapDescriptorSnapshot descriptorSnapshot,
        out string? warning) {
        warning = null;
        var normalizedDescriptorSnapshot = snapshot.DescriptorSnapshot;
        if (normalizedDescriptorSnapshot is null) {
            if (snapshot.RuntimePolicyDiagnostics is null || snapshot.RoutingCatalogDiagnostics is null) {
                descriptorSnapshot = null!;
                warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary("missing_diagnostics");
                return false;
            }

            normalizedDescriptorSnapshot = new ChatServiceToolingBootstrapDescriptorSnapshot {
                SchemaVersion = PersistedDescriptorSnapshotSchemaVersion,
                PreviewDiscoveryFingerprint = (snapshot.PreviewDiscoveryFingerprint ?? string.Empty).Trim(),
                ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
                PackSummaries = snapshot.PackSummaries ?? Array.Empty<ToolPackInfoDto>(),
                PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
                PluginAvailability = snapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = snapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>(),
                RuntimePolicyDiagnostics = snapshot.RuntimePolicyDiagnostics,
                RoutingCatalogDiagnostics = snapshot.RoutingCatalogDiagnostics,
                CapabilitySnapshot = snapshot.CapabilitySnapshot
            };
        } else if (normalizedDescriptorSnapshot.SchemaVersion != PersistedDescriptorSnapshotSchemaVersion) {
            descriptorSnapshot = null!;
            warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary(
                "descriptor_snapshot_schema_mismatch",
                expectedSchemaVersion: PersistedDescriptorSnapshotSchemaVersion,
                actualSchemaVersion: normalizedDescriptorSnapshot.SchemaVersion);
            return false;
        }

        if (normalizedDescriptorSnapshot.RuntimePolicyDiagnostics is null
            || normalizedDescriptorSnapshot.RoutingCatalogDiagnostics is null) {
            descriptorSnapshot = null!;
            warning = StartupBootstrapWarningBuilder.BuildPersistedPreviewIgnoredSummary("missing_descriptor_diagnostics");
            return false;
        }

        var capabilitySnapshot = normalizedDescriptorSnapshot.CapabilitySnapshot ?? ChatServiceSession.BuildCapabilitySnapshot(
            new ServiceOptions(),
            toolDefinitions: null,
            normalizedDescriptorSnapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            normalizedDescriptorSnapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
            normalizedDescriptorSnapshot.RoutingCatalogDiagnostics,
            pluginCatalog: normalizedDescriptorSnapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>());
        descriptorSnapshot = normalizedDescriptorSnapshot with {
            PreviewDiscoveryFingerprint = string.IsNullOrWhiteSpace(normalizedDescriptorSnapshot.PreviewDiscoveryFingerprint)
                ? BuildPreviewDiscoveryFingerprint(normalizedDescriptorSnapshot)
                : normalizedDescriptorSnapshot.PreviewDiscoveryFingerprint.Trim(),
            ToolDefinitions = normalizedDescriptorSnapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
            PackSummaries = normalizedDescriptorSnapshot.PackSummaries ?? Array.Empty<ToolPackInfoDto>(),
            PackAvailability = normalizedDescriptorSnapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            PluginAvailability = normalizedDescriptorSnapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
            PluginCatalog = normalizedDescriptorSnapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>(),
            CapabilitySnapshot = capabilitySnapshot
        };
        return true;
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

    private static string NormalizePreviewCacheKey(string? previewCacheKey, string? cacheKey) {
        var normalizedPreviewCacheKey = CanonicalizeCacheKey(previewCacheKey, removeDiscoveryFingerprint: true);
        if (normalizedPreviewCacheKey.Length > 0) {
            return normalizedPreviewCacheKey;
        }

        return CanonicalizeCacheKey(cacheKey, removeDiscoveryFingerprint: true);
    }

    private static string CanonicalizeCacheKey(string? key, bool removeDiscoveryFingerprint) {
        var normalizedKey = (key ?? string.Empty).Trim();
        if (normalizedKey.Length == 0) {
            return string.Empty;
        }

        var segments = normalizedKey
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(normalizedKey.Length + 1);
        for (var i = 0; i < segments.Length; i++) {
            if (removeDiscoveryFingerprint
                && segments[i].StartsWith("discovery_fingerprint=", StringComparison.Ordinal)) {
                continue;
            }

            builder.Append(segments[i]).Append(';');
        }

        return builder.ToString();
    }

    private static string BuildPreviewDiscoveryFingerprint(ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        return ToolPackBootstrap.BuildDeferredDescriptorPreviewFingerprint(new ToolPackBootstrapResult {
            ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
            Packs = Array.Empty<IToolPack>(),
            PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            PluginAvailability = snapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
            PluginCatalog = snapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>()
        });
    }

    private static string BuildPreviewDiscoveryFingerprint(ChatServiceToolingBootstrapSnapshot snapshot) {
        return ToolPackBootstrap.BuildDeferredDescriptorPreviewFingerprint(new ToolPackBootstrapResult {
            ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
            Packs = Array.Empty<IToolPack>(),
            PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            PluginAvailability = snapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
            PluginCatalog = snapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>()
        });
    }

    private static string BuildPreviewDiscoveryFingerprint(ChatServiceToolingBootstrapDescriptorSnapshot snapshot) {
        return ToolPackBootstrap.BuildDeferredDescriptorPreviewFingerprint(new ToolPackBootstrapResult {
            ToolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>(),
            Packs = Array.Empty<IToolPack>(),
            PackAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>(),
            PluginAvailability = snapshot.PluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>(),
            PluginCatalog = snapshot.PluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>()
        });
    }
}

internal sealed record ChatServiceToolingBootstrapDescriptorSnapshot {
    public int SchemaVersion { get; init; }
    public string PreviewDiscoveryFingerprint { get; init; } = string.Empty;
    public ToolDefinitionDto[] ToolDefinitions { get; init; } = Array.Empty<ToolDefinitionDto>();
    public ToolPackInfoDto[] PackSummaries { get; init; } = Array.Empty<ToolPackInfoDto>();
    public ToolPackAvailabilityInfo[] PackAvailability { get; init; } = Array.Empty<ToolPackAvailabilityInfo>();
    public ToolPluginAvailabilityInfo[] PluginAvailability { get; init; } = Array.Empty<ToolPluginAvailabilityInfo>();
    public ToolPluginCatalogInfo[] PluginCatalog { get; init; } = Array.Empty<ToolPluginCatalogInfo>();
    public required ToolRuntimePolicyDiagnostics RuntimePolicyDiagnostics { get; init; }
    public required ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics { get; init; }
    public SessionCapabilitySnapshotDto? CapabilitySnapshot { get; init; }
}

internal sealed record ChatServiceToolingBootstrapSnapshot {
    public required ToolRegistry Registry { get; init; }
    public required ToolDefinitionDto[] ToolDefinitions { get; init; }
    public required ToolPackInfoDto[] PackSummaries { get; init; }
    public required IToolPack[] Packs { get; init; }
    public required ToolPackAvailabilityInfo[] PackAvailability { get; init; }
    public required ToolPluginAvailabilityInfo[] PluginAvailability { get; init; }
    public ToolPluginCatalogInfo[] PluginCatalog { get; init; } = Array.Empty<ToolPluginCatalogInfo>();
    public required string[] StartupWarnings { get; init; }
    public required SessionStartupBootstrapTelemetryDto StartupBootstrap { get; init; }
    public required string[] PluginSearchPaths { get; init; }
    public required ToolRuntimePolicyDiagnostics RuntimePolicyDiagnostics { get; init; }
    public required ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics { get; init; }
    public required SessionCapabilitySnapshotDto CapabilitySnapshot { get; init; }
    public required ToolOrchestrationCatalog ToolOrchestrationCatalog { get; init; }
}

internal sealed record ChatServiceToolingBootstrapPersistedSnapshot {
    public int SchemaVersion { get; init; }
    public required string CacheKey { get; init; }
    public string PreviewCacheKey { get; init; } = string.Empty;
    public string PreviewDiscoveryFingerprint { get; init; } = string.Empty;
    public DateTime CachedAtUtc { get; init; }
    public required ToolDefinitionDto[] ToolDefinitions { get; init; }
    public ToolPackInfoDto[] PackSummaries { get; init; } = Array.Empty<ToolPackInfoDto>();
    public required ToolPackAvailabilityInfo[] PackAvailability { get; init; }
    public ToolPluginAvailabilityInfo[] PluginAvailability { get; init; } = Array.Empty<ToolPluginAvailabilityInfo>();
    public ToolPluginCatalogInfo[] PluginCatalog { get; init; } = Array.Empty<ToolPluginCatalogInfo>();
    public required string[] StartupWarnings { get; init; }
    public required SessionStartupBootstrapTelemetryDto StartupBootstrap { get; init; }
    public required string[] PluginSearchPaths { get; init; }
    public required ToolRuntimePolicyDiagnostics RuntimePolicyDiagnostics { get; init; }
    public required ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics { get; init; }
    public required SessionCapabilitySnapshotDto CapabilitySnapshot { get; init; }
    public ChatServiceToolingBootstrapDescriptorSnapshot? DescriptorSnapshot { get; init; }
}
