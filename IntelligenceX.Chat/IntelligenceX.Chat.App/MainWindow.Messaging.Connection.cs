using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    internal static TimeSpan ResolveStartupInitialPipeConnectTimeout(bool fromUserAction, bool hasTrackedRunningServiceProcess) {
        if (fromUserAction || hasTrackedRunningServiceProcess) {
            return StartupInitialPipeConnectTimeout;
        }

        return StartupInitialPipeConnectColdStartTimeout;
    }

    /// <summary>
    /// Applies settlement grace only when we expect an in-flight reconnect to succeed (user action or known-running sidecar).
    /// Cold startup probes skip grace because they always continue into ensure-sidecar + retry.
    /// </summary>
    internal static bool ShouldUseStartupInitialConnectSettlementGrace(bool fromUserAction, bool hasTrackedRunningServiceProcess) {
        return fromUserAction || hasTrackedRunningServiceProcess;
    }

    internal static bool ShouldSkipStartupInitialPipeConnectProbe(
        bool fromUserAction,
        bool captureStartupPhaseTelemetry,
        bool hasTrackedRunningServiceProcess) {
        return captureStartupPhaseTelemetry
               && !fromUserAction
               && !hasTrackedRunningServiceProcess;
    }

    internal static bool ShouldAttemptStartupConnectRecoveryAfterRetryFailure(
        bool fromUserAction,
        bool captureStartupPhaseTelemetry,
        Exception? connectException,
        bool serviceProcessExited,
        int recoveryAttemptsConsumed,
        int recoveryAttemptLimit) {
        if (fromUserAction
            || !captureStartupPhaseTelemetry
            || connectException is null
            || recoveryAttemptLimit <= 0
            || recoveryAttemptsConsumed >= recoveryAttemptLimit) {
            return false;
        }

        if (serviceProcessExited) {
            return true;
        }

        if (connectException is TimeoutException or OperationCanceledException) {
            return true;
        }

        return IsDisconnectedError(connectException);
    }

    internal static TimeSpan? ResolveStartupConnectBudget(bool fromUserAction, bool captureStartupPhaseTelemetry, TimeSpan? overrideBudget = null) {
        if (overrideBudget.HasValue) {
            return overrideBudget.Value > TimeSpan.Zero ? overrideBudget : null;
        }

        if (fromUserAction || !captureStartupPhaseTelemetry) {
            return null;
        }

        return StartupConnectBudget;
    }

    internal static bool TryResolveStartupConnectAttemptTimeout(
        TimeSpan requestedTimeout,
        TimeSpan? startupConnectBudget,
        TimeSpan startupConnectElapsed,
        out TimeSpan timeout) {
        timeout = TimeSpan.Zero;
        if (requestedTimeout <= TimeSpan.Zero) {
            return false;
        }

        if (!startupConnectBudget.HasValue) {
            timeout = requestedTimeout;
            return true;
        }

        var remaining = startupConnectBudget.Value - startupConnectElapsed;
        if (remaining <= TimeSpan.Zero) {
            return false;
        }

        timeout = remaining < requestedTimeout ? remaining : requestedTimeout;
        if (timeout < StartupConnectMinAttemptTimeout) {
            timeout = TimeSpan.Zero;
            return false;
        }

        return true;
    }

    internal static TimeSpan ResolveConnectAttemptHardTimeout(TimeSpan timeout) {
        if (timeout <= TimeSpan.Zero) {
            return TimeSpan.Zero;
        }

        try {
            var hardTimeout = timeout + StartupConnectAttemptHardTimeoutGrace;
            return hardTimeout < timeout ? timeout : hardTimeout;
        } catch (OverflowException) {
            return timeout;
        }
    }

    private void MarkDispatchConnectFailure() {
        Interlocked.Exchange(ref _lastDispatchConnectFailureUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void ClearDispatchConnectFailure() {
        Interlocked.Exchange(ref _lastDispatchConnectFailureUtcTicks, 0);
    }

    private bool TryGetDispatchConnectFailureCooldownRemaining(out TimeSpan remaining) {
        remaining = TimeSpan.Zero;
        var failureTicks = Interlocked.Read(ref _lastDispatchConnectFailureUtcTicks);
        if (failureTicks <= 0) {
            return false;
        }

        var failureUtc = new DateTime(failureTicks, DateTimeKind.Utc);
        var elapsed = DateTime.UtcNow - failureUtc;
        if (elapsed < TimeSpan.Zero) {
            elapsed = TimeSpan.Zero;
        }

        var cooldownRemaining = DispatchConnectFailureCooldown - elapsed;
        if (cooldownRemaining <= TimeSpan.Zero) {
            Interlocked.Exchange(ref _lastDispatchConnectFailureUtcTicks, 0);
            return false;
        }

        remaining = cooldownRemaining;
        return true;
    }

    private static TimeoutException CreateStartupConnectBudgetExceededException(TimeSpan budget, TimeSpan elapsed) {
        var budgetMs = Math.Round(Math.Max(0, budget.TotalMilliseconds));
        var elapsedMs = Math.Round(Math.Max(0, elapsed.TotalMilliseconds));
        return new TimeoutException($"Startup connect budget exhausted after {elapsedMs.ToString(CultureInfo.InvariantCulture)}ms (budget: {budgetMs.ToString(CultureInfo.InvariantCulture)}ms).");
    }

    internal static bool ShouldApplyDispatchConnectFailureCooldown(bool hasTrackedRunningServiceProcess, bool prioritizeLatency) {
        return !hasTrackedRunningServiceProcess && !prioritizeLatency;
    }

    internal static bool ShouldProbeExistingClientAfterJoinedConnectTimeout(bool joinedExistingInFlight, TimeSpan connectBudget) {
        return joinedExistingInFlight && connectBudget > TimeSpan.Zero;
    }

    internal static bool ShouldDeferStartupModelProfileSync(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDeferStartupHelloProbe(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDeferStartupToolCatalogSync(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDeferStartupAuthRefresh(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    private static string FormatStartupConnectDurationLabel(TimeSpan duration) {
        if (duration <= TimeSpan.Zero) {
            return "0ms";
        }

        if (duration.TotalSeconds >= 1d) {
            return duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        return Math.Max(1, Math.Round(duration.TotalMilliseconds))
            .ToString(CultureInfo.InvariantCulture)
               + "ms";
    }

    internal static string BuildStartupConnectAttemptStatusText(
        string phaseLabel,
        int attemptNumber,
        int totalAttempts,
        TimeSpan timeout,
        string? churnCause = null) {
        var phase = (phaseLabel ?? string.Empty).Trim();
        if (phase.Length == 0) {
            phase = "connecting";
        }

        var cause = (churnCause ?? string.Empty).Trim();
        var boundedAttempt = Math.Max(1, attemptNumber);
        var boundedTotal = Math.Max(boundedAttempt, totalAttempts);
        var timeoutLabel = FormatStartupConnectDurationLabel(timeout);
        return "Starting runtime... ("
               + phase
               + BuildStartupStatusContextSegment(StartupStatusPhaseStartupConnect, cause)
               + ", attempt "
               + boundedAttempt.ToString(CultureInfo.InvariantCulture)
               + "/"
               + boundedTotal.ToString(CultureInfo.InvariantCulture)
               + ", timeout "
               + timeoutLabel
               + ")";
    }

    internal static string BuildStartupConnectRetryDelayStatusText(
        int nextAttemptNumber,
        int totalAttempts,
        TimeSpan delay,
        string? churnCause = null) {
        var cause = (churnCause ?? string.Empty).Trim();
        var boundedAttempt = Math.Max(1, nextAttemptNumber);
        var boundedTotal = Math.Max(boundedAttempt, totalAttempts);
        var delayLabel = FormatStartupConnectDurationLabel(delay);
        return "Starting runtime... (waiting "
               + delayLabel
               + BuildStartupStatusContextSegment(StartupStatusPhaseStartupConnect, cause)
               + " before retry "
               + boundedAttempt.ToString(CultureInfo.InvariantCulture)
               + "/"
               + boundedTotal.ToString(CultureInfo.InvariantCulture)
               + ")";
    }

    internal static int ResolveStartupConnectRetryDisplayAttemptNumber(int retryAttemptIndex) {
        var boundedRetryIndex = Math.Max(0, retryAttemptIndex);
        // Display attempt numbering includes the initial cold connect attempt.
        return boundedRetryIndex + 2;
    }

    internal static int ResolveStartupConnectRetryDisplayTotalAttempts(int retryAttemptSlots) {
        var boundedRetrySlots = Math.Max(0, retryAttemptSlots);
        // Total displayed attempts include the initial cold connect attempt.
        return boundedRetrySlots + 1;
    }

    internal static bool ShouldDeferStartupWebViewPostInitialization(bool captureStartupPhaseTelemetry) {
        return captureStartupPhaseTelemetry;
    }

    internal static bool ShouldDelayDeferredStartupMetadataSyncForInteractiveSignIn(
        bool deferStartupMetadataSync,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress) {
        return deferStartupMetadataSync
               && requiresInteractiveSignIn
               && !isAuthenticated
               && loginInProgress;
    }

    internal readonly record struct DeferredStartupMetadataPlan(
        bool DeferStartupMetadataSync,
        bool QueueDeferredConnectMetadataSync,
        bool SkipDeferredMetadataUntilAuthenticated,
        bool DeferAuthRefresh,
        bool DeferModelProfileSync);

    internal static DeferredStartupMetadataPlan ResolveDeferredStartupMetadataPlan(
        bool deferPostConnectMetadataSync,
        bool deferStartupHelloProbe,
        bool deferStartupToolCatalogSync,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool deferStartupAuthRefresh,
        bool deferStartupModelProfileSync) {
        var deferStartupMetadataSync = deferPostConnectMetadataSync
                                       || deferStartupHelloProbe
                                       || deferStartupToolCatalogSync;
        var skipDeferredMetadataUntilAuthenticated = ShouldDelayDeferredStartupMetadataSyncForInteractiveSignIn(
            deferStartupMetadataSync: deferStartupMetadataSync,
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            isAuthenticated: isAuthenticated,
            loginInProgress: loginInProgress);

        return new DeferredStartupMetadataPlan(
            DeferStartupMetadataSync: deferStartupMetadataSync,
            QueueDeferredConnectMetadataSync: deferStartupMetadataSync,
            SkipDeferredMetadataUntilAuthenticated: skipDeferredMetadataUntilAuthenticated,
            DeferAuthRefresh: !skipDeferredMetadataUntilAuthenticated
                              && (deferPostConnectMetadataSync || deferStartupAuthRefresh),
            DeferModelProfileSync: !skipDeferredMetadataUntilAuthenticated
                                   && (deferPostConnectMetadataSync || deferStartupModelProfileSync));
    }

    private async Task ConnectAsync(
        bool fromUserAction = false,
        TimeSpan? connectBudgetOverride = null,
        bool deferPostConnectMetadataSync = false) {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try {
            var captureStartupPhaseTelemetry = !fromUserAction && Volatile.Read(ref _startupFlowState) == StartupFlowStateRunning;
            void LogStartupConnectPhase(string phase, string state) {
                if (captureStartupPhaseTelemetry) {
                    StartupLog.Write("StartupConnect." + phase + " " + state);
                }
            }
            var startupConnectBudget = ResolveStartupConnectBudget(fromUserAction, captureStartupPhaseTelemetry, connectBudgetOverride);
            var startupConnectStopwatch = startupConnectBudget.HasValue ? Stopwatch.StartNew() : null;
            var startupBudgetExhaustedLogged = false;

            static long RoundMs(TimeSpan value) {
                return (long)Math.Round(Math.Max(0, value.TotalMilliseconds));
            }

            void LogStartupConnectDetail(string detail) {
                if (!captureStartupPhaseTelemetry) {
                    return;
                }

                StartupLog.Write("StartupConnect." + detail);
            }

            void LogStartupConnectBudgetExhausted(TimeSpan elapsed) {
                if (startupBudgetExhaustedLogged || !startupConnectBudget.HasValue) {
                    return;
                }

                startupBudgetExhaustedLogged = true;
                LogStartupConnectPhase("budget", "exhausted");
                StartupLog.Write(
                    "StartupConnect.budget exhausted after "
                    + RoundMs(elapsed).ToString(CultureInfo.InvariantCulture)
                    + "ms (budget "
                    + RoundMs(startupConnectBudget.Value).ToString(CultureInfo.InvariantCulture)
                    + "ms).");
            }

            bool TryResolveConnectTimeout(TimeSpan requestedTimeout, out TimeSpan timeout) {
                var elapsed = startupConnectStopwatch?.Elapsed ?? TimeSpan.Zero;
                if (TryResolveStartupConnectAttemptTimeout(requestedTimeout, startupConnectBudget, elapsed, out timeout)) {
                    return true;
                }

                LogStartupConnectBudgetExhausted(elapsed);
                timeout = TimeSpan.Zero;
                return false;
            }

            string FormatRemainingBudgetForLog() {
                if (!startupConnectBudget.HasValue || startupConnectStopwatch is null) {
                    return "n/a";
                }

                var remaining = startupConnectBudget.Value - startupConnectStopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero) {
                    return "0";
                }

                return RoundMs(remaining).ToString(CultureInfo.InvariantCulture);
            }

            async Task SetConnectProgressStatusAsync(string text) {
                if (_shutdownRequested || _isSending || _turnStartupInProgress) {
                    return;
                }

                await SetStatusAsync(text, SessionStatusTone.Warn).ConfigureAwait(false);
            }

            void LogConnectAttemptStart(string phase, int attemptNumber, TimeSpan requestedTimeout, TimeSpan timeout, TimeSpan hardTimeout) {
                LogStartupConnectDetail(
                    phase
                    + " attempt="
                    + attemptNumber.ToString(CultureInfo.InvariantCulture)
                    + " start requested_timeout_ms="
                    + RoundMs(requestedTimeout).ToString(CultureInfo.InvariantCulture)
                    + " timeout_ms="
                    + RoundMs(timeout).ToString(CultureInfo.InvariantCulture)
                    + " hard_timeout_ms="
                    + RoundMs(hardTimeout).ToString(CultureInfo.InvariantCulture)
                    + " budget_remaining_ms="
                    + FormatRemainingBudgetForLog());
            }

            void LogConnectAttemptResult(string phase, int attemptNumber, bool success, TimeSpan attemptElapsed, Exception? exception) {
                var status = success ? "success" : "failed";
                var message = phase
                              + " attempt="
                              + attemptNumber.ToString(CultureInfo.InvariantCulture)
                              + " "
                              + status
                              + " elapsed_ms="
                              + RoundMs(attemptElapsed).ToString(CultureInfo.InvariantCulture);
                if (!success && exception is not null) {
                    message += " error_type=" + exception.GetType().Name;
                    if (!string.IsNullOrWhiteSpace(exception.Message)) {
                        message += " error=" + exception.Message;
                    }
                }

                LogStartupConnectDetail(message);
                if (attemptElapsed >= StartupConnectAttemptOutlierThreshold) {
                    LogStartupConnectDetail(
                        phase
                        + " attempt="
                        + attemptNumber.ToString(CultureInfo.InvariantCulture)
                        + " outlier elapsed_ms="
                        + RoundMs(attemptElapsed).ToString(CultureInfo.InvariantCulture)
                        + " threshold_ms="
                        + RoundMs(StartupConnectAttemptOutlierThreshold).ToString(CultureInfo.InvariantCulture));
                }
            }

            Exception CreateBudgetExceededException() {
                var budget = startupConnectBudget.GetValueOrDefault(TimeSpan.Zero);
                var elapsed = startupConnectStopwatch?.Elapsed ?? TimeSpan.Zero;
                return CreateStartupConnectBudgetExceededException(budget, elapsed);
            }

            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                _isConnected = true;
                ClearDispatchConnectFailure();
                StopAutoReconnectLoop();
                await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
                return;
            }

            _isConnected = false;
            await SetStatusAsync(SessionStatus.Connecting()).ConfigureAwait(false);
            await SetConnectProgressStatusAsync(
                    AppendStartupStatusContext(
                        "Starting runtime... (connecting to service)",
                        StartupStatusPhaseStartupConnect,
                        StartupStatusCausePipeRetry))
                .ConfigureAwait(false);
            await DisposeClientAsync().ConfigureAwait(false);
            var requiresInteractiveSignIn = RequiresInteractiveSignInForCurrentTransport();
            var preserveInteractiveAuthState = ShouldPreserveInteractiveAuthStateOnReconnect(
                requiresInteractiveSignIn: requiresInteractiveSignIn,
                isAuthenticated: _isAuthenticated,
                hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot(),
                loginInProgress: _loginInProgress);
            var resetEnsureLoginProbeCache = ShouldResetEnsureLoginProbeCacheOnReconnectAuthReset(
                requiresInteractiveSignIn: requiresInteractiveSignIn,
                preserveInteractiveAuthState: preserveInteractiveAuthState);
            if (!requiresInteractiveSignIn) {
                SetInteractiveAuthenticationUnknown();
                _loginInProgress = false;
                ApplyNonNativeAuthenticationStateIfNeeded();
                Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
            } else if (!preserveInteractiveAuthState) {
                SetInteractiveAuthenticationUnknown();
                _loginInProgress = false;
                Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
            } else if (!_isAuthenticated) {
                _authenticatedAccountId = null;
            }
            if (resetEnsureLoginProbeCache) {
                ResetEnsureLoginProbeCache();
            }

            var pipeName = _pipeName;
            if (_serviceProcess is not null && !_serviceProcess.HasExited && !string.Equals(_servicePipeName, pipeName, StringComparison.OrdinalIgnoreCase)) {
                pipeName = _servicePipeName!;
            }

            var client = new ChatServiceClient();
            client.MessageReceived += OnServiceMessage;
            client.Disconnected += OnClientDisconnected;
            Exception? initialConnectException = null;
            var hasTrackedRunningServiceProcess = _serviceProcess is not null && !_serviceProcess.HasExited;
            var initialPipeConnectTimeout = ResolveStartupInitialPipeConnectTimeout(fromUserAction, hasTrackedRunningServiceProcess);
            var useInitialSettlementGrace = ShouldUseStartupInitialConnectSettlementGrace(fromUserAction, hasTrackedRunningServiceProcess);
            var skipInitialPipeConnectProbe = ShouldSkipStartupInitialPipeConnectProbe(
                fromUserAction: fromUserAction,
                captureStartupPhaseTelemetry: captureStartupPhaseTelemetry,
                hasTrackedRunningServiceProcess: hasTrackedRunningServiceProcess);
            var connected = false;
            var initialAttemptElapsed = TimeSpan.Zero;
            Stopwatch? initialAttemptStopwatch = null;

            if (skipInitialPipeConnectProbe) {
                LogStartupConnectPhase("pipe_connect.initial", "skipped_cold_start");
                LogStartupConnectDetail("pipe_connect.initial skipped reason=cold_start_no_tracked_service");
            } else {
                try {
                    LogStartupConnectPhase("pipe_connect.initial", "begin");
                    if (!TryResolveConnectTimeout(initialPipeConnectTimeout, out var initialAttemptTimeout)) {
                        throw CreateBudgetExceededException();
                    }

                    var initialHardTimeout = ResolveConnectAttemptHardTimeout(initialAttemptTimeout);
                    LogConnectAttemptStart("pipe_connect.initial", 1, initialPipeConnectTimeout, initialAttemptTimeout, initialHardTimeout);
                    await SetConnectProgressStatusAsync(
                            BuildStartupConnectAttemptStatusText(
                                phaseLabel: "connecting to service",
                                attemptNumber: 1,
                                totalAttempts: 1,
                                timeout: initialAttemptTimeout,
                                churnCause: StartupStatusCausePipeRetry))
                        .ConfigureAwait(false);
                    initialAttemptStopwatch = Stopwatch.StartNew();
                    await ConnectClientWithTimeoutAsync(
                        client,
                        pipeName,
                        initialAttemptTimeout,
                        initialHardTimeout,
                        allowSettlementGrace: useInitialSettlementGrace).ConfigureAwait(false);
                    initialAttemptElapsed = initialAttemptStopwatch.Elapsed;
                    LogConnectAttemptResult("pipe_connect.initial", 1, success: true, attemptElapsed: initialAttemptElapsed, exception: null);
                    LogStartupConnectPhase("pipe_connect.initial", "done");
                    connected = true;
                } catch (Exception ex) {
                    if (initialAttemptStopwatch is not null) {
                        initialAttemptElapsed = initialAttemptStopwatch.Elapsed;
                    }

                    LogConnectAttemptResult("pipe_connect.initial", 1, success: false, attemptElapsed: initialAttemptElapsed, exception: ex);
                    LogStartupConnectPhase("pipe_connect.initial", "failed");
                    initialConnectException = ex;
                }
            }

            if (!connected) {
                Exception? sidecarConnectException = null;
                var recoveryAttemptsConsumed = 0;
                if (startupConnectBudget.HasValue
                    && startupConnectStopwatch is not null
                    && startupConnectStopwatch.Elapsed >= startupConnectBudget.Value) {
                    LogStartupConnectBudgetExhausted(startupConnectStopwatch.Elapsed);
                    LogStartupConnectPhase("ensure_sidecar", "skipped_budget");
                    sidecarConnectException = CreateBudgetExceededException();
                } else {
                    await SetConnectProgressStatusAsync(
                            AppendStartupStatusContext(
                                "Starting runtime... (starting local service)",
                                StartupStatusPhaseStartupConnect,
                                StartupStatusCauseRuntimeStart))
                        .ConfigureAwait(false);
                    LogStartupConnectPhase("ensure_sidecar", "begin");
                    var ensureSidecarStopwatch = Stopwatch.StartNew();
                    var sidecarRunning = await EnsureServiceRunningAsync(pipeName).ConfigureAwait(false);
                    LogStartupConnectDetail("ensure_sidecar elapsed_ms=" + RoundMs(ensureSidecarStopwatch.Elapsed).ToString(CultureInfo.InvariantCulture));
                    if (ensureSidecarStopwatch.Elapsed >= StartupConnectAttemptOutlierThreshold) {
                        LogStartupConnectDetail(
                            "ensure_sidecar outlier elapsed_ms="
                            + RoundMs(ensureSidecarStopwatch.Elapsed).ToString(CultureInfo.InvariantCulture)
                            + " threshold_ms="
                            + RoundMs(StartupConnectAttemptOutlierThreshold).ToString(CultureInfo.InvariantCulture));
                    }

                    if (sidecarRunning) {
                        await SetConnectProgressStatusAsync(
                                AppendStartupStatusContext(
                                    "Starting runtime... (retrying service connection)",
                                    StartupStatusPhaseStartupConnect,
                                    StartupStatusCausePipeRetry))
                            .ConfigureAwait(false);
                        LogStartupConnectPhase("ensure_sidecar", "done");
                        LogStartupConnectPhase("pipe_connect.retry", "begin");
                        var startupRetryDisplayTotalAttempts = ResolveStartupConnectRetryDisplayTotalAttempts(StartupConnectRetryTimeouts.Length);
                        for (var attempt = 0; attempt < StartupConnectRetryTimeouts.Length; attempt++) {
                            if (_serviceProcess is { HasExited: true }) {
                                sidecarConnectException = new InvalidOperationException("Service process exited before pipe connect retry could begin.");
                                break;
                            }

                            var requestedRetryTimeout = StartupConnectRetryTimeouts[attempt];
                            if (!fromUserAction && requestedRetryTimeout > StartupConnectRetryAttemptCapNonInteractive) {
                                requestedRetryTimeout = StartupConnectRetryAttemptCapNonInteractive;
                            }
                            if (!TryResolveConnectTimeout(requestedRetryTimeout, out var retryTimeout)) {
                                sidecarConnectException = CreateBudgetExceededException();
                                break;
                            }

                            Stopwatch? retryAttemptStopwatch = null;
                            var retryAttemptNumber = attempt + 1;
                            var retryDisplayAttemptNumber = ResolveStartupConnectRetryDisplayAttemptNumber(attempt);
                            try {
                                var retryHardTimeout = ResolveConnectAttemptHardTimeout(retryTimeout);
                                LogConnectAttemptStart("pipe_connect.retry", retryAttemptNumber, requestedRetryTimeout, retryTimeout, retryHardTimeout);
                                await SetConnectProgressStatusAsync(
                                        BuildStartupConnectAttemptStatusText(
                                            phaseLabel: "retrying service connection",
                                            attemptNumber: retryDisplayAttemptNumber,
                                            totalAttempts: startupRetryDisplayTotalAttempts,
                                            timeout: retryTimeout,
                                            churnCause: StartupStatusCausePipeRetry))
                                    .ConfigureAwait(false);
                                retryAttemptStopwatch = Stopwatch.StartNew();
                                await ConnectClientWithTimeoutAsync(client, pipeName, retryTimeout, retryHardTimeout).ConfigureAwait(false);
                                var retryAttemptElapsed = retryAttemptStopwatch.Elapsed;
                                LogConnectAttemptResult("pipe_connect.retry", retryAttemptNumber, success: true, attemptElapsed: retryAttemptElapsed, exception: null);
                                sidecarConnectException = null;
                                connected = true;
                                break;
                            } catch (Exception ex2) {
                                var retryAttemptElapsed = retryAttemptStopwatch?.Elapsed ?? TimeSpan.Zero;
                                LogConnectAttemptResult("pipe_connect.retry", retryAttemptNumber, success: false, attemptElapsed: retryAttemptElapsed, exception: ex2);
                                sidecarConnectException = ex2;
                                if (startupConnectBudget.HasValue && ex2 is TimeoutException) {
                                    LogStartupConnectDetail(
                                        "pipe_connect.retry attempt="
                                        + retryAttemptNumber.ToString(CultureInfo.InvariantCulture)
                                        + " guardrail=abort_after_timeout");
                                    break;
                                }

                                if (_serviceProcess is { HasExited: true }) {
                                    break;
                                }

                                if (attempt + 1 < StartupConnectRetryTimeouts.Length) {
                                    if (!TryResolveConnectTimeout(StartupConnectRetryDelay, out var retryDelay)) {
                                        sidecarConnectException = CreateBudgetExceededException();
                                        break;
                                    }

                                    await SetConnectProgressStatusAsync(
                                            BuildStartupConnectRetryDelayStatusText(
                                                nextAttemptNumber: retryDisplayAttemptNumber + 1,
                                                totalAttempts: startupRetryDisplayTotalAttempts,
                                                delay: retryDelay,
                                                churnCause: StartupStatusCausePipeRetry))
                                        .ConfigureAwait(false);
                                    await Task.Delay(retryDelay).ConfigureAwait(false);
                                }
                            }
                        }

                        if (sidecarConnectException is not null) {
                            var serviceProcessExited = _serviceProcess is { HasExited: true };
                            var shouldAttemptRecovery = ShouldAttemptStartupConnectRecoveryAfterRetryFailure(
                                fromUserAction: fromUserAction,
                                captureStartupPhaseTelemetry: captureStartupPhaseTelemetry,
                                connectException: sidecarConnectException,
                                serviceProcessExited: serviceProcessExited,
                                recoveryAttemptsConsumed: recoveryAttemptsConsumed,
                                recoveryAttemptLimit: StartupConnectRecoveryAttemptLimit);
                            if (shouldAttemptRecovery) {
                                recoveryAttemptsConsumed++;
                                if (serviceProcessExited) {
                                    await SetConnectProgressStatusAsync(
                                            AppendStartupStatusContext(
                                                "Starting runtime... (recovering local service startup)",
                                                StartupStatusPhaseStartupConnect,
                                                StartupStatusCauseRuntimeStart))
                                        .ConfigureAwait(false);
                                    LogStartupConnectPhase("ensure_sidecar.recovery", "begin");
                                    var ensureRecoveryStopwatch = Stopwatch.StartNew();
                                    var recoveredSidecarRunning = await EnsureServiceRunningAsync(pipeName).ConfigureAwait(false);
                                    LogStartupConnectDetail("ensure_sidecar.recovery elapsed_ms=" + RoundMs(ensureRecoveryStopwatch.Elapsed).ToString(CultureInfo.InvariantCulture));
                                    if (ensureRecoveryStopwatch.Elapsed >= StartupConnectAttemptOutlierThreshold) {
                                        LogStartupConnectDetail(
                                            "ensure_sidecar.recovery outlier elapsed_ms="
                                            + RoundMs(ensureRecoveryStopwatch.Elapsed).ToString(CultureInfo.InvariantCulture)
                                            + " threshold_ms="
                                            + RoundMs(StartupConnectAttemptOutlierThreshold).ToString(CultureInfo.InvariantCulture));
                                    }

                                    if (!recoveredSidecarRunning) {
                                        sidecarConnectException = new InvalidOperationException("Service recovery launch did not produce a running process.");
                                        LogStartupConnectPhase("ensure_sidecar.recovery", "failed");
                                    } else {
                                        LogStartupConnectPhase("ensure_sidecar.recovery", "done");
                                        serviceProcessExited = false;
                                    }
                                }

                                if (!serviceProcessExited && sidecarConnectException is not null) {
                                    var requestedRecoveryTimeout = fromUserAction
                                        ? StartupInitialPipeConnectTimeout
                                        : StartupConnectRetryAttemptCapNonInteractive;
                                    if (TryResolveConnectTimeout(requestedRecoveryTimeout, out var recoveryTimeout)) {
                                        var recoveryHardTimeout = ResolveConnectAttemptHardTimeout(recoveryTimeout);
                                        LogStartupConnectPhase("pipe_connect.recovery", "begin");
                                        LogConnectAttemptStart("pipe_connect.recovery", recoveryAttemptsConsumed, requestedRecoveryTimeout, recoveryTimeout, recoveryHardTimeout);
                                        await SetConnectProgressStatusAsync(
                                                BuildStartupConnectAttemptStatusText(
                                                    phaseLabel: "recovering service connection",
                                                    attemptNumber: recoveryAttemptsConsumed,
                                                    totalAttempts: StartupConnectRecoveryAttemptLimit,
                                                    timeout: recoveryTimeout,
                                                    churnCause: StartupStatusCausePipeRetry))
                                            .ConfigureAwait(false);
                                        Stopwatch? recoveryAttemptStopwatch = null;
                                        try {
                                            recoveryAttemptStopwatch = Stopwatch.StartNew();
                                            await ConnectClientWithTimeoutAsync(client, pipeName, recoveryTimeout, recoveryHardTimeout).ConfigureAwait(false);
                                            var recoveryAttemptElapsed = recoveryAttemptStopwatch.Elapsed;
                                            LogConnectAttemptResult("pipe_connect.recovery", recoveryAttemptsConsumed, success: true, attemptElapsed: recoveryAttemptElapsed, exception: null);
                                            LogStartupConnectPhase("pipe_connect.recovery", "done");
                                            sidecarConnectException = null;
                                            connected = true;
                                        } catch (Exception recoveryEx) {
                                            var recoveryAttemptElapsed = recoveryAttemptStopwatch?.Elapsed ?? TimeSpan.Zero;
                                            LogConnectAttemptResult("pipe_connect.recovery", recoveryAttemptsConsumed, success: false, attemptElapsed: recoveryAttemptElapsed, exception: recoveryEx);
                                            LogStartupConnectPhase("pipe_connect.recovery", "failed");
                                            sidecarConnectException = recoveryEx;
                                        }
                                    } else {
                                        sidecarConnectException = CreateBudgetExceededException();
                                        LogStartupConnectPhase("pipe_connect.recovery", "skipped_budget");
                                    }
                                }
                            }
                        }

                        if (sidecarConnectException is not null) {
                            LogStartupConnectPhase("pipe_connect.retry", "failed");
                        } else {
                            LogStartupConnectPhase("pipe_connect.retry", "done");
                        }
                    } else {
                        LogStartupConnectPhase("ensure_sidecar", "failed");
                        await client.DisposeAsync().ConfigureAwait(false);
                        _isConnected = false;
                        EndStartupMetadataSyncTracking();
                        MarkDispatchConnectFailure();
                        await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                        EnsureAutoReconnectLoop();
                        if (fromUserAction || _debugMode) {
                            AppendSystem(SystemNotice.ConnectFailed(FormatConnectError(initialConnectException ?? CreateBudgetExceededException())));
                            AppendSystem(SystemNotice.ServiceSidecarUnavailable());
                        }
                        return;
                    }
                }

                if (!connected) {
                    await client.DisposeAsync().ConfigureAwait(false);
                    _isConnected = false;
                    EndStartupMetadataSyncTracking();
                    MarkDispatchConnectFailure();
                    await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                    EnsureAutoReconnectLoop();
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.ConnectProbeFailed(FormatConnectError(initialConnectException ?? CreateBudgetExceededException())));
                    }
                    if (fromUserAction || _debugMode) {
                        AppendSystem(SystemNotice.ConnectFailedAfterSidecarStart(FormatConnectError(sidecarConnectException ?? CreateBudgetExceededException())));
                    }
                    return;
                }
            }

            _client = client;
            _isConnected = true;
            ResetStartupMetadataFailureRecoveryDiagnostics();
            BeginStartupMetadataSyncTracking("preparing runtime metadata sync");
            ClearDispatchConnectFailure();
            StopAutoReconnectLoop();
            await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);
            var inlineHelloPhaseSucceeded = false;
            var inlineToolCatalogPhaseSucceeded = false;

            var deferredMetadataPlan = ResolveDeferredStartupMetadataPlan(
                deferPostConnectMetadataSync: deferPostConnectMetadataSync,
                deferStartupHelloProbe: ShouldDeferStartupHelloProbe(captureStartupPhaseTelemetry),
                deferStartupToolCatalogSync: ShouldDeferStartupToolCatalogSync(captureStartupPhaseTelemetry),
                requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                isAuthenticated: _isAuthenticated,
                loginInProgress: _loginInProgress,
                deferStartupAuthRefresh: ShouldDeferStartupAuthRefresh(captureStartupPhaseTelemetry),
                deferStartupModelProfileSync: ShouldDeferStartupModelProfileSync(captureStartupPhaseTelemetry));
            if (deferredMetadataPlan.DeferStartupMetadataSync) {
                _sessionPolicy = null;
                ClearToolCatalogCache(clearCatalogMetadata: true);
                RecordStartupBootstrapCacheMode(_sessionPolicy);
                UpdateStartupMetadataSyncPhase(
                    deferredMetadataPlan.SkipDeferredMetadataUntilAuthenticated
                        ? "waiting for sign-in to finish startup sync"
                        : "startup metadata sync queued");
                await SetStatusAsync(
                    BuildStartupPendingOrAuthVerificationStatusText(
                        requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                        isAuthenticated: _isAuthenticated,
                        loginInProgress: _loginInProgress),
                    SessionStatusTone.Warn).ConfigureAwait(false);
                LogStartupConnectPhase("hello", deferredMetadataPlan.SkipDeferredMetadataUntilAuthenticated ? "deferred_unauthenticated" : "deferred");
                LogStartupConnectPhase("list_tools", deferredMetadataPlan.SkipDeferredMetadataUntilAuthenticated ? "deferred_unauthenticated" : "deferred");
                if (deferredMetadataPlan.QueueDeferredConnectMetadataSync) {
                    QueueDeferredStartupConnectMetadataSync();
                }
            } else {
                var helloStopwatch = Stopwatch.StartNew();
                var satisfyToolCatalogFromHelloPolicy = false;
                try {
                    UpdateStartupMetadataSyncPhase("syncing session policy");
                    LogStartupConnectPhase("hello", "begin");
                    var hello = await _client.RequestAsync<HelloMessage>(new HelloRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                    _sessionPolicy = hello.Policy;
                    satisfyToolCatalogFromHelloPolicy = ShouldSatisfyStartupToolCatalogFromHelloPolicy(_sessionPolicy);
                    ApplyHelloPolicyToolCatalogPreview(_sessionPolicy, clearExistingToolDefinitions: satisfyToolCatalogFromHelloPolicy);
                    SeedBackgroundSchedulerSnapshot(hello.Policy?.CapabilitySnapshot?.BackgroundScheduler);
                    RecordStartupBootstrapCacheMode(_sessionPolicy);
                    RecordStartupHelloPhaseDiagnostics(helloStopwatch.Elapsed, attempts: 1, success: true);
                    inlineHelloPhaseSucceeded = true;
                    LogStartupConnectPhase("hello", "done");
                } catch (Exception ex) {
                    LogStartupConnectPhase("hello", "failed");
                    _sessionPolicy = null;
                    ClearToolCatalogCache(clearCatalogMetadata: true);
                    RecordStartupBootstrapCacheMode(_sessionPolicy);
                    RecordStartupHelloPhaseDiagnostics(helloStopwatch.Elapsed, attempts: 1, success: false);
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.HelloFailed(ex.Message));
                    }
                }
                if (!deferredMetadataPlan.DeferStartupMetadataSync) {
                    if (satisfyToolCatalogFromHelloPolicy) {
                        RecordStartupListToolsPhaseDiagnostics(TimeSpan.Zero, attempts: 1, success: true);
                        inlineToolCatalogPhaseSucceeded = true;
                        LogStartupConnectPhase("list_tools", "satisfied_by_persisted_preview");
                    } else {
                        var listToolsStopwatch = Stopwatch.StartNew();
                        try {
                            UpdateStartupMetadataSyncPhase("loading tool catalog");
                            LogStartupConnectPhase("list_tools", "begin");
                            var toolList = await _client.RequestAsync<ToolListMessage>(new ListToolsRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                            UpdateToolCatalog(toolList.Tools, toolList.RoutingCatalog, toolList.Packs, toolList.Plugins, toolList.CapabilitySnapshot);
                            SeedBackgroundSchedulerSnapshot(toolList.CapabilitySnapshot?.BackgroundScheduler);
                            RecordStartupListToolsPhaseDiagnostics(listToolsStopwatch.Elapsed, attempts: 1, success: true);
                            inlineToolCatalogPhaseSucceeded = true;
                            LogStartupConnectPhase("list_tools", "done");
                        } catch (Exception ex) {
                            LogStartupConnectPhase("list_tools", "failed");
                            ClearToolCatalogCache(clearCatalogMetadata: false);
                            RecordStartupListToolsPhaseDiagnostics(listToolsStopwatch.Elapsed, attempts: 1, success: false);
                            if (VerboseServiceLogs || _debugMode) {
                                AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
                            }
                        }
                    }
                }
            }

            AppendStartupToolHealthWarningsFromPolicy();
            AppendUnavailablePacksFromPolicy();
            AppendStartupBootstrapSummaryFromPolicy();

            if (deferredMetadataPlan.SkipDeferredMetadataUntilAuthenticated) {
                LogStartupConnectPhase("auth_refresh", "skipped_unauthenticated");
            } else if (deferredMetadataPlan.DeferAuthRefresh) {
                LogStartupConnectPhase("auth_refresh", "deferred");
            } else {
                var authRefreshStopwatch = Stopwatch.StartNew();
                LogStartupConnectPhase("auth_refresh", "begin");
                try {
                    UpdateStartupMetadataSyncPhase("refreshing authentication");
                    _ = await RefreshAuthenticationStateAsync(
                            updateStatus: true,
                            probeTimeout: EnsureLoginFastPathProbeTimeout)
                        .ConfigureAwait(false);
                    RecordStartupAuthRefreshPhaseDiagnostics(authRefreshStopwatch.Elapsed, attempts: 1, success: true);
                    LogStartupConnectPhase("auth_refresh", "done");
                } catch {
                    RecordStartupAuthRefreshPhaseDiagnostics(authRefreshStopwatch.Elapsed, attempts: 1, success: false);
                    LogStartupConnectPhase("auth_refresh", "failed");
                    throw;
                }
            }
            if (deferredMetadataPlan.SkipDeferredMetadataUntilAuthenticated) {
                LogStartupConnectPhase("model_profile_sync", "skipped_unauthenticated");
            } else if (deferredMetadataPlan.DeferModelProfileSync) {
                LogStartupConnectPhase("model_profile_sync", "deferred");
                QueueDeferredStartupModelProfileSync();
            } else {
                try {
                    UpdateStartupMetadataSyncPhase("syncing model and profile state");
                    LogStartupConnectPhase("model_profile_sync", "begin");
                    await SyncConnectedServiceProfileAndModelsAsync(
                        forceModelRefresh: false,
                        setProfileNewThread: false,
                        appendWarnings: false).ConfigureAwait(false);
                    LogStartupConnectPhase("model_profile_sync", "done");
                } catch (Exception ex) {
                    LogStartupConnectPhase("model_profile_sync", "failed");
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem("Model/profile sync failed: " + ex.Message);
                    }
                }
            }

            if (!deferredMetadataPlan.DeferStartupMetadataSync) {
                var inlineMetadataSyncSucceeded = inlineHelloPhaseSucceeded && inlineToolCatalogPhaseSucceeded;
                var startupBootstrapCacheMode = Volatile.Read(ref _startupBootstrapCacheMode);
                var shouldQueuePersistedPreviewRefreshRerun = ShouldRequestDeferredStartupMetadataPersistedPreviewRefreshRerun(
                    metadataSyncSucceeded: inlineMetadataSyncSucceeded,
                    startupBootstrapCacheMode: startupBootstrapCacheMode,
                    isConnected: _isConnected,
                    shutdownRequested: _shutdownRequested,
                    retriesConsumed: Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount),
                    retryLimit: StartupDeferredMetadataPersistedPreviewRefreshRetryLimit);
                var persistedPreviewRefreshRerunQueued = shouldQueuePersistedPreviewRefreshRerun
                    && TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
                        ref _startupConnectMetadataPersistedPreviewRefreshRetryCount,
                        StartupDeferredMetadataPersistedPreviewRefreshRetryLimit);
                if (persistedPreviewRefreshRerunQueued) {
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 1);
                    QueueDeferredStartupConnectMetadataSync(
                        requestRerunIfBusy: true,
                        preferPersistedPreviewRefreshDelay: true);
                    var previewRetryCount = Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount);
                    StartupLog.Write(
                        "StartupConnect.inline_metadata_sync persisted_preview_refresh_queued retry="
                        + previewRetryCount.ToString(CultureInfo.InvariantCulture)
                        + "/"
                        + StartupDeferredMetadataPersistedPreviewRefreshRetryLimit.ToString(CultureInfo.InvariantCulture));
                }

                if (startupBootstrapCacheMode != StartupBootstrapCacheModePersistedPreview) {
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount, 0);
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 0);
                }
                var persistedPreviewRefreshRetryLimitReached = HasReachedDeferredStartupMetadataPersistedPreviewRefreshRetryLimit(
                    metadataSyncSucceeded: inlineMetadataSyncSucceeded,
                    startupBootstrapCacheMode: startupBootstrapCacheMode,
                    retriesConsumed: Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount),
                    retryLimit: StartupDeferredMetadataPersistedPreviewRefreshRetryLimit);

                EndStartupMetadataSyncTracking();
                if (persistedPreviewRefreshRerunQueued || persistedPreviewRefreshRetryLimitReached) {
                    await SetStatusAsync(
                            BuildStartupMetadataSyncPersistedPreviewStatusText(persistedPreviewRefreshRerunQueued),
                            SessionStatusTone.Warn)
                        .ConfigureAwait(false);
                    LogStartupConnectPhase(
                        "ready",
                        persistedPreviewRefreshRerunQueued
                            ? "inline_metadata_sync_preview_refresh_pending"
                            : "inline_metadata_sync_preview_refresh_retry_limit_reached");
                } else {
                    await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
                    LogStartupConnectPhase("ready", "inline_metadata_sync_done");
                }
                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
            }
        } catch {
            EndStartupMetadataSyncTracking();
            throw;
        } finally {
            _connectGate.Release();
        }
    }

}
