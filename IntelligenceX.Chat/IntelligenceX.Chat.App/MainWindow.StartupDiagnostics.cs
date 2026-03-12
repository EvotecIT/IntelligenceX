using System;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    internal static string ResolveStartupBootstrapCacheModeTokenFromPolicy(SessionPolicyDto? policy) {
        return policy is null
            ? StartupBootstrapContracts.CacheModeUnknown
            : StartupBootstrapContracts.ResolveCacheModeToken(policy.StartupBootstrap, policy.StartupWarnings);
    }

    private static int ParseStartupBootstrapCacheMode(string token) {
        return token switch {
            StartupBootstrapContracts.CacheModeHit => StartupBootstrapCacheModeHit,
            StartupBootstrapContracts.CacheModeMiss => StartupBootstrapCacheModeMiss,
            StartupBootstrapContracts.CacheModePersistedPreview => StartupBootstrapCacheModePersistedPreview,
            _ => StartupBootstrapCacheModeUnknown
        };
    }

    private static string ResolveStartupBootstrapCacheModeToken(int mode) {
        return mode switch {
            StartupBootstrapCacheModeHit => StartupBootstrapContracts.CacheModeHit,
            StartupBootstrapCacheModeMiss => StartupBootstrapContracts.CacheModeMiss,
            StartupBootstrapCacheModePersistedPreview => StartupBootstrapContracts.CacheModePersistedPreview,
            _ => StartupBootstrapContracts.CacheModeUnknown
        };
    }

    private static string DescribeStartupBootstrapCacheMode(int mode) {
        return mode switch {
            StartupBootstrapCacheModeHit => "Cache hit",
            StartupBootstrapCacheModeMiss => "Cache miss",
            StartupBootstrapCacheModePersistedPreview => "Persisted preview",
            _ => "Unknown"
        };
    }

    private static string DescribeStartupDiagnosticsPhaseResult(int result) {
        return result switch {
            > 0 => "success",
            0 => "failed",
            _ => "unknown"
        };
    }

    private static string DescribeStartupWatchdogClearKind(int kind) {
        return kind switch {
            StartupDiagnosticsWatchdogClearKindActive => "active_sync",
            StartupDiagnosticsWatchdogClearKindQueued => "queued_sync",
            _ => "none"
        };
    }

    private static string DescribeStartupMetadataFailureKind(string? kind) {
        var normalized = (kind ?? string.Empty).Trim();
        return normalized switch {
            "hello" => "hello",
            "list_tools" => "list_tools",
            "hello_and_list_tools" => "hello+list_tools",
            _ => "none"
        };
    }

    private static long NormalizeStartupDiagnosticsDurationMs(long value) {
        return value < 0 ? StartupDiagnosticsDurationUnknownMs : value;
    }

    private static long ResolveStartupDiagnosticsElapsedMs(DateTime utcNow, long startedUtcTicks) {
        if (startedUtcTicks <= 0) {
            return StartupDiagnosticsDurationUnknownMs;
        }

        var startedUtc = new DateTime(startedUtcTicks, DateTimeKind.Utc);
        var elapsed = utcNow - startedUtc;
        if (elapsed < TimeSpan.Zero) {
            elapsed = TimeSpan.Zero;
        }

        return Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
    }

    private static long ResolveStartupDiagnosticsDurationMs(TimeSpan elapsed) {
        if (elapsed < TimeSpan.Zero) {
            elapsed = TimeSpan.Zero;
        }

        return Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds));
    }

    private void RecordStartupBootstrapCacheMode(SessionPolicyDto? policy) {
        var modeToken = ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);
        Interlocked.Exchange(ref _startupBootstrapCacheMode, ParseStartupBootstrapCacheMode(modeToken));
        Interlocked.Exchange(ref _startupBootstrapCacheModeUpdatedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void RecordStartupHelloPhaseDiagnostics(TimeSpan elapsed, int attempts, bool success) {
        Interlocked.Exchange(ref _startupDiagnosticsHelloDurationMs, ResolveStartupDiagnosticsDurationMs(elapsed));
        Interlocked.Exchange(ref _startupDiagnosticsHelloAttempts, Math.Max(1, attempts));
        Interlocked.Exchange(ref _startupDiagnosticsHelloResult, success ? 1 : 0);
        Interlocked.Exchange(ref _startupDiagnosticsHelloUpdatedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void RecordStartupListToolsPhaseDiagnostics(TimeSpan elapsed, int attempts, bool success) {
        Interlocked.Exchange(ref _startupDiagnosticsListToolsDurationMs, ResolveStartupDiagnosticsDurationMs(elapsed));
        Interlocked.Exchange(ref _startupDiagnosticsListToolsAttempts, Math.Max(1, attempts));
        Interlocked.Exchange(ref _startupDiagnosticsListToolsResult, success ? 1 : 0);
        Interlocked.Exchange(ref _startupDiagnosticsListToolsUpdatedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void RecordStartupAuthRefreshPhaseDiagnostics(TimeSpan elapsed, int attempts, bool success) {
        Interlocked.Exchange(ref _startupDiagnosticsAuthRefreshDurationMs, ResolveStartupDiagnosticsDurationMs(elapsed));
        Interlocked.Exchange(ref _startupDiagnosticsAuthRefreshAttempts, Math.Max(1, attempts));
        Interlocked.Exchange(ref _startupDiagnosticsAuthRefreshResult, success ? 1 : 0);
        Interlocked.Exchange(ref _startupDiagnosticsAuthRefreshUpdatedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void RecordStartupMetadataSyncDiagnostics(TimeSpan elapsed, bool success) {
        Interlocked.Exchange(ref _startupDiagnosticsMetadataSyncDurationMs, ResolveStartupDiagnosticsDurationMs(elapsed));
        Interlocked.Exchange(ref _startupDiagnosticsMetadataSyncResult, success ? 1 : 0);
        Interlocked.Exchange(ref _startupDiagnosticsMetadataSyncUpdatedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void RecordStartupMetadataFailureRecoveryDiagnostics(
        string failureKind,
        bool rerunQueued,
        bool retryLimitReached) {
        var normalizedFailureKind = (failureKind ?? string.Empty).Trim();
        if (string.Equals(normalizedFailureKind, "none", StringComparison.OrdinalIgnoreCase)
            || normalizedFailureKind.Length == 0) {
            return;
        }

        lock (_startupMetadataSyncLock) {
            _startupConnectMetadataFailureLastKind = normalizedFailureKind;
        }
        Interlocked.Exchange(ref _startupConnectMetadataFailureLastUtcTicks, DateTime.UtcNow.Ticks);
        if (rerunQueued) {
            Interlocked.Increment(ref _startupConnectMetadataFailureRecoveryQueuedCount);
        }
        if (retryLimitReached) {
            Interlocked.Increment(ref _startupConnectMetadataFailureRecoveryLimitReachedCount);
        }
    }

    private void ClearStartupMetadataFailureRecoveryFailureMarker() {
        lock (_startupMetadataSyncLock) {
            _startupConnectMetadataFailureLastKind = string.Empty;
        }
        Interlocked.Exchange(ref _startupConnectMetadataFailureLastUtcTicks, 0);
    }

    private void ResetStartupMetadataFailureRecoveryDiagnostics() {
        Interlocked.Exchange(ref _startupConnectMetadataFailureAutoRetryCount, 0);
        Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount, 0);
        Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 0);
        Interlocked.Exchange(ref _startupConnectMetadataFailureRecoveryQueuedCount, 0);
        Interlocked.Exchange(ref _startupConnectMetadataFailureRecoveryLimitReachedCount, 0);
        ClearStartupMetadataFailureRecoveryFailureMarker();
    }

    private string SnapshotStartupMetadataFailureLastKind() {
        lock (_startupMetadataSyncLock) {
            return _startupConnectMetadataFailureLastKind;
        }
    }

    private void MarkStartupAuthGateWaiting() {
        var nowUtcTicks = DateTime.UtcNow.Ticks;
        if (Interlocked.CompareExchange(ref _startupDiagnosticsAuthGateWaitStartedUtcTicks, nowUtcTicks, 0) == 0) {
            Interlocked.Increment(ref _startupDiagnosticsAuthGateWaitCount);
        }

        Interlocked.Exchange(ref _startupDiagnosticsAuthGateActive, 1);
    }

    private void MarkStartupAuthGateResolved() {
        var nowUtc = DateTime.UtcNow;
        var startedUtcTicks = Interlocked.Exchange(ref _startupDiagnosticsAuthGateWaitStartedUtcTicks, 0);
        if (startedUtcTicks > 0) {
            var elapsedMs = ResolveStartupDiagnosticsElapsedMs(nowUtc, startedUtcTicks);
            Interlocked.Exchange(ref _startupDiagnosticsAuthGateLastWaitMs, elapsedMs);
            Interlocked.Exchange(ref _startupDiagnosticsAuthGateLastResolvedUtcTicks, nowUtc.Ticks);
        }

        Interlocked.Exchange(ref _startupDiagnosticsAuthGateActive, 0);
    }

    private void RecordStartupWatchdogClearDiagnostics(int kind) {
        if (kind == StartupDiagnosticsWatchdogClearKindActive) {
            Interlocked.Increment(ref _startupDiagnosticsWatchdogClearActiveCount);
        } else if (kind == StartupDiagnosticsWatchdogClearKindQueued) {
            Interlocked.Increment(ref _startupDiagnosticsWatchdogClearQueuedCount);
        } else {
            return;
        }

        Interlocked.Exchange(ref _startupDiagnosticsWatchdogLastClearKind, kind);
        Interlocked.Exchange(ref _startupDiagnosticsWatchdogLastClearUtcTicks, DateTime.UtcNow.Ticks);
    }

    private object BuildStartupDiagnosticsState() {
        var cacheMode = Volatile.Read(ref _startupBootstrapCacheMode);
        var cacheModeToken = ResolveStartupBootstrapCacheModeToken((int)cacheMode);
        var nowUtc = DateTime.UtcNow;

        long? helloMs = null;
        var helloDuration = NormalizeStartupDiagnosticsDurationMs(Interlocked.Read(ref _startupDiagnosticsHelloDurationMs));
        if (helloDuration >= 0) {
            helloMs = helloDuration;
        }

        long? listToolsMs = null;
        var listToolsDuration = NormalizeStartupDiagnosticsDurationMs(Interlocked.Read(ref _startupDiagnosticsListToolsDurationMs));
        if (listToolsDuration >= 0) {
            listToolsMs = listToolsDuration;
        }

        long? authRefreshMs = null;
        var authRefreshDuration = NormalizeStartupDiagnosticsDurationMs(Interlocked.Read(ref _startupDiagnosticsAuthRefreshDurationMs));
        if (authRefreshDuration >= 0) {
            authRefreshMs = authRefreshDuration;
        }

        long? metadataSyncMs = null;
        var metadataSyncDuration = NormalizeStartupDiagnosticsDurationMs(Interlocked.Read(ref _startupDiagnosticsMetadataSyncDurationMs));
        if (metadataSyncDuration >= 0) {
            metadataSyncMs = metadataSyncDuration;
        }

        var authGateStartedUtcTicks = Interlocked.Read(ref _startupDiagnosticsAuthGateWaitStartedUtcTicks);
        var authGateCurrentWaitMs = ResolveStartupDiagnosticsElapsedMs(nowUtc, authGateStartedUtcTicks);
        long? authGateCurrentWait = authGateCurrentWaitMs >= 0 ? authGateCurrentWaitMs : null;

        var authGateLastWaitValue = NormalizeStartupDiagnosticsDurationMs(Interlocked.Read(ref _startupDiagnosticsAuthGateLastWaitMs));
        long? authGateLastWait = authGateLastWaitValue >= 0 ? authGateLastWaitValue : null;

        var authGateActive = Volatile.Read(ref _startupDiagnosticsAuthGateActive) != 0 || authGateStartedUtcTicks > 0;
        var metadataFailureLastKind = SnapshotStartupMetadataFailureLastKind();

        return new {
            cache = new {
                mode = cacheModeToken,
                label = DescribeStartupBootstrapCacheMode(cacheMode),
                updatedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupBootstrapCacheModeUpdatedUtcTicks))
            },
            hello = new {
                durationMs = helloMs,
                attempts = Math.Max(0, Volatile.Read(ref _startupDiagnosticsHelloAttempts)),
                result = DescribeStartupDiagnosticsPhaseResult(Volatile.Read(ref _startupDiagnosticsHelloResult)),
                updatedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupDiagnosticsHelloUpdatedUtcTicks))
            },
            listTools = new {
                durationMs = listToolsMs,
                attempts = Math.Max(0, Volatile.Read(ref _startupDiagnosticsListToolsAttempts)),
                result = DescribeStartupDiagnosticsPhaseResult(Volatile.Read(ref _startupDiagnosticsListToolsResult)),
                updatedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupDiagnosticsListToolsUpdatedUtcTicks))
            },
            authRefresh = new {
                durationMs = authRefreshMs,
                attempts = Math.Max(0, Volatile.Read(ref _startupDiagnosticsAuthRefreshAttempts)),
                result = DescribeStartupDiagnosticsPhaseResult(Volatile.Read(ref _startupDiagnosticsAuthRefreshResult)),
                updatedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupDiagnosticsAuthRefreshUpdatedUtcTicks))
            },
            metadataSync = new {
                durationMs = metadataSyncMs,
                result = DescribeStartupDiagnosticsPhaseResult(Volatile.Read(ref _startupDiagnosticsMetadataSyncResult)),
                inProgress = Volatile.Read(ref _startupMetadataSyncInProgress) != 0,
                queued = Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0,
                queuedSinceLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupConnectMetadataDeferredQueuedUtcTicks)),
                failureRecovery = new {
                    retriesConsumed = Math.Max(0, Volatile.Read(ref _startupConnectMetadataFailureAutoRetryCount)),
                    retryLimit = Math.Max(0, StartupDeferredMetadataFailureAutoRetryLimit),
                    rerunRequested = Volatile.Read(ref _startupConnectMetadataDeferredRerunRequested) != 0,
                    queuedCount = Math.Max(0, Interlocked.Read(ref _startupConnectMetadataFailureRecoveryQueuedCount)),
                    limitReachedCount = Math.Max(0, Interlocked.Read(ref _startupConnectMetadataFailureRecoveryLimitReachedCount)),
                    lastFailureKind = DescribeStartupMetadataFailureKind(metadataFailureLastKind),
                    lastFailureLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupConnectMetadataFailureLastUtcTicks))
                },
                updatedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupDiagnosticsMetadataSyncUpdatedUtcTicks))
            },
            authGate = new {
                active = authGateActive,
                waitCount = Math.Max(0, Interlocked.Read(ref _startupDiagnosticsAuthGateWaitCount)),
                currentWaitMs = authGateCurrentWait,
                lastWaitMs = authGateLastWait,
                waitingSinceLocal = FormatUtcTicksAsLocalTimestamp(authGateStartedUtcTicks),
                lastResolvedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupDiagnosticsAuthGateLastResolvedUtcTicks))
            },
            watchdog = new {
                activeClears = Math.Max(0, Interlocked.Read(ref _startupDiagnosticsWatchdogClearActiveCount)),
                queuedClears = Math.Max(0, Interlocked.Read(ref _startupDiagnosticsWatchdogClearQueuedCount)),
                lastKind = DescribeStartupWatchdogClearKind(Volatile.Read(ref _startupDiagnosticsWatchdogLastClearKind)),
                lastClearedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _startupDiagnosticsWatchdogLastClearUtcTicks))
            }
        };
    }
}
