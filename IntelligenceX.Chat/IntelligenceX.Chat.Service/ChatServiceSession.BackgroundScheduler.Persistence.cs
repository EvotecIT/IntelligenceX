using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Service.Persistence;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string BackgroundSchedulerRuntimeStoreLoadStateEmpty = "empty";
    private const string BackgroundSchedulerRuntimeStoreLoadStateLoaded = "loaded";
    private const string BackgroundSchedulerRuntimeStoreLoadStateInvalid = "invalid";
    private const string BackgroundSchedulerRuntimeStoreLoadStateDeferred = "deferred";
    private const int BackgroundSchedulerRuntimeStoreVersion = 1;
    private static readonly JsonSerializerOptions BackgroundSchedulerRuntimeStoreReadJsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private static Func<string, bool?>? BackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting;
    private readonly object _backgroundSchedulerRuntimeStorePersistenceLock = new();
    private int _backgroundSchedulerRuntimeStoreRehydratePending;
    private string _backgroundSchedulerRuntimeStoreLoadState = BackgroundSchedulerRuntimeStoreLoadStateEmpty;
    private BackgroundSchedulerRuntimeStoreDto _backgroundSchedulerRuntimeStoreLocalBaseline = new();
    private BackgroundSchedulerRuntimeStoreDto? _backgroundSchedulerRuntimeStoreDeferredPersistStore;
    private BackgroundSchedulerRuntimeStoreDto? _backgroundSchedulerRuntimeStoreDeferredPersistBaseline;

    private static string ResolveDefaultBackgroundSchedulerRuntimeStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("background-scheduler-runtime.json");

    private string ResolveBackgroundSchedulerRuntimeStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "background-scheduler-runtime.json");

    private void TryRehydrateBackgroundSchedulerRuntimeState() {
        lock (_backgroundSchedulerRuntimeStorePersistenceLock) {
            TryRehydrateBackgroundSchedulerRuntimeStateCore();
        }
    }

    private void TryRehydrateBackgroundSchedulerRuntimeStateCore() {
        var path = ResolveBackgroundSchedulerRuntimeStorePath();
        BackgroundSchedulerRuntimeStoreDto? deferredPersistStore;
        BackgroundSchedulerRuntimeStoreDto? deferredPersistBaseline;
        lock (_backgroundSchedulerTelemetryLock) {
            deferredPersistStore = _backgroundSchedulerRuntimeStoreDeferredPersistStore is null
                ? null
                : CloneBackgroundSchedulerRuntimeStore(_backgroundSchedulerRuntimeStoreDeferredPersistStore);
            deferredPersistBaseline = _backgroundSchedulerRuntimeStoreDeferredPersistBaseline is null
                ? null
                : CloneBackgroundSchedulerRuntimeStore(_backgroundSchedulerRuntimeStoreDeferredPersistBaseline);
        }

        if (deferredPersistStore is not null) {
            TryPersistDeferredBackgroundSchedulerRuntimeState(
                path,
                deferredPersistBaseline ?? new BackgroundSchedulerRuntimeStoreDto(),
                deferredPersistStore);
            return;
        }

        var localBeforeRead = CaptureBackgroundSchedulerRuntimeStoreSnapshot();
        if (!TryWithBackgroundSchedulerRuntimeStoreLock(
                path,
                static runtimeStorePath => ReadBackgroundSchedulerRuntimeStoreNoThrow(runtimeStorePath),
                path,
                out var readResult)) {
            Interlocked.Exchange(ref _backgroundSchedulerRuntimeStoreRehydratePending, 1);
            lock (_backgroundSchedulerTelemetryLock) {
                _backgroundSchedulerRuntimeStoreLoadState = BackgroundSchedulerRuntimeStoreLoadStateDeferred;
            }
            return;
        }

        var store = readResult.Store is null
            ? new BackgroundSchedulerRuntimeStoreDto()
            : CloneBackgroundSchedulerRuntimeStore(readResult.Store);
        NormalizeBackgroundSchedulerRuntimeStoreForPersistence(store, DateTime.UtcNow.Ticks);
        if (!TryApplyBackgroundSchedulerRuntimeStoreIfUnchanged(localBeforeRead, store)) {
            DeferBackgroundSchedulerRuntimeStoreSnapshot(
                CaptureBackgroundSchedulerRuntimeStoreSnapshot(),
                localBeforeRead);
            return;
        }

        Interlocked.Exchange(ref _backgroundSchedulerRuntimeStoreRehydratePending, 0);
        lock (_backgroundSchedulerTelemetryLock) {
            _backgroundSchedulerRuntimeStoreLocalBaseline = CloneBackgroundSchedulerRuntimeStore(store);
            _backgroundSchedulerRuntimeStoreLoadState = readResult.LoadState;
        }
    }

    private void TryPersistDeferredBackgroundSchedulerRuntimeState(
        string path,
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto deferredStore) {
        if (!TryPersistBackgroundSchedulerRuntimeStoreSnapshot(path, baseline, deferredStore, out var result)) {
            DeferBackgroundSchedulerRuntimeStoreSnapshot(
                CaptureBackgroundSchedulerRuntimeStoreSnapshot(),
                baseline);
            return;
        }

        CompleteBackgroundSchedulerRuntimeStorePersistence(baseline, deferredStore, result);
    }

    private bool TryApplyBackgroundSchedulerRuntimeStoreIfUnchanged(
        BackgroundSchedulerRuntimeStoreDto expectedStore,
        BackgroundSchedulerRuntimeStoreDto store) {
        lock (_backgroundSchedulerTelemetryLock) {
            var currentStore = CaptureBackgroundSchedulerRuntimeStoreSnapshot();
            if (!BackgroundSchedulerRuntimeStoreEquals(currentStore, expectedStore)) {
                return false;
            }

            ApplyBackgroundSchedulerRuntimeStore(store);
            return true;
        }
    }

    private void ApplyBackgroundSchedulerRuntimeStore(BackgroundSchedulerRuntimeStoreDto store) {
        ArgumentNullException.ThrowIfNull(store);
        var nowTicks = DateTime.UtcNow.Ticks;
        (store.LastAdaptiveIdleUtcTicks, store.LastAdaptiveIdleDelaySeconds, store.LastAdaptiveIdleReason) =
            NormalizeBackgroundSchedulerAdaptiveIdleState(
                store.LastAdaptiveIdleUtcTicks,
                store.LastAdaptiveIdleDelaySeconds,
                store.LastAdaptiveIdleReason,
                nowTicks);

        var normalizedRecentActivity = NormalizeBackgroundSchedulerActivities(store.RecentActivity);
        lock (_backgroundSchedulerTelemetryLock) {
            Interlocked.Exchange(ref _backgroundSchedulerLastTickUtcTicks, Math.Max(0, store.LastSchedulerTickUtcTicks));
            _backgroundSchedulerLastOutcome = NormalizeBackgroundSchedulerActivityText(store.LastOutcome, maxLength: 80);
            _backgroundSchedulerLastOutcomeUtcTicks = Math.Max(0, store.LastOutcomeUtcTicks);
            _backgroundSchedulerLastSuccessUtcTicks = Math.Max(0, store.LastSuccessUtcTicks);
            _backgroundSchedulerLastFailureUtcTicks = Math.Max(0, store.LastFailureUtcTicks);
            _backgroundSchedulerCompletedExecutionCount = Math.Max(0, store.CompletedExecutionCount);
            _backgroundSchedulerRequeuedExecutionCount = Math.Max(0, store.RequeuedExecutionCount);
            _backgroundSchedulerReleasedExecutionCount = Math.Max(0, store.ReleasedExecutionCount);
            _backgroundSchedulerConsecutiveFailureCount = Math.Max(0, store.ConsecutiveFailureCount);
            _backgroundSchedulerPausedUntilUtcTicks = Math.Max(0, store.PausedUntilUtcTicks);
            _backgroundSchedulerPauseReason = NormalizeBackgroundSchedulerActivityText(store.PauseReason, maxLength: 120);
            _backgroundSchedulerLastAdaptiveIdleUtcTicks = Math.Max(0, store.LastAdaptiveIdleUtcTicks);
            _backgroundSchedulerLastAdaptiveIdleDelaySeconds = Math.Max(0, store.LastAdaptiveIdleDelaySeconds);
            _backgroundSchedulerLastAdaptiveIdleReason = NormalizeBackgroundSchedulerActivityText(store.LastAdaptiveIdleReason, maxLength: 160);
            _backgroundSchedulerRecentActivity.Clear();
            _backgroundSchedulerRecentActivity.AddRange(normalizedRecentActivity);
            NormalizeBackgroundSchedulerPauseStateNoLock(DateTime.UtcNow.Ticks);
        }
    }

    private void EnsureBackgroundSchedulerRuntimeStateRehydratedIfPending() {
        if (Volatile.Read(ref _backgroundSchedulerRuntimeStoreRehydratePending) == 0) {
            return;
        }

        TryRehydrateBackgroundSchedulerRuntimeState();
    }

    private void PersistBackgroundSchedulerRuntimeStateNoThrow() {
        lock (_backgroundSchedulerRuntimeStorePersistenceLock) {
            var store = CaptureBackgroundSchedulerRuntimeStoreSnapshot();
            if (Volatile.Read(ref _backgroundSchedulerRuntimeStoreRehydratePending) != 0) {
                BackgroundSchedulerRuntimeStoreDto baseline;
                lock (_backgroundSchedulerTelemetryLock) {
                    baseline = CloneBackgroundSchedulerRuntimeStore(
                        _backgroundSchedulerRuntimeStoreDeferredPersistBaseline
                        ?? _backgroundSchedulerRuntimeStoreLocalBaseline);
                    _backgroundSchedulerRuntimeStoreDeferredPersistStore = CloneBackgroundSchedulerRuntimeStore(store);
                    _backgroundSchedulerRuntimeStoreDeferredPersistBaseline ??= CloneBackgroundSchedulerRuntimeStore(baseline);
                }
                TryRehydrateBackgroundSchedulerRuntimeStateCore();
                return;
            }

            BackgroundSchedulerRuntimeStoreDto localBaseline;
            lock (_backgroundSchedulerTelemetryLock) {
                localBaseline = CloneBackgroundSchedulerRuntimeStore(_backgroundSchedulerRuntimeStoreLocalBaseline);
            }

            if (!TryPersistBackgroundSchedulerRuntimeStoreSnapshot(
                    ResolveBackgroundSchedulerRuntimeStorePath(),
                    localBaseline,
                    store,
                    out var result)) {
                DeferBackgroundSchedulerRuntimeStoreSnapshot(
                    CaptureBackgroundSchedulerRuntimeStoreSnapshot(),
                    localBaseline);
                return;
            }

            CompleteBackgroundSchedulerRuntimeStorePersistence(localBaseline, store, result);
        }
    }

    private void CompleteBackgroundSchedulerRuntimeStorePersistence(
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto attemptedStore,
        BackgroundSchedulerRuntimeStorePersistResult result) {
        var appliedMergedStore = TryApplyBackgroundSchedulerRuntimeStoreIfUnchanged(
            attemptedStore,
            result.MergedStore);
        if (result.Persisted && appliedMergedStore) {
            Interlocked.Exchange(ref _backgroundSchedulerRuntimeStoreRehydratePending, 0);
            lock (_backgroundSchedulerTelemetryLock) {
                _backgroundSchedulerRuntimeStoreLocalBaseline = CloneBackgroundSchedulerRuntimeStore(result.MergedStore);
                _backgroundSchedulerRuntimeStoreDeferredPersistStore = null;
                _backgroundSchedulerRuntimeStoreDeferredPersistBaseline = null;
                _backgroundSchedulerRuntimeStoreLoadState = BackgroundSchedulerRuntimeStoreLoadStateLoaded;
            }
            return;
        }

        if (result.Persisted) {
            DeferBackgroundSchedulerRuntimeStoreSnapshot(
                CaptureBackgroundSchedulerRuntimeStoreSnapshot(),
                attemptedStore);
            return;
        }

        if (appliedMergedStore) {
            DeferBackgroundSchedulerRuntimeStoreSnapshot(result.MergedStore, result.PersistedStore);
            return;
        }

        DeferBackgroundSchedulerRuntimeStoreSnapshot(
            CaptureBackgroundSchedulerRuntimeStoreSnapshot(),
            baseline);
    }

    private void DeferBackgroundSchedulerRuntimeStoreSnapshot(
        BackgroundSchedulerRuntimeStoreDto store,
        BackgroundSchedulerRuntimeStoreDto baseline) {
        Interlocked.Exchange(ref _backgroundSchedulerRuntimeStoreRehydratePending, 1);
        lock (_backgroundSchedulerTelemetryLock) {
            _backgroundSchedulerRuntimeStoreLocalBaseline = CloneBackgroundSchedulerRuntimeStore(baseline);
            _backgroundSchedulerRuntimeStoreDeferredPersistStore = CloneBackgroundSchedulerRuntimeStore(store);
            _backgroundSchedulerRuntimeStoreDeferredPersistBaseline = CloneBackgroundSchedulerRuntimeStore(baseline);
            _backgroundSchedulerRuntimeStoreLoadState = BackgroundSchedulerRuntimeStoreLoadStateDeferred;
        }
    }

    private BackgroundSchedulerRuntimeStoreDto CaptureBackgroundSchedulerRuntimeStoreSnapshot() {
        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_backgroundSchedulerTelemetryLock) {
            NormalizeBackgroundSchedulerPauseStateNoLock(nowTicks);
            var store = new BackgroundSchedulerRuntimeStoreDto {
                Version = BackgroundSchedulerRuntimeStoreVersion,
                LastSchedulerTickUtcTicks = Math.Max(0, Interlocked.Read(ref _backgroundSchedulerLastTickUtcTicks)),
                LastOutcome = NormalizeBackgroundSchedulerActivityText(_backgroundSchedulerLastOutcome, maxLength: 80),
                LastOutcomeUtcTicks = Math.Max(0, _backgroundSchedulerLastOutcomeUtcTicks),
                LastSuccessUtcTicks = Math.Max(0, _backgroundSchedulerLastSuccessUtcTicks),
                LastFailureUtcTicks = Math.Max(0, _backgroundSchedulerLastFailureUtcTicks),
                CompletedExecutionCount = Math.Max(0, _backgroundSchedulerCompletedExecutionCount),
                RequeuedExecutionCount = Math.Max(0, _backgroundSchedulerRequeuedExecutionCount),
                ReleasedExecutionCount = Math.Max(0, _backgroundSchedulerReleasedExecutionCount),
                ConsecutiveFailureCount = Math.Max(0, _backgroundSchedulerConsecutiveFailureCount),
                PausedUntilUtcTicks = Math.Max(0, _backgroundSchedulerPausedUntilUtcTicks),
                PauseReason = NormalizeBackgroundSchedulerActivityText(_backgroundSchedulerPauseReason, maxLength: 120),
                LastAdaptiveIdleUtcTicks = Math.Max(0, _backgroundSchedulerLastAdaptiveIdleUtcTicks),
                LastAdaptiveIdleDelaySeconds = Math.Max(0, _backgroundSchedulerLastAdaptiveIdleDelaySeconds),
                LastAdaptiveIdleReason = NormalizeBackgroundSchedulerActivityText(_backgroundSchedulerLastAdaptiveIdleReason, maxLength: 160),
                RecentActivity = NormalizeBackgroundSchedulerActivities(_backgroundSchedulerRecentActivity)
            };
            (store.LastAdaptiveIdleUtcTicks, store.LastAdaptiveIdleDelaySeconds, store.LastAdaptiveIdleReason) =
                NormalizeBackgroundSchedulerAdaptiveIdleState(
                    store.LastAdaptiveIdleUtcTicks,
                    store.LastAdaptiveIdleDelaySeconds,
                    store.LastAdaptiveIdleReason,
                    nowTicks);
            return store;
        }
    }

    private static bool TryPersistBackgroundSchedulerRuntimeStoreSnapshot(
        string path,
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto store,
        out BackgroundSchedulerRuntimeStorePersistResult result) {
        return TryWithBackgroundSchedulerRuntimeStoreLock(
            path,
            static state => {
                var readResult = ReadBackgroundSchedulerRuntimeStoreNoThrow(state.Path);
                var persistedStore = readResult.Store is null
                    ? new BackgroundSchedulerRuntimeStoreDto()
                    : CloneBackgroundSchedulerRuntimeStore(readResult.Store);
                var mergedStore = MergeBackgroundSchedulerRuntimeStore(
                    persistedStore,
                    state.Baseline,
                    state.Store);
                NormalizeBackgroundSchedulerRuntimeStoreForPersistence(mergedStore, DateTime.UtcNow.Ticks);
                var persisted = WriteBackgroundSchedulerRuntimeStoreNoThrow(state.Path, mergedStore);
                if (!persisted) {
                    var verification = ReadBackgroundSchedulerRuntimeStoreNoThrow(state.Path);
                    persisted = verification.Store is not null
                        && BackgroundSchedulerRuntimeStoreEquals(verification.Store, mergedStore);
                }
                return new BackgroundSchedulerRuntimeStorePersistResult(
                    persisted,
                    persistedStore,
                    mergedStore);
            },
            (Path: path, Baseline: baseline, Store: store),
            out result);
    }

    private static BackgroundSchedulerRuntimeStoreDto MergeBackgroundSchedulerRuntimeStore(
        BackgroundSchedulerRuntimeStoreDto? persistedStore,
        BackgroundSchedulerRuntimeStoreDto? baselineStore,
        BackgroundSchedulerRuntimeStoreDto desiredStore) {
        ArgumentNullException.ThrowIfNull(desiredStore);

        var persisted = persistedStore is null
            ? new BackgroundSchedulerRuntimeStoreDto()
            : CloneBackgroundSchedulerRuntimeStore(persistedStore);
        var baseline = baselineStore is null
            ? new BackgroundSchedulerRuntimeStoreDto()
            : CloneBackgroundSchedulerRuntimeStore(baselineStore);
        var desired = CloneBackgroundSchedulerRuntimeStore(desiredStore);
        var merged = CloneBackgroundSchedulerRuntimeStore(persisted);

        if (desired.LastSchedulerTickUtcTicks != baseline.LastSchedulerTickUtcTicks) {
            merged.LastSchedulerTickUtcTicks = Math.Max(merged.LastSchedulerTickUtcTicks, desired.LastSchedulerTickUtcTicks);
        }

        if (BackgroundSchedulerOutcomeStateChanged(baseline, desired)
            && (BackgroundSchedulerOutcomeStateEquals(persisted, baseline)
                || desired.LastOutcomeUtcTicks >= persisted.LastOutcomeUtcTicks)) {
            merged.LastOutcomeUtcTicks = Math.Max(0, desired.LastOutcomeUtcTicks);
            merged.LastOutcome = NormalizeBackgroundSchedulerActivityText(desired.LastOutcome, maxLength: 80);
        }

        if (desired.LastSuccessUtcTicks != baseline.LastSuccessUtcTicks) {
            merged.LastSuccessUtcTicks = Math.Max(merged.LastSuccessUtcTicks, desired.LastSuccessUtcTicks);
        }
        if (desired.LastFailureUtcTicks != baseline.LastFailureUtcTicks) {
            merged.LastFailureUtcTicks = Math.Max(merged.LastFailureUtcTicks, desired.LastFailureUtcTicks);
        }

        merged.CompletedExecutionCount = AddBackgroundSchedulerCounterDelta(
            persisted.CompletedExecutionCount,
            baseline.CompletedExecutionCount,
            desired.CompletedExecutionCount);
        merged.RequeuedExecutionCount = AddBackgroundSchedulerCounterDelta(
            persisted.RequeuedExecutionCount,
            baseline.RequeuedExecutionCount,
            desired.RequeuedExecutionCount);
        merged.ReleasedExecutionCount = AddBackgroundSchedulerCounterDelta(
            persisted.ReleasedExecutionCount,
            baseline.ReleasedExecutionCount,
            desired.ReleasedExecutionCount);

        MergeBackgroundSchedulerConsecutiveFailureState(merged, persisted, baseline, desired);

        if (BackgroundSchedulerPauseStateChanged(baseline, desired)
            && (BackgroundSchedulerPauseStateEquals(persisted, baseline)
                || desired.LastOutcomeUtcTicks >= persisted.LastOutcomeUtcTicks)) {
            merged.PausedUntilUtcTicks = Math.Max(0, desired.PausedUntilUtcTicks);
            merged.PauseReason = NormalizeBackgroundSchedulerActivityText(desired.PauseReason, maxLength: 120);
        }

        if (BackgroundSchedulerAdaptiveIdleStateChanged(baseline, desired)
            && (BackgroundSchedulerAdaptiveIdleStateEquals(persisted, baseline)
                || desired.LastAdaptiveIdleUtcTicks >= persisted.LastAdaptiveIdleUtcTicks)) {
            merged.LastAdaptiveIdleUtcTicks = Math.Max(0, desired.LastAdaptiveIdleUtcTicks);
            merged.LastAdaptiveIdleDelaySeconds = Math.Max(0, desired.LastAdaptiveIdleDelaySeconds);
            merged.LastAdaptiveIdleReason = NormalizeBackgroundSchedulerActivityText(desired.LastAdaptiveIdleReason, maxLength: 160);
        }

        merged.RecentActivity = MergeBackgroundSchedulerActivities(
            persisted.RecentActivity,
            GetBackgroundSchedulerActivityDelta(baseline.RecentActivity, desired.RecentActivity));
        return merged;
    }

    private static int AddBackgroundSchedulerCounterDelta(int persisted, int baseline, int desired) {
        var delta = Math.Max(0L, (long)Math.Max(0, desired) - Math.Max(0, baseline));
        return (int)Math.Min(int.MaxValue, Math.Max(0L, persisted) + delta);
    }

    private static void MergeBackgroundSchedulerConsecutiveFailureState(
        BackgroundSchedulerRuntimeStoreDto merged,
        BackgroundSchedulerRuntimeStoreDto persisted,
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto desired) {
        var localSuccessChanged = desired.LastSuccessUtcTicks != baseline.LastSuccessUtcTicks;
        var localFailureChanged = desired.LastFailureUtcTicks != baseline.LastFailureUtcTicks;
        if (!localSuccessChanged && !localFailureChanged
            && desired.ConsecutiveFailureCount == baseline.ConsecutiveFailureCount) {
            return;
        }

        var localLatestEvent = Math.Max(desired.LastSuccessUtcTicks, desired.LastFailureUtcTicks);
        var persistedLatestEvent = Math.Max(persisted.LastSuccessUtcTicks, persisted.LastFailureUtcTicks);
        if (localLatestEvent < persistedLatestEvent) {
            return;
        }

        if (desired.LastSuccessUtcTicks >= desired.LastFailureUtcTicks
            || desired.LastSuccessUtcTicks > baseline.LastSuccessUtcTicks) {
            merged.ConsecutiveFailureCount = Math.Max(0, desired.ConsecutiveFailureCount);
            return;
        }

        var failureDelta = Math.Max(
            0,
            Math.Max(0, desired.ConsecutiveFailureCount) - Math.Max(0, baseline.ConsecutiveFailureCount));
        merged.ConsecutiveFailureCount = (int)Math.Min(
            int.MaxValue,
            (long)Math.Max(0, persisted.ConsecutiveFailureCount) + failureDelta);
    }

    private static bool BackgroundSchedulerOutcomeStateChanged(
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto desired) =>
        !BackgroundSchedulerOutcomeStateEquals(baseline, desired);

    private static bool BackgroundSchedulerOutcomeStateEquals(
        BackgroundSchedulerRuntimeStoreDto left,
        BackgroundSchedulerRuntimeStoreDto right) =>
        left.LastOutcomeUtcTicks == right.LastOutcomeUtcTicks
        && string.Equals(left.LastOutcome, right.LastOutcome, StringComparison.Ordinal);

    private static bool BackgroundSchedulerPauseStateChanged(
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto desired) =>
        !BackgroundSchedulerPauseStateEquals(baseline, desired);

    private static bool BackgroundSchedulerPauseStateEquals(
        BackgroundSchedulerRuntimeStoreDto left,
        BackgroundSchedulerRuntimeStoreDto right) =>
        left.PausedUntilUtcTicks == right.PausedUntilUtcTicks
        && string.Equals(left.PauseReason, right.PauseReason, StringComparison.Ordinal);

    private static bool BackgroundSchedulerAdaptiveIdleStateChanged(
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto desired) =>
        !BackgroundSchedulerAdaptiveIdleStateEquals(baseline, desired);

    private static bool BackgroundSchedulerAdaptiveIdleStateEquals(
        BackgroundSchedulerRuntimeStoreDto left,
        BackgroundSchedulerRuntimeStoreDto right) =>
        left.LastAdaptiveIdleUtcTicks == right.LastAdaptiveIdleUtcTicks
        && left.LastAdaptiveIdleDelaySeconds == right.LastAdaptiveIdleDelaySeconds
        && string.Equals(left.LastAdaptiveIdleReason, right.LastAdaptiveIdleReason, StringComparison.Ordinal);

    private static SessionCapabilityBackgroundSchedulerActivityDto[] GetBackgroundSchedulerActivityDelta(
        IReadOnlyList<SessionCapabilityBackgroundSchedulerActivityDto>? baselineActivities,
        IReadOnlyList<SessionCapabilityBackgroundSchedulerActivityDto>? desiredActivities) {
        var baselineKeys = (baselineActivities ?? Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>())
            .Select(static activity => (
                activity.RecordedUtcTicks,
                activity.Outcome,
                activity.ThreadId,
                activity.ItemId,
                activity.ToolName,
                activity.Reason,
                activity.OutputCount,
                activity.FailureDetail))
            .ToHashSet();
        return NormalizeBackgroundSchedulerActivities(
            (desiredActivities ?? Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>())
            .Where(activity => !baselineKeys.Contains((
                activity.RecordedUtcTicks,
                activity.Outcome,
                activity.ThreadId,
                activity.ItemId,
                activity.ToolName,
                activity.Reason,
                activity.OutputCount,
                activity.FailureDetail))));
    }

    private static SessionCapabilityBackgroundSchedulerActivityDto[] MergeBackgroundSchedulerActivities(
        IReadOnlyList<SessionCapabilityBackgroundSchedulerActivityDto>? persistedActivities,
        IReadOnlyList<SessionCapabilityBackgroundSchedulerActivityDto>? deferredActivities) {
        return NormalizeBackgroundSchedulerActivities(
            Enumerable.Concat(
                deferredActivities ?? Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>(),
                persistedActivities ?? Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>())
            .GroupBy(static activity => (
                    activity.RecordedUtcTicks,
                    activity.Outcome,
                    activity.ThreadId,
                    activity.ItemId,
                    activity.ToolName,
                    activity.Reason,
                    activity.OutputCount,
                    activity.FailureDetail))
            .Select(static group => group.First())
            .OrderByDescending(static activity => activity.RecordedUtcTicks));
    }

    private static void NormalizeBackgroundSchedulerRuntimeStoreForPersistence(
        BackgroundSchedulerRuntimeStoreDto store,
        long nowTicks) {
        ArgumentNullException.ThrowIfNull(store);
        if (store.PausedUntilUtcTicks > 0 && nowTicks > 0 && store.PausedUntilUtcTicks <= nowTicks) {
            store.PausedUntilUtcTicks = 0;
            store.PauseReason = string.Empty;
        }

        (store.LastAdaptiveIdleUtcTicks, store.LastAdaptiveIdleDelaySeconds, store.LastAdaptiveIdleReason) =
            NormalizeBackgroundSchedulerAdaptiveIdleState(
                store.LastAdaptiveIdleUtcTicks,
                store.LastAdaptiveIdleDelaySeconds,
                store.LastAdaptiveIdleReason,
                nowTicks);
        store.RecentActivity = NormalizeBackgroundSchedulerActivities(store.RecentActivity);
    }

    private static BackgroundSchedulerRuntimeStoreDto CloneBackgroundSchedulerRuntimeStore(BackgroundSchedulerRuntimeStoreDto source) {
        ArgumentNullException.ThrowIfNull(source);
        return new BackgroundSchedulerRuntimeStoreDto {
            Version = BackgroundSchedulerRuntimeStoreVersion,
            LastSchedulerTickUtcTicks = Math.Max(0, source.LastSchedulerTickUtcTicks),
            LastOutcome = NormalizeBackgroundSchedulerActivityText(source.LastOutcome, maxLength: 80),
            LastOutcomeUtcTicks = Math.Max(0, source.LastOutcomeUtcTicks),
            LastSuccessUtcTicks = Math.Max(0, source.LastSuccessUtcTicks),
            LastFailureUtcTicks = Math.Max(0, source.LastFailureUtcTicks),
            CompletedExecutionCount = Math.Max(0, source.CompletedExecutionCount),
            RequeuedExecutionCount = Math.Max(0, source.RequeuedExecutionCount),
            ReleasedExecutionCount = Math.Max(0, source.ReleasedExecutionCount),
            ConsecutiveFailureCount = Math.Max(0, source.ConsecutiveFailureCount),
            PausedUntilUtcTicks = Math.Max(0, source.PausedUntilUtcTicks),
            PauseReason = NormalizeBackgroundSchedulerActivityText(source.PauseReason, maxLength: 120),
            LastAdaptiveIdleUtcTicks = Math.Max(0, source.LastAdaptiveIdleUtcTicks),
            LastAdaptiveIdleDelaySeconds = Math.Max(0, source.LastAdaptiveIdleDelaySeconds),
            LastAdaptiveIdleReason = NormalizeBackgroundSchedulerActivityText(source.LastAdaptiveIdleReason, maxLength: 160),
            RecentActivity = NormalizeBackgroundSchedulerActivities(source.RecentActivity)
        };
    }

    private static bool BackgroundSchedulerRuntimeStoreEquals(
        BackgroundSchedulerRuntimeStoreDto left,
        BackgroundSchedulerRuntimeStoreDto right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return JsonSerializer.Serialize(left, BackgroundSchedulerRuntimeStoreJsonContext.Default.BackgroundSchedulerRuntimeStoreDto)
            == JsonSerializer.Serialize(right, BackgroundSchedulerRuntimeStoreJsonContext.Default.BackgroundSchedulerRuntimeStoreDto);
    }

    private static SessionCapabilityBackgroundSchedulerActivityDto[] NormalizeBackgroundSchedulerActivities(
        IEnumerable<SessionCapabilityBackgroundSchedulerActivityDto>? activities) {
        if (activities is null) {
            return Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>();
        }

        return activities
            .Where(static activity => activity is not null)
            .Select(static activity => NormalizeBackgroundSchedulerActivity(activity))
            .Where(static activity => activity.RecordedUtcTicks > 0
                || activity.Outcome.Length > 0
                || activity.ThreadId.Length > 0
                || activity.ItemId.Length > 0
                || activity.ToolName.Length > 0
                || activity.Reason.Length > 0
                || activity.OutputCount > 0
                || activity.FailureDetail.Length > 0)
            .Take(MaxBackgroundSchedulerRecentActivity)
            .ToArray();
    }

    private static SessionCapabilityBackgroundSchedulerActivityDto NormalizeBackgroundSchedulerActivity(SessionCapabilityBackgroundSchedulerActivityDto activity) {
        return new SessionCapabilityBackgroundSchedulerActivityDto {
            RecordedUtcTicks = Math.Max(0, activity.RecordedUtcTicks),
            Outcome = NormalizeBackgroundSchedulerActivityText(activity.Outcome, maxLength: 80),
            ThreadId = NormalizeBackgroundSchedulerActivityText(activity.ThreadId, maxLength: 120),
            ItemId = NormalizeBackgroundSchedulerActivityText(activity.ItemId, maxLength: 160),
            ToolName = NormalizeBackgroundSchedulerActivityText(activity.ToolName, maxLength: 120),
            Reason = NormalizeBackgroundSchedulerActivityText(activity.Reason, maxLength: 160),
            OutputCount = Math.Max(0, activity.OutputCount),
            FailureDetail = NormalizeBackgroundSchedulerActivityText(activity.FailureDetail, MaxBackgroundSchedulerActivityDetailLength)
        };
    }

    private static BackgroundSchedulerRuntimeStoreReadResult ReadBackgroundSchedulerRuntimeStoreNoThrow(string path) {
        var result = ChatServiceJsonFileStore.Read(
            path,
            maximumBytes: 512 * 1024,
            static json => JsonSerializer.Deserialize<BackgroundSchedulerRuntimeStoreDto>(
                json,
                BackgroundSchedulerRuntimeStoreReadJsonOptions),
            static store => store.Version == BackgroundSchedulerRuntimeStoreVersion,
            normalize: null,
            "Background scheduler runtime store");

        return result.State switch {
            ChatServiceJsonFileReadState.Loaded when result.Value is not null =>
                BackgroundSchedulerRuntimeStoreReadResult.Loaded(result.Value),
            ChatServiceJsonFileReadState.Empty => BackgroundSchedulerRuntimeStoreReadResult.Empty(),
            _ => BackgroundSchedulerRuntimeStoreReadResult.Invalid()
        };
    }

    private static bool WriteBackgroundSchedulerRuntimeStoreNoThrow(string path, BackgroundSchedulerRuntimeStoreDto store) {
        return ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(
                value,
                BackgroundSchedulerRuntimeStoreJsonContext.Default.BackgroundSchedulerRuntimeStoreDto),
            "Background scheduler runtime store");
    }

    private static bool TryWithBackgroundSchedulerRuntimeStoreLock<TState, TStateResult>(
        string path,
        Func<TState, TStateResult> action,
        TState state,
        out TStateResult result) {
        return ChatServiceJsonFileStore.TryWithExclusiveAccess(
            path,
            action,
            state,
            out result,
            "Background scheduler runtime store",
            BackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting);
    }

    private static (long LastAdaptiveIdleUtcTicks, int LastAdaptiveIdleDelaySeconds, string LastAdaptiveIdleReason) NormalizeBackgroundSchedulerAdaptiveIdleState(
        long lastAdaptiveIdleUtcTicks,
        int lastAdaptiveIdleDelaySeconds,
        string lastAdaptiveIdleReason,
        long nowTicks) {
        lastAdaptiveIdleUtcTicks = Math.Max(0, lastAdaptiveIdleUtcTicks);
        lastAdaptiveIdleDelaySeconds = Math.Max(0, lastAdaptiveIdleDelaySeconds);
        lastAdaptiveIdleReason = NormalizeBackgroundSchedulerActivityText(lastAdaptiveIdleReason, maxLength: 160);
        if (IsBackgroundSchedulerAdaptiveIdleActive(lastAdaptiveIdleUtcTicks, lastAdaptiveIdleDelaySeconds, nowTicks)) {
            return (lastAdaptiveIdleUtcTicks, lastAdaptiveIdleDelaySeconds, lastAdaptiveIdleReason);
        }

        return (0, 0, string.Empty);
    }

    private readonly record struct BackgroundSchedulerRuntimeStoreReadResult(
        string LoadState,
        BackgroundSchedulerRuntimeStoreDto? Store) {
        public static BackgroundSchedulerRuntimeStoreReadResult Empty() {
            return new(BackgroundSchedulerRuntimeStoreLoadStateEmpty, null);
        }

        public static BackgroundSchedulerRuntimeStoreReadResult Invalid() {
            return new(BackgroundSchedulerRuntimeStoreLoadStateInvalid, null);
        }

        public static BackgroundSchedulerRuntimeStoreReadResult Loaded(BackgroundSchedulerRuntimeStoreDto store) {
            return new(BackgroundSchedulerRuntimeStoreLoadStateLoaded, store);
        }
    }

    private readonly record struct BackgroundSchedulerRuntimeStorePersistResult(
        bool Persisted,
        BackgroundSchedulerRuntimeStoreDto PersistedStore,
        BackgroundSchedulerRuntimeStoreDto MergedStore);
}
