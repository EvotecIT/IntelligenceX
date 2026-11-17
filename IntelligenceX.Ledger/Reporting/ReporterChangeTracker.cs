using System.Security.Cryptography;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Persists admitted watcher comparisons independently of the durable event outbox.</summary>
internal sealed class ReporterChangeTracker {
    private sealed record Entry(string Signature, DateTimeOffset ObservedAtUtc, bool HadPullRequests = false, int SessionOrderRevision = 0);
    private readonly ReporterSpool _spool;
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries;
    private readonly Dictionary<string, ReporterProgress> _progress = new(StringComparer.Ordinal);
    private bool _dirty;
    private long _sequence;

    internal ReporterChangeTracker(ReporterSpool spool, string directory) {
        _spool = spool;
        _file = Path.Combine(directory, "reporter-comparisons-v1.catalog");
        try {
            _entries = File.Exists(_file)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_file), LedgerJson.Options) ?? [] : [];
        } catch (JsonException) {
            _entries = [];
            Console.Error.WriteLine("ledger-comparisons-reconciliation-pending");
        }
        // An event can reach the outbox immediately before shutdown interrupts its checkpoint.
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal)) {
            var observation = JsonSerializer.Deserialize<LedgerEvent>(File.ReadAllText(file), LedgerJson.Options)!;
            if (!Tracked(observation)) continue;
            var key = Key(observation);
            if (_entries.TryGetValue(key, out var prior) && prior.ObservedAtUtc > observation.ObservedAtUtc) continue;
            observation = PreserveHistory(observation, _entries.GetValueOrDefault(key));
            // A retained ID may already be journaled by the old store. Recovery alone
            // cannot satisfy the fresh replay needed to repair its rejected projection.
            _entries[key] = new(Signature(observation), observation.ObservedAtUtc, observation.Work?.HadPullRequests == true,
                prior?.SessionOrderRevision ?? 0);
            _dirty = true;
        }
    }

    internal bool TryAppend(LedgerEvent observation) {
        lock (_gate) {
            if (observation.ItemKind is null or "session" && observation.Session is { } session) {
                if (session is { TurnOpen: true, TurnId: { } turn, LastActivityAtUtc: { } activity }) {
                    if (!_progress.TryGetValue(observation.ItemId, out var previous) || previous.TurnId != turn || previous.LastActivityAtUtc < activity)
                        _progress[observation.ItemId] = new(observation.ItemId, turn, activity);
                } else _progress.Remove(observation.ItemId);
            }
            var key = Key(observation);
            observation = PreserveHistory(observation, _entries.GetValueOrDefault(key));
            var signature = Signature(observation);
            // Replay the current session once after the store's terminal-order correction.
            // Older catalogs can have acknowledged a completion that the old store rejected.
            if (_entries.TryGetValue(key, out var prior) && prior.Signature == signature &&
                (observation.Session is null || prior.SessionOrderRevision >= 1)) return false;
            observation = observation with { Sequence = ++_sequence };
            if (!_spool.TryAppend(observation)) return false;
            _entries[key] = new(signature, observation.ObservedAtUtc, observation.Work?.HadPullRequests == true, 1);
            _dirty = true;
            return true;
        }
    }

    internal ReporterProgress[] Progress {
        get { lock (_gate) return _progress.Values.OrderByDescending(progress => progress.LastActivityAtUtc)
            .ThenBy(progress => progress.ItemId, StringComparer.Ordinal).ToArray(); }
    }

    internal bool IsCurrent(LedgerEvent observation) {
        lock (_gate) return _entries.TryGetValue(Key(observation), out var entry) &&
            entry.Signature == Signature(PreserveHistory(observation, entry));
    }

    internal void Checkpoint() {
        lock (_gate) {
            try { Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
                Console.Error.WriteLine("ledger-comparisons-checkpoint-pending");
            }
        }
    }

    internal void Acknowledge(IEnumerable<Guid> ids) {
        lock (_gate) {
            // Delivery cannot delete the only durable copy before its comparison is committed.
            Save();
            _spool.Acknowledge(ids);
        }
    }

    private void Save() {
        if (!_dirty) return;
        var temporary = _file + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
            JsonSerializer.Serialize(output, _entries, LedgerJson.Options);
            output.Flush(true);
        }
        File.Move(temporary, _file, overwrite: true);
        _dirty = false;
    }

    private static bool Tracked(LedgerEvent observation) => observation.Source == "watcher" &&
        (observation.Session is not null || observation.Work is not null || observation.Delivery is not null || observation.GlobalWork is not null);

    private static string Key(LedgerEvent observation) => Hash(new {
        observation.MachineId, observation.ReporterId, observation.ItemId, observation.ItemKind,
        observation.ParentItemId, observation.Provider, observation.Source,
        Dimension = observation.Session is not null ? "session" : observation.Work is not null ? "work" :
            observation.GlobalWork is not null ? "global" : "delivery-" + observation.Delivery?.Stage
    });

    private static string Signature(LedgerEvent observation) => Hash(new {
        Session = observation.Session is { } session ? session with {
            ArchiveObservedAtUtc = Known(session.ArchiveObservedAtUtc),
            LastActivityAtUtc = session.TurnOpen == true && session.TurnId is not null
                ? Known(session.LastActivityAtUtc) : session.LastActivityAtUtc
        } : null,
        Work = observation.Work is { } work ? work with {
            ClaimsAtUtc = Known(work.ClaimsAtUtc), PullRequestsAtUtc = Known(work.PullRequestsAtUtc),
            OpenPullRequests = Ordered(work.OpenPullRequests), OwnedPullRequests = Ordered(work.OwnedPullRequests),
            CancelledPullRequests = Ordered(work.CancelledPullRequests)
        } : null,
        observation.Delivery,
        GlobalWork = observation.GlobalWork is { } global ? ComparableGlobal(global) : null
    });

    // Missing a current association cannot undo positively observed PR history.
    private static LedgerEvent PreserveHistory(LedgerEvent observation, Entry? previous) {
        if (previous is null || observation.Work is not { HadPullRequests: false } work) return observation;
        var positive = observation with { Work = work with { HadPullRequests = true } };
        // Version-one catalogs stored the semantic signature without the history flag.
        return previous.HadPullRequests || previous.Signature == Signature(positive) ? positive : observation;
    }

    /// <summary>Canonical global state comparison shared by watcher admission and observational refresh admission.</summary>
    internal static string GlobalSignature(GlobalWorkFact global) => Hash(ComparableGlobal(global));

    private static GlobalWorkFact ComparableGlobal(GlobalWorkFact global) => global with {
            ClaimsAtUtc = Known(global.ClaimsAtUtc), PullRequestsAtUtc = Known(global.PullRequestsAtUtc),
            ActiveClaimItems = Ordered(global.ActiveClaimItems),
            OpenPullRequestsByBranch = global.OpenPullRequestsByBranch.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => Ordered(pair.Value), StringComparer.Ordinal),
            PullRequestProofs = global.PullRequestProofs?.OrderBy(proof => proof.Id, StringComparer.Ordinal)
                .Select(proof => proof with { CheckedAtUtc = DateTimeOffset.UnixEpoch }).ToArray(),
            LinksByItem = global.LinksByItem?.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value with {
                    Ids = Ordered(pair.Value.Ids), CheckedAtUtc = DateTimeOffset.UnixEpoch
                }, StringComparer.Ordinal)
        };

    // Availability remains comparison evidence; repeated observation times do not represent activity.
    private static DateTimeOffset? Known(DateTimeOffset? observedAt) => observedAt.HasValue ? DateTimeOffset.UnixEpoch : null;
    private static string[] Ordered(string[] values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string Hash<T>(T value) => Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, LedgerJson.Options)));
}
