using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const int MaxTrackedProviderReliabilityEntries = 12;
    private const int MaxLatencySamplesPerProvider = 40;
    private const int CircuitBreakerTransientFailureThreshold = 3;
    private static readonly TimeSpan CircuitBreakerBaseCooldown = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan CircuitBreakerMaxCooldown = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, TurnLatencyTracker> _turnLatencyByRequestId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderReliabilitySnapshot> _providerReliabilityByKey = new(StringComparer.OrdinalIgnoreCase);

    private sealed class TurnLatencyTracker {
        public required string RequestId { get; init; }
        public required string ProviderKey { get; init; }
        public required string ProviderLabel { get; set; }
        public required DateTime DispatchStartedUtc { get; init; }
        public DateTime LastUpdatedUtc { get; set; }
        public long? QueueWaitMs { get; init; }
        public long? AuthProbeMs { get; init; }
        public DateTime? ConnectStartedUtc { get; set; }
        public DateTime? ConnectCompletedUtc { get; set; }
        public bool ConnectSuccessful { get; set; }
        public DateTime? FirstStatusUtc { get; set; }
        public string? FirstStatusCode { get; set; }
        public DateTime? ModelSelectedUtc { get; set; }
        public DateTime? FirstToolRunningUtc { get; set; }
        public DateTime? FirstDeltaUtc { get; set; }
        public DateTime? LastDeltaUtc { get; set; }
    }

    private readonly record struct TurnWatchdogProgressSnapshot(
        DateTime DispatchStartedUtc,
        bool HasFirstStatus,
        string? FirstStatusCode,
        bool HasModelSelected,
        bool HasFirstToolRunning,
        bool HasFirstDelta);

    private sealed class ProviderReliabilitySnapshot {
        public required string Key { get; init; }
        public required string Label { get; set; }
        public required List<long> DurationSamplesMs { get; init; }
        public DateTime LastSeenUtc { get; set; }
        public int ConsecutiveTransientFailures { get; set; }
        public int CircuitOpenCount { get; set; }
        public DateTime? CircuitOpenUntilUtc { get; set; }
        public DateTime? LastFailureUtc { get; set; }
        public string? LastFailure { get; set; }
    }

    private sealed record TurnLatencyCompletion(
        string RequestId,
        string ProviderKey,
        string ProviderLabel,
        DateTime CompletedUtc,
        long DurationMs,
        long? QueueWaitMs,
        long? AuthProbeMs,
        long? ConnectMs,
        long? DispatchToFirstStatusMs,
        long? DispatchToModelSelectedMs,
        long? DispatchToFirstToolRunningMs,
        long? DispatchToFirstDeltaMs,
        long? DispatchToLastDeltaMs,
        long? StreamDurationMs);

    internal static (long? P50Ms, long? P95Ms) ComputeLatencyPercentiles(IReadOnlyList<long>? samplesMs) {
        if (samplesMs is null || samplesMs.Count == 0) {
            return (null, null);
        }

        var normalized = new List<long>(samplesMs.Count);
        for (var i = 0; i < samplesMs.Count; i++) {
            if (samplesMs[i] > 0) {
                normalized.Add(samplesMs[i]);
            }
        }

        if (normalized.Count == 0) {
            return (null, null);
        }

        normalized.Sort();
        return (
            PickNearestRankPercentile(normalized, 0.50d),
            PickNearestRankPercentile(normalized, 0.95d));
    }

    internal static long PickNearestRankPercentile(IReadOnlyList<long> sortedValues, double percentile) {
        if (sortedValues is null || sortedValues.Count == 0) {
            return 0;
        }

        if (percentile <= 0d) {
            return sortedValues[0];
        }

        if (percentile >= 1d) {
            return sortedValues[^1];
        }

        var rank = (int)Math.Ceiling(percentile * sortedValues.Count);
        var index = Math.Clamp(rank - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    internal static TimeSpan ResolveCircuitBreakerCooldown(int openCount) {
        var normalizedOpenCount = Math.Max(1, openCount);
        var exponent = Math.Min(normalizedOpenCount - 1, 6);
        var multiplier = 1 << exponent;
        var cooldownSeconds = CircuitBreakerBaseCooldown.TotalSeconds * multiplier;
        if (cooldownSeconds > CircuitBreakerMaxCooldown.TotalSeconds) {
            cooldownSeconds = CircuitBreakerMaxCooldown.TotalSeconds;
        }

        return TimeSpan.FromSeconds(cooldownSeconds);
    }

    internal static (int ConsecutiveFailures, int OpenCount, DateTime? OpenUntilUtc, bool OpenedNow) RegisterCircuitTransientFailure(
        DateTime nowUtc,
        int previousConsecutiveFailures,
        int previousOpenCount,
        DateTime? previousOpenUntilUtc,
        int threshold = CircuitBreakerTransientFailureThreshold) {
        var normalizedNowUtc = EnsureUtc(nowUtc);
        var normalizedThreshold = Math.Max(1, threshold);
        var consecutiveFailures = Math.Max(0, previousConsecutiveFailures) + 1;
        var openCount = Math.Max(0, previousOpenCount);
        var openUntilUtc = previousOpenUntilUtc;
        var openedNow = false;

        if (consecutiveFailures >= normalizedThreshold) {
            openCount++;
            var nextCooldown = ResolveCircuitBreakerCooldown(openCount);
            var nextOpenUntil = normalizedNowUtc + nextCooldown;
            if (!openUntilUtc.HasValue || EnsureUtc(openUntilUtc.Value) < nextOpenUntil) {
                openUntilUtc = nextOpenUntil;
            }
            openedNow = true;
        }

        return (consecutiveFailures, openCount, openUntilUtc, openedNow);
    }

    internal static bool TryResolveCircuitOpenWindow(DateTime nowUtc, DateTime? openUntilUtc, out TimeSpan remaining) {
        remaining = TimeSpan.Zero;
        if (!openUntilUtc.HasValue) {
            return false;
        }

        var normalizedNowUtc = EnsureUtc(nowUtc);
        var normalizedOpenUntilUtc = EnsureUtc(openUntilUtc.Value);
        var delta = normalizedOpenUntilUtc - normalizedNowUtc;
        if (delta <= TimeSpan.Zero) {
            return false;
        }

        remaining = delta;
        return true;
    }

    internal static bool ShouldCountAsTransientProviderFailure(AssistantTurnOutcome outcome) {
        if (outcome.Kind == AssistantTurnOutcomeKind.Disconnected) {
            return true;
        }

        if (outcome.Kind is AssistantTurnOutcomeKind.Canceled
            or AssistantTurnOutcomeKind.UsageLimit
            or AssistantTurnOutcomeKind.ToolRoundLimit) {
            return false;
        }

        var detail = (outcome.Detail ?? string.Empty).Trim();
        if (detail.Length == 0) {
            return false;
        }

        if (detail.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("sign in", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return detail.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("timed out", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("connection", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("service unavailable", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("(429)", StringComparison.OrdinalIgnoreCase)
               || detail.Contains(" 429", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("(502)", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("(503)", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("(504)", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("network", StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterTurnDispatchStart(string requestId, ActiveUsageIdentity identity, DateTime dispatchStartedUtc, long? queueWaitMs, long? authProbeMs) {
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId.Length == 0) {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var normalizedDispatchStartedUtc = EnsureUtc(dispatchStartedUtc);
        lock (_turnDiagnosticsSync) {
            var providerKey = (identity.Key ?? string.Empty).Trim();
            if (providerKey.Length == 0) {
                providerKey = "unknown-provider";
            }
            var providerLabel = string.IsNullOrWhiteSpace(identity.Label)
                ? providerKey
                : identity.Label.Trim();
            _turnLatencyByRequestId[normalizedRequestId] = new TurnLatencyTracker {
                RequestId = normalizedRequestId,
                ProviderKey = providerKey,
                ProviderLabel = providerLabel,
                DispatchStartedUtc = normalizedDispatchStartedUtc,
                LastUpdatedUtc = nowUtc,
                QueueWaitMs = queueWaitMs,
                AuthProbeMs = authProbeMs
            };
            TrimTurnLatencyTrackersLocked();
        }
    }

    private void MarkTurnConnectStage(string requestId, DateTime connectStartedUtc, DateTime connectCompletedUtc, bool connected) {
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId.Length == 0) {
            return;
        }

        lock (_turnDiagnosticsSync) {
            if (!_turnLatencyByRequestId.TryGetValue(normalizedRequestId, out var tracker)) {
                return;
            }

            tracker.ConnectStartedUtc = EnsureUtc(connectStartedUtc);
            tracker.ConnectCompletedUtc = EnsureUtc(connectCompletedUtc);
            tracker.ConnectSuccessful = connected;
            tracker.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    private void MarkTurnStatusStage(ChatStatusMessage status) {
        var normalizedRequestId = NormalizeRequestId(status.RequestId);
        if (normalizedRequestId.Length == 0) {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_turnDiagnosticsSync) {
            if (!_turnLatencyByRequestId.TryGetValue(normalizedRequestId, out var tracker)) {
                return;
            }

            var normalizedStatus = (status.Status ?? string.Empty).Trim();
            if (!tracker.FirstStatusUtc.HasValue) {
                tracker.FirstStatusUtc = nowUtc;
                tracker.FirstStatusCode = normalizedStatus.Length == 0 ? null : normalizedStatus;
            }

            if (!tracker.ModelSelectedUtc.HasValue
                && string.Equals(normalizedStatus, ChatStatusCodes.ModelSelected, StringComparison.OrdinalIgnoreCase)) {
                tracker.ModelSelectedUtc = nowUtc;
            }

            if (!tracker.FirstToolRunningUtc.HasValue
                && string.Equals(normalizedStatus, ChatStatusCodes.ToolRunning, StringComparison.OrdinalIgnoreCase)) {
                tracker.FirstToolRunningUtc = nowUtc;
            }

            tracker.LastUpdatedUtc = nowUtc;
        }
    }

    private void MarkTurnDeltaStage(ChatDeltaMessage delta) {
        var normalizedRequestId = NormalizeRequestId(delta.RequestId);
        if (normalizedRequestId.Length == 0) {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_turnDiagnosticsSync) {
            if (!_turnLatencyByRequestId.TryGetValue(normalizedRequestId, out var tracker)) {
                return;
            }

            tracker.FirstDeltaUtc ??= nowUtc;
            tracker.LastDeltaUtc = nowUtc;
            tracker.LastUpdatedUtc = nowUtc;
        }
    }

    private bool TryGetTurnWatchdogProgressSnapshot(string? requestId, out TurnWatchdogProgressSnapshot snapshot) {
        snapshot = default;
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId.Length == 0) {
            return false;
        }

        lock (_turnDiagnosticsSync) {
            if (!_turnLatencyByRequestId.TryGetValue(normalizedRequestId, out var tracker)) {
                return false;
            }

            snapshot = new TurnWatchdogProgressSnapshot(
                DispatchStartedUtc: tracker.DispatchStartedUtc,
                HasFirstStatus: tracker.FirstStatusUtc.HasValue,
                FirstStatusCode: tracker.FirstStatusCode,
                HasModelSelected: tracker.ModelSelectedUtc.HasValue,
                HasFirstToolRunning: tracker.FirstToolRunningUtc.HasValue,
                HasFirstDelta: tracker.FirstDeltaUtc.HasValue);
            return true;
        }
    }

    private bool TryGetActiveProviderCircuitOpen(ActiveUsageIdentity identity, out TimeSpan remaining, out int consecutiveFailures) {
        remaining = TimeSpan.Zero;
        consecutiveFailures = 0;
        var key = (identity.Key ?? string.Empty).Trim();
        if (key.Length == 0) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_turnDiagnosticsSync) {
            if (!_providerReliabilityByKey.TryGetValue(key, out var snapshot)) {
                return false;
            }

            if (!TryResolveCircuitOpenWindow(nowUtc, snapshot.CircuitOpenUntilUtc, out remaining)) {
                snapshot.CircuitOpenUntilUtc = null;
                if (snapshot.ConsecutiveTransientFailures >= CircuitBreakerTransientFailureThreshold) {
                    snapshot.ConsecutiveTransientFailures = CircuitBreakerTransientFailureThreshold - 1;
                }
                return false;
            }

            consecutiveFailures = snapshot.ConsecutiveTransientFailures;
            snapshot.LastSeenUtc = nowUtc;
            return true;
        }
    }

    private TurnLatencyCompletion? CompleteTurnLatencyTracking(string requestId, DateTime completedUtc, long? explicitDurationMs = null) {
        var normalizedRequestId = NormalizeRequestId(requestId);
        if (normalizedRequestId.Length == 0) {
            return null;
        }

        lock (_turnDiagnosticsSync) {
            if (!_turnLatencyByRequestId.TryGetValue(normalizedRequestId, out var tracker)) {
                return null;
            }

            _turnLatencyByRequestId.Remove(normalizedRequestId);
            var normalizedCompletedUtc = EnsureUtc(completedUtc);
            var durationMs = explicitDurationMs.HasValue
                ? Math.Max(0L, explicitDurationMs.Value)
                : Math.Max(0L, (long)Math.Round((normalizedCompletedUtc - tracker.DispatchStartedUtc).TotalMilliseconds));

            return new TurnLatencyCompletion(
                RequestId: tracker.RequestId,
                ProviderKey: tracker.ProviderKey,
                ProviderLabel: tracker.ProviderLabel,
                CompletedUtc: normalizedCompletedUtc,
                DurationMs: durationMs,
                QueueWaitMs: tracker.QueueWaitMs,
                AuthProbeMs: tracker.AuthProbeMs,
                ConnectMs: TryComputeElapsedMs(tracker.ConnectStartedUtc, tracker.ConnectCompletedUtc),
                DispatchToFirstStatusMs: TryComputeElapsedMs(tracker.DispatchStartedUtc, tracker.FirstStatusUtc),
                DispatchToModelSelectedMs: TryComputeElapsedMs(tracker.DispatchStartedUtc, tracker.ModelSelectedUtc),
                DispatchToFirstToolRunningMs: TryComputeElapsedMs(tracker.DispatchStartedUtc, tracker.FirstToolRunningUtc),
                DispatchToFirstDeltaMs: TryComputeElapsedMs(tracker.DispatchStartedUtc, tracker.FirstDeltaUtc),
                DispatchToLastDeltaMs: TryComputeElapsedMs(tracker.DispatchStartedUtc, tracker.LastDeltaUtc),
                StreamDurationMs: TryComputeElapsedMs(tracker.FirstDeltaUtc, tracker.LastDeltaUtc));
        }
    }

    private void RegisterTurnSuccessReliability(TurnLatencyCompletion completion) {
        lock (_turnDiagnosticsSync) {
            var provider = GetOrCreateProviderReliabilitySnapshotLocked(
                completion.ProviderKey,
                completion.ProviderLabel,
                completion.CompletedUtc);
            provider.ConsecutiveTransientFailures = 0;
            provider.CircuitOpenUntilUtc = null;
            provider.LastFailure = null;
            provider.LastFailureUtc = null;
            provider.LastSeenUtc = completion.CompletedUtc;
            AddLatencySampleLocked(provider, completion.DurationMs);
            TrimProviderReliabilityCacheLocked();
        }
    }

    private void RegisterTurnFailureReliability(TurnLatencyCompletion completion, AssistantTurnOutcome outcome) {
        lock (_turnDiagnosticsSync) {
            var provider = GetOrCreateProviderReliabilitySnapshotLocked(
                completion.ProviderKey,
                completion.ProviderLabel,
                completion.CompletedUtc);
            provider.LastSeenUtc = completion.CompletedUtc;
            provider.LastFailureUtc = completion.CompletedUtc;
            provider.LastFailure = BuildFailureSummary(outcome);
            AddLatencySampleLocked(provider, completion.DurationMs);

            if (ShouldCountAsTransientProviderFailure(outcome)) {
                var transition = RegisterCircuitTransientFailure(
                    completion.CompletedUtc,
                    provider.ConsecutiveTransientFailures,
                    provider.CircuitOpenCount,
                    provider.CircuitOpenUntilUtc);
                provider.ConsecutiveTransientFailures = transition.ConsecutiveFailures;
                provider.CircuitOpenCount = transition.OpenCount;
                provider.CircuitOpenUntilUtc = transition.OpenUntilUtc;
            } else {
                provider.ConsecutiveTransientFailures = 0;
            }

            TrimProviderReliabilityCacheLocked();
        }
    }

    private object? BuildActiveProviderLatencySummaryState() {
        var identity = ResolveActiveUsageIdentity();
        var key = (identity.Key ?? string.Empty).Trim();
        if (key.Length == 0) {
            return null;
        }

        lock (_turnDiagnosticsSync) {
            if (!_providerReliabilityByKey.TryGetValue(key, out var provider)
                || provider.DurationSamplesMs.Count == 0) {
                return null;
            }

            var (p50Ms, p95Ms) = ComputeLatencyPercentiles(provider.DurationSamplesMs);
            return new {
                key = provider.Key,
                label = provider.Label,
                samples = provider.DurationSamplesMs.Count,
                p50Ms,
                p95Ms,
                lastMs = provider.DurationSamplesMs[^1]
            };
        }
    }

    private object? BuildActiveProviderCircuitState() {
        var identity = ResolveActiveUsageIdentity();
        var key = (identity.Key ?? string.Empty).Trim();
        if (key.Length == 0) {
            return null;
        }

        lock (_turnDiagnosticsSync) {
            if (!_providerReliabilityByKey.TryGetValue(key, out var provider)) {
                return null;
            }

            var nowUtc = DateTime.UtcNow;
            if (!TryResolveCircuitOpenWindow(nowUtc, provider.CircuitOpenUntilUtc, out var remaining)) {
                provider.CircuitOpenUntilUtc = null;
                remaining = TimeSpan.Zero;
            }

            return new {
                key = provider.Key,
                label = provider.Label,
                open = remaining > TimeSpan.Zero,
                retryAfterSeconds = remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0,
                consecutiveFailures = provider.ConsecutiveTransientFailures,
                openCount = provider.CircuitOpenCount,
                lastFailureLocal = provider.LastFailureUtc?.ToLocalTime().ToString(_timestampFormat, CultureInfo.InvariantCulture),
                lastFailure = provider.LastFailure ?? string.Empty
            };
        }
    }

    private static string BuildFailureSummary(AssistantTurnOutcome outcome) {
        var prefix = outcome.Kind switch {
            AssistantTurnOutcomeKind.Canceled => "canceled",
            AssistantTurnOutcomeKind.Disconnected => "disconnected",
            AssistantTurnOutcomeKind.UsageLimit => "usage_limit",
            AssistantTurnOutcomeKind.ToolRoundLimit => "tool_round_limit",
            AssistantTurnOutcomeKind.Error => "error",
            _ => "failure"
        };
        var detail = (outcome.Detail ?? string.Empty).Trim();
        return detail.Length == 0 ? prefix : prefix + ": " + detail;
    }

    private ProviderReliabilitySnapshot GetOrCreateProviderReliabilitySnapshotLocked(string key, string label, DateTime nowUtc) {
        var normalizedKey = (key ?? string.Empty).Trim();
        if (normalizedKey.Length == 0) {
            normalizedKey = "unknown-provider";
        }
        var normalizedLabel = (label ?? string.Empty).Trim();
        if (normalizedLabel.Length == 0) {
            normalizedLabel = normalizedKey;
        }

        if (_providerReliabilityByKey.TryGetValue(normalizedKey, out var existing)) {
            existing.Label = normalizedLabel;
            existing.LastSeenUtc = nowUtc;
            return existing;
        }

        var created = new ProviderReliabilitySnapshot {
            Key = normalizedKey,
            Label = normalizedLabel,
            DurationSamplesMs = new List<long>(MaxLatencySamplesPerProvider),
            LastSeenUtc = nowUtc
        };
        _providerReliabilityByKey[normalizedKey] = created;
        return created;
    }

    private static void AddLatencySampleLocked(ProviderReliabilitySnapshot provider, long durationMs) {
        if (durationMs <= 0) {
            return;
        }

        provider.DurationSamplesMs.Add(durationMs);
        if (provider.DurationSamplesMs.Count > MaxLatencySamplesPerProvider) {
            provider.DurationSamplesMs.RemoveAt(0);
        }
    }

    private void TrimProviderReliabilityCacheLocked() {
        if (_providerReliabilityByKey.Count <= MaxTrackedProviderReliabilityEntries) {
            return;
        }

        var items = new List<ProviderReliabilitySnapshot>(_providerReliabilityByKey.Values);
        items.Sort((left, right) => right.LastSeenUtc.CompareTo(left.LastSeenUtc));
        while (items.Count > MaxTrackedProviderReliabilityEntries) {
            var removed = items[^1];
            items.RemoveAt(items.Count - 1);
            _providerReliabilityByKey.Remove(removed.Key);
        }
    }

    private void TrimTurnLatencyTrackersLocked() {
        if (_turnLatencyByRequestId.Count <= MaxQueuedTurns * 3) {
            return;
        }

        var items = new List<TurnLatencyTracker>(_turnLatencyByRequestId.Values);
        items.Sort((left, right) => left.LastUpdatedUtc.CompareTo(right.LastUpdatedUtc));
        while (items.Count > MaxQueuedTurns * 2) {
            var removed = items[0];
            items.RemoveAt(0);
            _turnLatencyByRequestId.Remove(removed.RequestId);
        }
    }

    private static long? TryComputeElapsedMs(DateTime? startedUtc, DateTime? completedUtc) {
        if (!startedUtc.HasValue || !completedUtc.HasValue) {
            return null;
        }

        return TryComputeElapsedMs(startedUtc.Value, completedUtc.Value);
    }

    private static long? TryComputeElapsedMs(DateTime startedUtc, DateTime? completedUtc) {
        if (!completedUtc.HasValue) {
            return null;
        }

        return TryComputeElapsedMs(startedUtc, completedUtc.Value);
    }

    private static long TryComputeElapsedMs(DateTime startedUtc, DateTime completedUtc) {
        var start = EnsureUtc(startedUtc);
        var end = EnsureUtc(completedUtc);
        var elapsed = end - start;
        if (elapsed <= TimeSpan.Zero) {
            return 0L;
        }

        return Math.Max(0L, (long)Math.Round(elapsed.TotalMilliseconds));
    }

    internal static bool ShouldEmitTurnLatencySystemNotice(long durationMs) {
        return ShouldEmitTurnLatencySystemNotice(durationMs, emitForFirstTurn: false);
    }

    internal static bool ShouldEmitTurnLatencySystemNotice(long durationMs, bool emitForFirstTurn) {
        if (durationMs >= SlowTurnSystemNoticeThresholdMs) {
            return true;
        }

        return emitForFirstTurn && durationMs >= FirstTurnLatencySystemNoticeThresholdMs;
    }

    internal static string BuildTurnLatencySystemNotice(
        long durationMs,
        long? queueWaitMs,
        long? authProbeMs,
        long? connectMs,
        long? dispatchToFirstDeltaMs,
        long? streamDurationMs,
        bool firstTurnNotice = false) {
        static long Normalize(long? value) {
            return Math.Max(0L, value.GetValueOrDefault(0L));
        }

        var totalMs = Math.Max(0L, durationMs);
        var queueMs = Normalize(queueWaitMs);
        var authMs = Normalize(authProbeMs);
        var connectStageMs = Normalize(connectMs);
        var ttftMs = Normalize(dispatchToFirstDeltaMs);
        var streamMs = Normalize(streamDurationMs);
        var preflightMs = queueMs + authMs + connectStageMs;
        var modelUntilFirstDeltaMs = ttftMs > preflightMs ? ttftMs - preflightMs : 0L;
        var postFirstDeltaMs = totalMs > ttftMs ? totalMs - ttftMs : streamMs;
        var dominantPhase = modelUntilFirstDeltaMs > preflightMs + 300
            ? "model/runtime"
            : preflightMs > modelUntilFirstDeltaMs + 300
                ? "preflight"
                : "mixed";

        var noticePrefix = firstTurnNotice ? "First turn latency" : "Slow turn";
        return noticePrefix + ": total "
               + totalMs.ToString(CultureInfo.InvariantCulture)
               + "ms | preflight "
               + preflightMs.ToString(CultureInfo.InvariantCulture)
               + "ms (queue "
               + queueMs.ToString(CultureInfo.InvariantCulture)
               + ", auth "
               + authMs.ToString(CultureInfo.InvariantCulture)
               + ", connect "
               + connectStageMs.ToString(CultureInfo.InvariantCulture)
               + ") | first-token "
               + ttftMs.ToString(CultureInfo.InvariantCulture)
               + "ms | model-pre-token ~"
               + modelUntilFirstDeltaMs.ToString(CultureInfo.InvariantCulture)
               + "ms | post-first-token ~"
               + postFirstDeltaMs.ToString(CultureInfo.InvariantCulture)
               + "ms | dominant: "
               + dominantPhase
               + ".";
    }

    private void AppendTurnLatencySystemNoticeIfNeeded(TurnLatencyCompletion completion) {
        var emitForFirstTurn = Interlocked.CompareExchange(ref _startupFirstTurnLatencyNoticePending, 0, 1) == 1;
        if (!ShouldEmitTurnLatencySystemNotice(completion.DurationMs, emitForFirstTurn)) {
            return;
        }

        AppendSystem(BuildTurnLatencySystemNotice(
            completion.DurationMs,
            completion.QueueWaitMs,
            completion.AuthProbeMs,
            completion.ConnectMs,
            completion.DispatchToFirstDeltaMs,
            completion.StreamDurationMs,
            firstTurnNotice: emitForFirstTurn));
    }

}
