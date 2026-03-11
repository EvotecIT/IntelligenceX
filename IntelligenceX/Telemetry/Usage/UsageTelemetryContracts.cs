using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Describes a provider that can contribute one or more usage adapters.
/// </summary>
public interface IUsageTelemetryProviderDescriptor {
    /// <summary>
    /// Gets the stable provider identifier.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Creates adapters exposed by this provider.
    /// </summary>
    IReadOnlyList<IUsageTelemetryAdapter> CreateAdapters();
}

/// <summary>
/// Discovers default source roots for a provider.
/// </summary>
public interface IUsageTelemetryRootDiscovery {
    /// <summary>
    /// Gets the stable provider identifier exposed by this discovery strategy.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Discovers source roots that should be registered for import.
    /// </summary>
    IReadOnlyList<SourceRootRecord> DiscoverRoots();
}

/// <summary>
/// Imports usage from a single provider/source strategy.
/// </summary>
public interface IUsageTelemetryAdapter {
    /// <summary>
    /// Gets the stable adapter identifier.
    /// </summary>
    string AdapterId { get; }

    /// <summary>
    /// Returns true when the adapter can import from the given source root.
    /// </summary>
    bool CanImport(SourceRootRecord root);

    /// <summary>
    /// Imports normalized usage events from the source root.
    /// </summary>
    Task<IReadOnlyList<UsageEventRecord>> ImportAsync(
        SourceRootRecord root,
        UsageImportContext context,
        CancellationToken cancellationToken = default(CancellationToken));
}

/// <summary>
/// Resolves account identity for imported usage.
/// </summary>
public interface IUsageAccountResolver {
    /// <summary>
    /// Resolves account information for a normalized usage event.
    /// </summary>
    ResolvedUsageAccount Resolve(UsageEventRecord usageEvent, RawArtifactDescriptor? artifact = null);
}

/// <summary>
/// Stores source roots used for usage discovery.
/// </summary>
public interface ISourceRootStore {
    /// <summary>
    /// Inserts or replaces a source root.
    /// </summary>
    void Upsert(SourceRootRecord root);

    /// <summary>
    /// Looks up a source root by id.
    /// </summary>
    bool TryGet(string id, out SourceRootRecord root);

    /// <summary>
    /// Returns all known source roots.
    /// </summary>
    IReadOnlyList<SourceRootRecord> GetAll();
}

/// <summary>
/// Stores manual account bindings used to normalize imported usage.
/// </summary>
public interface IUsageAccountBindingStore {
    /// <summary>
    /// Inserts or replaces an account binding.
    /// </summary>
    void Upsert(UsageAccountBindingRecord binding);

    /// <summary>
    /// Looks up an account binding by id.
    /// </summary>
    bool TryGet(string id, out UsageAccountBindingRecord binding);

    /// <summary>
    /// Returns all known account bindings.
    /// </summary>
    IReadOnlyList<UsageAccountBindingRecord> GetAll();
}

/// <summary>
/// Stores raw artifact metadata used for incremental imports.
/// </summary>
public interface IRawArtifactStore {
    /// <summary>
    /// Inserts or replaces a raw-artifact descriptor.
    /// </summary>
    void Upsert(RawArtifactDescriptor artifact);

    /// <summary>
    /// Looks up a raw artifact by source root, adapter, and normalized path.
    /// </summary>
    bool TryGet(string sourceRootId, string adapterId, string path, out RawArtifactDescriptor artifact);

    /// <summary>
    /// Returns all tracked raw artifacts.
    /// </summary>
    IReadOnlyList<RawArtifactDescriptor> GetAll();
}

/// <summary>
/// Stores normalized usage events with dedupe-aware upsert behavior.
/// </summary>
public interface IUsageEventStore {
    /// <summary>
    /// Inserts or merges a usage event.
    /// </summary>
    UsageEventUpsertResult Upsert(UsageEventRecord record);

    /// <summary>
    /// Inserts or merges a batch of usage events.
    /// </summary>
    UsageEventBatchUpsertResult UpsertRange(IReadOnlyList<UsageEventRecord> records);

    /// <summary>
    /// Looks up an event by canonical event id.
    /// </summary>
    bool TryGet(string eventId, out UsageEventRecord record);

    /// <summary>
    /// Returns all canonical events.
    /// </summary>
    IReadOnlyList<UsageEventRecord> GetAll();
}

/// <summary>
/// Import context shared across adapters.
/// </summary>
public sealed class UsageImportContext {
    /// <summary>
    /// Gets or sets the parser version for imported artifacts.
    /// </summary>
    public string? ParserVersion { get; set; }

    /// <summary>
    /// Gets or sets the optional machine identifier for imported events.
    /// </summary>
    public string? MachineId { get; set; }

    /// <summary>
    /// Gets or sets the optional account resolver.
    /// </summary>
    public IUsageAccountResolver? AccountResolver { get; set; }

