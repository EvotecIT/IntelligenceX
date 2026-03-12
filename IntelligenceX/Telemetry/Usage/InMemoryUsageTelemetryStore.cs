using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Thread-safe in-memory source-root store for usage telemetry discovery.
/// </summary>
public sealed class InMemorySourceRootStore : ISourceRootStore {
    private readonly ConcurrentDictionary<string, SourceRootRecord> _roots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(SourceRootRecord root) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }

        _roots[root.Id] = root;
    }

    /// <inheritdoc />
    public bool TryGet(string id, out SourceRootRecord root) {
        if (string.IsNullOrWhiteSpace(id)) {
            root = null!;
            return false;
        }

        return _roots.TryGetValue(id.Trim(), out root!);
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> GetAll() {
        return _roots.Values
            .OrderBy(value => value.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Thread-safe in-memory raw-artifact store for incremental imports.
/// </summary>
public sealed class InMemoryRawArtifactStore : IRawArtifactStore {
    private readonly ConcurrentDictionary<string, RawArtifactDescriptor> _artifacts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(RawArtifactDescriptor artifact) {
        if (artifact is null) {
            throw new ArgumentNullException(nameof(artifact));
        }

        _artifacts[BuildKey(artifact.SourceRootId, artifact.AdapterId, artifact.Path)] = artifact;
    }

    /// <inheritdoc />
    public bool TryGet(string sourceRootId, string adapterId, string path, out RawArtifactDescriptor artifact) {
        if (string.IsNullOrWhiteSpace(sourceRootId) ||
            string.IsNullOrWhiteSpace(adapterId) ||
            string.IsNullOrWhiteSpace(path)) {
            artifact = null!;
            return false;
        }

        return _artifacts.TryGetValue(
            BuildKey(sourceRootId.Trim(), adapterId.Trim(), UsageTelemetryIdentity.NormalizePath(path)),
            out artifact!);
    }

    /// <inheritdoc />
    public IReadOnlyList<RawArtifactDescriptor> GetAll() {
        return _artifacts.Values
            .OrderBy(value => value.SourceRootId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.AdapterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildKey(string sourceRootId, string adapterId, string path) {
        return sourceRootId + "|" + adapterId + "|" + UsageTelemetryIdentity.NormalizePath(path);
    }
}

/// <summary>
/// Thread-safe in-memory usage-event store with dedupe-aware upsert semantics.
/// </summary>
public sealed class InMemoryUsageEventStore : IUsageEventStore {
    private readonly object _gate = new();
    private readonly Dictionary<string, UsageEventRecord> _events =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _dedupeIndex =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public UsageEventUpsertResult Upsert(UsageEventRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_gate) {
            return UpsertCore(record);
        }
    }

    /// <inheritdoc />
    public UsageEventBatchUpsertResult UpsertRange(IReadOnlyList<UsageEventRecord> records) {
        if (records is null) {
            throw new ArgumentNullException(nameof(records));
        }

        lock (_gate) {
            var result = new UsageEventBatchUpsertResult();
            for (var i = 0; i < records.Count; i++) {
                var upsert = UpsertCore(records[i]);
                if (upsert.Inserted) {
                    result.Inserted++;
                }
                if (upsert.Updated) {
                    result.Updated++;
                }
            }

            return result;
        }
    }

    /// <inheritdoc />
    public bool TryGet(string eventId, out UsageEventRecord record) {
        if (string.IsNullOrWhiteSpace(eventId)) {
            record = null!;
            return false;
        }

        lock (_gate) {
            return _events.TryGetValue(eventId.Trim(), out record!);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UsageEventRecord> GetAll() {
        lock (_gate) {
            return _events.Values
                .OrderBy(value => value.TimestampUtc)
                .ThenBy(value => value.ProviderId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.EventId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private string ResolveCanonicalEventId(UsageEventRecord record) {
        foreach (var key in record.GetDeduplicationKeys()) {
            if (_dedupeIndex.TryGetValue(key, out var existingId)) {
                return existingId;
            }
        }

        return record.EventId;
    }

    private UsageEventUpsertResult UpsertCore(UsageEventRecord record) {
        string canonicalId;
        UsageEventRecord canonical;
        var inserted = false;
        var updated = false;

        if (_events.TryGetValue(record.EventId, out canonical!)) {
            canonicalId = canonical.EventId;
            updated = UsageTelemetryEventMerge.MergeInto(canonical, record);
        } else {
            canonicalId = ResolveCanonicalEventId(record);
            if (_events.TryGetValue(canonicalId, out canonical!)) {
                updated = UsageTelemetryEventMerge.MergeInto(canonical, record);
            } else {
                canonical = UsageTelemetryEventMerge.Clone(record);
                _events[canonical.EventId] = canonical;
                canonicalId = canonical.EventId;
                inserted = true;
            }
        }

        foreach (var key in canonical.GetDeduplicationKeys()) {
            _dedupeIndex[key] = canonicalId;
        }
        foreach (var key in record.GetDeduplicationKeys()) {
            _dedupeIndex[key] = canonicalId;
        }

        return new UsageEventUpsertResult {
            CanonicalEventId = canonicalId,
            Inserted = inserted,
            Updated = !inserted && updated,
        };
    }
}
