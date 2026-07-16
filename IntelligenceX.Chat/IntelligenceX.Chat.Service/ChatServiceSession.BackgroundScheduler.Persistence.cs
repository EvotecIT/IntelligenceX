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
    private int _backgroundSchedulerRuntimeStoreRehydratePending;
    private string _backgroundSchedulerRuntimeStoreLoadState = BackgroundSchedulerRuntimeStoreLoadStateEmpty;
    private BackgroundSchedulerRuntimeStoreDto? _backgroundSchedulerRuntimeStoreDeferredPersistStore;

    private static string ResolveDefaultBackgroundSchedulerRuntimeStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("background-scheduler-runtime.json");

    private string ResolveBackgroundSchedulerRuntimeStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "background-scheduler-runtime.json");

    private void TryRehydrateBackgroundSchedulerRuntimeState() {
        var path = ResolveBackgroundSchedulerRuntimeStorePath();
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

        Interlocked.Exchange(ref _backgroundSchedulerRuntimeStoreRehydratePending, 0);
        lock (_backgroundSchedulerTelemetryLock) {
            _backgroundSchedulerRuntimeStoreLoadState = readResult.LoadState;
        }

        if (readResult.Store is null) {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var store = readResult.Store;
        BackgroundSchedulerRuntimeStoreDto? deferredPersistStore;
        lock (_backgroundSchedulerTelemetryLock) {
            deferredPersistStore = _backgroundSchedulerRuntimeStoreDeferredPersistStore;
        }

        if (deferredPersistStore is not null) {
            store = MergeBackgroundSchedulerRuntimeStore(store, deferredPersistStore);
        }

        if (store is null) {
            return;
        }

        (store.LastAdaptiveIdleUtcTicks, store.LastAdaptiveIdleDelaySeconds, store.LastAdaptiveIdleReason) =
            NormalizeBackgroundSchedulerAdaptiveIdleState(
                store.LastAdaptiveIdleUtcTicks,
                store.LastAdaptiveIdleDelaySeconds,
                store.LastAdaptiveIdleReason,
                nowTicks);

        if (string.IsNullOrWhiteSpace(store.LastOutcome)
            && store.LastOutcomeUtcTicks <= 0
            && store.LastSuccessUtcTicks <= 0
            && store.LastFailureUtcTicks <= 0
            && store.CompletedExecutionCount <= 0
            && store.RequeuedExecutionCount <= 0
            && store.ReleasedExecutionCount <= 0
            && store.ConsecutiveFailureCount <= 0
            && store.PausedUntilUtcTicks <= 0
            && store.LastAdaptiveIdleUtcTicks <= 0
            && (store.RecentActivity?.Length ?? 0) == 0) {
            return;
        }

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

        if (deferredPersistStore is null) {
            return;
        }

        if (TryPersistBackgroundSchedulerRuntimeStoreSnapshot(path, store)) {
            lock (_backgroundSchedulerTelemetryLock) {
                if (_backgroundSchedulerRuntimeStoreDeferredPersistStore is not null
                    && BackgroundSchedulerRuntimeStoreEquals(_backgroundSchedulerRuntimeStoreDeferredPersistStore, deferredPersistStore)) {
                    _backgroundSchedulerRuntimeStoreDeferredPersistStore = null;
                }
            }
            return;
        }

        lock (_backgroundSchedulerTelemetryLock) {
            _backgroundSchedulerRuntimeStoreDeferredPersistStore = store;
        }
    }

    private void EnsureBackgroundSchedulerRuntimeStateRehydratedIfPending() {
        if (Volatile.Read(ref _backgroundSchedulerRuntimeStoreRehydratePending) == 0) {
            return;
        }

        TryRehydrateBackgroundSchedulerRuntimeState();
    }

    private void PersistBackgroundSchedulerRuntimeStateNoThrow() {
        EnsureBackgroundSchedulerRuntimeStateRehydratedIfPending();
        var store = CaptureBackgroundSchedulerRuntimeStoreSnapshot();
        if (Volatile.Read(ref _backgroundSchedulerRuntimeStoreRehydratePending) != 0) {
            lock (_backgroundSchedulerTelemetryLock) {
                _backgroundSchedulerRuntimeStoreDeferredPersistStore = store;
            }
            return;
        }

        _ = TryPersistBackgroundSchedulerRuntimeStoreSnapshot(
            ResolveBackgroundSchedulerRuntimeStorePath(),
            store);
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

    private static bool TryPersistBackgroundSchedulerRuntimeStoreSnapshot(string path, BackgroundSchedulerRuntimeStoreDto store) {
        return TryWithBackgroundSchedulerRuntimeStoreLock(
            path,
            static state => {
                WriteBackgroundSchedulerRuntimeStoreNoThrow(state.Path, state.Store);
                return true;
            },
            (Path: path, Store: store),
            out _);
    }

    private static BackgroundSchedulerRuntimeStoreDto MergeBackgroundSchedulerRuntimeStore(
        BackgroundSchedulerRuntimeStoreDto? persistedStore,
        BackgroundSchedulerRuntimeStoreDto deferredStore) {
        ArgumentNullException.ThrowIfNull(deferredStore);

        if (persistedStore is null) {
            return CloneBackgroundSchedulerRuntimeStore(deferredStore);
        }

        var merged = CloneBackgroundSchedulerRuntimeStore(persistedStore);
        merged.LastSchedulerTickUtcTicks = Math.Max(merged.LastSchedulerTickUtcTicks, deferredStore.LastSchedulerTickUtcTicks);
        if (deferredStore.LastOutcomeUtcTicks >= merged.LastOutcomeUtcTicks && deferredStore.LastOutcomeUtcTicks > 0) {
            merged.LastOutcomeUtcTicks = deferredStore.LastOutcomeUtcTicks;
            merged.LastOutcome = NormalizeBackgroundSchedulerActivityText(deferredStore.LastOutcome, maxLength: 80);
        }

        merged.LastSuccessUtcTicks = Math.Max(merged.LastSuccessUtcTicks, deferredStore.LastSuccessUtcTicks);
        merged.LastFailureUtcTicks = Math.Max(merged.LastFailureUtcTicks, deferredStore.LastFailureUtcTicks);
        merged.CompletedExecutionCount = Math.Max(0, merged.CompletedExecutionCount) + Math.Max(0, deferredStore.CompletedExecutionCount);
        merged.RequeuedExecutionCount = Math.Max(0, merged.RequeuedExecutionCount) + Math.Max(0, deferredStore.RequeuedExecutionCount);
        merged.ReleasedExecutionCount = Math.Max(0, merged.ReleasedExecutionCount) + Math.Max(0, deferredStore.ReleasedExecutionCount);

        if (deferredStore.LastSuccessUtcTicks > persistedStore.LastSuccessUtcTicks) {
            merged.ConsecutiveFailureCount = Math.Max(0, deferredStore.ConsecutiveFailureCount);
        } else if (deferredStore.LastFailureUtcTicks > persistedStore.LastFailureUtcTicks
            && deferredStore.ConsecutiveFailureCount > 0) {
            merged.ConsecutiveFailureCount = Math.Max(0, merged.ConsecutiveFailureCount) + Math.Max(0, deferredStore.ConsecutiveFailureCount);
        }

        if (deferredStore.LastOutcomeUtcTicks > 0
            && deferredStore.PausedUntilUtcTicks <= 0
            && string.IsNullOrWhiteSpace(deferredStore.PauseReason)) {
            merged.PausedUntilUtcTicks = 0;
            merged.PauseReason = string.Empty;
        } else if (deferredStore.PausedUntilUtcTicks > 0 || !string.IsNullOrWhiteSpace(deferredStore.PauseReason)) {
            merged.PausedUntilUtcTicks = Math.Max(0, deferredStore.PausedUntilUtcTicks);
            merged.PauseReason = NormalizeBackgroundSchedulerActivityText(deferredStore.PauseReason, maxLength: 120);
        }

        if (deferredStore.LastOutcomeUtcTicks > 0
            && deferredStore.LastAdaptiveIdleUtcTicks <= 0
            && deferredStore.LastAdaptiveIdleDelaySeconds <= 0
            && string.IsNullOrWhiteSpace(deferredStore.LastAdaptiveIdleReason)) {
            merged.LastAdaptiveIdleUtcTicks = 0;
            merged.LastAdaptiveIdleDelaySeconds = 0;
            merged.LastAdaptiveIdleReason = string.Empty;
        } else if (deferredStore.LastAdaptiveIdleUtcTicks > 0
            || deferredStore.LastAdaptiveIdleDelaySeconds > 0
            || !string.IsNullOrWhiteSpace(deferredStore.LastAdaptiveIdleReason)) {
            merged.LastAdaptiveIdleUtcTicks = Math.Max(0, deferredStore.LastAdaptiveIdleUtcTicks);
            merged.LastAdaptiveIdleDelaySeconds = Math.Max(0, deferredStore.LastAdaptiveIdleDelaySeconds);
            merged.LastAdaptiveIdleReason = NormalizeBackgroundSchedulerActivityText(deferredStore.LastAdaptiveIdleReason, maxLength: 160);
        }

        merged.RecentActivity = MergeBackgroundSchedulerActivities(merged.RecentActivity, deferredStore.RecentActivity);
        return merged;
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
            .Select(static group => group.First()));
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

    private static void WriteBackgroundSchedulerRuntimeStoreNoThrow(string path, BackgroundSchedulerRuntimeStoreDto store) {
        ChatServiceJsonFileStore.Write(
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
}
