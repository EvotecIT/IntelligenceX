using System;
using System.Globalization;
using System.Threading;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    internal const string StartupStatusCausePipeRetry = "pipe_retry";
    internal const string StartupStatusCauseRuntimeStart = "runtime_start";
    internal const string StartupStatusCauseMetadataRetry = "metadata_retry";
    internal const string StartupStatusCauseRuntimeDisconnect = "runtime_disconnect";
    internal const string StartupStatusCauseAuthWait = "auth_wait";
    internal const string StartupStatusCauseMetadataSync = "metadata_sync";

    private static string NormalizeStartupStatusCause(string? cause) {
        return (cause ?? string.Empty).Trim();
    }

    internal static string BuildStartupStatusCauseSuffix(string? cause) {
        var normalizedCause = NormalizeStartupStatusCause(cause);
        return normalizedCause.Length == 0 ? string.Empty : " (cause " + normalizedCause + ")";
    }

    internal static string BuildStartupStatusCauseSegment(string? cause) {
        var normalizedCause = NormalizeStartupStatusCause(cause);
        return normalizedCause.Length == 0 ? string.Empty : ", cause " + normalizedCause;
    }

    internal static string AppendStartupStatusCause(string statusText, string? cause) {
        ArgumentNullException.ThrowIfNull(statusText);
        return statusText + BuildStartupStatusCauseSuffix(cause);
    }

    internal static string BuildStartupPendingStatusText(bool requiresInteractiveSignIn, bool isAuthenticated) {
        if (requiresInteractiveSignIn && !isAuthenticated) {
            return AppendStartupStatusCause(
                "Runtime connected. Sign in to finish loading tool packs...",
                StartupStatusCauseAuthWait);
        }

        return AppendStartupStatusCause(
            "Runtime connected. Loading tool packs in background...",
            StartupStatusCauseMetadataSync);
    }

    internal static bool ShouldWaitForAuthenticationBeforeDeferredStartupMetadataSync(
        bool requiresInteractiveSignIn,
        bool isAuthenticated) {
        return requiresInteractiveSignIn && !isAuthenticated;
    }

    internal static bool ShouldQueueDeferredStartupMetadataSyncAfterLoginSuccess(
        bool shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync,
        bool loginSuccessMetadataSyncAlreadyQueued) {
        return !shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync
               && !loginSuccessMetadataSyncAlreadyQueued;
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

    private static string NormalizeStartupMetadataSyncPhase(string? phase) {
        var normalized = (phase ?? string.Empty).Trim();
        return normalized.Length == 0 ? "syncing startup metadata" : normalized;
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
        statusText = $"Runtime connected. Startup sync in progress ({phase}, {elapsedLabel}).";
        return true;
    }

    private bool TryBuildStartupPendingStatusText(SessionStatus status, out string statusText) {
        statusText = string.Empty;
        if (!ShouldShowToolsLoading(
                isConnected: _isConnected,
                hasSessionPolicy: _sessionPolicy is not null,
                startupFlowState: Volatile.Read(ref _startupFlowState),
                startupMetadataSyncQueued: Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0)) {
            return false;
        }

        var requiresInteractiveSignIn = RequiresInteractiveSignInForCurrentTransport();
        var isAuthenticated = IsEffectivelyAuthenticatedForCurrentTransport();
        if (status.Kind == SessionStatusKind.SignInRequired) {
            isAuthenticated = false;
        }

        statusText = BuildStartupPendingStatusText(requiresInteractiveSignIn, isAuthenticated);
        return true;
    }
}
