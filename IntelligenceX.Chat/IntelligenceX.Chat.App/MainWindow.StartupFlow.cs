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
using IntelligenceX.Chat.App.Theming;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Input;
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
    private async Task RunStartupFlowAsync() {
        try {
            Interlocked.Exchange(ref _startupWebViewBudgetExceededThisRun, 0);
            StartupLog.Write("MainWindow.StartupFlow begin");
            StartupLog.Write("StartupPhase.AppState begin");
            await EnsureAppStateLoadedAsync().ConfigureAwait(false);
            StartupLog.Write("StartupPhase.AppState done");
            StartupLog.Write("StartupPhase.WebView begin");
            var startupWebViewBudgetCache = SnapshotStartupWebViewBudgetCache();
            var startupWebViewBudgetDecision = ResolveStartupWebViewBudgetDecisionIfEnabled(
                captureStartupPhaseTelemetry: Volatile.Read(ref _startupFlowState) == StartupFlowStateRunning,
                lastEnsureWebViewMs: startupWebViewBudgetCache.LastEnsureWebViewMs,
                consecutiveBudgetExhaustions: startupWebViewBudgetCache.ConsecutiveBudgetExhaustions,
                consecutiveStableCompletions: startupWebViewBudgetCache.ConsecutiveStableCompletions,
                adaptiveCooldownRunsRemaining: startupWebViewBudgetCache.AdaptiveCooldownRunsRemaining,
                lastAppliedBudgetMs: startupWebViewBudgetCache.LastAppliedBudgetMs);
            var startupWebViewBudget = startupWebViewBudgetDecision is null
                ? (TimeSpan?)null
                : TimeSpan.FromMilliseconds(startupWebViewBudgetDecision.BudgetMs);
            if (startupWebViewBudgetDecision is not null) {
                StartupLog.Write("StartupPhase.WebView budget_ms=" + startupWebViewBudgetDecision.BudgetMs.ToString(CultureInfo.InvariantCulture));
                StartupLog.Write(
                    "StartupPhase.WebView budget_policy last_ensure_ms="
                    + (startupWebViewBudgetCache.LastEnsureWebViewMs?.ToString(CultureInfo.InvariantCulture) ?? "null")
                    + " exhausted_count="
                    + startupWebViewBudgetCache.ConsecutiveBudgetExhaustions.ToString(CultureInfo.InvariantCulture)
                    + " stable_count="
                    + startupWebViewBudgetCache.ConsecutiveStableCompletions.ToString(CultureInfo.InvariantCulture)
                    + " cooldown_runs="
                    + startupWebViewBudgetCache.AdaptiveCooldownRunsRemaining.ToString(CultureInfo.InvariantCulture)
                    + " last_budget_ms="
                    + (startupWebViewBudgetCache.LastAppliedBudgetMs?.ToString(CultureInfo.InvariantCulture) ?? "null"));
                StartupLog.Write("StartupPhase.WebView budget_reason=" + startupWebViewBudgetDecision.Reason);
            }
            RecordStartupWebViewBudgetSelection(startupWebViewBudget);
            var webViewInitializationTask = EnsureWebViewInitializedAsync();
            var startupWebViewBudgetWaitTask = TryAwaitStartupWebViewWithinBudgetAsync(webViewInitializationTask, startupWebViewBudget);
            StartupLog.Write("StartupPhase.Connect begin");
            await EnsureStartupConnectedAsync().ConfigureAwait(false);
            StartupLog.Write("StartupPhase.Connect done");
            StartupLog.Write("StartupPhase.Auth deferred");
            QueueDeferredStartupAuthentication();
            if (await startupWebViewBudgetWaitTask.ConfigureAwait(false)) {
                StartupLog.Write("StartupPhase.WebView done");
            } else {
                StartupLog.Write("StartupPhase.WebView budget_exhausted");
                StartupLog.Write("StartupPhase.WebView deferred");
                MarkStartupWebViewBudgetExhausted();
                ObserveDeferredStartupWebViewInitialization(webViewInitializationTask);
            }
            StartupLog.Write("StartupPhase.Onboarding deferred");
            QueueDeferredStartupOnboarding();
            Interlocked.Exchange(ref _startupFlowState, StartupFlowStateComplete);
            StartupLog.Write("StartupPhase.DispatchPrewarm deferred");
            QueueDeferredStartupDispatchPrewarm();
            QueueDeferredStartupBenchAutoSend();
            StartupLog.Write("MainWindow.StartupFlow done");
        } catch (Exception ex) {
            Interlocked.Exchange(ref _startupFlowState, StartupFlowStateIdle);
            StartupLog.Write("MainWindow.StartupFlow failed: " + ex);
        }
    }

    private static async Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment?> CreateWebViewEnvironmentAsync() {
        try {
            StartupLog.Write("EnsureWebViewInitializedAsync.env_prewarm begin");
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync().AsTask().ConfigureAwait(false);
            StartupLog.Write("EnsureWebViewInitializedAsync.env_prewarm done");
            return environment;
        } catch (Exception ex) {
            StartupLog.Write("EnsureWebViewInitializedAsync.env_prewarm failed: " + ex.Message);
            return null;
        }
    }

    private static async Task<bool> TryAwaitStartupWebViewWithinBudgetAsync(Task webViewInitializationTask, TimeSpan? startupWebViewBudget) {
        if (webViewInitializationTask.IsCompleted) {
            await webViewInitializationTask.ConfigureAwait(false);
            return true;
        }

        if (!startupWebViewBudget.HasValue || startupWebViewBudget.Value <= TimeSpan.Zero) {
            await webViewInitializationTask.ConfigureAwait(false);
            return true;
        }

        var completed = await Task.WhenAny(webViewInitializationTask, Task.Delay(startupWebViewBudget.Value)).ConfigureAwait(false);
        if (ReferenceEquals(completed, webViewInitializationTask)) {
            await webViewInitializationTask.ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static void ObserveDeferredStartupWebViewInitialization(Task webViewInitializationTask) {
        _ = webViewInitializationTask.ContinueWith(task => {
            if (task.IsCanceled) {
                StartupLog.Write("StartupPhase.WebView eventual_canceled");
                return;
            }

            if (task.IsFaulted) {
                var root = task.Exception?.GetBaseException();
                StartupLog.Write("StartupPhase.WebView eventual_failed: " + (root?.Message ?? "unknown"));
                return;
            }

            StartupLog.Write("StartupPhase.WebView eventual_done");
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ApplyWebViewPostInitializationAsync() {
        await SetStatusAsync(_statusText, _statusTone, _usageLimitSwitchRecommended).ConfigureAwait(false);
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
    }

    private void QueueDeferredStartupWebViewPostInitialization() {
        if (_shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupWebViewPostInitDeferredQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(75).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }

                StartupLog.Write("StartupPhase.WebView.post_init begin");
                await RunOnUiThreadAsync(() => ApplyWebViewPostInitializationAsync()).ConfigureAwait(false);
                StartupLog.Write("StartupPhase.WebView.post_init done");
            } catch (Exception ex) {
                StartupLog.Write("StartupPhase.WebView.post_init failed: " + ex.Message);
            } finally {
                Interlocked.Exchange(ref _startupWebViewPostInitDeferredQueued, 0);
            }
        });
    }

    private void QueueDeferredStartupOnboarding() {
        if (_shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupOnboardingDeferredQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(250).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }
                StartupLog.Write("StartupPhase.Onboarding begin");
                await EnsureOnboardingStartedAsync().ConfigureAwait(false);
                StartupLog.Write("StartupPhase.Onboarding done");
            } catch (Exception ex) {
                StartupLog.Write("StartupPhase.Onboarding failed: " + ex.Message);
            } finally {
                Interlocked.Exchange(ref _startupOnboardingDeferredQueued, 0);
            }
        });
    }

    private void QueueDeferredStartupAuthentication() {
        if (_shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupAuthDeferredQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(200).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }

                if (!await WaitForStartupDeferredMetadataSyncIdleAsync("StartupPhase.Auth").ConfigureAwait(false)) {
                    return;
                }

                StartupLog.Write("StartupPhase.Auth begin");
                await EnsureFirstRunAuthenticatedAsync().ConfigureAwait(false);
                StartupLog.Write("StartupPhase.Auth done");
            } catch (Exception ex) {
                StartupLog.Write("StartupPhase.Auth failed: " + ex.Message);
            } finally {
                Interlocked.Exchange(ref _startupAuthDeferredQueued, 0);
            }
        });
    }


    private void QueueDeferredStartupModelProfileSync() {
        if (_shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupModelProfileSyncDeferredQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(StartupDeferredModelProfileSyncDelay).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }

                if (!await WaitForStartupDeferredBackgroundTurnIdleAsync("StartupConnect.model_profile_sync").ConfigureAwait(false)) {
                    return;
                }

                if (!await WaitForStartupDeferredMetadataSyncIdleAsync("StartupConnect.model_profile_sync").ConfigureAwait(false)) {
                    return;
                }

                StartupLog.Write("StartupConnect.model_profile_sync begin");
                await SyncConnectedServiceProfileAndModelsAsync(
                    forceModelRefresh: false,
                    setProfileNewThread: false,
                    appendWarnings: false).ConfigureAwait(false);
                StartupLog.Write("StartupConnect.model_profile_sync done");
            } catch (Exception ex) {
                StartupLog.Write("StartupConnect.model_profile_sync failed: " + DescribeStartupExceptionForLog(ex));
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem("Model/profile sync failed: " + ex.Message);
                }
            } finally {
                Interlocked.Exchange(ref _startupModelProfileSyncDeferredQueued, 0);
            }
        });
    }

    private void QueueDeferredStartupBenchAutoSend() {
        var prompt = (Environment.GetEnvironmentVariable("IXCHAT_BENCH_AUTOSEND_PROMPT") ?? string.Empty).Trim();
        if (prompt.Length == 0 || _shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupBenchAutoSendQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(350).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }

                StartupLog.Write("StartupPhase.BenchAutoSend begin");
                await RunOnUiThreadAsync(() => SendPromptAsync(prompt)).ConfigureAwait(false);
                StartupLog.Write("StartupPhase.BenchAutoSend done");
            } catch (Exception ex) {
                StartupLog.Write("StartupPhase.BenchAutoSend failed: " + ex.Message);
            } finally {
                Interlocked.Exchange(ref _startupBenchAutoSendQueued, 0);
            }
        });
    }

    private void QueueDeferredStartupConnectMetadataSync(
        bool requestRerunIfBusy = false,
        bool preferPersistedPreviewRefreshDelay = false) {
        if (_shutdownRequested) {
            return;
        }

        if (preferPersistedPreviewRefreshDelay) {
            Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 1);
        }

        var metadataSyncAlreadyQueued = Interlocked.CompareExchange(ref _startupConnectMetadataDeferredQueued, 1, 0) != 0;
        if (metadataSyncAlreadyQueued) {
            if (ShouldRequestDeferredStartupMetadataSyncRerun(
                    metadataSyncAlreadyQueued: metadataSyncAlreadyQueued,
                    requestRerunIfBusy: requestRerunIfBusy)) {
                Interlocked.Exchange(ref _startupConnectMetadataDeferredRerunRequested, 1);
                StartupLog.Write("StartupConnect.metadata_sync rerun_requested_while_busy");
            }

            return;
        }

        Interlocked.Exchange(ref _startupConnectMetadataDeferredQueuedUtcTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _startupConnectMetadataDeferredRerunRequested, 0);
        var metadataSyncDelay = Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 0) != 0
            ? StartupDeferredMetadataPersistedPreviewRefreshDelay
            : StartupDeferredConnectMetadataDelay;
        _ = Task.Run(async () => {
            var metadataSyncStopwatch = Stopwatch.StartNew();
            var helloPhaseSucceeded = false;
            var toolCatalogPhaseSucceeded = false;
            var preserveStartupMetadataFailureStatus = false;
            var exitedForAuthWait = false;
            try {
                await Task.Delay(metadataSyncDelay).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }

                if (!await WaitForStartupDeferredBackgroundTurnIdleAsync("StartupConnect.metadata_sync").ConfigureAwait(false)) {
                    return;
                }

                var client = _client;
                if (client is null) {
                    return;
                }

                metadataSyncStopwatch.Restart();
                var requiresInteractiveSignIn = RequiresInteractiveSignInForCurrentTransport();
                var isAuthenticated = IsEffectivelyAuthenticatedForCurrentTransport();
                if (ShouldWaitForAuthenticationBeforeDeferredStartupMetadataSync(
                        requiresInteractiveSignIn: requiresInteractiveSignIn,
                        isAuthenticated: isAuthenticated,
                        loginInProgress: _loginInProgress)) {
                    exitedForAuthWait = true;
                    MarkStartupAuthGateWaiting();
                    BeginStartupMetadataSyncTracking("waiting for sign-in to finish startup sync");
                    if (_isConnected && !_isSending && !_turnStartupInProgress) {
                        await SetStatusAsync(
                                BuildStartupPendingOrAuthVerificationStatusText(
                                    requiresInteractiveSignIn: requiresInteractiveSignIn,
                                    isAuthenticated: isAuthenticated,
                                    loginInProgress: _loginInProgress),
                                SessionStatusTone.Warn)
                            .ConfigureAwait(false);
                    }

                    StartupLog.Write("StartupConnect.metadata_sync deferred_unauthenticated");
                    // This branch is a deliberate auth wait (not an active metadata operation).
                    // End sync-tracking now so header chips/session status cannot get stuck in
                    // a generic metadata-sync state while waiting for interactive sign-in.
                    EndStartupMetadataSyncTracking();
                    return;
                }

                MarkStartupAuthGateResolved();
                BeginStartupMetadataSyncTracking("startup metadata sync queued");
                if (!_isSending && !_turnStartupInProgress) {
                    UpdateStartupMetadataSyncPhase("loading tool packs in background");
                    await SetStatusAsync(
                            BuildStartupPendingOrAuthVerificationStatusText(
                                requiresInteractiveSignIn: requiresInteractiveSignIn,
                                isAuthenticated: isAuthenticated,
                                loginInProgress: _loginInProgress),
                            SessionStatusTone.Warn)
                        .ConfigureAwait(false);
                }

                async Task SetMetadataSyncStatusAsync(string message, string? phase = null) {
                    if (!string.IsNullOrWhiteSpace(phase)) {
                        UpdateStartupMetadataSyncPhase(phase!);
                    }

                    if (_isConnected && !_isSending && !_turnStartupInProgress) {
                        await SetStatusAsync(message, SessionStatusTone.Warn).ConfigureAwait(false);
                    }
                }

                static string FormatPhaseDuration(TimeSpan elapsed) {
                    return FormatStartupPhaseDuration(elapsed);
                }

                async Task<T> AwaitWithMetadataHeartbeatAsync<T>(
                    Func<CancellationToken, Task<T>> operationFactory,
                    string initialMessage,
                    string heartbeatMessagePrefix,
                    string phase) {
                    await SetMetadataSyncStatusAsync(initialMessage, phase).ConfigureAwait(false);
                    var started = Stopwatch.StartNew();
                    using var requestTimeoutCts = StartupDeferredMetadataRequestTimeout > TimeSpan.Zero
                        ? new CancellationTokenSource(StartupDeferredMetadataRequestTimeout)
                        : null;
                    var operationTask = operationFactory(requestTimeoutCts?.Token ?? CancellationToken.None);
                    while (!operationTask.IsCompleted) {
                        if (started.Elapsed >= StartupDeferredMetadataPhaseTimeout) {
                            requestTimeoutCts?.Cancel();
                            throw new TimeoutException(
                                "Startup metadata sync phase '" + phase + "' exceeded "
                                + FormatPhaseDuration(StartupDeferredMetadataPhaseTimeout) + ".");
                        }

                        var completed = await Task.WhenAny(operationTask, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                        if (ReferenceEquals(completed, operationTask)) {
                            break;
                        }

                        await SetMetadataSyncStatusAsync(
                                $"{heartbeatMessagePrefix} ({FormatPhaseDuration(started.Elapsed)} elapsed)...",
                                phase)
                            .ConfigureAwait(false);
                    }

                    return await operationTask.ConfigureAwait(false);
                }

                TimeSpan? helloDuration = null;
                TimeSpan? toolCatalogDuration = null;
                TimeSpan? authRefreshDuration = null;
                var enabledPackCount = 0;
                var totalPackCount = 0;
                var listedToolCount = 0;
                var satisfyToolCatalogFromHelloPolicy = false;
                const int metadataPhaseMaxAttempts = 2;

                var helloStopwatch = Stopwatch.StartNew();
                var helloAttemptCount = 0;
                try {
                    HelloMessage? hello = null;
                    for (var attempt = 1; attempt <= metadataPhaseMaxAttempts; attempt++) {
                        helloAttemptCount = attempt;
                        if (attempt == 1) {
                            StartupLog.Write("StartupConnect.hello begin");
                        } else {
                            StartupLog.Write(
                                "StartupConnect.hello retry attempt="
                                + attempt.ToString(CultureInfo.InvariantCulture)
                                + "/"
                                + metadataPhaseMaxAttempts.ToString(CultureInfo.InvariantCulture));
                        }

                        try {
                            hello = await AwaitWithMetadataHeartbeatAsync(
                                    operationFactory: cancellationToken => client.RequestAsync<HelloMessage>(
                                        new HelloRequest { RequestId = NextId() },
                                        cancellationToken),
                                    initialMessage: "Runtime connected. Syncing session policy...",
                                    heartbeatMessagePrefix: "Runtime connected. Session policy sync in progress",
                                    phase: "syncing session policy")
                                .ConfigureAwait(false);
                            break;
                        } catch (Exception ex) when (attempt < metadataPhaseMaxAttempts && ShouldRetryDeferredStartupMetadataPhaseAttempt(ex)) {
                            StartupLog.Write(
                                "StartupConnect.hello transient_retry attempt="
                                + attempt.ToString(CultureInfo.InvariantCulture)
                                + "/"
                                + metadataPhaseMaxAttempts.ToString(CultureInfo.InvariantCulture)
                                + " after "
                                + FormatPhaseDuration(helloStopwatch.Elapsed)
                                + ": "
                                + DescribeStartupExceptionForLog(ex));
                            await SetMetadataSyncStatusAsync(
                                    AppendStartupStatusContext(
                                        "Runtime connected. Session policy sync interrupted; retrying...",
                                        StartupStatusPhaseStartupMetadataSync,
                                        StartupStatusCauseMetadataRetry),
                                    phase: "syncing session policy")
                                .ConfigureAwait(false);
                            await Task.Delay(250).ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (hello is null) {
                        throw new InvalidOperationException("Session policy sync did not complete.");
                    }

                    helloStopwatch.Stop();
                    helloDuration = helloStopwatch.Elapsed;
                    _sessionPolicy = hello.Policy;
                    satisfyToolCatalogFromHelloPolicy = ShouldSatisfyStartupToolCatalogFromHelloPolicy(_sessionPolicy);
                    ApplyHelloPolicyToolCatalogPreview(_sessionPolicy, clearExistingToolDefinitions: satisfyToolCatalogFromHelloPolicy);
                    SeedBackgroundSchedulerSnapshot(hello.Policy?.CapabilitySnapshot?.BackgroundScheduler);
                    RecordStartupBootstrapCacheMode(_sessionPolicy);
                    RecordStartupHelloPhaseDiagnostics(helloStopwatch.Elapsed, helloAttemptCount, success: true);
                    helloPhaseSucceeded = true;
                    StartupLog.Write("StartupConnect.hello done");
                    AppendStartupToolHealthWarningsFromPolicy();
                    AppendUnavailablePacksFromPolicy();
                    AppendStartupBootstrapSummaryFromPolicy();
                    if (hello.Policy is { Packs: { } packs }) {
                        totalPackCount = packs.Length;
                        for (var i = 0; i < packs.Length; i++) {
                            if (packs[i].Enabled) {
                                enabledPackCount++;
                            }
                        }
                    }
                    var startupBootstrapDetail = BuildStartupBootstrapStatusDetail(hello.Policy?.StartupBootstrap);
                    var sessionPolicyStatus = $"Runtime connected. Session policy synced in {FormatPhaseDuration(helloStopwatch.Elapsed)} "
                                              + $"({enabledPackCount}/{Math.Max(totalPackCount, 0)} packs enabled";
                    if (!string.IsNullOrWhiteSpace(startupBootstrapDetail)) {
                        sessionPolicyStatus += ", " + startupBootstrapDetail;
                    }
                    if (helloAttemptCount > 1) {
                        sessionPolicyStatus += ", retries " + (helloAttemptCount - 1).ToString(CultureInfo.InvariantCulture);
                    }
                    sessionPolicyStatus += ").";
                    await SetMetadataSyncStatusAsync(
                            sessionPolicyStatus,
                            phase: "session policy synced")
                        .ConfigureAwait(false);
                } catch (Exception ex) {
                    _sessionPolicy = null;
                    ClearToolCatalogCache(clearCatalogMetadata: true);
                    RecordStartupBootstrapCacheMode(_sessionPolicy);
                    RecordStartupHelloPhaseDiagnostics(helloStopwatch.Elapsed, helloAttemptCount, success: false);
                    StartupLog.Write(
                        "StartupConnect.hello failed after "
                        + FormatPhaseDuration(helloStopwatch.Elapsed)
                        + ": "
                        + DescribeStartupExceptionForLog(ex));
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.HelloFailed(ex.Message));
                    }
                }

                if (satisfyToolCatalogFromHelloPolicy) {
                    RecordStartupListToolsPhaseDiagnostics(TimeSpan.Zero, attempts: 1, success: true);
                    toolCatalogPhaseSucceeded = true;
                    StartupLog.Write("StartupConnect.list_tools satisfied_by_persisted_preview");
                } else {
                    var listToolsStopwatch = Stopwatch.StartNew();
                    var listToolsAttemptCount = 0;
                    try {
                        ToolListMessage? toolList = null;
                        for (var attempt = 1; attempt <= metadataPhaseMaxAttempts; attempt++) {
                            listToolsAttemptCount = attempt;
                            if (attempt == 1) {
                                StartupLog.Write("StartupConnect.list_tools begin");
                            } else {
                                StartupLog.Write(
                                    "StartupConnect.list_tools retry attempt="
                                    + attempt.ToString(CultureInfo.InvariantCulture)
                                    + "/"
                                    + metadataPhaseMaxAttempts.ToString(CultureInfo.InvariantCulture));
                            }

                            try {
                                toolList = await AwaitWithMetadataHeartbeatAsync(
                                        operationFactory: cancellationToken => client.RequestAsync<ToolListMessage>(
                                            new ListToolsRequest { RequestId = NextId() },
                                            cancellationToken),
                                        initialMessage: "Runtime connected. Loading tool catalog...",
                                        heartbeatMessagePrefix: "Runtime connected. Tool catalog load in progress",
                                        phase: "loading tool catalog")
                                    .ConfigureAwait(false);
                                break;
                            } catch (Exception ex) when (attempt < metadataPhaseMaxAttempts && ShouldRetryDeferredStartupMetadataPhaseAttempt(ex)) {
                                StartupLog.Write(
                                    "StartupConnect.list_tools transient_retry attempt="
                                    + attempt.ToString(CultureInfo.InvariantCulture)
                                    + "/"
                                    + metadataPhaseMaxAttempts.ToString(CultureInfo.InvariantCulture)
                                    + " after "
                                    + FormatPhaseDuration(listToolsStopwatch.Elapsed)
                                    + ": "
                                    + DescribeStartupExceptionForLog(ex));
                                await SetMetadataSyncStatusAsync(
                                        AppendStartupStatusContext(
                                            "Runtime connected. Tool catalog sync interrupted; retrying...",
                                            StartupStatusPhaseStartupMetadataSync,
                                            StartupStatusCauseMetadataRetry),
                                        phase: "loading tool catalog")
                                    .ConfigureAwait(false);
                                await Task.Delay(250).ConfigureAwait(false);
                                continue;
                            }
                        }

                        if (toolList is null) {
                            throw new InvalidOperationException("Tool catalog sync did not complete.");
                        }

                        listToolsStopwatch.Stop();
                        toolCatalogDuration = listToolsStopwatch.Elapsed;
                        RecordStartupListToolsPhaseDiagnostics(listToolsStopwatch.Elapsed, listToolsAttemptCount, success: true);
                        UpdateToolCatalog(toolList.Tools, toolList.RoutingCatalog, toolList.Packs, toolList.Plugins, toolList.CapabilitySnapshot);
                        SeedBackgroundSchedulerSnapshot(toolList.CapabilitySnapshot?.BackgroundScheduler);
                        toolCatalogPhaseSucceeded = true;
                        listedToolCount = toolList.Tools?.Length ?? 0;
                        StartupLog.Write("StartupConnect.list_tools done");
                        var toolCatalogStatus = $"Runtime connected. Tool catalog loaded ({listedToolCount} tools, {FormatPhaseDuration(listToolsStopwatch.Elapsed)}";
                        if (listToolsAttemptCount > 1) {
                            toolCatalogStatus += ", retries " + (listToolsAttemptCount - 1).ToString(CultureInfo.InvariantCulture);
                        }
                        toolCatalogStatus += ").";
                        await SetMetadataSyncStatusAsync(
                                toolCatalogStatus,
                                phase: "tool catalog loaded")
                            .ConfigureAwait(false);
                    } catch (Exception ex) {
                        ClearToolCatalogCache(clearCatalogMetadata: false);
                        RecordStartupListToolsPhaseDiagnostics(listToolsStopwatch.Elapsed, listToolsAttemptCount, success: false);
                        StartupLog.Write(
                            "StartupConnect.list_tools failed after "
                            + FormatPhaseDuration(listToolsStopwatch.Elapsed)
                            + ": "
                            + DescribeStartupExceptionForLog(ex));
                        if (VerboseServiceLogs || _debugMode) {
                            AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
                        }
                    }
                }

                var shouldRunInlineAuthRefresh = ShouldRunDeferredStartupMetadataInlineAuthRefresh(
                    startupAuthDeferredQueued: Volatile.Read(ref _startupAuthDeferredQueued) != 0,
                    shutdownRequested: _shutdownRequested);
                if (!shouldRunInlineAuthRefresh) {
                    StartupLog.Write("StartupConnect.auth_refresh skipped_deferred_startup_auth");
                } else {
                    var authRefreshStopwatch = Stopwatch.StartNew();
                    var authRefreshAttemptCount = 0;
                    try {
                        for (var attempt = 1; attempt <= metadataPhaseMaxAttempts; attempt++) {
                            authRefreshAttemptCount = attempt;
                            if (attempt == 1) {
                                StartupLog.Write("StartupConnect.auth_refresh begin");
                            } else {
                                StartupLog.Write(
                                    "StartupConnect.auth_refresh retry attempt="
                                    + attempt.ToString(CultureInfo.InvariantCulture)
                                    + "/"
                                    + metadataPhaseMaxAttempts.ToString(CultureInfo.InvariantCulture));
                            }

                            try {
                                _ = await AwaitWithMetadataHeartbeatAsync(
                                        operationFactory: _ => RefreshAuthenticationStateAsync(
                                            updateStatus: true,
                                            probeTimeout: EnsureLoginFastPathProbeTimeout),
                                        initialMessage: "Runtime connected. Refreshing authentication state...",
                                        heartbeatMessagePrefix: "Runtime connected. Authentication refresh in progress",
                                        phase: "refreshing authentication")
                                    .ConfigureAwait(false);
                                break;
                            } catch (Exception ex) when (attempt < metadataPhaseMaxAttempts && ShouldRetryDeferredStartupMetadataPhaseAttempt(ex)) {
                                StartupLog.Write(
                                    "StartupConnect.auth_refresh transient_retry attempt="
                                    + attempt.ToString(CultureInfo.InvariantCulture)
                                    + "/"
                                    + metadataPhaseMaxAttempts.ToString(CultureInfo.InvariantCulture)
                                    + " after "
                                    + FormatPhaseDuration(authRefreshStopwatch.Elapsed)
                                    + ": "
                                    + DescribeStartupExceptionForLog(ex));
                                await SetMetadataSyncStatusAsync(
                                        AppendStartupStatusContext(
                                            "Runtime connected. Authentication refresh interrupted; retrying...",
                                            StartupStatusPhaseStartupMetadataSync,
                                            StartupStatusCauseMetadataRetry),
                                        phase: "refreshing authentication")
                                    .ConfigureAwait(false);
                                await Task.Delay(250).ConfigureAwait(false);
                                continue;
                            }
                        }

                        authRefreshStopwatch.Stop();
                        authRefreshDuration = authRefreshStopwatch.Elapsed;
                        RecordStartupAuthRefreshPhaseDiagnostics(authRefreshStopwatch.Elapsed, authRefreshAttemptCount, success: true);
                        StartupLog.Write("StartupConnect.auth_refresh done");
                        var authRefreshStatus = "Runtime connected. Authentication refreshed in " + FormatPhaseDuration(authRefreshStopwatch.Elapsed);
                        if (authRefreshAttemptCount > 1) {
                            authRefreshStatus += " (retries " + (authRefreshAttemptCount - 1).ToString(CultureInfo.InvariantCulture) + ")";
                        }
                        authRefreshStatus += ".";
                        await SetMetadataSyncStatusAsync(
                                authRefreshStatus,
                                phase: "authentication refreshed")
                            .ConfigureAwait(false);
                    } catch (Exception ex) {
                        RecordStartupAuthRefreshPhaseDiagnostics(authRefreshStopwatch.Elapsed, authRefreshAttemptCount, success: false);
                        StartupLog.Write(
                            "StartupConnect.auth_refresh failed after "
                            + FormatPhaseDuration(authRefreshStopwatch.Elapsed)
                            + ": "
                            + DescribeStartupExceptionForLog(ex));
                        if (VerboseServiceLogs || _debugMode) {
                            AppendSystem(SystemNotice.EnsureLoginFailed(ex.Message));
                        }
                    }
                }

                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
                if ((helloDuration.HasValue || toolCatalogDuration.HasValue || authRefreshDuration.HasValue)
                    && (toolCatalogDuration.GetValueOrDefault() >= TimeSpan.FromSeconds(2)
                        || helloDuration.GetValueOrDefault() >= TimeSpan.FromSeconds(2)
                        || authRefreshDuration.GetValueOrDefault() >= TimeSpan.FromSeconds(2))) {
                    var summaryParts = new List<string>();
                    if (helloDuration.HasValue) {
                        summaryParts.Add($"policy {FormatPhaseDuration(helloDuration.Value)}");
                    }
                    if (toolCatalogDuration.HasValue) {
                        summaryParts.Add($"tool catalog {FormatPhaseDuration(toolCatalogDuration.Value)} ({listedToolCount} tools)");
                    }
                    if (authRefreshDuration.HasValue) {
                        summaryParts.Add($"auth {FormatPhaseDuration(authRefreshDuration.Value)}");
                    }

                    AppendSystem("Runtime startup sync timing: " + string.Join(", ", summaryParts) + ".");
                }

                var metadataSyncSucceeded = helloPhaseSucceeded && toolCatalogPhaseSucceeded;
                var metadataFailureKind = ResolveDeferredStartupMetadataFailureKind(
                    helloPhaseSucceeded: helloPhaseSucceeded,
                    toolCatalogPhaseSucceeded: toolCatalogPhaseSucceeded);
                var startupBootstrapCacheMode = Volatile.Read(ref _startupBootstrapCacheMode);
                var shouldRequestFailureRecoveryRerun = ShouldRequestDeferredStartupMetadataFailureRecoveryRerun(
                    isConnected: _isConnected,
                    shutdownRequested: _shutdownRequested,
                    helloPhaseSucceeded: helloPhaseSucceeded,
                    toolCatalogPhaseSucceeded: toolCatalogPhaseSucceeded,
                    retriesConsumed: Volatile.Read(ref _startupConnectMetadataFailureAutoRetryCount),
                    retryLimit: StartupDeferredMetadataFailureAutoRetryLimit);
                var failureRecoveryRerunQueued = shouldRequestFailureRecoveryRerun
                    && TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
                        ref _startupConnectMetadataFailureAutoRetryCount,
                        StartupDeferredMetadataFailureAutoRetryLimit);
                if (failureRecoveryRerunQueued) {
                    Interlocked.Exchange(ref _startupConnectMetadataDeferredRerunRequested, 1);
                    var retryCount = Volatile.Read(ref _startupConnectMetadataFailureAutoRetryCount);
                    StartupLog.Write(
                        "StartupConnect.metadata_sync rerun_requested_after_phase_failure hello="
                        + (helloPhaseSucceeded ? "1" : "0")
                        + " list_tools="
                        + (toolCatalogPhaseSucceeded ? "1" : "0")
                        + " retry="
                        + retryCount.ToString(CultureInfo.InvariantCulture)
                        + "/"
                        + StartupDeferredMetadataFailureAutoRetryLimit.ToString(CultureInfo.InvariantCulture));
                }

                var shouldRequestPersistedPreviewRefreshRerun = ShouldRequestDeferredStartupMetadataPersistedPreviewRefreshRerun(
                    metadataSyncSucceeded: metadataSyncSucceeded,
                    startupBootstrapCacheMode: startupBootstrapCacheMode,
                    isConnected: _isConnected,
                    shutdownRequested: _shutdownRequested,
                    retriesConsumed: Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount),
                    retryLimit: StartupDeferredMetadataPersistedPreviewRefreshRetryLimit);
                var persistedPreviewRefreshRerunQueued = shouldRequestPersistedPreviewRefreshRerun
                    && TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
                        ref _startupConnectMetadataPersistedPreviewRefreshRetryCount,
                        StartupDeferredMetadataPersistedPreviewRefreshRetryLimit);
                if (persistedPreviewRefreshRerunQueued) {
                    Interlocked.Exchange(ref _startupConnectMetadataDeferredRerunRequested, 1);
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 1);
                    var previewRetryCount = Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount);
                    StartupLog.Write(
                        "StartupConnect.metadata_sync rerun_requested_after_persisted_preview retry="
                        + previewRetryCount.ToString(CultureInfo.InvariantCulture)
                        + "/"
                        + StartupDeferredMetadataPersistedPreviewRefreshRetryLimit.ToString(CultureInfo.InvariantCulture));
                }

                if (startupBootstrapCacheMode != StartupBootstrapCacheModePersistedPreview) {
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount, 0);
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 0);
                }
                var persistedPreviewRefreshRetryLimitReached = HasReachedDeferredStartupMetadataPersistedPreviewRefreshRetryLimit(
                    metadataSyncSucceeded: metadataSyncSucceeded,
                    startupBootstrapCacheMode: startupBootstrapCacheMode,
                    retriesConsumed: Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshRetryCount),
                    retryLimit: StartupDeferredMetadataPersistedPreviewRefreshRetryLimit);

                if (metadataSyncSucceeded) {
                    Interlocked.Exchange(ref _startupConnectMetadataFailureAutoRetryCount, 0);
                    ClearStartupMetadataFailureRecoveryFailureMarker();
                } else if (!failureRecoveryRerunQueued) {
                    preserveStartupMetadataFailureStatus = true;
                }
                if (!metadataSyncSucceeded) {
                    var retryLimitReached = shouldRequestFailureRecoveryRerun && !failureRecoveryRerunQueued;
                    RecordStartupMetadataFailureRecoveryDiagnostics(
                        failureKind: metadataFailureKind,
                        rerunQueued: failureRecoveryRerunQueued,
                        retryLimitReached: retryLimitReached);
                }

                EndStartupMetadataSyncTracking();
                RecordStartupMetadataSyncDiagnostics(metadataSyncStopwatch.Elapsed, success: metadataSyncSucceeded);
                if (_isConnected && !_isSending && !_turnStartupInProgress) {
                    if (metadataSyncSucceeded) {
                        if (persistedPreviewRefreshRerunQueued || persistedPreviewRefreshRetryLimitReached) {
                            await SetStatusAsync(
                                    BuildStartupMetadataSyncPersistedPreviewStatusText(persistedPreviewRefreshRerunQueued),
                                    SessionStatusTone.Warn)
                                .ConfigureAwait(false);
                            StartupLog.Write(
                                persistedPreviewRefreshRerunQueued
                                    ? "StartupConnect.ready deferred_metadata_sync_preview_refresh_pending"
                                    : "StartupConnect.ready deferred_metadata_sync_preview_refresh_retry_limit_reached");
                        } else {
                            await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
                            StartupLog.Write("StartupConnect.ready deferred_metadata_sync_done");
                        }
                    } else {
                        await SetStatusAsync(
                                BuildStartupMetadataSyncRecoveryStatusText(failureRecoveryRerunQueued),
                                SessionStatusTone.Warn)
                            .ConfigureAwait(false);
                        StartupLog.Write("StartupConnect.ready deferred_metadata_sync_incomplete");
                    }
                }
            } catch (Exception ex) {
                EndStartupMetadataSyncTracking();
                RecordStartupMetadataSyncDiagnostics(metadataSyncStopwatch.Elapsed, success: false);
                StartupLog.Write(
                    "StartupConnect.metadata_sync failed after "
                    + FormatStartupPhaseDuration(metadataSyncStopwatch.Elapsed)
                    + ": "
                    + DescribeStartupExceptionForLog(ex));
            } finally {
                var keepAuthGateWaiting = ShouldKeepStartupAuthGateWaitingOnDeferredMetadataSyncExit(
                    exitedForAuthWait: exitedForAuthWait,
                    shutdownRequested: _shutdownRequested,
                    requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                    isAuthenticated: IsEffectivelyAuthenticatedForCurrentTransport(),
                    loginInProgress: _loginInProgress);
                if (!keepAuthGateWaiting) {
                    MarkStartupAuthGateResolved();
                }

                if (!_isConnected) {
                    EndStartupMetadataSyncTracking();
                }
                Interlocked.Exchange(ref _startupConnectMetadataDeferredQueued, 0);
                Interlocked.Exchange(ref _startupConnectMetadataDeferredQueuedUtcTicks, 0);
                Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
                var shouldDispatchRerun = ShouldDispatchDeferredStartupMetadataSyncRerun(
                    rerunRequested: Interlocked.Exchange(ref _startupConnectMetadataDeferredRerunRequested, 0) != 0,
                    shutdownRequested: _shutdownRequested,
                    isConnected: _isConnected);
                if (shouldDispatchRerun) {
                    var preferPersistedPreviewRefreshDelay = Volatile.Read(ref _startupConnectMetadataPersistedPreviewRefreshPending) != 0;
                    StartupLog.Write("StartupConnect.metadata_sync rerun_dispatch");
                    QueueDeferredStartupConnectMetadataSync(preferPersistedPreviewRefreshDelay: preferPersistedPreviewRefreshDelay);
                } else {
                    Interlocked.Exchange(ref _startupConnectMetadataPersistedPreviewRefreshPending, 0);
                }

                if (!shouldDispatchRerun
                    && _isConnected
                           && !preserveStartupMetadataFailureStatus
                           && !_shutdownRequested
                           && !_isSending
                           && !_turnStartupInProgress
                           && Volatile.Read(ref _startupMetadataSyncInProgress) == 0) {
                    var currentStatus = (_statusText ?? string.Empty).Trim();
                    if (currentStatus.Length > 0
                        && (StartupStatusPhaseContextRegex.IsMatch(currentStatus)
                            || currentStatus.StartsWith("Starting runtime...", StringComparison.OrdinalIgnoreCase))) {
                        try {
                            await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
                            StartupLog.Write("StartupConnect.ready cleared_stale_startup_status");
                        } catch (Exception refreshEx) {
                            StartupLog.Write("StartupConnect.ready stale_status_refresh_failed: " + DescribeStartupExceptionForLog(refreshEx));
                        }
                    }
                }
            }
        });
    }

    private void EnsureRestoredIfMinimized() {
        try {
            if (AppWindow?.Presenter is OverlappedPresenter overlapped
                && overlapped.State == OverlappedPresenterState.Minimized) {
                overlapped.Restore();
            }
        } catch {
            // Ignore.
        }
    }

    private void ConfigureWindowPlacement() {
        try {
            var appWindow = AppWindow;
            if (appWindow is null) {
                return;
            }

            const int width = 760;
            const int height = 900;
            appWindow.Resize(new SizeInt32(width, height));

            var iconPath = EnsureAppIcon();
            if (!string.IsNullOrEmpty(iconPath)) {
                appWindow.SetIcon(iconPath);
            }

            var display = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            var area = display.WorkArea;
            var x = area.X + Math.Max(0, (area.Width - width) / 2);
            var y = area.Y + Math.Max(0, (area.Height - height) / 2);
            appWindow.Move(new PointInt32(x, y));

            if (appWindow.Presenter is OverlappedPresenter overlapped) {
                overlapped.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            }

            EnsureNativeTitleBarRegionSupport();
        } catch (Exception ex) {
            StartupLog.Write("ConfigureWindowPlacement failed: " + ex.Message);
        }
    }

    private async Task EnsureWebViewInitializedAsync() {
        if (_webViewReady) {
            return;
        }

        try {
            var ensureWebViewStopwatch = Stopwatch.StartNew();
            var webViewEnvironment = await _webViewEnvironmentTask.ConfigureAwait(false);
            Task navReadyTask = Task.CompletedTask;
            await RunOnUiThreadAsync(async () => {
                if (_webViewReady) {
                    return;
                }

                StartupLog.Write("EnsureWebViewInitializedAsync begin");
                InstallWindowMessageHook();
                RefreshGlobalWheelHookPolicy();
                StartupLog.Write("EnsureWebViewInitializedAsync.ensure_core begin");
                if (webViewEnvironment is null) {
                    await _webView.EnsureCoreWebView2Async().AsTask().ConfigureAwait(false);
                } else {
                    await _webView.EnsureCoreWebView2Async(webViewEnvironment).AsTask().ConfigureAwait(false);
                }
                StartupLog.Write("EnsureWebViewInitializedAsync.ensure_core done");
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                ConfigureWebViewLocalAssetMapping();

                // WebView2 in WinUI 3 can swallow wheel events before they
                // reach web content. Intercept at the XAML level and forward.
                _webView.AddHandler(UIElement.PointerWheelChangedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => {
                        var delta = e.GetCurrentPoint(_webView).Properties.MouseWheelDelta;
                        if (delta != 0 && _webViewReady) {
                            RecordNativeWheelObserved();
                            QueueWheelForward(delta, fromGlobalHook: false);
                            // Keep native WebView wheel delivery as fallback for device-specific paths.
                            e.Handled = false;
                        }
                    }), true);

                var navTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnNavigationCompleted(WebView2 _, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs __) {
                    _webView.NavigationCompleted -= OnNavigationCompleted;
                    navTcs.TrySetResult(null);
                }
                _webView.NavigationCompleted += OnNavigationCompleted;
                StartupLog.Write("EnsureWebViewInitializedAsync.navigate begin");
                _webView.NavigateToString(BuildShellHtml());
                navReadyTask = navTcs.Task;
                _webViewReady = true;
                EnsureNativeTitleBarEventSubscriptions();
                RequestTitleBarMetricsRefresh();
            }).ConfigureAwait(false);

            var navReadyCompleted = ReferenceEquals(await Task.WhenAny(navReadyTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false), navReadyTask);
            StartupLog.Write(navReadyCompleted
                ? "EnsureWebViewInitializedAsync.navigate done"
                : "EnsureWebViewInitializedAsync.navigate timeout");
            await RunOnUiThreadAsync(() => {
                InstallWindowMessageHook();
                EnsureNativeTitleBarEventSubscriptions();
                RequestTitleBarMetricsRefresh();
                RefreshGlobalWheelHookPolicy();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            var captureStartupPhaseTelemetry = Volatile.Read(ref _startupFlowState) == StartupFlowStateRunning;
            if (ShouldDeferStartupWebViewPostInitialization(captureStartupPhaseTelemetry)) {
                StartupLog.Write("StartupPhase.WebView.post_init deferred");
                QueueDeferredStartupWebViewPostInitialization();
            } else {
                await ApplyWebViewPostInitializationAsync().ConfigureAwait(false);
            }
            RecordStartupWebViewEnsureCompletion(
                ensureDuration: ensureWebViewStopwatch.Elapsed,
                budgetExceeded: Volatile.Read(ref _startupWebViewBudgetExceededThisRun) != 0);
            StartupLog.Write("EnsureWebViewInitializedAsync ok");
        } catch (Exception ex) {
            StartupLog.Write("EnsureWebViewInitializedAsync failed: " + ex);
            throw;
        }
    }

    private void ConfigureWebViewLocalAssetMapping() {
        try {
            var uiDir = Path.Combine(AppContext.BaseDirectory, "Ui");
            if (!Directory.Exists(uiDir)) {
                return;
            }

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "ixchat.local",
                uiDir,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        } catch (Exception ex) {
            StartupLog.Write("ConfigureWebViewLocalAssetMapping failed: " + ex.Message);
        }
    }

    private async Task EnsureStartupConnectedAsync() {
        if (_startupInitialized) {
            return;
        }

        _startupInitialized = true;
        await ConnectAsync(fromUserAction: false).ConfigureAwait(false);
    }

    private async Task EnsureFirstRunAuthenticatedAsync() {
        var requiresInteractiveSignIn = RequiresInteractiveSignInForCurrentTransport();
        if (!requiresInteractiveSignIn) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            _autoSignInAttempted = true;
            return;
        }

        if (_loginInProgress) {
            return;
        }

        // Returning sessions should perform a fast, non-interactive auth refresh so startup metadata
        // sync can continue with cached runtime login state.
        if (_autoSignInAttempted || _appState.OnboardingCompleted || AnyConversationHasMessages()) {
            var authReady = await RefreshAuthenticationStateAsync(
                    updateStatus: true,
                    requireFreshProbe: false,
                    allowCachedAuthenticatedFallback: false,
                    probeTimeout: EnsureLoginFastPathProbeTimeout)
                .ConfigureAwait(false);
            if (!authReady && !_ensureLoginProbeCacheHasValue && !_loginInProgress) {
                if (EnsureLoginUnknownProbeRetryDelay > TimeSpan.Zero) {
                    await Task.Delay(EnsureLoginUnknownProbeRetryDelay).ConfigureAwait(false);
                }

                authReady = await RefreshAuthenticationStateAsync(
                        updateStatus: true,
                        requireFreshProbe: true,
                        allowCachedAuthenticatedFallback: false,
                        probeTimeout: EnsureLoginPostLoginProbeTimeout)
                    .ConfigureAwait(false);
            }
            if (ShouldQueueDeferredStartupMetadataSyncAfterAuthenticationReady(
                    isConnected: _isConnected,
                    requiresInteractiveSignIn: requiresInteractiveSignIn,
                    isAuthenticated: authReady,
                    loginInProgress: _loginInProgress,
                    hasSessionPolicy: _sessionPolicy is not null)) {
                QueueDeferredStartupConnectMetadataSync(requestRerunIfBusy: true);
            }

            return;
        }

        _autoSignInAttempted = true;
        await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
    }

    private static string FormatStartupPhaseDuration(TimeSpan elapsed) {
        return elapsed.TotalSeconds >= 1
            ? $"{elapsed.TotalSeconds:0.0}s"
            : $"{Math.Max(1, elapsed.TotalMilliseconds):0}ms";
    }

    private static string DescribeStartupExceptionForLog(Exception ex) {
        var primaryMessage = NormalizeExceptionMessageForStartupLog(ex.Message);
        var description = ex.GetType().Name + ": " + primaryMessage;

        var root = ex.GetBaseException();
        if (!ReferenceEquals(root, ex)) {
            description += " | root=" + root.GetType().Name + ": " + NormalizeExceptionMessageForStartupLog(root.Message);
        }

        if (ex.HResult != 0) {
            description += " | hresult=0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture);
        }

        return description;
    }

    private static string NormalizeExceptionMessageForStartupLog(string? message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return "(no message)";
        }

        var normalized = Regex.Replace(message, "\\s+", " ").Trim();
        const int maxLength = 240;
        if (normalized.Length <= maxLength) {
            return normalized;
        }

        return normalized[..maxLength] + "...";
    }

    private async Task EnsureAppStateLoadedAsync() {
        if (_appStateLoaded) {
            return;
        }

        _appStateLoaded = true;

        try {
            var names = await _stateStore.ListProfileNamesAsync(CancellationToken.None).ConfigureAwait(false);
            foreach (var name in names) {
                if (!string.IsNullOrWhiteSpace(name)) {
                    _knownProfiles.Add(name.Trim());
                }
            }

            await LoadProfileStateAsync(_appProfileName, render: true).ConfigureAwait(false);
        } catch (Exception ex) {
            AppendSystem(SystemNotice.StateLoadFailed(ex.Message));
            _appState = new ChatAppState { ProfileName = _appProfileName };
        }
    }

    private async Task LoadProfileStateAsync(string profileName, bool render) {
        var normalized = ResolveAppProfileName(profileName);
        var loaded = await _stateStore.GetAsync(normalized, CancellationToken.None).ConfigureAwait(false);
        var previousTransport = _localProviderTransport;

        _appProfileName = normalized;
        _knownProfiles.Add(normalized);
        _appState = loaded ?? new ChatAppState { ProfileName = normalized };
        _sessionUserNameOverride = null;
        _sessionAssistantPersonaOverride = null;
        _sessionThemeOverride = null;

        _appState.UserName = NormalizeUserNameValue(_appState.UserName);
        _appState.AssistantPersona = NormalizeAssistantPersonaValue(_appState.AssistantPersona);
        _themePreset = NormalizeTheme(_appState.ThemePreset) ?? "default";
        _appState.ThemePreset = _themePreset;
        _localProviderTransport = NormalizeLocalProviderTransport(_appState.LocalProviderTransport);
        _localProviderBaseUrl = NormalizeLocalProviderBaseUrl(_appState.LocalProviderBaseUrl, _localProviderTransport);
        _localProviderModel = NormalizeLocalProviderModel(_appState.LocalProviderModel, _localProviderTransport);
        _localProviderOpenAIAuthMode = NormalizeLocalProviderOpenAIAuthMode(_appState.LocalProviderOpenAIAuthMode);
        _localProviderOpenAIBasicUsername = NormalizeLocalProviderOpenAIBasicUsername(_appState.LocalProviderOpenAIBasicUsername);
        RestoreNativeAccountSlotsFromAppState();
        _localProviderReasoningEffort = NormalizeLocalProviderReasoningEffort(_appState.LocalProviderReasoningEffort);
        _localProviderReasoningSummary = NormalizeLocalProviderReasoningSummary(_appState.LocalProviderReasoningSummary);
        _localProviderTextVerbosity = NormalizeLocalProviderTextVerbosity(_appState.LocalProviderTextVerbosity);
        _localProviderTemperature = NormalizeLocalProviderTemperature(_appState.LocalProviderTemperature);
        _appState.LocalProviderTransport = _localProviderTransport;
        _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
        _appState.LocalProviderModel = _localProviderModel;
        _appState.LocalProviderOpenAIAuthMode = _localProviderOpenAIAuthMode;
        _appState.LocalProviderOpenAIBasicUsername = _localProviderOpenAIBasicUsername;
        _appState.LocalProviderOpenAIAccountId = _localProviderOpenAIAccountId;
        _appState.LocalProviderReasoningEffort = _localProviderReasoningEffort;
        _appState.LocalProviderReasoningSummary = _localProviderReasoningSummary;
        _appState.LocalProviderTextVerbosity = _localProviderTextVerbosity;
        _appState.LocalProviderTemperature = _localProviderTemperature;
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
        } else if (!string.Equals(previousTransport, TransportNative, StringComparison.OrdinalIgnoreCase)) {
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            _loginInProgress = false;
        }
        _authenticatedAccountId = null;
        RestoreAccountUsageFromAppState();
        _localRuntimeDetectionRan = false;
        _localRuntimeLmStudioAvailable = false;
        _localRuntimeOllamaAvailable = false;
        _localRuntimeDetectedName = null;
        _localRuntimeDetectedBaseUrl = null;
        _localRuntimeDetectionWarning = null;
        RestoreCachedModelCatalogFromAppState();
        _serviceProfileNames = Array.Empty<string>();
        _serviceActiveProfileName = null;
        if (_appState.OnboardingCompleted
            && BuildMissingOnboardingFields(
                _appState.UserName,
                _appState.AssistantPersona,
                _appState.ThemePreset,
                onboardingCompleted: true).Count > 0) {
            _appState.OnboardingCompleted = false;
        }

        if (!string.IsNullOrWhiteSpace(_appState.TimestampMode)) {
            _timestampMode = ResolveTimestampMode(_appState.TimestampMode);
            _timestampFormat = ResolveTimestampFormat(_appState.TimestampMode);
        }
        RestoreAutonomyOverridesFromAppState();
        _exportSaveMode = ExportPreferencesContract.NormalizeSaveMode(_appState.ExportSaveMode);
        _appState.ExportSaveMode = _exportSaveMode;
        _exportDefaultFormat = ExportPreferencesContract.NormalizeFormat(_appState.ExportDefaultFormat);
        _appState.ExportDefaultFormat = _exportDefaultFormat;
        _exportVisualThemeMode = ExportPreferencesContract.NormalizeVisualThemeMode(_appState.ExportVisualThemeMode);
        _appState.ExportVisualThemeMode = _exportVisualThemeMode;
        _exportDocxVisualMaxWidthPx = ExportPreferencesContract.NormalizeDocxVisualMaxWidthPx(_appState.ExportDocxVisualMaxWidthPx);
        _appState.ExportDocxVisualMaxWidthPx = _exportDocxVisualMaxWidthPx;
        _lastExportDirectory = ExportPreferencesContract.NormalizeDirectory(_appState.ExportLastDirectory);
        _appState.ExportLastDirectory = _lastExportDirectory;
        _queueAutoDispatchEnabled = _appState.QueueAutoDispatchEnabled;
        _appState.QueueAutoDispatchEnabled = _queueAutoDispatchEnabled;
        _proactiveModeEnabled = _appState.ProactiveModeEnabled;
        _appState.ProactiveModeEnabled = _proactiveModeEnabled;
        _persistentMemoryEnabled = _appState.PersistentMemoryEnabled;
        _appState.PersistentMemoryEnabled = _persistentMemoryEnabled;
        _showAssistantTurnTrace = _appState.ShowAssistantTurnTrace;
        _appState.ShowAssistantTurnTrace = _showAssistantTurnTrace;
        _showAssistantDraftBubbles = _appState.ShowAssistantDraftBubbles;
        _appState.ShowAssistantDraftBubbles = _showAssistantDraftBubbles;
        _appState.MemoryFacts = NormalizeMemoryFacts(_appState.MemoryFacts);
        ResetMemoryDiagnosticsState();

        var repairedLegacyTranscriptState = LoadConversationsFromState(_appState);
        RestoreQueuedTurnsFromState(_appState);
        ActivateConversation(ResolveInitialConversationId(_appState));
        if (repairedLegacyTranscriptState) {
            await PersistAppStateAsync().ConfigureAwait(false);
        }

        var knownToolNames = new List<string>(_toolDescriptions.Keys);
        _modelKickoffAttempted = _messages.Count > 0;
        _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();

        _toolStates.Clear();
        ClearToolRoutingInsights();
        foreach (var toolName in knownToolNames) {
            _toolStates[toolName] = !_toolWriteCapabilities.TryGetValue(toolName, out var isWriteCapable) || !isWriteCapable;
        }

        if (_appState.DisabledTools is { Count: > 0 }) {
            foreach (var tool in _appState.DisabledTools) {
                if (!string.IsNullOrWhiteSpace(tool)) {
                    _toolStates[tool.Trim()] = false;
                }
            }
        }

        if (!render) {
            return;
        }

        await RenderTranscriptAsync().ConfigureAwait(false);
        await ApplyThemeFromStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
    }

}
