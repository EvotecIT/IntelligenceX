using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Agents;

// A global session key can contain several local/recovered copies. Keep its merge/rules result
// until one of those observations, its metadata, PRs, or a clock boundary changes.
internal sealed class AgentSnapshotIndex {
    private readonly AgentWorkOptions _options;
    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private Dictionary<string, AgentSessionState[]> _logs = new(StringComparer.Ordinal), _metadata = new(StringComparer.Ordinal);
    private IEnumerable<AgentSessionState>? _metadataSource;
    private IReadOnlyList<AgentPullRequestState> _prs = Array.Empty<AgentPullRequestState>();
    private HashSet<string> _running = new(StringComparer.Ordinal);
    private AgentWorkSnapshot _snapshot = new();

    internal AgentSnapshotIndex(AgentWorkOptions options) { _options = options; }

    internal AgentWorkSnapshot Build(IEnumerable<AgentSessionState> logs, IEnumerable<AgentSessionState> metadata,
        HashSet<string> running, DateTimeOffset now, IReadOnlyList<AgentPullRequestState> prs,
        IReadOnlyList<AgentSourceDiagnostic> diagnostics, AgentWorkScanStatistics statistics) {
        Update(logs, ref _logs);
        if (!ReferenceEquals(_metadataSource, metadata)) {
            Update(metadata.Where(static row => row.Provider == "codex"), ref _metadata);
            _metadataSource = metadata;
        }
        if (!_prs.SequenceEqual(prs) || !_running.SetEquals(running)) {
            foreach (var bucket in _buckets.Values) bucket.Dirty = true;
        }
        _prs = prs;
        _running = running;
        var changed = false;
        foreach (var key in _buckets.Keys.Where(key => !_logs.ContainsKey(key) && !_metadata.ContainsKey(key)).ToArray()) {
            _buckets.Remove(key);
            changed = true;
        }
        var since = _options.ActiveWithin.HasValue ? now - _options.ActiveWithin.Value : (DateTimeOffset?)null;
        foreach (var pair in _buckets) {
            var bucket = pair.Value;
            if (!bucket.Dirty && !(bucket.Deadline <= now)) continue;
            changed = true;
            _logs.TryGetValue(pair.Key, out var local);
            _metadata.TryGetValue(pair.Key, out var desktop);
            local ??= Array.Empty<AgentSessionState>();
            desktop ??= Array.Empty<AgentSessionState>();
            var inputs = local.Concat(desktop).ToArray();
            if (since is null || inputs.Any(row => row.LastActivityAtUtc >= since)) {
                AgentWorkMeasurement.Add("session-groups-resolved", 1);
                var result = LocalAgentWorkStateService.BuildSnapshot(local, desktop, running, now, since,
                    _options.ActiveTurnWindow, _options.StaleAfter, Array.Empty<AgentSourceDiagnostic>(), prs, statistics);
                bucket.Session = result.Sessions.FirstOrDefault();
                bucket.Attention = result.NeedsAttention.Where(static item => item.SessionId is not null).ToArray();
            } else {
                bucket.Session = null;
                bucket.Attention = Array.Empty<AgentAttentionItem>();
            }
            bucket.Deadline = null;
            // Running resolution can use newer same-root Desktop activity. Its effective
            // timestamp, rather than the unfinished log's timestamp, drives quiet expiry.
            // The running window is inclusive, so reevaluate one tick after its boundary.
            if (bucket.Session is { StateIsInferred: true, LastActivityAtUtc: { } effectiveActivity })
                AddBoundary(effectiveActivity, _options.ActiveTurnWindow, strict: true);
            foreach (var input in inputs) {
                if (input.LastActivityAtUtc is not { } activity) continue;
                AddDeadline(activity);
                if (_options.ActiveWithin.HasValue) AddBoundary(activity, _options.ActiveWithin.Value, strict: true);
                if (bucket.Session is not null) AddBoundary(activity, _options.StaleAfter);
            }
            void AddBoundary(DateTimeOffset activity, TimeSpan interval, bool strict = false) {
                if (interval > DateTimeOffset.MaxValue - activity) return;
                var deadline = activity + interval;
                if (strict) { if (deadline == DateTimeOffset.MaxValue) return; deadline += TimeSpan.FromTicks(1); }
                AddDeadline(deadline);
            }
            void AddDeadline(DateTimeOffset deadline) {
                if (deadline > now && (bucket.Deadline is null || deadline < bucket.Deadline)) bucket.Deadline = deadline;
            }
            bucket.Dirty = false;
        }
        if (changed) {
            var remote = AgentWorkStateRules.Evaluate(Array.Empty<AgentSessionState>(), prs, now, _options.StaleAfter);
            _snapshot = new AgentWorkSnapshot {
                Sessions = _buckets.Values.Select(static bucket => bucket.Session).Where(static session => session is not null)
                    .Select(static session => session!).OrderByDescending(static row => row.LastActivityAtUtc)
                    .ThenBy(static row => row.Provider, StringComparer.Ordinal).ThenBy(static row => row.Id, StringComparer.Ordinal).ToArray(),
                PullRequests = remote.PullRequests,
                NeedsAttention = _buckets.Values.SelectMany(static bucket => bucket.Attention).Concat(remote.NeedsAttention).ToArray()
            };
        }
        // Empty local collections can still have changed remote attention.
        if (_buckets.Count == 0) _snapshot = AgentWorkStateRules.Evaluate(Array.Empty<AgentSessionState>(), prs, now, _options.StaleAfter);
        return _snapshot with { CapturedAtUtc = now, Diagnostics = diagnostics, Scan = statistics };
    }

    private void Update(IEnumerable<AgentSessionState> rows, ref Dictionary<string, AgentSessionState[]> previous) {
        var current = rows.GroupBy(Key, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        foreach (var key in previous.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)) {
            if (!_buckets.TryGetValue(key, out var bucket)) _buckets[key] = bucket = new Bucket();
            if (!previous.TryGetValue(key, out var old) || !current.TryGetValue(key, out var value) || !old.SequenceEqual(value)) bucket.Dirty = true;
        }
        previous = current;
    }

    private static string Key(AgentSessionState row) => row.Provider + "\0" + row.Machine + "\0" + row.Id;
    private sealed class Bucket {
        internal bool Dirty = true;
        internal DateTimeOffset? Deadline;
        internal AgentSessionState? Session;
        internal IReadOnlyList<AgentAttentionItem> Attention = Array.Empty<AgentAttentionItem>();
    }
}
