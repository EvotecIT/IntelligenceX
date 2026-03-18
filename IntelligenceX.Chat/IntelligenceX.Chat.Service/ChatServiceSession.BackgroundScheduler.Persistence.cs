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
    private const int BackgroundSchedulerRuntimeStoreVersion = 1;
    private static readonly JsonSerializerOptions BackgroundSchedulerRuntimeStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class BackgroundSchedulerRuntimeStoreDto {
        public int Version { get; set; } = BackgroundSchedulerRuntimeStoreVersion;
        public long LastSchedulerTickUtcTicks { get; set; }
        public string LastOutcome { get; set; } = string.Empty;
        public long LastOutcomeUtcTicks { get; set; }
        public long LastSuccessUtcTicks { get; set; }
        public long LastFailureUtcTicks { get; set; }
        public int CompletedExecutionCount { get; set; }
        public int RequeuedExecutionCount { get; set; }
        public int ReleasedExecutionCount { get; set; }
        public int ConsecutiveFailureCount { get; set; }
        public long PausedUntilUtcTicks { get; set; }
        public string PauseReason { get; set; } = string.Empty;
        public long LastAdaptiveIdleUtcTicks { get; set; }
        public int LastAdaptiveIdleDelaySeconds { get; set; }
        public string LastAdaptiveIdleReason { get; set; } = string.Empty;
        public SessionCapabilityBackgroundSchedulerActivityDto[] RecentActivity { get; set; } = Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>();
    }

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
        var store = WithBackgroundSchedulerRuntimeStoreLock(path, static runtimeStorePath => ReadBackgroundSchedulerRuntimeStoreNoThrow(runtimeStorePath));

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

    private void PersistBackgroundSchedulerRuntimeStateNoThrow() {
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
        }

        var path = ResolveBackgroundSchedulerRuntimeStorePath();
        WithBackgroundSchedulerRuntimeStoreLock(
            path,
            static state => WriteBackgroundSchedulerRuntimeStoreNoThrow(state.Path, state.Store),
            (Path: path, Store: store));
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

    private static BackgroundSchedulerRuntimeStoreDto ReadBackgroundSchedulerRuntimeStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new BackgroundSchedulerRuntimeStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return new BackgroundSchedulerRuntimeStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new BackgroundSchedulerRuntimeStoreDto();
            }

            var store = JsonSerializer.Deserialize<BackgroundSchedulerRuntimeStoreDto>(json, BackgroundSchedulerRuntimeStoreJsonOptions);
            return store is null || store.Version != BackgroundSchedulerRuntimeStoreVersion
                ? new BackgroundSchedulerRuntimeStoreDto()
                : store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler runtime store read failed: {ex.GetType().Name}: {ex.Message}");
            return new BackgroundSchedulerRuntimeStoreDto();
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

            var json = JsonSerializer.Serialize(store, BackgroundSchedulerRuntimeStoreJsonOptions);
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

    private static T WithBackgroundSchedulerRuntimeStoreLock<T>(string path, Func<string, T> action) {
        ArgumentNullException.ThrowIfNull(action);
        return WithBackgroundSchedulerRuntimeStoreLock(path, action, path);
    }

    private static TStateResult WithBackgroundSchedulerRuntimeStoreLock<TState, TStateResult>(
        string path,
        Func<TState, TStateResult> action,
        TState state) {
        ArgumentNullException.ThrowIfNull(action);

        var mutexName = BuildBackgroundSchedulerRuntimeStoreMutexName(path);
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
                return action(state);
            }

            return action(state);
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler runtime store lock failed: {ex.GetType().Name}: {ex.Message}");
            return action(state);
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

    private static void WithBackgroundSchedulerRuntimeStoreLock<TState>(
        string path,
        Action<TState> action,
        TState state) {
        ArgumentNullException.ThrowIfNull(action);
        _ = WithBackgroundSchedulerRuntimeStoreLock(
            path,
            static localState => {
                localState.Action(localState.State);
                return 0;
            },
            (Action: action, State: state));
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
}
