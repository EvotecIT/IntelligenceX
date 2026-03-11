using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Registry of provider descriptors and their adapters for usage telemetry.
/// </summary>
public sealed class UsageTelemetryProviderRegistry {
    private readonly Dictionary<string, IReadOnlyList<IUsageTelemetryAdapter>> _adaptersByProvider =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new provider registry.
    /// </summary>
    /// <param name="descriptors">Provider descriptors to register.</param>
    public UsageTelemetryProviderRegistry(IEnumerable<IUsageTelemetryProviderDescriptor> descriptors) {
        if (descriptors is null) {
            throw new ArgumentNullException(nameof(descriptors));
        }

        foreach (var descriptor in descriptors) {
            if (descriptor is null) {
                continue;
            }

            var providerId = descriptor.ProviderId?.Trim();
            if (string.IsNullOrWhiteSpace(providerId)) {
                continue;
            }

            var adapters = descriptor.CreateAdapters() ?? Array.Empty<IUsageTelemetryAdapter>();
            _adaptersByProvider[providerId!] = adapters
                .Where(adapter => adapter is not null)
                .GroupBy(adapter => adapter.AdapterId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }
    }

    /// <summary>
    /// Gets registered provider identifiers.
    /// </summary>
    public IReadOnlyList<string> ProviderIds => _adaptersByProvider.Keys
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Returns adapters registered for the given provider identifier.
    /// </summary>
    public IReadOnlyList<IUsageTelemetryAdapter> GetAdapters(string providerId) {
        if (string.IsNullOrWhiteSpace(providerId)) {
            return Array.Empty<IUsageTelemetryAdapter>();
        }

        if (_adaptersByProvider.TryGetValue(providerId.Trim(), out var adapters)) {
            return adapters;
        }

        return Array.Empty<IUsageTelemetryAdapter>();
    }
}

/// <summary>
/// Coordinates source-root registration, discovery, and adapter-driven imports.
/// </summary>
public sealed class UsageTelemetryImportCoordinator {
    private readonly ISourceRootStore _sourceRootStore;
    private readonly IUsageEventStore _usageEventStore;
    private readonly UsageTelemetryProviderRegistry _registry;
    private readonly IReadOnlyList<IUsageTelemetryRootDiscovery> _rootDiscoveries;

    /// <summary>
    /// Initializes a new import coordinator.
    /// </summary>
    public UsageTelemetryImportCoordinator(
        ISourceRootStore sourceRootStore,
        IUsageEventStore usageEventStore,
        UsageTelemetryProviderRegistry registry,
        IEnumerable<IUsageTelemetryRootDiscovery>? rootDiscoveries = null) {
        _sourceRootStore = sourceRootStore ?? throw new ArgumentNullException(nameof(sourceRootStore));
        _usageEventStore = usageEventStore ?? throw new ArgumentNullException(nameof(usageEventStore));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _rootDiscoveries = (rootDiscoveries ?? Array.Empty<IUsageTelemetryRootDiscovery>())
            .Where(discovery => discovery is not null)
            .ToArray();
    }

    /// <summary>
    /// Registers or updates a source root.
    /// </summary>
    public SourceRootRecord RegisterRoot(
        string providerId,
        UsageSourceKind sourceKind,
        string path,
        string? platformHint = null,
        string? machineLabel = null,
        string? accountHint = null,
        bool enabled = true) {
        var root = new SourceRootRecord(
            SourceRootRecord.CreateStableId(providerId, sourceKind, path),
            providerId,
            sourceKind,
            path) {
            PlatformHint = NormalizeOptional(platformHint),
            MachineLabel = NormalizeOptional(machineLabel),
            AccountHint = NormalizeOptional(accountHint),
            Enabled = enabled
        };

        _sourceRootStore.Upsert(root);
        return root;
    }

