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
                captureStartupPhaseTelemetry: Volatile.Read(ref _startupFlowState) == 1,
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
            if (await startupWebViewBudgetWaitTask.ConfigureAwait(false)) {
                StartupLog.Write("StartupPhase.WebView done");
            } else {
                StartupLog.Write("StartupPhase.WebView budget_exhausted");
                StartupLog.Write("StartupPhase.WebView deferred");
                MarkStartupWebViewBudgetExhausted();
                ObserveDeferredStartupWebViewInitialization(webViewInitializationTask);
            }
            StartupLog.Write("StartupPhase.Auth deferred");
            QueueDeferredStartupAuthentication();
            StartupLog.Write("StartupPhase.Onboarding deferred");
            QueueDeferredStartupOnboarding();
            Interlocked.Exchange(ref _startupFlowState, 2);
            StartupLog.Write("MainWindow.StartupFlow done");
        } catch (Exception ex) {
            Interlocked.Exchange(ref _startupFlowState, 0);
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

    private bool IsStartupInteractivePriorityRequested() {
        return Volatile.Read(ref _startupInteractivePriorityRequested) != 0;
    }

    private void MarkStartupInteractivePriorityRequested() {
        Interlocked.Exchange(ref _startupInteractivePriorityRequested, 1);
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
                if (IsStartupInteractivePriorityRequested()) {
                    StartupLog.Write("StartupConnect.model_profile_sync skipped_interactive_priority");
                    return;
                }

                StartupLog.Write("StartupConnect.model_profile_sync begin");
                await SyncConnectedServiceProfileAndModelsAsync(
                    forceModelRefresh: false,
                    setProfileNewThread: false,
                    appendWarnings: false).ConfigureAwait(false);
                StartupLog.Write("StartupConnect.model_profile_sync done");
            } catch (Exception ex) {
                StartupLog.Write("StartupConnect.model_profile_sync failed");
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem("Model/profile sync failed: " + ex.Message);
                }
            } finally {
                Interlocked.Exchange(ref _startupModelProfileSyncDeferredQueued, 0);
            }
        });
    }

    private void QueueDeferredStartupConnectMetadataSync() {
        if (_shutdownRequested) {
            return;
        }

        if (Interlocked.CompareExchange(ref _startupConnectMetadataDeferredQueued, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(StartupDeferredConnectMetadataDelay).ConfigureAwait(false);
                if (_shutdownRequested) {
                    return;
                }
                if (IsStartupInteractivePriorityRequested()) {
                    StartupLog.Write("StartupConnect.metadata_sync skipped_interactive_priority");
                    return;
                }

                var client = _client;
                if (client is null) {
                    return;
                }

                try {
                    if (IsStartupInteractivePriorityRequested()) {
                        StartupLog.Write("StartupConnect.hello skipped_interactive_priority");
                        return;
                    }
                    StartupLog.Write("StartupConnect.hello begin");
                    var hello = await client.RequestAsync<HelloMessage>(new HelloRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                    _sessionPolicy = hello.Policy;
                    StartupLog.Write("StartupConnect.hello done");
                    AppendStartupToolHealthWarningsFromPolicy();
                    AppendUnavailablePacksFromPolicy();
                } catch (Exception ex) {
                    _sessionPolicy = null;
                    StartupLog.Write("StartupConnect.hello failed");
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.HelloFailed(ex.Message));
                    }
                }

                try {
                    if (IsStartupInteractivePriorityRequested()) {
                        StartupLog.Write("StartupConnect.list_tools skipped_interactive_priority");
                        return;
                    }
                    StartupLog.Write("StartupConnect.list_tools begin");
                    var toolList = await client.RequestAsync<ToolListMessage>(new ListToolsRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                    UpdateToolCatalog(toolList.Tools);
                    StartupLog.Write("StartupConnect.list_tools done");
                } catch (Exception ex) {
                    StartupLog.Write("StartupConnect.list_tools failed");
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
                    }
                }

                try {
                    if (IsStartupInteractivePriorityRequested()) {
                        StartupLog.Write("StartupConnect.auth_refresh skipped_interactive_priority");
                        return;
                    }
                    StartupLog.Write("StartupConnect.auth_refresh begin");
                    _ = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
                    StartupLog.Write("StartupConnect.auth_refresh done");
                } catch (Exception ex) {
                    StartupLog.Write("StartupConnect.auth_refresh failed");
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(SystemNotice.EnsureLoginFailed(ex.Message));
                    }
                }

                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                StartupLog.Write("StartupConnect.metadata_sync failed: " + ex.Message);
            } finally {
                Interlocked.Exchange(ref _startupConnectMetadataDeferredQueued, 0);
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
            var captureStartupPhaseTelemetry = Volatile.Read(ref _startupFlowState) == 1;
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
        if (_autoSignInAttempted) {
            return;
        }

        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
            _autoSignInAttempted = true;
            return;
        }

        if (_appState.OnboardingCompleted || AnyConversationHasMessages() || _isAuthenticated || _loginInProgress) {
            return;
        }

        _autoSignInAttempted = true;
        await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
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
        _appState.MemoryFacts = NormalizeMemoryFacts(_appState.MemoryFacts);
        ResetMemoryDiagnosticsState();

        var repairedLegacyTranscriptState = LoadConversationsFromState(_appState);
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
            _toolStates[toolName] = true;
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

    private bool AnyConversationHasMessages() {
        foreach (var conversation in _conversations) {
            if (conversation.Messages.Count > 0) {
                return true;
            }
        }

        return false;
    }

    private sealed record StartupWebViewBudgetCacheEntry(
        int? LastEnsureWebViewMs,
        int ConsecutiveBudgetExhaustions,
        int ConsecutiveStableCompletions,
        int AdaptiveCooldownRunsRemaining,
        int? LastAppliedBudgetMs,
        DateTime? UpdatedUtc) {
        public static StartupWebViewBudgetCacheEntry Default { get; } = new(
            LastEnsureWebViewMs: null,
            ConsecutiveBudgetExhaustions: 0,
            ConsecutiveStableCompletions: 0,
            AdaptiveCooldownRunsRemaining: 0,
            LastAppliedBudgetMs: null,
            UpdatedUtc: null);
    }

    private sealed class StartupWebViewBudgetCachePayload {
        public int? LastEnsureWebViewMs { get; set; }
        public int ConsecutiveBudgetExhaustions { get; set; }
        public int ConsecutiveStableCompletions { get; set; }
        public int AdaptiveCooldownRunsRemaining { get; set; }
        public int? LastAppliedBudgetMs { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    private sealed record StartupWebViewBudgetDecision(int BudgetMs, string Reason);
}
