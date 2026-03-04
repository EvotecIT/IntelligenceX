using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    internal static bool ShouldRunStartupDispatchAuthPrewarm(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool startupMetadataSyncQueued = false,
        bool startupMetadataSyncInProgress = false) {
        if (!requiresInteractiveSignIn) {
            return false;
        }

        if (isAuthenticated) {
            return false;
        }

        if (loginInProgress) {
            return false;
        }

        if (startupMetadataSyncQueued || startupMetadataSyncInProgress) {
            return false;
        }

        return true;
    }

    internal static string BuildStartupDispatchPrewarmSummary(
        long connectMs,
        bool authProbeAttempted,
        bool? authProbeAuthenticated,
        bool authProbeInconclusive,
        long? authProbeMs) {
        var safeConnectMs = Math.Max(0, connectMs);
        var connectionSegment = safeConnectMs == 0
            ? "runtime already connected"
            : "runtime connected in " + safeConnectMs.ToString(CultureInfo.InvariantCulture) + "ms";
        if (!authProbeAttempted) {
            return "Startup prewarm ready: " + connectionSegment + " (auth check deferred).";
        }

        var safeAuthMs = Math.Max(0, authProbeMs.GetValueOrDefault(0));
        if (authProbeInconclusive) {
            return "Startup prewarm ready: " + connectionSegment
                   + "; auth check inconclusive after "
                   + safeAuthMs.ToString(CultureInfo.InvariantCulture)
                   + "ms (will verify on first message).";
        }

        if (authProbeAuthenticated == true) {
            return "Startup prewarm ready: " + connectionSegment
                   + "; account verified in "
                   + safeAuthMs.ToString(CultureInfo.InvariantCulture)
                   + "ms.";
        }

        return "Startup prewarm ready: " + connectionSegment
               + "; sign-in still required (checked in "
               + safeAuthMs.ToString(CultureInfo.InvariantCulture)
               + "ms).";
    }

    private void QueueDeferredStartupDispatchPrewarm() {
        if (_shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupDispatchPrewarmDeferredQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(StartupDeferredDispatchPrewarmDelay).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }

                StartupLog.Write("StartupPhase.DispatchPrewarm begin");
                var connectStartedUtc = DateTime.UtcNow;
                var connected = false;
                long connectMs;
                if (_client is not null && _isConnected) {
                    connected = true;
                    connectMs = 0;
                    StartupLog.Write("StartupPhase.DispatchPrewarm connect_reused");
                } else {
                    connected = await EnsureConnectedAsync(
                            connectBudgetOverride: StartupDispatchPrewarmConnectBudget,
                            deferPostConnectMetadataSync: true)
                        .ConfigureAwait(false);
                    connectMs = TryComputeElapsedMs(connectStartedUtc, DateTime.UtcNow);
                }
                if (!connected) {
                    StartupLog.Write("StartupPhase.DispatchPrewarm connect_failed");
                    await AppendSystemBestEffortAsync(
                        "Startup prewarm couldn't connect to runtime. First message may need a reconnect.")
                        .ConfigureAwait(false);
                    return;
                }

                StartupLog.Write("StartupPhase.DispatchPrewarm connect_done");
                var shouldProbeAuth = ShouldRunStartupDispatchAuthPrewarm(
                    requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                    isAuthenticated: _isAuthenticated,
                    loginInProgress: _loginInProgress,
                    startupMetadataSyncQueued: Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0,
                    startupMetadataSyncInProgress: Volatile.Read(ref _startupMetadataSyncInProgress) != 0);
                if (!shouldProbeAuth) {
                    await AppendSystemBestEffortAsync(
                        BuildStartupDispatchPrewarmSummary(
                            connectMs: connectMs,
                            authProbeAttempted: false,
                            authProbeAuthenticated: null,
                            authProbeInconclusive: false,
                            authProbeMs: null)).ConfigureAwait(false);
                    return;
                }

                var authProbeStartedUtc = DateTime.UtcNow;
                var authOutcome = await ProbeAuthenticationStateForDispatchAsync(EnsureLoginFastPathProbeTimeout).ConfigureAwait(false);
                var authProbeMs = TryComputeElapsedMs(authProbeStartedUtc, DateTime.UtcNow);
                StartupLog.Write(
                    "StartupPhase.DispatchPrewarm auth_probe="
                    + authOutcome.ToString().ToLowerInvariant());
                if (authOutcome == DispatchAuthenticationProbeOutcome.Authenticated
                    && ShouldQueueDeferredStartupMetadataSyncAfterAuthenticationReady(
                        isConnected: _isConnected,
                        requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                        isAuthenticated: IsEffectivelyAuthenticatedForCurrentTransport(),
                        loginInProgress: _loginInProgress,
                        hasSessionPolicy: _sessionPolicy is not null)) {
                    StartupLog.Write("StartupPhase.DispatchPrewarm metadata_sync queue_after_auth_probe");
                    QueueDeferredStartupConnectMetadataSync(requestRerunIfBusy: true);
                }

                await AppendSystemBestEffortAsync(
                    BuildStartupDispatchPrewarmSummary(
                        connectMs: connectMs,
                        authProbeAttempted: true,
                        authProbeAuthenticated: authOutcome == DispatchAuthenticationProbeOutcome.Authenticated
                            ? true
                            : authOutcome == DispatchAuthenticationProbeOutcome.Unauthenticated
                                ? false
                                : null,
                        authProbeInconclusive: authOutcome == DispatchAuthenticationProbeOutcome.Unknown,
                        authProbeMs: authProbeMs)).ConfigureAwait(false);
            } catch (Exception ex) {
                StartupLog.Write("StartupPhase.DispatchPrewarm failed: " + ex.Message);
                await AppendSystemBestEffortAsync("Startup prewarm failed: " + ex.Message).ConfigureAwait(false);
            } finally {
                Interlocked.Exchange(ref _startupDispatchPrewarmDeferredQueued, 0);
            }
        });
    }

    private bool IsStartupInteractivePriorityRequested() {
        return Volatile.Read(ref _startupInteractivePriorityRequested) != 0;
    }

    private void MarkStartupInteractivePriorityRequested() {
        Interlocked.Exchange(ref _startupInteractivePriorityRequested, 1);
    }

    private async Task<bool> WaitForStartupDeferredBackgroundTurnIdleAsync(string phasePrefix) {
        if (!IsStartupInteractivePriorityRequested() || !IsTurnDispatchInProgress()) {
            return true;
        }

        StartupLog.Write(phasePrefix + " deferred_for_active_turn");
        var waited = Stopwatch.StartNew();
        while (!_shutdownRequested && IsTurnDispatchInProgress()) {
            if (waited.Elapsed >= StartupDeferredInteractiveBackgroundTurnWaitTimeout) {
                StartupLog.Write(phasePrefix + " resumed_after_turn_wait_timeout");
                return true;
            }

            await Task.Delay(StartupDeferredInteractiveBackgroundPollInterval).ConfigureAwait(false);
        }

        if (_shutdownRequested) {
            StartupLog.Write(phasePrefix + " canceled_shutdown");
            return false;
        }

        StartupLog.Write(phasePrefix + " resumed_after_turn");
        return true;
    }

    private async Task<bool> WaitForStartupDeferredMetadataSyncIdleAsync(string phasePrefix) {
        var startupMetadataSyncQueued = Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0;
        var startupMetadataSyncInProgress = Volatile.Read(ref _startupMetadataSyncInProgress) != 0;
        if (!ShouldDelayStartupModelProfileSyncUntilMetadataReady(startupMetadataSyncQueued, startupMetadataSyncInProgress)) {
            return true;
        }

        StartupLog.Write(phasePrefix + " waiting_for_metadata_sync");
        var waited = Stopwatch.StartNew();
        while (!_shutdownRequested) {
            startupMetadataSyncQueued = Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0;
            startupMetadataSyncInProgress = Volatile.Read(ref _startupMetadataSyncInProgress) != 0;
            if (!ShouldDelayStartupModelProfileSyncUntilMetadataReady(startupMetadataSyncQueued, startupMetadataSyncInProgress)) {
                StartupLog.Write(phasePrefix + " resumed_after_metadata_sync");
                return true;
            }

            if (waited.Elapsed >= StartupDeferredMetadataPhaseTimeout) {
                StartupLog.Write(phasePrefix + " metadata_sync_wait_timeout");
                return false;
            }

            await Task.Delay(StartupDeferredInteractiveBackgroundPollInterval).ConfigureAwait(false);
        }

        StartupLog.Write(phasePrefix + " canceled_shutdown");
        return false;
    }
}