    /// <summary>
    /// Discovers and registers default source roots.
    /// </summary>
    public Task<IReadOnlyList<SourceRootRecord>> DiscoverRootsAsync(
        string? providerId = null,
        CancellationToken cancellationToken = default(CancellationToken)) {
        cancellationToken.ThrowIfCancellationRequested();

        var providerFilter = NormalizeOptional(providerId);
        var discovered = new List<SourceRootRecord>();
        foreach (var discovery in _rootDiscoveries) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(providerFilter) &&
                !string.Equals(providerFilter, discovery.ProviderId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var roots = discovery.DiscoverRoots() ?? Array.Empty<SourceRootRecord>();
            for (var i = 0; i < roots.Count; i++) {
                var root = roots[i];
                _sourceRootStore.Upsert(root);
                discovered.Add(root);
            }
        }

        return Task.FromResult<IReadOnlyList<SourceRootRecord>>(discovered
            .OrderBy(root => root.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    /// <summary>
    /// Imports all enabled roots currently registered in the source-root store.
    /// </summary>
    public async Task<UsageImportBatchResult> ImportAllAsync(
        UsageImportContext? context = null,
        string? providerId = null,
        CancellationToken cancellationToken = default(CancellationToken)) {
        var result = new UsageImportBatchResult();
        var importContext = context ?? new UsageImportContext();
        var providerFilter = NormalizeOptional(providerId);
        var roots = _sourceRootStore.GetAll()
            .Where(root => root.Enabled)
            .Where(root => string.IsNullOrWhiteSpace(providerFilter) ||
                           string.Equals(root.ProviderId, providerFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        for (var i = 0; i < roots.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            result.Roots.Add(await ImportRootAsync(roots[i], importContext, cancellationToken).ConfigureAwait(false));
            if (importContext.ArtifactBudgetReached) {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Imports a registered source root by id.
    /// </summary>
    public Task<UsageImportRootResult> ImportRootAsync(
        string rootId,
        UsageImportContext? context = null,
        CancellationToken cancellationToken = default(CancellationToken)) {
        if (string.IsNullOrWhiteSpace(rootId)) {
            throw new ArgumentException("Root id cannot be empty.", nameof(rootId));
        }

        if (!_sourceRootStore.TryGet(rootId.Trim(), out var root)) {
            throw new KeyNotFoundException($"Source root '{rootId}' was not found.");
        }

        return ImportRootAsync(root, context, cancellationToken);
    }

    /// <summary>
    /// Imports a specific source root instance.
    /// </summary>
    public async Task<UsageImportRootResult> ImportRootAsync(
        SourceRootRecord root,
        UsageImportContext? context = null,
        CancellationToken cancellationToken = default(CancellationToken)) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }

        var result = new UsageImportRootResult {
            RootId = root.Id,
            ProviderId = root.ProviderId
        };

        if (!root.Enabled) {
            result.Message = "Source root is disabled.";
            return result;
        }

        var adapters = _registry.GetAdapters(root.ProviderId)
            .Where(adapter => adapter.CanImport(root))
            .ToArray();

        if (adapters.Length == 0) {
            result.Message = "No compatible adapter was registered for this source root.";
            return result;
        }

        var importContext = context ?? new UsageImportContext();
        for (var i = 0; i < adapters.Length; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = adapters[i];
            result.AdapterIds.Add(adapter.AdapterId);

            var artifactsBefore = importContext.ArtifactsProcessed;
            var artifactStore = importContext.RawArtifactStore;
            var deferredArtifactStore = artifactStore is null ? null : new DeferredRawArtifactStore(artifactStore);
            if (deferredArtifactStore is not null) {
                importContext.RawArtifactStore = deferredArtifactStore;
            }

            IReadOnlyList<UsageEventRecord> records;
            try {
                records = await adapter.ImportAsync(root, importContext, cancellationToken).ConfigureAwait(false);
            } finally {
                importContext.RawArtifactStore = artifactStore;
            }

            result.EventsRead += records.Count;
            result.ArtifactsProcessed += Math.Max(0, importContext.ArtifactsProcessed - artifactsBefore);
            result.Imported = true;
            if (records.Count > 0) {
                var batch = _usageEventStore.UpsertRange(records);
                result.EventsInserted += batch.Inserted;
                result.EventsUpdated += batch.Updated;
            }
            deferredArtifactStore?.Commit();
            if (importContext.ArtifactBudgetReached) {
                result.ArtifactBudgetReached = true;
                result.Message = "Artifact budget reached; rerun import to resume from cached progress.";
                break;
            }
        }

        return result;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed class DeferredRawArtifactStore : IRawArtifactStore {
        private readonly IRawArtifactStore _inner;
        private readonly Dictionary<string, RawArtifactDescriptor> _pending =
            new(StringComparer.OrdinalIgnoreCase);

        public DeferredRawArtifactStore(IRawArtifactStore inner) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Upsert(RawArtifactDescriptor artifact) {
            if (artifact is null) {
                throw new ArgumentNullException(nameof(artifact));
            }

            _pending[BuildKey(artifact.SourceRootId, artifact.AdapterId, artifact.Path)] = artifact;
        }

        public bool TryGet(string sourceRootId, string adapterId, string path, out RawArtifactDescriptor artifact) {
            var key = BuildKey(sourceRootId, adapterId, path);
            if (_pending.TryGetValue(key, out artifact!)) {
                return true;
            }

            return _inner.TryGet(sourceRootId, adapterId, path, out artifact!);
        }

        public IReadOnlyList<RawArtifactDescriptor> GetAll() {
            var merged = _inner.GetAll()
                .ToDictionary(value => BuildKey(value.SourceRootId, value.AdapterId, value.Path), value => value, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _pending) {
                merged[pair.Key] = pair.Value;
            }

            return merged.Values
                .OrderBy(value => value.SourceRootId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.AdapterId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void Commit() {
            foreach (var artifact in _pending.Values) {
                _inner.Upsert(artifact);
            }

            _pending.Clear();
        }

        private static string BuildKey(string sourceRootId, string adapterId, string path) {
            return sourceRootId.Trim() + "|" + adapterId.Trim() + "|" + UsageTelemetryIdentity.NormalizePath(path);
        }
    }
}
