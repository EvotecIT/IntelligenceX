using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Policy;

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

    private static string ResolveDefaultBackgroundSchedulerRuntimeStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "background-scheduler-runtime.json");
    }

    private string ResolveBackgroundSchedulerRuntimeStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "background-scheduler-runtime.json");
        }

        return ResolveDefaultBackgroundSchedulerRuntimeStorePath();
    }

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
    }

    private void EnsureBackgroundSchedulerRuntimeStateRehydratedIfPending() {
        if (Volatile.Read(ref _backgroundSchedulerRuntimeStoreRehydratePending) == 0) {
            return;
        }

        TryRehydrateBackgroundSchedulerRuntimeState();
    }

    private void PersistBackgroundSchedulerRuntimeStateNoThrow() {
        EnsureBackgroundSchedulerRuntimeStateRehydratedIfPending();
        if (Volatile.Read(ref _backgroundSchedulerRuntimeStoreRehydratePending) != 0) {
            return;
        }

        BackgroundSchedulerRuntimeStoreDto store;
        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_backgroundSchedulerTelemetryLock) {
            NormalizeBackgroundSchedulerPauseStateNoLock(nowTicks);
            store = new BackgroundSchedulerRuntimeStoreDto {
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
        }

        var path = ResolveBackgroundSchedulerRuntimeStorePath();
        _ = TryWithBackgroundSchedulerRuntimeStoreLock(
            path,
            static state => {
                WriteBackgroundSchedulerRuntimeStoreNoThrow(state.Path, state.Store);
                return true;
            },
            (Path: path, Store: store),
            out _);
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
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return BackgroundSchedulerRuntimeStoreReadResult.Empty();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return BackgroundSchedulerRuntimeStoreReadResult.Invalid();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return BackgroundSchedulerRuntimeStoreReadResult.Empty();
            }

            var store = JsonSerializer.Deserialize<BackgroundSchedulerRuntimeStoreDto>(json, BackgroundSchedulerRuntimeStoreReadJsonOptions);
            if (store is null) {
                return BackgroundSchedulerRuntimeStoreReadResult.Invalid();
            }

            if (store.Version != BackgroundSchedulerRuntimeStoreVersion) {
                Trace.TraceWarning(
                    $"Background scheduler runtime store version mismatch: expected {BackgroundSchedulerRuntimeStoreVersion}, found {store.Version}.");
                return BackgroundSchedulerRuntimeStoreReadResult.Invalid();
            }

            return BackgroundSchedulerRuntimeStoreReadResult.Loaded(store);
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler runtime store read failed: {ex.GetType().Name}: {ex.Message}");
            return BackgroundSchedulerRuntimeStoreReadResult.Invalid();
        }
    }

    private static void WriteBackgroundSchedulerRuntimeStoreNoThrow(string path, BackgroundSchedulerRuntimeStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                store,
                BackgroundSchedulerRuntimeStoreJsonContext.Default.BackgroundSchedulerRuntimeStoreDto);
            var fileName = Path.GetFileName(path);
            var tmpName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            tmp = string.IsNullOrWhiteSpace(directory) ? tmpName : Path.Combine(directory!, tmpName);

            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                TryHardenPendingActionsStoreAclNoThrow(tmp);
                using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }

            if (File.Exists(path)) {
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            } else {
                File.Move(tmp, path);
            }

            TryHardenPendingActionsStoreAclNoThrow(path);
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler runtime store write failed: {ex.GetType().Name}: {ex.Message}");
        } finally {
            if (!string.IsNullOrWhiteSpace(tmp) && File.Exists(tmp)) {
                try {
                    File.Delete(tmp);
                } catch {
                    // Best effort only.
                }
            }
        }
    }

    private static bool TryWithBackgroundSchedulerRuntimeStoreLock<TState, TStateResult>(
        string path,
        Func<TState, TStateResult> action,
        TState state,
        out TStateResult result) {
        ArgumentNullException.ThrowIfNull(action);
        result = default!;

        var mutexName = BuildBackgroundSchedulerRuntimeStoreMutexName(path);
        var acquisitionOverride = BackgroundSchedulerRuntimeStoreLockAcquisitionOverrideForTesting;
        if (acquisitionOverride is not null) {
            var overrideResult = acquisitionOverride(path);
            if (overrideResult.HasValue) {
                if (!overrideResult.Value) {
                    Trace.TraceWarning($"Background scheduler runtime store lock timeout for '{mutexName}'.");
                    return false;
                }

                result = action(state);
                return true;
            }
        }

        Mutex? mutex = null;
        var acquired = false;
        try {
            mutex = new Mutex(initiallyOwned: false, mutexName);
            try {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
            } catch (AbandonedMutexException) {
                acquired = true;
            }

            if (!acquired) {
                Trace.TraceWarning($"Background scheduler runtime store lock timeout for '{mutexName}'.");
                return false;
            }

            result = action(state);
            return true;
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler runtime store lock failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        } finally {
            if (acquired && mutex is not null) {
                try {
                    mutex.ReleaseMutex();
                } catch {
                    // Best effort only.
                }
            }

            mutex?.Dispose();
        }
    }

    private static string BuildBackgroundSchedulerRuntimeStoreMutexName(string path) {
        var normalizedPath = NormalizeBackgroundSchedulerRuntimeStoreMutexPath(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"IntelligenceX.Chat.BackgroundSchedulerRuntimeStore.{Convert.ToHexString(hash)}";
    }

    private static string NormalizeBackgroundSchedulerRuntimeStoreMutexPath(string path) {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? ResolveDefaultBackgroundSchedulerRuntimeStorePath()
            : path.Trim();

        try {
            candidate = Path.GetFullPath(candidate);
        } catch {
            // Keep the original candidate when full path resolution fails.
        }

        return OperatingSystem.IsWindows() ? candidate.ToUpperInvariant() : candidate;
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
