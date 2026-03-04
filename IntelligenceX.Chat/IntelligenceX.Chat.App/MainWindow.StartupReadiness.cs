using System;
using System.Globalization;
using System.Threading;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    internal const string StartupStatusPhaseStartupConnect = "startup_connect";
    internal const string StartupStatusPhaseStartupMetadataSync = "startup_metadata_sync";
    internal const string StartupStatusPhaseStartupAuthWait = "startup_auth_wait";

    internal const string StartupStatusCausePipeRetry = "pipe_retry";
    internal const string StartupStatusCauseRuntimeStart = "runtime_start";
    internal const string StartupStatusCauseMetadataRetry = "metadata_retry";
    internal const string StartupStatusCauseRuntimeDisconnect = "runtime_disconnect";
    internal const string StartupStatusCauseAuthWait = "auth_wait";
    internal const string StartupStatusCauseMetadataSync = "metadata_sync";

    private static string NormalizeStartupStatusPhase(string? phase) {
        return (phase ?? string.Empty).Trim();
    }

    private static string NormalizeStartupStatusCause(string? cause) {
        return (cause ?? string.Empty).Trim();
    }

    internal static string BuildStartupStatusContextSuffix(string? phase, string? cause) {
        var normalizedPhase = NormalizeStartupStatusPhase(phase);
        var normalizedCause = NormalizeStartupStatusCause(cause);
        if (normalizedPhase.Length == 0 && normalizedCause.Length == 0) {
            return string.Empty;
        }

        if (normalizedPhase.Length == 0) {
            return " (cause " + normalizedCause + ")";
        }

        if (normalizedCause.Length == 0) {
            return " (phase " + normalizedPhase + ")";
        }

        return " (phase " + normalizedPhase + ", cause " + normalizedCause + ")";
    }

    internal static string BuildStartupStatusContextSegment(string? phase, string? cause) {
        var normalizedPhase = NormalizeStartupStatusPhase(phase);
        var normalizedCause = NormalizeStartupStatusCause(cause);
        if (normalizedPhase.Length == 0 && normalizedCause.Length == 0) {
            return string.Empty;
        }

        if (normalizedPhase.Length == 0) {
            return ", cause " + normalizedCause;
        }

        if (normalizedCause.Length == 0) {
            return ", phase " + normalizedPhase;
        }

        return ", phase " + normalizedPhase + ", cause " + normalizedCause;
    }

    internal static string AppendStartupStatusContext(string statusText, string? phase, string? cause) {
        ArgumentNullException.ThrowIfNull(statusText);
        return statusText + BuildStartupStatusContextSuffix(phase, cause);
    }

    internal static string BuildStartupStatusCauseSuffix(string? cause) {
        return BuildStartupStatusContextSuffix(phase: null, cause);
    }

    internal static string BuildStartupStatusCauseSegment(string? cause) {
        return BuildStartupStatusContextSegment(phase: null, cause);
    }

    internal static string AppendStartupStatusCause(string statusText, string? cause) {
        return AppendStartupStatusContext(statusText, phase: null, cause);
    }

    internal static string BuildStartupPendingStatusText(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress) {
        if (requiresInteractiveSignIn && !isAuthenticated) {
            return AppendStartupStatusContext(
                loginInProgress
                    ? "Runtime connected. Finish sign-in in browser to continue loading tool packs..."
                    : "Runtime connected. Sign in to finish loading tool packs...",
                StartupStatusPhaseStartupAuthWait,
                StartupStatusCauseAuthWait);
        }

        return AppendStartupStatusContext(
            "Runtime connected. Loading tool packs in background...",
            StartupStatusPhaseStartupMetadataSync,
            StartupStatusCauseMetadataSync);
    }

    internal static string BuildStartupMetadataSyncRecoveryStatusText(bool retryQueued) {
        if (retryQueued) {
            return "Runtime is ready. Retrying tool metadata sync in background...";
        }

        return "Runtime is ready. Tool metadata sync is degraded; some tools may be unavailable.";
    }

    internal static string BuildStartupMetadataSyncPersistedPreviewStatusText(bool refreshQueued) {
        if (refreshQueued) {
            return "Runtime connected. Finalizing tool catalog after startup preview...";
        }

        return "Runtime is ready. Tool catalog preview is still active; refresh tools to load final metadata.";
    }

    private string BuildStartupPendingOrAuthVerificationStatusText(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress) {
        if (ShouldShowStartupAuthVerificationPending(
                isConnectedStatus: _isConnected,
                requiresInteractiveSignIn: requiresInteractiveSignIn,
                isAuthenticated: isAuthenticated,
                loginInProgress: loginInProgress,
                hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot())) {
            return AppendStartupStatusContext(
                "Runtime connected. Verifying sign-in state before loading tool packs...",
                StartupStatusPhaseStartupAuthWait,
                StartupStatusCauseAuthWait);
        }

        return BuildStartupPendingStatusText(
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress);
    }

    internal static bool ShouldWaitForAuthenticationBeforeDeferredStartupMetadataSync(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress = false) {
        return requiresInteractiveSignIn && !isAuthenticated && loginInProgress;
    }

    internal static bool ShouldQueueDeferredStartupMetadataSyncAfterLoginSuccess(
        bool shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync,
        bool loginSuccessMetadataSyncAlreadyQueued) {
        return !shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync
               && !loginSuccessMetadataSyncAlreadyQueued;
    }

    internal static bool ShouldQueueDeferredStartupMetadataSyncAfterAuthenticationReady(
        bool isConnected,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasSessionPolicy) {
        if (!isConnected || loginInProgress || hasSessionPolicy) {
            return false;
        }

        return !requiresInteractiveSignIn || isAuthenticated;
    }

    internal static bool ShouldRequestDeferredStartupMetadataSyncRerun(
        bool metadataSyncAlreadyQueued,
        bool requestRerunIfBusy) {
        return metadataSyncAlreadyQueued && requestRerunIfBusy;
    }

    internal static bool ShouldDispatchDeferredStartupMetadataSyncRerun(
        bool rerunRequested,
        bool shutdownRequested,
        bool isConnected) {
        return rerunRequested && !shutdownRequested && isConnected;
    }

    internal static bool ShouldKeepStartupAuthGateWaitingOnDeferredMetadataSyncExit(
        bool exitedForAuthWait,
        bool shutdownRequested,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress) {
        if (!exitedForAuthWait || shutdownRequested) {
            return false;
        }

        return requiresInteractiveSignIn && !isAuthenticated && loginInProgress;
    }

    internal static string ResolveDeferredStartupMetadataFailureKind(
        bool helloPhaseSucceeded,
        bool toolCatalogPhaseSucceeded) {
        if (helloPhaseSucceeded && toolCatalogPhaseSucceeded) {
            return "none";
        }

        if (!helloPhaseSucceeded && !toolCatalogPhaseSucceeded) {
            return "hello_and_list_tools";
        }

        return helloPhaseSucceeded ? "list_tools" : "hello";
    }

    internal static bool ShouldRequestDeferredStartupMetadataFailureRecoveryRerun(
        bool isConnected,
        bool shutdownRequested,
        bool helloPhaseSucceeded,
        bool toolCatalogPhaseSucceeded,
        int retriesConsumed,
        int retryLimit) {
        if (!isConnected || shutdownRequested || retryLimit <= 0 || retriesConsumed >= retryLimit) {
            return false;
        }

        return !helloPhaseSucceeded || !toolCatalogPhaseSucceeded;
    }

    internal static bool ShouldRequestDeferredStartupMetadataPersistedPreviewRefreshRerun(
        bool metadataSyncSucceeded,
        int startupBootstrapCacheMode,
        bool isConnected,
        bool shutdownRequested,
        int retriesConsumed,
        int retryLimit) {
        if (!metadataSyncSucceeded
            || !isConnected
            || shutdownRequested
            || retryLimit <= 0
            || retriesConsumed >= retryLimit) {
            return false;
        }

        return startupBootstrapCacheMode == StartupBootstrapCacheModePersistedPreview;
    }

    internal static bool HasReachedDeferredStartupMetadataPersistedPreviewRefreshRetryLimit(
        bool metadataSyncSucceeded,
        int startupBootstrapCacheMode,
        int retriesConsumed,
        int retryLimit) {
        if (!metadataSyncSucceeded || retryLimit <= 0 || retriesConsumed < retryLimit) {
            return false;
        }

        return startupBootstrapCacheMode == StartupBootstrapCacheModePersistedPreview;
    }

    internal static bool ShouldRetryDeferredStartupMetadataPhaseAttempt(Exception ex) {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is TimeoutException or OperationCanceledException) {
            return true;
        }

        return IsDisconnectedError(ex);
    }

    internal static bool ShouldRunDeferredStartupMetadataInlineAuthRefresh(
        bool startupAuthDeferredQueued,
        bool shutdownRequested) {
        return !shutdownRequested && !startupAuthDeferredQueued;
    }

    internal static bool TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
        ref int retriesConsumed,
        int retryLimit) {
        if (retryLimit <= 0) {
            return false;
        }

        while (true) {
            var observed = Volatile.Read(ref retriesConsumed);
            if (observed >= retryLimit) {
                return false;
            }

            if (Interlocked.CompareExchange(ref retriesConsumed, observed + 1, observed) == observed) {
                return true;
            }
        }
    }

    internal static bool ShouldDelayStartupModelProfileSyncUntilMetadataReady(
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress) {
        return startupMetadataSyncQueued || startupMetadataSyncInProgress;
    }

    internal static bool ShouldShowStartupAuthVerificationPending(
        bool isConnectedStatus,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasExplicitUnauthenticatedProbeSnapshot) {
        return isConnectedStatus
               && requiresInteractiveSignIn
               && !isAuthenticated
               && !loginInProgress
               && !hasExplicitUnauthenticatedProbeSnapshot;
    }

    internal static bool ShouldClearStaleActiveStartupMetadataSync(
        TimeSpan metadataSyncElapsed,
        TimeSpan staleThreshold) {
        return staleThreshold > TimeSpan.Zero && metadataSyncElapsed >= staleThreshold;
    }

    internal static bool ShouldClearStaleQueuedStartupMetadataSync(
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress,
        TimeSpan queuedElapsed,
        TimeSpan staleThreshold) {
        if (!startupMetadataSyncQueued || startupMetadataSyncInProgress) {
            return false;
        }

        return staleThreshold > TimeSpan.Zero && queuedElapsed >= staleThreshold;
    }

    private bool ApplyStartupMetadataSyncWatchdog() {
        var cleared = false;
        if (Volatile.Read(ref _startupMetadataSyncInProgress) != 0) {
            long startedUtcTicks;
            lock (_startupMetadataSyncLock) {
                startedUtcTicks = _startupMetadataSyncStartedUtcTicks;
            }

            var elapsed = TimeSpan.Zero;
            if (startedUtcTicks > 0) {
                var startedUtc = new DateTime(startedUtcTicks, DateTimeKind.Utc);
                elapsed = DateTime.UtcNow - startedUtc;
                if (elapsed < TimeSpan.Zero) {
                    elapsed = TimeSpan.Zero;
                }
            }

            if (ShouldClearStaleActiveStartupMetadataSync(
                    metadataSyncElapsed: elapsed,
                    staleThreshold: StartupDeferredMetadataStaleWatchdogTimeout)) {
                StartupLog.Write(
                    "StartupConnect.metadata_sync watchdog_clear_active elapsed="
                    + FormatStartupPhaseDuration(elapsed));
                RecordStartupWatchdogClearDiagnostics(StartupDiagnosticsWatchdogClearKindActive);
                EndStartupMetadataSyncTracking();
                Interlocked.Exchange(ref _startupConnectMetadataDeferredQueued, 0);
                Interlocked.Exchange(ref _startupConnectMetadataDeferredQueuedUtcTicks, 0);
                cleared = true;
            }
        }

        var startupMetadataSyncQueued = Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0;
        var startupMetadataSyncInProgress = Volatile.Read(ref _startupMetadataSyncInProgress) != 0;
        if (startupMetadataSyncQueued
            && TryResolveStartupMetadataSyncQueuedElapsed(out var queuedElapsed)
            && ShouldClearStaleQueuedStartupMetadataSync(
                startupMetadataSyncQueued: startupMetadataSyncQueued,
                startupMetadataSyncInProgress: startupMetadataSyncInProgress,
                queuedElapsed: queuedElapsed,
                staleThreshold: StartupDeferredMetadataStaleWatchdogTimeout)) {
            StartupLog.Write(
                "StartupConnect.metadata_sync watchdog_clear_queued elapsed="
                + FormatStartupPhaseDuration(queuedElapsed));
            RecordStartupWatchdogClearDiagnostics(StartupDiagnosticsWatchdogClearKindQueued);
            Interlocked.Exchange(ref _startupConnectMetadataDeferredQueued, 0);
            Interlocked.Exchange(ref _startupConnectMetadataDeferredQueuedUtcTicks, 0);
            EndStartupMetadataSyncTracking();
            cleared = true;
        }

        return cleared;
    }

    private static string NormalizeStartupMetadataSyncPhase(string? phase) {
        var normalized = (phase ?? string.Empty).Trim();
        return normalized.Length == 0 ? "syncing startup metadata" : normalized;
    }

    private bool TryResolveStartupMetadataSyncQueuedElapsed(out TimeSpan elapsed) {
        elapsed = TimeSpan.Zero;
        var queuedUtcTicks = Volatile.Read(ref _startupConnectMetadataDeferredQueuedUtcTicks);
        if (queuedUtcTicks <= 0) {
            return false;
        }

        var startedUtc = new DateTime(queuedUtcTicks, DateTimeKind.Utc);
        elapsed = DateTime.UtcNow - startedUtc;
        if (elapsed < TimeSpan.Zero) {
            elapsed = TimeSpan.Zero;
        }

        return true;
    }

    private void BeginStartupMetadataSyncTracking(string phase) {
        var normalizedPhase = NormalizeStartupMetadataSyncPhase(phase);
        lock (_startupMetadataSyncLock) {
            _startupMetadataSyncStartedUtcTicks = DateTime.UtcNow.Ticks;
            _startupMetadataSyncPhase = normalizedPhase;
            Volatile.Write(ref _startupMetadataSyncInProgress, 1);
        }
    }

    private void UpdateStartupMetadataSyncPhase(string phase) {
        var normalizedPhase = NormalizeStartupMetadataSyncPhase(phase);
        lock (_startupMetadataSyncLock) {
            if (Volatile.Read(ref _startupMetadataSyncInProgress) == 0) {
                _startupMetadataSyncStartedUtcTicks = DateTime.UtcNow.Ticks;
                Volatile.Write(ref _startupMetadataSyncInProgress, 1);
            }

            _startupMetadataSyncPhase = normalizedPhase;
        }
    }

    private void EndStartupMetadataSyncTracking() {
        lock (_startupMetadataSyncLock) {
            Volatile.Write(ref _startupMetadataSyncInProgress, 0);
            _startupMetadataSyncStartedUtcTicks = 0;
            _startupMetadataSyncPhase = string.Empty;
        }
    }

    private bool TryBuildStartupMetadataSyncStatusText(out string statusText) {
        statusText = string.Empty;
        if (ApplyStartupMetadataSyncWatchdog()) {
            return false;
        }

        if (Volatile.Read(ref _startupMetadataSyncInProgress) == 0) {
            return false;
        }

        string phase;
        long startedUtcTicks;
        lock (_startupMetadataSyncLock) {
            phase = NormalizeStartupMetadataSyncPhase(_startupMetadataSyncPhase);
            startedUtcTicks = _startupMetadataSyncStartedUtcTicks;
        }

        var elapsed = TimeSpan.Zero;
        if (startedUtcTicks > 0) {
            var startedUtc = new DateTime(startedUtcTicks, DateTimeKind.Utc);
            elapsed = DateTime.UtcNow - startedUtc;
            if (elapsed < TimeSpan.Zero) {
                elapsed = TimeSpan.Zero;
            }
        }

        var elapsedLabel = elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
            : Math.Max(1, (long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms";
        statusText = AppendStartupStatusContext(
            $"Runtime connected. Startup sync in progress ({phase}, {elapsedLabel}).",
            StartupStatusPhaseStartupMetadataSync,
            StartupStatusCauseMetadataSync);
        return true;
    }

    private bool TryBuildStartupPendingStatusText(SessionStatus status, out string statusText) {
        statusText = string.Empty;
        _ = ApplyStartupMetadataSyncWatchdog();

        var startupMetadataSyncQueued = Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0;

        if (!ShouldShowToolsLoading(
                isConnected: _isConnected,
                hasSessionPolicy: _sessionPolicy is not null,
                startupFlowState: Volatile.Read(ref _startupFlowState),
                startupMetadataSyncQueued: startupMetadataSyncQueued)) {
            return false;
        }

        var requiresInteractiveSignIn = RequiresInteractiveSignInForCurrentTransport();
        var isAuthenticated = IsEffectivelyAuthenticatedForCurrentTransport();
        if (status.Kind is SessionStatusKind.SignInRequired
            or SessionStatusKind.WaitingForSignIn
            or SessionStatusKind.CompleteSignInInBrowser
            or SessionStatusKind.OpeningSignIn) {
            isAuthenticated = false;
        }
        var loginInProgress = status.Kind is SessionStatusKind.WaitingForSignIn
            or SessionStatusKind.CompleteSignInInBrowser
            or SessionStatusKind.OpeningSignIn
            || _loginInProgress;
        if (ShouldShowStartupAuthVerificationPending(
                isConnectedStatus: status.Kind is SessionStatusKind.Connected,
                requiresInteractiveSignIn: requiresInteractiveSignIn,
                isAuthenticated: isAuthenticated,
                loginInProgress: loginInProgress,
                hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot())) {
            statusText = AppendStartupStatusContext(
                "Runtime connected. Verifying sign-in state before loading tool packs...",
                StartupStatusPhaseStartupAuthWait,
                StartupStatusCauseAuthWait);
            return true;
        }

        statusText = BuildStartupPendingStatusText(requiresInteractiveSignIn, isAuthenticated, loginInProgress);
        return true;
    }
}
