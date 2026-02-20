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
    private const int MaxRoutingInsightPayloadChars = 4096;
    private static readonly string[] BridgeAuthFailureWarningTokens = {
        "401",
        "403",
        "unauthorized",
        "forbidden",
        "authentication failed",
        "invalid credentials",
        "invalid username",
        "invalid password",
        "invalid token",
        "auth required"
    };

    private void ClearConversation() {
        var conversation = GetActiveConversation();
        if (IsSystemConversation(conversation)) {
            _ = SetStatusAsync("System conversation cannot be cleared.", SessionStatusTone.Warn);
            return;
        }
        conversation.Messages.Clear();
        conversation.Title = DefaultConversationTitle;
        conversation.ThreadId = null;
        conversation.UpdatedUtc = DateTime.UtcNow;
        _messages = conversation.Messages;
        _assistantStreaming.Clear();
        _threadId = null;
        ClearToolRoutingInsights();
        if (string.Equals(_activeRequestConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            _activeRequestConversationId = null;
        }
        _modelKickoffAttempted = false;
        _modelKickoffInProgress = false;
        _pendingLoginPrompt = null;
        _ = RenderTranscriptAsync();
        _ = PublishOptionsStateSafeAsync();
        QueuePersistAppState();
    }

    private void AppendSystem(string text) {
        var conversation = EnsureSystemConversation();
        AppendSystem(conversation, text);
    }

    private void AppendSystem(ConversationRuntime conversation, string text) {
        conversation.Messages.Add(("System", text, DateTime.Now));
        conversation.UpdatedUtc = DateTime.UtcNow;
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            _ = RenderTranscriptAsync();
        }
    }

    private void AppendSystem(SystemNotice notice) {
        AppendSystem(SystemNoticeFormatter.Format(notice));
    }

    private void AppendSystem(ConversationRuntime conversation, SystemNotice notice) {
        AppendSystem(conversation, SystemNoticeFormatter.Format(notice));
    }

    private async Task RenderTranscriptAsync() {
        if (!_webViewReady) {
            return;
        }

        var requestedGeneration = Interlocked.Increment(ref _transcriptRenderGeneration);
        await _transcriptRenderGate.WaitAsync().ConfigureAwait(false);
        try {
            var latestGeneration = Interlocked.Read(ref _transcriptRenderGeneration);
            if (requestedGeneration < latestGeneration) {
                return;
            }

            if (_isSending && _assistantStreaming.Length > 0) {
                var previousTicks = Interlocked.Read(ref _transcriptLastRenderUtcTicks);
                if (previousTicks > 0) {
                    var elapsedTicks = DateTime.UtcNow.Ticks - previousTicks;
                    var minimumTicks = StreamingTranscriptRenderCadence.Ticks;
                    if (elapsedTicks < minimumTicks) {
                        await Task.Delay(TimeSpan.FromTicks(minimumTicks - elapsedTicks)).ConfigureAwait(false);
                        latestGeneration = Interlocked.Read(ref _transcriptRenderGeneration);
                        if (requestedGeneration < latestGeneration) {
                            return;
                        }
                    }
                }
            }

            var messagesSnapshot = SnapshotMessagesForRender(_messages);
            var timestampFormat = _timestampFormat;
            var markdownOptions = _markdownOptions;
            var html = await Task.Run(() => BuildMessagesHtml(messagesSnapshot, timestampFormat, markdownOptions)).ConfigureAwait(false);
            latestGeneration = Interlocked.Read(ref _transcriptRenderGeneration);
            if (requestedGeneration < latestGeneration) {
                return;
            }
            var json = JsonSerializer.Serialize(html);
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetTranscript(" + json + ");").AsTask()).ConfigureAwait(false);
            Interlocked.Exchange(ref _transcriptLastRenderUtcTicks, DateTime.UtcNow.Ticks);
        } finally {
            _transcriptRenderGate.Release();
        }
    }

    private static (string Role, string Text, DateTime Time)[] SnapshotMessagesForRender(IReadOnlyList<(string Role, string Text, DateTime Time)> messages) {
        var snapshot = new (string Role, string Text, DateTime Time)[messages.Count];
        for (var i = 0; i < messages.Count; i++) {
            var message = messages[i];
            snapshot[i] = (message.Role ?? string.Empty, message.Text ?? string.Empty, message.Time);
        }

        return snapshot;
    }

    private async Task SetStatusAsync(string text, SessionStatusTone? tone = null, bool? usageLimitSwitchRecommended = null) {
        _statusText = text ?? string.Empty;
        _statusTone = tone ?? InferStatusTone(_statusText);
        _usageLimitSwitchRecommended = usageLimitSwitchRecommended ?? InferUsageLimitSwitchRecommendation(_statusText);
        if (!_webViewReady) {
            return;
        }

        var textJson = JsonSerializer.Serialize(_statusText);
        var toneJson = JsonSerializer.Serialize(MapStatusTone(_statusTone));
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetStatus(" + textJson + "," + toneJson + ");").AsTask())
            .ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
    }

    private Task SetStatusAsync(SessionStatus status) {
        return SetStatusAsync(
            SessionStatusFormatter.Format(status),
            SessionStatusToneResolver.Resolve(status),
            status.Kind == SessionStatusKind.UsageLimitReached);
    }

    private async Task PublishSessionStateAsync() {
        await QueueUiPublishAsync(requestSessionState: true, requestOptionsState: false).ConfigureAwait(false);
    }

    private async Task PublishSessionStateCoreAsync() {
        if (!_webViewReady) {
            return;
        }

        var effectiveAuthenticated = IsEffectivelyAuthenticatedForCurrentTransport();
        var effectiveLoginInProgress = RequiresInteractiveSignInForCurrentTransport() && _loginInProgress;
        var queuedPromptCount = GetQueuedPromptAfterLoginCount();
        var queuedTurnCount = GetQueuedTurnCount();
        var json = JsonSerializer.Serialize(new {
            status = _statusText,
            statusTone = MapStatusTone(_statusTone),
            usageLimitSwitchRecommended = _usageLimitSwitchRecommended,
            queuedPromptPending = queuedPromptCount > 0,
            queuedPromptCount,
            queuedTurnCount,
            connected = _isConnected,
            authenticated = effectiveAuthenticated,
            accountId = _authenticatedAccountId ?? string.Empty,
            loginInProgress = effectiveLoginInProgress,
            sending = _isSending,
            cancelable = _isSending && !string.IsNullOrWhiteSpace(_activeTurnRequestId),
            cancelRequested = _isSending && !string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId),
            activityTimeline = SnapshotActivityTimeline(),
            lastTurnMetrics = BuildLastTurnMetricsState(),
            debugMode = _debugMode,
            windowMaximized = IsWindowMaximized()
        });
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetSessionState(" + json + ");").AsTask()).ConfigureAwait(false);
    }

    private static string MapStatusTone(SessionStatusTone tone) {
        return tone switch {
            SessionStatusTone.Ok => "ok",
            SessionStatusTone.Warn => "warn",
            SessionStatusTone.Bad => "bad",
            _ => "neutral"
        };
    }

    private static SessionStatusTone InferStatusTone(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return SessionStatusTone.Neutral;
        }

        if (normalized.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("limit", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("unavailable", StringComparison.OrdinalIgnoreCase)) {
            return SessionStatusTone.Bad;
        }

        if (normalized.Contains("ready", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("connected", StringComparison.OrdinalIgnoreCase)) {
            return SessionStatusTone.Ok;
        }

        if (normalized.Contains("sign", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("wait", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("open", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("start", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("connecting", StringComparison.OrdinalIgnoreCase)) {
            return SessionStatusTone.Warn;
        }

        return SessionStatusTone.Neutral;
    }

    private static bool InferUsageLimitSwitchRecommendation(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("switch account", StringComparison.OrdinalIgnoreCase);
    }

    private object BuildLocalRuntimeCapabilitiesState() {
        var transport = NormalizeLocalProviderTransport(_localProviderTransport);
        var baseUrl = (_localProviderBaseUrl ?? string.Empty).Trim();
        var preset = DetectCompatibleProviderPreset(baseUrl);
        var isNative = string.Equals(transport, TransportNative, StringComparison.OrdinalIgnoreCase);
        var isCopilotCli = string.Equals(transport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase);
        var isCompatible = string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase);
        var copilotConnected = isCompatible
            && baseUrl.Contains("api.githubcopilot.com", StringComparison.OrdinalIgnoreCase);
        var supportsReasoningControls = SupportsLocalProviderReasoningControls(transport, baseUrl);
        var reasoningSupport = DescribeLocalProviderReasoningSupport(transport, baseUrl);
        var (trackedAccounts, accountsWithRetrySignals) = GetRuntimeUsageCapabilityCounts();
        var isBridgePreset = string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                             && IsBridgeCompatiblePreset(preset);
        var bridgeAccountIdentity = ResolveBridgeAccountIdentity(
            _localProviderOpenAIAccountId,
            _localProviderOpenAIAuthMode,
            _localProviderOpenAIBasicUsername);
        var bridgeSessionState = isBridgePreset
            ? ResolveBridgeSessionState(
                Volatile.Read(ref _localProviderApplyInFlight) != 0 || _runtimeApplyActive,
                _modelListWarning,
                _availableModels.Length)
            : string.Empty;
        var bridgeSessionDetail = isBridgePreset
            ? ResolveBridgeSessionDetail(bridgeSessionState, bridgeAccountIdentity, _modelListWarning)
            : string.Empty;

        return new {
            providerLabel = ResolveRuntimeProviderLabelForState(transport, preset, copilotConnected, baseUrl),
            compatiblePreset = preset,
            supportsModelCatalog = isNative || isCopilotCli || isCompatible,
            supportsReasoningControls,
            reasoningSupport,
            supportsNativeAccountSlots = isNative,
            nativeAccountSlots = isNative ? 3 : 0,
            supportsLiveApply = true,
            requiresProcessRestart = false,
            trackedAccounts,
            accountsWithRetrySignals,
            isBridgePreset,
            bridgeAccountIdentity,
            bridgeSessionState,
            bridgeSessionDetail
        };
    }

    internal static bool IsBridgeCompatiblePreset(string? compatiblePreset) {
        var normalized = (compatiblePreset ?? string.Empty).Trim();
        return string.Equals(normalized, "anthropic-bridge", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "gemini-bridge", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsBridgeAuthFailureWarning(string? warning) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < BridgeAuthFailureWarningTokens.Length; i++) {
            if (normalized.Contains(BridgeAuthFailureWarningTokens[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    internal static string ResolveBridgeSessionState(bool applyInFlight, string? warning, int discoveredModels) {
        if (applyInFlight) {
            return "connecting";
        }

        if (IsBridgeAuthFailureWarning(warning)) {
            return "auth-failed";
        }

        return discoveredModels > 0 ? "ready" : "connecting";
    }

    internal static string ResolveBridgeSessionDetail(
        string? state,
        string? bridgeAccountIdentity,
        string? warning) {
        var normalizedState = (state ?? string.Empty).Trim();
        var normalizedIdentity = (bridgeAccountIdentity ?? string.Empty).Trim();
        var normalizedWarning = (warning ?? string.Empty).Trim();

        if (string.Equals(normalizedState, "ready", StringComparison.OrdinalIgnoreCase)) {
            return normalizedIdentity.Length == 0
                ? "Bridge session ready."
                : "Bridge session ready for " + normalizedIdentity + ".";
        }

        if (string.Equals(normalizedState, "auth-failed", StringComparison.OrdinalIgnoreCase)) {
            return normalizedWarning.Length == 0
                ? "Bridge authentication failed. Update login/email + secret/token and apply again."
                : normalizedWarning;
        }

        return normalizedIdentity.Length == 0
            ? "Connecting to bridge runtime..."
            : "Connecting to bridge runtime for " + normalizedIdentity + "...";
    }

    private static string ResolveBridgeAccountIdentity(
        string? openAIAccountId,
        string? openAIAuthMode,
        string? openAIBasicUsername) {
        var normalizedAccountId = (openAIAccountId ?? string.Empty).Trim();
        if (normalizedAccountId.Length > 0) {
            return normalizedAccountId;
        }

        var normalizedAuthMode = (openAIAuthMode ?? string.Empty).Trim();
        if (!string.Equals(normalizedAuthMode, "basic", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        return (openAIBasicUsername ?? string.Empty).Trim();
    }

    private (int TrackedAccounts, int AccountsWithRetrySignals) GetRuntimeUsageCapabilityCounts() {
        lock (_turnDiagnosticsSync) {
            if (_accountUsageByKey.Count == 0) {
                return (0, 0);
            }

            var tracked = 0;
            var retrySignals = 0;
            foreach (var snapshot in _accountUsageByKey.Values) {
                tracked++;
                if (snapshot.UsageLimitRetryAfterUtc.HasValue
                    || snapshot.RateLimitWindowResetUtc.HasValue
                    || snapshot.RateLimitReached == true) {
                    retrySignals++;
                }
            }

            return (tracked, retrySignals);
        }
    }

    private static string ResolveRuntimeProviderLabelForState(
        string transport,
        string compatiblePreset,
        bool copilotConnected,
        string baseUrl) {
        if (string.Equals(transport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return "GitHub Copilot subscription runtime";
        }

        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return "ChatGPT runtime (OpenAI native)";
        }

        if (string.Equals(compatiblePreset, "lmstudio", StringComparison.OrdinalIgnoreCase)) {
            return "LM Studio runtime";
        }

        if (string.Equals(compatiblePreset, "ollama", StringComparison.OrdinalIgnoreCase)) {
            return "Ollama runtime";
        }

        if (string.Equals(compatiblePreset, "openai", StringComparison.OrdinalIgnoreCase)) {
            return "OpenAI API runtime";
        }

        if (string.Equals(compatiblePreset, "azure-openai", StringComparison.OrdinalIgnoreCase)) {
            return "Azure OpenAI runtime";
        }

        if (string.Equals(compatiblePreset, "anthropic-bridge", StringComparison.OrdinalIgnoreCase)) {
            return "Anthropic subscription bridge runtime";
        }

        if (string.Equals(compatiblePreset, "gemini-bridge", StringComparison.OrdinalIgnoreCase)) {
            return "Gemini subscription bridge runtime";
        }

        if (copilotConnected) {
            return "GitHub Copilot runtime";
        }

        return baseUrl.Length == 0
            ? "Compatible HTTP runtime"
            : "Compatible HTTP runtime (" + DescribeRuntimeFromBaseUrl(baseUrl) + ")";
    }

    private async Task PublishOptionsStateAsync() {
        await QueueUiPublishAsync(requestSessionState: false, requestOptionsState: true).ConfigureAwait(false);
    }

    private async Task PublishOptionsStateCoreAsync() {
        if (!_webViewReady) {
            return;
        }

        var packs = _sessionPolicy?.Packs is { Length: > 0 }
            ? BuildPackState(_sessionPolicy.Packs)
            : Array.Empty<object>();

        var tools = BuildToolState();
        var toolsLoading = _isConnected && _sessionPolicy is null;
        var conversations = BuildConversationState();
        var accountUsageState = BuildAccountUsageState();
        var activeAccountUsageState = BuildActiveAccountUsageState();
        var runtimeCapabilitiesState = BuildLocalRuntimeCapabilitiesState();
        var json = JsonSerializer.Serialize(new {
            timestampMode = _timestampMode,
            timestampFormat = _timestampFormat,
            export = new {
                saveMode = _exportSaveMode,
                defaultFormat = _exportDefaultFormat,
                visualThemeMode = _exportVisualThemeMode,
                lastDirectory = _lastExportDirectory ?? string.Empty
            },
            autonomy = new {
                maxToolRounds = _autonomyMaxToolRounds,
                parallelTools = _autonomyParallelTools,
                parallelToolMode = ResolveParallelToolMode(_autonomyParallelTools),
                turnTimeoutSeconds = _autonomyTurnTimeoutSeconds,
                toolTimeoutSeconds = _autonomyToolTimeoutSeconds,
                weightedToolRouting = _autonomyWeightedToolRouting,
                maxCandidateTools = _autonomyMaxCandidateTools,
                planExecuteReviewLoop = _autonomyPlanExecuteReviewLoop,
                maxReviewPasses = _autonomyMaxReviewPasses,
                modelHeartbeatSeconds = _autonomyModelHeartbeatSeconds,
                queueAutoDispatch = _queueAutoDispatchEnabled,
                proactiveMode = _proactiveModeEnabled
            },
            memory = BuildMemoryState(),
            memoryDebug = BuildMemoryDebugState(),
            activeProfileName = _appProfileName,
            profileNames = BuildKnownProfiles(),
            activeConversationId = _activeConversationId,
            conversations,
            profile = new {
                userName = GetEffectiveUserName() ?? string.Empty,
                persona = GetEffectiveAssistantPersona() ?? string.Empty,
                theme = GetEffectiveThemePreset(),
                onboardingCompleted = _appState.OnboardingCompleted,
                sessionOverrides = new {
                    userName = !string.IsNullOrWhiteSpace(_sessionUserNameOverride),
                    persona = !string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride),
                    theme = !string.IsNullOrWhiteSpace(_sessionThemeOverride)
                }
            },
            localModel = new {
                transport = _localProviderTransport,
                baseUrl = _localProviderBaseUrl ?? string.Empty,
                isApplying = Volatile.Read(ref _localProviderApplyInFlight) != 0,
                modelsEndpoint = string.Equals(_localProviderTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                    ? BuildModelsProbeUrl(_localProviderBaseUrl ?? string.Empty)
                    : string.Empty,
                model = _localProviderModel,
                openAIAuthMode = _localProviderOpenAIAuthMode,
                openAIBasicUsername = _localProviderOpenAIBasicUsername,
                openAIAccountId = _localProviderOpenAIAccountId,
                activeNativeAccountSlot = _activeNativeAccountSlot,
                nativeAccountSlots = BuildNativeAccountSlotState(),
                reasoningEffort = _localProviderReasoningEffort,
                reasoningSummary = _localProviderReasoningSummary,
                textVerbosity = _localProviderTextVerbosity,
                temperature = _localProviderTemperature,
                models = _availableModels,
                favoriteModels = _favoriteModels,
                recentModels = _recentModels,
                isStale = _modelListIsStale,
                warning = _modelListWarning,
                profileSaved = Array.Exists(_serviceProfileNames, name => string.Equals(name, _appProfileName, StringComparison.OrdinalIgnoreCase)),
                authenticatedAccountId = RequiresInteractiveSignInForCurrentTransport() ? (_authenticatedAccountId ?? string.Empty) : string.Empty,
                accountUsage = accountUsageState,
                activeAccountUsage = activeAccountUsageState,
                runtimeCapabilities = runtimeCapabilitiesState,
                runtimeDetection = new {
                    hasRun = _localRuntimeDetectionRan,
                    lmStudioAvailable = _localRuntimeLmStudioAvailable,
                    ollamaAvailable = _localRuntimeOllamaAvailable,
                    detectedName = _localRuntimeDetectedName ?? string.Empty,
                    detectedBaseUrl = _localRuntimeDetectedBaseUrl ?? string.Empty,
                    warning = _localRuntimeDetectionWarning ?? string.Empty
                },
                runtimeApply = new {
                    stage = _runtimeApplyStage,
                    detail = _runtimeApplyDetail,
                    isActive = _runtimeApplyActive,
                    requestId = _runtimeApplyRequestId,
                    updatedLocal = _runtimeApplyUpdatedUtc.HasValue
                        ? _runtimeApplyUpdatedUtc.Value.ToLocalTime().ToString(_timestampFormat, CultureInfo.InvariantCulture)
                        : string.Empty
                }
            },
            packs,
            tools,
            toolsLoading,
            policy = _sessionPolicy is null ? null : new {
                readOnly = _sessionPolicy.ReadOnly,
                dangerousToolsEnabled = _sessionPolicy.DangerousToolsEnabled,
                turnTimeoutSeconds = _sessionPolicy.TurnTimeoutSeconds,
                toolTimeoutSeconds = _sessionPolicy.ToolTimeoutSeconds,
                maxToolRounds = _sessionPolicy.MaxToolRounds,
                parallelTools = _sessionPolicy.ParallelTools,
                startupWarnings = _sessionPolicy.StartupWarnings,
                pluginSearchPaths = _sessionPolicy.PluginSearchPaths
            }
        });

        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetOptionsData(" + json + ");").AsTask()).ConfigureAwait(false);
    }

    private Task QueueUiPublishAsync(bool requestSessionState, bool requestOptionsState) {
        if (!requestSessionState && !requestOptionsState) {
            return Task.CompletedTask;
        }

        Task sessionTask = Task.CompletedTask;
        Task optionsTask = Task.CompletedTask;
        var shouldStartPump = false;
        CancellationToken pumpToken = CancellationToken.None;

        lock (_uiPublishSync) {
            if (_shutdownRequested) {
                return Task.CompletedTask;
            }

            if (requestSessionState) {
                _pendingSessionStatePublish = true;
                _pendingSessionStatePublishTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                sessionTask = _pendingSessionStatePublishTcs.Task;
            }

            if (requestOptionsState) {
                _pendingOptionsStatePublish = true;
                _pendingOptionsStatePublishTcs ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                optionsTask = _pendingOptionsStatePublishTcs.Task;
            }

            if (!_uiPublishPumpRunning || _uiPublishPumpCts is null || _uiPublishPumpCts.IsCancellationRequested) {
                _uiPublishPumpCts?.Dispose();
                _uiPublishPumpCts = new CancellationTokenSource();
                pumpToken = _uiPublishPumpCts.Token;
                _uiPublishPumpRunning = true;
                shouldStartPump = true;
            }
        }

        if (shouldStartPump) {
            _ = Task.Run(() => ProcessUiPublishQueueAsync(pumpToken));
        }

        var queuedTask = requestSessionState && requestOptionsState
            ? Task.WhenAll(sessionTask, optionsTask)
            : requestSessionState
                ? sessionTask
                : optionsTask;

        // Public publish calls are best-effort and must not surface queue cancellation races.
        return CompleteUiPublishBestEffortAsync(queuedTask);
    }

    private static async Task CompleteUiPublishBestEffortAsync(Task publishTask) {
        try {
            await publishTask.ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Queue cancellation during shutdown is expected and intentionally non-fatal.
        }
    }

    private async Task ProcessUiPublishQueueAsync(CancellationToken cancellationToken) {
        try {
            while (true) {
                bool publishSession;
                bool publishOptions;
                TaskCompletionSource<object?>? sessionTcs;
                TaskCompletionSource<object?>? optionsTcs;

                lock (_uiPublishSync) {
                    publishSession = _pendingSessionStatePublish;
                    publishOptions = _pendingOptionsStatePublish;
                    sessionTcs = _pendingSessionStatePublishTcs;
                    optionsTcs = _pendingOptionsStatePublishTcs;
                    _pendingSessionStatePublish = false;
                    _pendingOptionsStatePublish = false;
                    _pendingSessionStatePublishTcs = null;
                    _pendingOptionsStatePublishTcs = null;
                    _activeSessionStatePublishTcs = sessionTcs;
                    _activeOptionsStatePublishTcs = optionsTcs;
                }

                try {
                    var coalesceDelayApplied = false;
                    if (!publishSession && !publishOptions) {
                        // Re-check after one coalesce window so requests arriving during idle transition are not dropped.
                        try {
                            await Task.Delay(UiPublishCoalesceInterval, cancellationToken).ConfigureAwait(false);
                            coalesceDelayApplied = true;
                        } catch (OperationCanceledException) {
                            FinalizeUiPublishAwaiter(sessionTcs, preferCancel: _shutdownRequested);
                            FinalizeUiPublishAwaiter(optionsTcs, preferCancel: _shutdownRequested);
                            break;
                        }

                        lock (_uiPublishSync) {
                            publishSession = _pendingSessionStatePublish;
                            publishOptions = _pendingOptionsStatePublish;
                            sessionTcs = _pendingSessionStatePublishTcs;
                            optionsTcs = _pendingOptionsStatePublishTcs;
                            _pendingSessionStatePublish = false;
                            _pendingOptionsStatePublish = false;
                            _pendingSessionStatePublishTcs = null;
                            _pendingOptionsStatePublishTcs = null;
                            _activeSessionStatePublishTcs = sessionTcs;
                            _activeOptionsStatePublishTcs = optionsTcs;
                        }

                        if (!publishSession && !publishOptions) {
                            // Idle transition: no-op publish requests should complete rather than cancel.
                            FinalizeUiPublishAwaiter(sessionTcs, preferCancel: false);
                            FinalizeUiPublishAwaiter(optionsTcs, preferCancel: false);
                            break;
                        }
                    }

                    if (!coalesceDelayApplied) {
                        try {
                            await Task.Delay(UiPublishCoalesceInterval, cancellationToken).ConfigureAwait(false);
                        } catch (OperationCanceledException) {
                            FinalizeUiPublishAwaiter(sessionTcs, preferCancel: _shutdownRequested);
                            FinalizeUiPublishAwaiter(optionsTcs, preferCancel: _shutdownRequested);
                            break;
                        }
                    }

                    if (publishSession) {
                        try {
                            await RunOnUiThreadAsync(() => PublishSessionStateCoreAsync()).ConfigureAwait(false);
                            sessionTcs?.TrySetResult(null);
                        } catch (OperationCanceledException) {
                            FinalizeUiPublishAwaiter(sessionTcs, preferCancel: _shutdownRequested);
                        } catch (Exception ex) {
                            sessionTcs?.TrySetException(ex);
                        }
                    }

                    if (publishOptions) {
                        try {
                            await RunOnUiThreadAsync(() => PublishOptionsStateCoreAsync()).ConfigureAwait(false);
                            optionsTcs?.TrySetResult(null);
                        } catch (OperationCanceledException) {
                            FinalizeUiPublishAwaiter(optionsTcs, preferCancel: _shutdownRequested);
                        } catch (Exception ex) {
                            optionsTcs?.TrySetException(ex);
                        }
                    }
                } finally {
                    lock (_uiPublishSync) {
                        if (ReferenceEquals(_activeSessionStatePublishTcs, sessionTcs)) {
                            _activeSessionStatePublishTcs = null;
                        }

                        if (ReferenceEquals(_activeOptionsStatePublishTcs, optionsTcs)) {
                            _activeOptionsStatePublishTcs = null;
                        }
                    }
                }
            }
        } finally {
            var shouldRestart = false;
            CancellationToken restartToken = CancellationToken.None;

            lock (_uiPublishSync) {
                // Ignore stale pump finalizers after ownership moved to a newer token/worker.
                if (_uiPublishPumpCts is { } activePumpCts && activePumpCts.Token == cancellationToken) {
                    _uiPublishPumpRunning = false;

                    if (!_shutdownRequested && (_pendingSessionStatePublish || _pendingOptionsStatePublish)) {
                        if (activePumpCts.IsCancellationRequested) {
                            activePumpCts.Dispose();
                            activePumpCts = new CancellationTokenSource();
                            _uiPublishPumpCts = activePumpCts;
                        }

                        restartToken = activePumpCts.Token;
                        _uiPublishPumpRunning = true;
                        shouldRestart = true;
                    } else {
                        activePumpCts.Dispose();
                        _uiPublishPumpCts = null;
                    }
                }
            }

            if (shouldRestart) {
                _ = Task.Run(() => ProcessUiPublishQueueAsync(restartToken));
            }
        }
    }

    private void FinalizeUiPublishAwaiter(TaskCompletionSource<object?>? tcs, bool preferCancel) {
        if (tcs is null) {
            return;
        }

        if (preferCancel) {
            tcs.TrySetCanceled();
            return;
        }

        tcs.TrySetResult(null);
    }

    private static void CancelUiPublishAwaiter(TaskCompletionSource<object?>? tcs) {
        tcs?.TrySetCanceled();
    }

    private void CancelQueuedUiPublishesForShutdown() {
        TaskCompletionSource<object?>? pendingSession;
        TaskCompletionSource<object?>? pendingOptions;
        TaskCompletionSource<object?>? activeSession;
        TaskCompletionSource<object?>? activeOptions;
        CancellationTokenSource? pumpCts;

        lock (_uiPublishSync) {
            // Terminal shutdown boundary only: cancel queue state and freeze new publishes.
            _shutdownRequested = true;
            pendingSession = _pendingSessionStatePublishTcs;
            pendingOptions = _pendingOptionsStatePublishTcs;
            activeSession = _activeSessionStatePublishTcs;
            activeOptions = _activeOptionsStatePublishTcs;
            pumpCts = _uiPublishPumpCts;
            _pendingSessionStatePublish = false;
            _pendingOptionsStatePublish = false;
            _pendingSessionStatePublishTcs = null;
            _pendingOptionsStatePublishTcs = null;
            _activeSessionStatePublishTcs = null;
            _activeOptionsStatePublishTcs = null;
            if (_uiPublishPumpCts is null) {
                _uiPublishPumpRunning = false;
            }
        }

        // Queue teardown is a cancellation boundary: pending awaiters should observe cancellation.
        CancelUiPublishAwaiter(pendingSession);
        CancelUiPublishAwaiter(pendingOptions);
        CancelUiPublishAwaiter(activeSession);
        CancelUiPublishAwaiter(activeOptions);

        if (pumpCts is null) {
            return;
        }

        try {
            // Preserve token ownership semantics: the running pump finalizer disposes and clears state.
            pumpCts.Cancel();
        } catch (ObjectDisposedException) {
            // Pump already finalized/disposed concurrently.
        }
    }

    private object[] BuildConversationState() {
        if (_conversations.Count == 0) {
            return Array.Empty<object>();
        }

        var ordered = new List<ConversationRuntime>(_conversations);
        ordered.Sort(CompareConversationsForDisplay);
        var list = new List<object>(ordered.Count);
        foreach (var conversation in ordered) {
            var isSystem = IsSystemConversation(conversation);
            var updatedUtc = conversation.UpdatedUtc == default ? DateTime.UtcNow : conversation.UpdatedUtc;
            var updatedLocal = EnsureUtc(updatedUtc).ToLocalTime();
            var preview = string.Empty;
            for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
                var text = (conversation.Messages[i].Text ?? string.Empty).Trim();
                if (text.Length == 0) {
                    continue;
                }

                preview = BuildConversationTitleFromText(text);
                break;
            }

            list.Add(new {
                id = conversation.Id,
                title = isSystem
                    ? SystemConversationTitle
                    : string.IsNullOrWhiteSpace(conversation.Title)
                        ? DefaultConversationTitle
                        : conversation.Title,
                messageCount = conversation.Messages.Count,
                preview,
                isActive = string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase),
                isSystem,
                updatedLocal = updatedLocal.ToString(_timestampFormat, CultureInfo.InvariantCulture)
            });
        }

        return list.ToArray();
    }

    private static object[] BuildPackState(ToolPackInfoDto[] packs) {
        var ordered = new List<ToolPackInfoDto>(packs.Length);
        ordered.AddRange(packs);
        ordered.Sort(static (a, b) => {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) {
                return byName;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        var list = new List<object>(ordered.Count);
        foreach (var pack in ordered) {
            var normalizedPackId = NormalizePackId(pack.Id);
            list.Add(new {
                id = string.IsNullOrWhiteSpace(normalizedPackId) ? pack.Id : normalizedPackId,
                name = ResolvePackDisplayName(normalizedPackId, pack.Name),
                description = string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim(),
                tier = pack.Tier.ToString(),
                enabled = pack.Enabled,
                disabledReason = string.IsNullOrWhiteSpace(pack.DisabledReason) ? null : pack.DisabledReason.Trim(),
                isDangerous = pack.IsDangerous,
                sourceKind = pack.SourceKind switch {
                    ToolPackSourceKind.Builtin => "builtin",
                    ToolPackSourceKind.ClosedSource => "closed_source",
                    _ => "open_source"
                }
            });
        }
        return list.ToArray();
    }

    private static string ResolvePackDisplayName(string? id, string? fallbackName) {
        var normalized = NormalizePackId(id);
        if (!string.IsNullOrWhiteSpace(fallbackName)) {
            return fallbackName.Trim();
        }

        return normalized;
    }

    private object BuildMemoryState() {
        var normalizedFacts = NormalizeMemoryFacts(_appState.MemoryFacts);
        _appState.MemoryFacts = normalizedFacts;
        var facts = new List<object>(normalizedFacts.Count);
        for (var i = 0; i < normalizedFacts.Count; i++) {
            var memory = normalizedFacts[i];
            var updatedLocal = EnsureUtc(memory.UpdatedUtc).ToLocalTime();
            facts.Add(new {
                id = memory.Id,
                fact = memory.Fact,
                weight = memory.Weight,
                tags = memory.Tags ?? Array.Empty<string>(),
                updatedLocal = updatedLocal.ToString(_timestampFormat, CultureInfo.InvariantCulture)
            });
        }

        return new {
            enabled = _persistentMemoryEnabled,
            count = normalizedFacts.Count,
            facts = facts.ToArray()
        };
    }

    private object? BuildMemoryDebugState() {
        MemoryDebugSnapshot? snapshot;
        MemoryDebugSnapshot[] history;
        lock (_memoryDiagnosticsSync) {
            snapshot = _lastMemoryDebugSnapshot;
            if (_memoryDebugHistory.Count == 0) {
                history = Array.Empty<MemoryDebugSnapshot>();
            } else {
                var start = Math.Max(0, _memoryDebugHistory.Count - 12);
                var count = _memoryDebugHistory.Count - start;
                history = new MemoryDebugSnapshot[count];
                for (var i = 0; i < count; i++) {
                    history[i] = _memoryDebugHistory[start + i];
                }
            }
        }

        if (snapshot is null) {
            return null;
        }

        var updatedLocal = EnsureUtc(snapshot.UpdatedUtc).ToLocalTime();
        var historyState = BuildMemoryDebugHistoryState(history);
        return new {
            updatedLocal = updatedLocal.ToString(_timestampFormat, CultureInfo.InvariantCulture),
            sequence = snapshot.Sequence,
            availableFacts = snapshot.AvailableFacts,
            candidateFacts = snapshot.CandidateFacts,
            selectedFacts = snapshot.SelectedFacts,
            userTokenCount = snapshot.UserTokenCount,
            topScore = snapshot.TopScore,
            topSemanticSimilarity = snapshot.TopSemanticSimilarity,
            averageSelectedSimilarity = snapshot.AverageSelectedSimilarity,
            averageSelectedRelevance = snapshot.AverageSelectedRelevance,
            cacheEntries = snapshot.CacheEntries,
            quality = snapshot.Quality,
            history = historyState
        };
    }

    private object[] BuildMemoryDebugHistoryState(MemoryDebugSnapshot[] history) {
        if (history.Length == 0) {
            return Array.Empty<object>();
        }

        var list = new List<object>(history.Length);
        for (var i = 0; i < history.Length; i++) {
            var item = history[i];
            var updatedLocal = EnsureUtc(item.UpdatedUtc).ToLocalTime();
            list.Add(new {
                updatedLocal = updatedLocal.ToString(_timestampFormat, CultureInfo.InvariantCulture),
                sequence = item.Sequence,
                selectedFacts = item.SelectedFacts,
                userTokenCount = item.UserTokenCount,
                averageSelectedSimilarity = item.AverageSelectedSimilarity,
                averageSelectedRelevance = item.AverageSelectedRelevance,
                quality = item.Quality
            });
        }

        return list.ToArray();
    }

    private static object[] BuildToolParameterState(ToolParameterDto[]? parameters) {
        if (parameters is not { Length: > 0 }) {
            return Array.Empty<object>();
        }

        var list = new List<object>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++) {
            var parameter = parameters[i];
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name)) {
                continue;
            }

            list.Add(new {
                name = parameter.Name,
                type = string.IsNullOrWhiteSpace(parameter.Type) ? "any" : parameter.Type,
                description = parameter.Description ?? string.Empty,
                required = parameter.Required,
                enumValues = parameter.EnumValues ?? Array.Empty<string>(),
                defaultJson = parameter.DefaultJson,
                exampleJson = parameter.ExampleJson
            });
        }

        return list.ToArray();
    }

    private object[] BuildToolState() {
        if (_toolStates.Count == 0) {
            return Array.Empty<object>();
        }

        var names = new List<string>(_toolStates.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        var list = new List<object>(names.Count);
        foreach (var name in names) {
            _toolDescriptions.TryGetValue(name, out var description);
            _toolDisplayNames.TryGetValue(name, out var displayName);
            _toolCategories.TryGetValue(name, out var category);
            _toolTags.TryGetValue(name, out var tags);
            _toolPackIds.TryGetValue(name, out var packId);
            _toolPackNames.TryGetValue(name, out var packName);
            _toolParameters.TryGetValue(name, out var parameters);
            _toolStates.TryGetValue(name, out var enabled);
            _toolRoutingConfidence.TryGetValue(name, out var routingConfidence);
            _toolRoutingReason.TryGetValue(name, out var routingReason);
            _toolRoutingScore.TryGetValue(name, out var routingScore);
            var normalizedPackId = NormalizePackId(packId);
            var normalizedPackName = ResolvePackDisplayName(normalizedPackId, packName);
            var parameterState = BuildToolParameterState(parameters);
            list.Add(new {
                name,
                displayName = string.IsNullOrWhiteSpace(displayName) ? FormatToolDisplayName(name) : displayName,
                description = description ?? string.Empty,
                category = string.IsNullOrWhiteSpace(category) ? "other" : category,
                packId = string.IsNullOrWhiteSpace(normalizedPackId) ? null : normalizedPackId,
                packName = string.IsNullOrWhiteSpace(normalizedPackName) ? null : normalizedPackName,
                tags = tags ?? Array.Empty<string>(),
                parameters = parameterState,
                routingConfidence = string.IsNullOrWhiteSpace(routingConfidence) ? null : routingConfidence,
                routingReason = string.IsNullOrWhiteSpace(routingReason) ? null : routingReason,
                routingScore = _toolRoutingScore.ContainsKey(name) ? Math.Round(routingScore, 3) : (double?)null,
                enabled
            });
        }

        return list.ToArray();
    }

    private async Task SetActivityAsync(string? text, IReadOnlyList<string>? timeline = null) {
        if (!_webViewReady) {
            return;
        }

        var textJson = JsonSerializer.Serialize(text ?? string.Empty);
        var timelineJson = JsonSerializer.Serialize(timeline ?? Array.Empty<string>());
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetActivity(" + textJson + "," + timelineJson + ");").AsTask())
            .ConfigureAwait(false);
    }

    private void StartTurnWatchdog() {
        Interlocked.Exchange(ref _activeTurnStartedUtcTicks, DateTime.UtcNow.Ticks);

        lock (_turnWatchdogSync) {
            _turnWatchdogCts?.Cancel();
            _turnWatchdogCts?.Dispose();
            _turnWatchdogCts = new CancellationTokenSource();
            var token = _turnWatchdogCts.Token;
            _ = Task.Run(() => TurnWatchdogLoopAsync(token), token);
        }
    }

    private void StopTurnWatchdog() {
        Interlocked.Exchange(ref _activeTurnStartedUtcTicks, 0);
        lock (_turnWatchdogSync) {
            _turnWatchdogCts?.Cancel();
            _turnWatchdogCts?.Dispose();
            _turnWatchdogCts = null;
        }
    }

    private async Task TurnWatchdogLoopAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await Task.Delay(TurnWatchdogTickInterval, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                return;
            }

            if (cancellationToken.IsCancellationRequested || !_isSending) {
                continue;
            }

            var startedTicks = Interlocked.Read(ref _activeTurnStartedUtcTicks);
            if (startedTicks <= 0) {
                continue;
            }

            var startedUtc = new DateTime(startedTicks, DateTimeKind.Utc);
            var elapsed = DateTime.UtcNow - startedUtc;
            if (elapsed < TurnWatchdogHintThreshold) {
                continue;
            }

            var baseActivity = string.IsNullOrWhiteSpace(_latestServiceActivityText)
                ? "Working..."
                : _latestServiceActivityText;
            var elapsedSeconds = Math.Max(1, (int)Math.Round(elapsed.TotalSeconds));
            var watchdogText = $"{baseActivity} ({elapsedSeconds}s elapsed - press Stop to cancel)";
            await SetActivityAsync(watchdogText, SnapshotActivityTimeline()).ConfigureAwait(false);
        }
    }

    private string FormatActivityText(ChatStatusMessage status) {
        if (string.Equals(status.Status, "routing_tool", StringComparison.OrdinalIgnoreCase)) {
            var routingToolLabel = string.IsNullOrWhiteSpace(status.ToolName)
                ? "tool"
                : ResolveToolActivityName(status.ToolName!);
            if (TryParseRoutingInsightPayload(status.Message, out var routingConfidence, out _)) {
                return $"Routing {routingToolLabel} ({routingConfidence})";
            }
            return $"Routing {routingToolLabel}...";
        }

        if (string.Equals(status.Status, "routing_meta", StringComparison.OrdinalIgnoreCase)) {
            if (TryParseRoutingMetaPayload(status.Message, out var strategy, out var selectedToolCount, out var totalToolCount)) {
                return $"Routing strategy {strategy} ({selectedToolCount}/{totalToolCount} tools)";
            }

            return "Routing strategy updated...";
        }

        if (!string.IsNullOrWhiteSpace(status.Message)) {
            return status.Message!;
        }

        var toolLabel = string.IsNullOrWhiteSpace(status.ToolName)
            ? string.Empty
            : ResolveToolActivityName(status.ToolName!);

        return status.Status switch {
            "thinking" => "Thinking...",
            "tool_call" when toolLabel.Length > 0 => "Preparing " + toolLabel + "...",
            "tool_running" when toolLabel.Length > 0 => "Running " + toolLabel + "...",
            "tool_heartbeat" when toolLabel.Length > 0 =>
                status.DurationMs is not null
                    ? toolLabel + " still running (" + FormatDuration(status.DurationMs.Value) + ")"
                    : toolLabel + " still running...",
            "tool_completed" when toolLabel.Length > 0 =>
                status.DurationMs is not null
                    ? toolLabel + " done (" + FormatDuration(status.DurationMs.Value) + ")"
                    : toolLabel + " done",
            "tool_canceled" when toolLabel.Length > 0 => toolLabel + " canceled",
            "tool_recovered" when toolLabel.Length > 0 => toolLabel + " recovered with safe defaults",
            "tool_parallel_mode" => "Parallel mode changed for this turn...",
            "tool_parallel_forced" => "Parallel mode forced for mutating tools...",
            "tool_parallel_safety_off" => "Using sequential mode for mutating tools...",
            "tool_batch_started" => "Starting parallel tool batch...",
            "tool_batch_progress" => "Parallel tool batch in progress...",
            "tool_batch_heartbeat" => "Parallel tool batch still running...",
            "tool_batch_recovering" => "Recovering transient tool failures...",
            "tool_batch_recovered" => "Recovery pass complete",
            "tool_batch_completed" => "Parallel tool batch complete",
            "phase_plan" => "Planning...",
            "phase_execute" => "Executing plan...",
            "phase_review" => "Reviewing...",
            "phase_heartbeat" => "Still working...",
            _ => string.IsNullOrWhiteSpace(status.Status)
                ? "Working..."
                : char.ToUpperInvariant(status.Status[0]) + status.Status[1..]
        };
    }

    private void ResetActivityTimeline() {
        lock (_turnDiagnosticsSync) {
            _activityTimeline.Clear();
        }
    }

    private void AppendActivityTimeline(ChatStatusMessage status, string activityText) {
        var label = BuildActivityTimelineLabel(status, activityText);
        if (label.Length == 0) {
            return;
        }

        lock (_turnDiagnosticsSync) {
            if (_activityTimeline.Count > 0
                && string.Equals(_activityTimeline[^1], label, StringComparison.OrdinalIgnoreCase)) {
                return;
            }

            _activityTimeline.Add(label);
            while (_activityTimeline.Count > MaxActivityTimelineEntries) {
                _activityTimeline.RemoveAt(0);
            }
        }
    }

    private string[] SnapshotActivityTimeline() {
        lock (_turnDiagnosticsSync) {
            return _activityTimeline.Count == 0 ? Array.Empty<string>() : _activityTimeline.ToArray();
        }
    }

    private object? BuildLastTurnMetricsState() {
        TurnMetricsSnapshot? snapshot;
        lock (_turnDiagnosticsSync) {
            snapshot = _lastTurnMetrics;
        }

        if (snapshot is null) {
            return null;
        }

        return new {
            completedLocal = EnsureUtc(snapshot.CompletedUtc).ToLocalTime().ToString(_timestampFormat, CultureInfo.InvariantCulture),
            durationMs = snapshot.DurationMs,
            ttftMs = snapshot.TtftMs,
            queueWaitMs = snapshot.QueueWaitMs,
            toolCalls = snapshot.ToolCallsCount,
            toolRounds = snapshot.ToolRounds,
            projectionFallbacks = snapshot.ProjectionFallbackCount,
            outcome = snapshot.Outcome,
            errorCode = snapshot.ErrorCode ?? string.Empty,
            promptTokens = snapshot.PromptTokens,
            completionTokens = snapshot.CompletionTokens,
            totalTokens = snapshot.TotalTokens,
            cachedPromptTokens = snapshot.CachedPromptTokens,
            reasoningTokens = snapshot.ReasoningTokens
        };
    }

    private string BuildActivityTimelineLabel(ChatStatusMessage status, string activityText) {
        var toolLabel = string.IsNullOrWhiteSpace(status.ToolName) ? string.Empty : ResolveToolActivityName(status.ToolName!);
        var normalizedStatus = (status.Status ?? string.Empty).Trim().ToLowerInvariant();
        var label = normalizedStatus switch {
            "thinking" => "thinking",
            "routing_tool" when toolLabel.Length > 0 => "route " + toolLabel,
            "routing_meta" => "route strategy",
            "tool_call" when toolLabel.Length > 0 => "prepare " + toolLabel,
            "tool_running" when toolLabel.Length > 0 => "run " + toolLabel,
            "tool_heartbeat" when toolLabel.Length > 0 => "run " + toolLabel,
            "tool_completed" when toolLabel.Length > 0 => "done " + toolLabel,
            "tool_canceled" when toolLabel.Length > 0 => "cancel " + toolLabel,
            "tool_recovered" when toolLabel.Length > 0 => "recover " + toolLabel,
            "tool_parallel_mode" => "mode parallel",
            "tool_parallel_forced" => "mode forced",
            "tool_parallel_safety_off" => "safety serialized",
            "tool_batch_started" => "batch start",
            "tool_batch_progress" => "batch progress",
            "tool_batch_heartbeat" => "batch wait",
            "tool_batch_recovering" => "batch recovery",
            "tool_batch_recovered" => "batch recovered",
            "tool_batch_completed" => "batch completed",
            "phase_plan" => "plan",
            "phase_execute" => "execute",
            "phase_review" => "review",
            "phase_heartbeat" => "phase wait",
            "completed" => "completed",
            "finished" => "finished",
            "done" => "done",
            _ => activityText
        };

        label = (label ?? string.Empty).Trim();
        if (label.Length > MaxActivityTimelineLabelChars) {
            label = label[..MaxActivityTimelineLabelChars].TrimEnd() + "...";
        }

        return label;
    }

    private void ClearToolRoutingInsights() {
        _toolRoutingConfidence.Clear();
        _toolRoutingReason.Clear();
        _toolRoutingScore.Clear();

        // Keep explicit per-tool keys so the next options publish clears stale routing state
        // for every visible tool row even if no fresh routing_tool events arrive this turn.
        if (_toolStates.Count == 0) {
            return;
        }

        var toolNames = new List<string>(_toolStates.Keys);
        for (var i = 0; i < toolNames.Count; i++) {
            var name = toolNames[i];
            _toolRoutingConfidence[name] = string.Empty;
            _toolRoutingReason[name] = string.Empty;
        }
    }

    private bool ApplyToolRoutingInsight(ChatStatusMessage status) {
        if (!string.Equals(status.Status, "routing_tool", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var toolName = (status.ToolName ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            return false;
        }

        if (!TryParseRoutingInsightPayload(status.Message, out var confidence, out var reason, out var score)) {
            confidence = "medium";
            reason = "model-selected tool";
            score = null;
        }

        _toolRoutingConfidence[toolName] = confidence;
        _toolRoutingReason[toolName] = reason;
        if (score.HasValue) {
            _toolRoutingScore[toolName] = score.Value;
        } else {
            _toolRoutingScore.Remove(toolName);
        }

        return true;
    }

    private static bool TryParseRoutingInsightPayload(string? payload, out string confidence, out string reason)
        => TryParseRoutingInsightPayload(payload, out confidence, out reason, out _);

    private static bool TryParseRoutingInsightPayload(string? payload, out string confidence, out string reason, out double? score) {
        confidence = "medium";
        reason = "model-selected tool";
        score = null;

        var json = (payload ?? string.Empty).Trim();
        if (json.Length == 0 || json[0] != '{' || json.Length > MaxRoutingInsightPayloadChars) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (root.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind == JsonValueKind.String) {
                var parsed = (confidenceElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                confidence = parsed switch {
                    "high" => "high",
                    "low" => "low",
                    _ => "medium"
                };
            }

            if (root.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String) {
                var parsedReason = (reasonElement.GetString() ?? string.Empty).Trim();
                if (parsedReason.Length > 0) {
                    reason = parsedReason;
                }
            }

            if (root.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number
                && scoreElement.TryGetDouble(out var parsedScore) && !double.IsNaN(parsedScore) && !double.IsInfinity(parsedScore)) {
                score = Math.Clamp(parsedScore, 0d, 9999d);
            }

            return true;
        } catch {
            return false;
        }
    }

    private static bool TryParseRoutingMetaPayload(string? payload, out string strategy, out int selectedToolCount, out int totalToolCount) {
        strategy = "updated";
        selectedToolCount = 0;
        totalToolCount = 0;

        var json = (payload ?? string.Empty).Trim();
        if (json.Length == 0 || json[0] != '{' || json.Length > MaxRoutingInsightPayloadChars) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            var hasStrategy = false;
            if (root.TryGetProperty("strategy", out var strategyElement) && strategyElement.ValueKind == JsonValueKind.String) {
                var parsed = (strategyElement.GetString() ?? string.Empty).Trim().Replace('_', ' ');
                if (parsed.Length > 0) {
                    strategy = parsed;
                    hasStrategy = true;
                }
            }

            var hasSelectedToolCount = false;
            if (root.TryGetProperty("selectedToolCount", out var selectedElement)
                && TryParseRoutingMetaCount(selectedElement, out var parsedSelected)) {
                selectedToolCount = parsedSelected;
                hasSelectedToolCount = true;
            }

            var hasTotalToolCount = false;
            if (root.TryGetProperty("totalToolCount", out var totalElement)
                && TryParseRoutingMetaCount(totalElement, out var parsedTotal)) {
                totalToolCount = parsedTotal;
                hasTotalToolCount = true;
            }

            if (hasSelectedToolCount && hasTotalToolCount && selectedToolCount > totalToolCount) {
                selectedToolCount = totalToolCount;
            }

            return hasStrategy && hasSelectedToolCount && hasTotalToolCount;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryParseRoutingMetaCount(JsonElement element, out int value) {
        value = 0;

        switch (element.ValueKind) {
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var parsedInt)) {
                    value = Math.Max(0, parsedInt);
                    return true;
                }

                if (element.TryGetInt64(out var parsedLong)) {
                    value = ClampRoutingMetaCount(parsedLong);
                    return true;
                }

                if (element.TryGetDecimal(out var parsedDecimal)) {
                    return TryClampRoutingMetaDecimalCount(parsedDecimal, out value);
                }

                return false;
            case JsonValueKind.String:
                var text = (element.GetString() ?? string.Empty).Trim();
                if (text.Length == 0) {
                    return false;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intText)) {
                    value = Math.Max(0, intText);
                    return true;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longText)) {
                    value = ClampRoutingMetaCount(longText);
                    return true;
                }

                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture,
                        out var decimalText)) {
                    return TryClampRoutingMetaDecimalCount(decimalText, out value);
                }

                return false;
            default:
                return false;
        }
    }

    private static int ClampRoutingMetaCount(long value) {
        if (value <= 0) {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    private static bool TryClampRoutingMetaDecimalCount(decimal value, out int normalized) {
        normalized = 0;
        if (value < 0m) {
            return false;
        }

        if (value >= int.MaxValue) {
            normalized = int.MaxValue;
            return true;
        }

        normalized = (int)decimal.Truncate(value);
        return true;
    }

    private string ResolveToolActivityName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "tool";
        }

        if (_toolDisplayNames.TryGetValue(normalized, out var displayName) && !string.IsNullOrWhiteSpace(displayName)) {
            return displayName.Trim();
        }

        return FormatToolDisplayName(normalized);
    }

    private static string FormatDuration(long durationMs) {
        if (durationMs >= 1000) {
            return (durationMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        return durationMs.ToString(CultureInfo.InvariantCulture) + "ms";
    }

    private static string FormatStatusTrace(ChatStatusMessage status) {
        var text = $"status: {status.Status}"
                   + (string.IsNullOrWhiteSpace(status.ToolName) ? string.Empty : $" tool={status.ToolName}")
                   + (status.DurationMs is null ? string.Empty : $" {status.DurationMs}ms");
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            text += $" msg={status.Message}";
        }

        return text;
    }

}
