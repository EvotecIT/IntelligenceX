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
        ClearConversationAssistantVisualState(conversation.Id);
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
        QueueTranscriptRender("clear_conversation");
        _ = PublishOptionsStateSafeAsync();
        QueuePersistAppState();
    }

    private void AppendSystem(string text) {
        var conversation = EnsureSystemConversation();
        AppendSystem(conversation, text);
    }

    private void AppendSystem(ConversationRuntime conversation, string text) {
        conversation.Messages.Add(("System", text, DateTime.Now, null));
        conversation.UpdatedUtc = DateTime.UtcNow;
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            QueueTranscriptRender("append_system");
        }
    }

    private void AppendSystem(SystemNotice notice) {
        AppendSystem(SystemNoticeFormatter.Format(notice));
    }

    private void AppendSystem(ConversationRuntime conversation, SystemNotice notice) {
        AppendSystem(conversation, SystemNoticeFormatter.Format(notice));
    }

    private void QueueTranscriptRender(string reason) {
        _ = RenderTranscriptBestEffortAsync(reason);
    }

    private async Task RenderTranscriptBestEffortAsync(string reason) {
        try {
            await RenderTranscriptAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            // Keep UI flow resilient; transcript failures should be visible in startup diagnostics.
            StartupLog.Write("RenderTranscriptAsync failed (" + reason + "): " + ex.Message);
            if (_debugMode) {
                Debug.WriteLine("RenderTranscriptAsync failed (" + reason + "): " + ex);
            }
        }
    }

    private async Task RenderTranscriptAsync() {
        if (!_webViewReady) {
            _lastTranscriptScriptPayload = null;
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

            var conversation = GetActiveConversation();
            var messagesSnapshot = SnapshotMessagesForRender(conversation.Messages);
            var messageDecorations = SnapshotTranscriptMessageDecorations(conversation);
            var timestampFormat = _timestampFormat;
            var markdownOptions = _markdownOptions;
            var html = await Task.Run(() => BuildMessagesHtml(messagesSnapshot, timestampFormat, markdownOptions, messageDecorations)).ConfigureAwait(false);
            latestGeneration = Interlocked.Read(ref _transcriptRenderGeneration);
            if (requestedGeneration < latestGeneration) {
                return;
            }
            var json = JsonSerializer.Serialize(html);
            if (string.Equals(_lastTranscriptScriptPayload, json, StringComparison.Ordinal)) {
                return;
            }

            var previousPayload = _lastTranscriptScriptPayload;
            _lastTranscriptScriptPayload = json;
            try {
                await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetTranscript(" + json + ");").AsTask())
                    .ConfigureAwait(false);
            } catch {
                if (string.Equals(_lastTranscriptScriptPayload, json, StringComparison.Ordinal)) {
                    _lastTranscriptScriptPayload = previousPayload;
                }
                throw;
            }
            Interlocked.Exchange(ref _transcriptLastRenderUtcTicks, DateTime.UtcNow.Ticks);
        } finally {
            _transcriptRenderGate.Release();
        }
    }

    private static (string Role, string Text, DateTime Time, string? Model)[] SnapshotMessagesForRender(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        var snapshot = new (string Role, string Text, DateTime Time, string? Model)[messages.Count];
        for (var i = 0; i < messages.Count; i++) {
            var message = messages[i];
            snapshot[i] = (message.Role ?? string.Empty, message.Text ?? string.Empty, message.Time, string.IsNullOrWhiteSpace(message.Model) ? null : message.Model.Trim());
        }

        return snapshot;
    }

    private async Task SetStatusAsync(string text, SessionStatusTone? tone = null, bool? usageLimitSwitchRecommended = null) {
        _statusText = text ?? string.Empty;
        _statusTone = tone ?? InferStatusTone(_statusText);
        _usageLimitSwitchRecommended = usageLimitSwitchRecommended ?? InferUsageLimitSwitchRecommendation(_statusText);
        if (!_webViewReady) {
            lock (_uiPublishSync) {
                _lastStatusScriptPayload = null;
                _lastStatusDrivenSessionStamp = null;
                _lastStatusDrivenOptionsStamp = null;
            }
            return;
        }

        var textJson = JsonSerializer.Serialize(_statusText);
        var toneJson = JsonSerializer.Serialize(MapStatusTone(_statusTone));
        var scriptPayload = textJson + "|" + toneJson;
        var publishStatusScript = false;
        lock (_uiPublishSync) {
            if (!string.Equals(_lastStatusScriptPayload, scriptPayload, StringComparison.Ordinal)) {
                _lastStatusScriptPayload = scriptPayload;
                publishStatusScript = true;
            }
        }

        if (publishStatusScript) {
            try {
                await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetStatus(" + textJson + "," + toneJson + ");").AsTask())
                    .ConfigureAwait(false);
            } catch {
                lock (_uiPublishSync) {
                    if (string.Equals(_lastStatusScriptPayload, scriptPayload, StringComparison.Ordinal)) {
                        _lastStatusScriptPayload = null;
                    }
                }
                throw;
            }
        }

        var statusDrivenOptionsStamp = BuildStatusDrivenOptionsStamp();
        var statusDrivenSessionStamp = BuildStatusDrivenSessionStamp();
        var publishSessionFromStatus = false;
        var publishOptionsFromStatus = false;
        lock (_uiPublishSync) {
            if (!string.Equals(_lastStatusDrivenSessionStamp, statusDrivenSessionStamp, StringComparison.Ordinal)) {
                _lastStatusDrivenSessionStamp = statusDrivenSessionStamp;
                publishSessionFromStatus = true;
            }
            if (!string.Equals(_lastStatusDrivenOptionsStamp, statusDrivenOptionsStamp, StringComparison.Ordinal)) {
                _lastStatusDrivenOptionsStamp = statusDrivenOptionsStamp;
                publishOptionsFromStatus = true;
            }
        }

        if (publishSessionFromStatus) {
            await PublishSessionStateAsync().ConfigureAwait(false);
        }
        if (publishOptionsFromStatus) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
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
            _lastPublishedSessionStateJson = null;
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
            sending = _isSending || _turnStartupInProgress,
            cancelable = _isSending && !string.IsNullOrWhiteSpace(_activeTurnRequestId),
            cancelRequested = _isSending && !string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId),
            activityTimeline = SnapshotActivityTimeline(),
            lastTurnMetrics = BuildLastTurnMetricsState(),
            latencySummary = BuildActiveProviderLatencySummaryState(),
            providerCircuit = BuildActiveProviderCircuitState(),
            serviceSessionPublish = BuildServiceSessionPublishDiagnosticsState(),
            debugMode = _debugMode,
            windowMaximized = IsWindowMaximized()
        });
        if (!UiPublishDedupe.TryBeginPublish(_uiPublishSync, ref _lastPublishedSessionStateJson, json)) {
            return;
        }

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetSessionState(" + json + ");").AsTask()).ConfigureAwait(false);
        } catch {
            UiPublishDedupe.RollbackFailedPublish(_uiPublishSync, ref _lastPublishedSessionStateJson, json);
            throw;
        }
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

    private string BuildStatusDrivenOptionsStamp() {
        return string.Join(
            "|",
            _isConnected ? "1" : "0",
            IsEffectivelyAuthenticatedForCurrentTransport() ? "1" : "0",
            _authenticatedAccountId ?? string.Empty,
            _localProviderTransport,
            _activeNativeAccountSlot.ToString(CultureInfo.InvariantCulture));
    }

    private string BuildStatusDrivenSessionStamp() {
        var effectiveAuthenticated = IsEffectivelyAuthenticatedForCurrentTransport();
        var effectiveLoginInProgress = RequiresInteractiveSignInForCurrentTransport() && _loginInProgress;
        var queuedPromptCount = GetQueuedPromptAfterLoginCount();
        var queuedTurnCount = GetQueuedTurnCount();
        var sending = _isSending || _turnStartupInProgress;
        var cancelable = _isSending && !string.IsNullOrWhiteSpace(_activeTurnRequestId);
        var cancelRequested = _isSending && !string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId);

        return string.Join(
            "|",
            _usageLimitSwitchRecommended ? "1" : "0",
            _isConnected ? "1" : "0",
            effectiveAuthenticated ? "1" : "0",
            _authenticatedAccountId ?? string.Empty,
            effectiveLoginInProgress ? "1" : "0",
            queuedPromptCount.ToString(CultureInfo.InvariantCulture),
            queuedTurnCount.ToString(CultureInfo.InvariantCulture),
            sending ? "1" : "0",
            cancelable ? "1" : "0",
            cancelRequested ? "1" : "0",
            _debugMode ? "1" : "0");
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
            nativeAccountSlots = isNative ? GetNativeAccountSlotCount() : 0,
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

    internal static bool ShouldShowToolsLoading(
        bool isConnected,
        bool hasSessionPolicy,
        int startupFlowState,
        bool startupMetadataSyncQueued) {
        if (!isConnected || hasSessionPolicy) {
            return false;
        }

        return startupMetadataSyncQueued || startupFlowState == StartupFlowStateRunning;
    }

    private async Task PublishOptionsStateAsync() {
        await QueueUiPublishAsync(requestSessionState: false, requestOptionsState: true).ConfigureAwait(false);
    }

    private async Task PublishOptionsStateCoreAsync() {
        if (!_webViewReady) {
            _lastPublishedOptionsStateJson = null;
            return;
        }

        var packs = _sessionPolicy?.Packs is { Length: > 0 }
            ? BuildPackState(_sessionPolicy.Packs)
            : Array.Empty<object>();

        var tools = BuildToolState();
        var toolsLoading = ShouldShowToolsLoading(
            isConnected: _isConnected,
            hasSessionPolicy: _sessionPolicy is not null,
            startupFlowState: Volatile.Read(ref _startupFlowState),
            startupMetadataSyncQueued: Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0);
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
                docxVisualMaxWidthPx = _exportDocxVisualMaxWidthPx,
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
                allowMutatingParallelToolCalls = _sessionPolicy.AllowMutatingParallelToolCalls,
                startupWarnings = _sessionPolicy.StartupWarnings,
                pluginSearchPaths = _sessionPolicy.PluginSearchPaths
            }
        });
        if (!UiPublishDedupe.TryBeginPublish(_uiPublishSync, ref _lastPublishedOptionsStateJson, json)) {
            return;
        }

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetOptionsData(" + json + ");").AsTask()).ConfigureAwait(false);
        } catch {
            UiPublishDedupe.RollbackFailedPublish(_uiPublishSync, ref _lastPublishedOptionsStateJson, json);
            throw;
        }
    }

}