    /// <summary>
    /// Gets or sets the optional raw-artifact store used for incremental imports.
    /// </summary>
    public IRawArtifactStore? RawArtifactStore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unchanged artifacts should be reparsed.
    /// </summary>
    public bool ForceReimport { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether adapters should prefer newer artifacts first.
    /// </summary>
    public bool PreferRecentArtifacts { get; set; }

    /// <summary>
    /// Gets or sets the optional maximum number of artifacts that adapters may parse during this import run.
    /// </summary>
    public int? MaxArtifacts { get; set; }

    /// <summary>
    /// Gets the number of artifacts consumed by the current import run.
    /// </summary>
    public int ArtifactsProcessed => _artifactsProcessed;

    /// <summary>
    /// Gets a value indicating whether the import stopped early because the artifact budget was exhausted.
    /// </summary>
    public bool ArtifactBudgetReached { get; private set; }

    /// <summary>
    /// Gets or sets the UTC clock provider.
    /// </summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    private int _artifactsProcessed;

    /// <summary>
    /// Attempts to reserve capacity for parsing one more artifact.
    /// </summary>
    public bool TryBeginArtifact() {
        var limit = MaxArtifacts;
        if (!limit.HasValue) {
            Interlocked.Increment(ref _artifactsProcessed);
            return true;
        }

        while (true) {
            var current = Volatile.Read(ref _artifactsProcessed);
            if (current >= limit.Value) {
                ArtifactBudgetReached = true;
                return false;
            }

            var updated = Interlocked.CompareExchange(ref _artifactsProcessed, current + 1, current);
            if (updated == current) {
                return true;
            }
        }
    }
}

/// <summary>
/// Resolved account identity for a usage event.
/// </summary>
public sealed class ResolvedUsageAccount {
    /// <summary>
    /// Gets or sets the stable provider-owned account identifier when available.
    /// </summary>
    public string? ProviderAccountId { get; set; }

    /// <summary>
    /// Gets or sets the provider-specific account label.
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// Gets or sets an optional person-level label used to group several provider accounts.
    /// </summary>
    public string? PersonLabel { get; set; }
}

/// <summary>
/// Describes the outcome of a usage-event upsert.
/// </summary>
public sealed class UsageEventUpsertResult {
    /// <summary>
    /// Gets or sets the canonical event identifier retained by the store.
    /// </summary>
    public string CanonicalEventId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a new canonical event was inserted.
    /// </summary>
    public bool Inserted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an existing event was updated by merge.
    /// </summary>
    public bool Updated { get; set; }
}

/// <summary>
/// Describes the outcome of a batch usage-event upsert.
/// </summary>
public sealed class UsageEventBatchUpsertResult {
    /// <summary>
    /// Gets or sets the number of canonical events inserted by the batch.
    /// </summary>
    public int Inserted { get; set; }

    /// <summary>
    /// Gets or sets the number of canonical events updated by merge during the batch.
    /// </summary>
    public int Updated { get; set; }
}

/// <summary>
/// Describes the outcome of importing a single source root.
/// </summary>
public sealed class UsageImportRootResult {
    /// <summary>
    /// Gets or sets the imported source-root identifier.
    /// </summary>
    public string RootId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider identifier for the import.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the adapter identifiers that participated in the import.
    /// </summary>
    public List<string> AdapterIds { get; } = new();

    /// <summary>
    /// Gets or sets the number of normalized records returned by adapters.
    /// </summary>
    public int EventsRead { get; set; }

    /// <summary>
    /// Gets or sets the number of canonical events inserted into the ledger.
    /// </summary>
    public int EventsInserted { get; set; }

    /// <summary>
    /// Gets or sets the number of canonical events updated by merge.
    /// </summary>
    public int EventsUpdated { get; set; }

    /// <summary>
    /// Gets or sets the number of source artifacts parsed by adapters for this root.
    /// </summary>
    public int ArtifactsProcessed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this root stopped early because the artifact budget was exhausted.
    /// </summary>
    public bool ArtifactBudgetReached { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether at least one adapter imported this root.
    /// </summary>
    public bool Imported { get; set; }

    /// <summary>
    /// Gets or sets an optional informational message when no import occurred.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Describes the outcome of importing one or more source roots.
/// </summary>
public sealed class UsageImportBatchResult {
    /// <summary>
    /// Gets the per-root import results.
    /// </summary>
    public List<UsageImportRootResult> Roots { get; } = new();

    /// <summary>
    /// Gets the number of roots considered by the batch.
    /// </summary>
    public int RootsConsidered => Roots.Count;

    /// <summary>
    /// Gets the number of roots that were successfully imported by at least one adapter.
    /// </summary>
    public int RootsImported => Roots.Count(root => root.Imported);

    /// <summary>
    /// Gets the total number of normalized records produced by adapters.
    /// </summary>
    public int EventsRead => Roots.Sum(root => root.EventsRead);

    /// <summary>
    /// Gets the total number of canonical events inserted into the ledger.
    /// </summary>
    public int EventsInserted => Roots.Sum(root => root.EventsInserted);

    /// <summary>
    /// Gets the total number of canonical events updated by merge.
    /// </summary>
    public int EventsUpdated => Roots.Sum(root => root.EventsUpdated);

    /// <summary>
    /// Gets the total number of source artifacts parsed by adapters.
    /// </summary>
    public int ArtifactsProcessed => Roots.Sum(root => root.ArtifactsProcessed);

    /// <summary>
    /// Gets a value indicating whether the import stopped early because the artifact budget was exhausted.
    /// </summary>
    public bool ArtifactBudgetReached => Roots.Any(root => root.ArtifactBudgetReached);
}
