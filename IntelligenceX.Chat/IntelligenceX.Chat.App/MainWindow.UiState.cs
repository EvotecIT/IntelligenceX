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

    private void ClearConversation() {
        var conversation = GetActiveConversation();
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
        var conversation = GetActiveConversation();
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
            var html = await Task.Run(() => BuildMessagesHtml(messagesSnapshot, timestampFormat)).ConfigureAwait(false);
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
        try {
            await QueueUiPublishAsync(requestSessionState: true, requestOptionsState: false).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Caller-facing publish APIs are best-effort and non-throwing on queue cancellation.
        }
    }

    private async Task PublishSessionStateCoreAsync() {
        if (!_webViewReady) {
            return;
        }

        var json = JsonSerializer.Serialize(new {
            status = _statusText,
            statusTone = MapStatusTone(_statusTone),
            usageLimitSwitchRecommended = _usageLimitSwitchRecommended,
            queuedPromptPending = !string.IsNullOrWhiteSpace(_queuedPromptAfterLogin),
            connected = _isConnected,
            authenticated = _isAuthenticated,
            loginInProgress = _loginInProgress,
            sending = _isSending,
            cancelable = _isSending && !string.IsNullOrWhiteSpace(_activeTurnRequestId),
            cancelRequested = _isSending && !string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId),
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

    private async Task PublishOptionsStateAsync() {
        try {
            await QueueUiPublishAsync(requestSessionState: false, requestOptionsState: true).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Caller-facing publish APIs are best-effort and non-throwing on queue cancellation.
        }
    }

    private async Task PublishOptionsStateCoreAsync() {
        if (!_webViewReady) {
            return;
        }

        var packs = _sessionPolicy?.Packs is { Length: > 0 }
            ? BuildPackState(_sessionPolicy.Packs)
            : Array.Empty<object>();

        var tools = BuildToolState();
        var conversations = BuildConversationState();
        var json = JsonSerializer.Serialize(new {
            timestampMode = _timestampMode,
            timestampFormat = _timestampFormat,
            export = new {
                saveMode = _exportSaveMode,
                defaultFormat = _exportDefaultFormat,
                lastDirectory = _lastExportDirectory ?? string.Empty
            },
            autonomy = new {
                maxToolRounds = _autonomyMaxToolRounds,
                parallelTools = _autonomyParallelTools,
                turnTimeoutSeconds = _autonomyTurnTimeoutSeconds,
                toolTimeoutSeconds = _autonomyToolTimeoutSeconds,
                weightedToolRouting = _autonomyWeightedToolRouting,
                maxCandidateTools = _autonomyMaxCandidateTools
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
                modelsEndpoint = string.Equals(_localProviderTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                    ? BuildModelsProbeUrl(_localProviderBaseUrl ?? string.Empty)
                    : string.Empty,
                model = _localProviderModel,
                models = _availableModels,
                favoriteModels = _favoriteModels,
                recentModels = _recentModels,
                isStale = _modelListIsStale,
                warning = _modelListWarning,
                profileSaved = Array.Exists(_serviceProfileNames, name => string.Equals(name, _appProfileName, StringComparison.OrdinalIgnoreCase)),
                runtimeDetection = new {
                    hasRun = _localRuntimeDetectionRan,
                    lmStudioAvailable = _localRuntimeLmStudioAvailable,
                    ollamaAvailable = _localRuntimeOllamaAvailable,
                    detectedName = _localRuntimeDetectedName ?? string.Empty,
                    detectedBaseUrl = _localRuntimeDetectedBaseUrl ?? string.Empty,
                    warning = _localRuntimeDetectionWarning ?? string.Empty
                }
            },
            packs,
            tools,
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
        if (_shutdownRequested) {
            return Task.CompletedTask;
        }

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

        if (requestSessionState && requestOptionsState) {
            return Task.WhenAll(sessionTask, optionsTask);
        }

        return requestSessionState ? sessionTask : optionsTask;
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
                }

                var coalesceDelayApplied = false;
                if (!publishSession && !publishOptions) {
                    // Re-check after one coalesce window so requests arriving during idle transition are not dropped.
                    try {
                        await Task.Delay(UiPublishCoalesceInterval, cancellationToken).ConfigureAwait(false);
                        coalesceDelayApplied = true;
                    } catch (OperationCanceledException) {
                        FinalizeUiPublishAwaiter(sessionTcs, preferCancel: true);
                        FinalizeUiPublishAwaiter(optionsTcs, preferCancel: true);
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
                        FinalizeUiPublishAwaiter(sessionTcs, preferCancel: true);
                        FinalizeUiPublishAwaiter(optionsTcs, preferCancel: true);
                        break;
                    }
                }

                if (publishSession) {
                    try {
                        await RunOnUiThreadAsync(() => PublishSessionStateCoreAsync()).ConfigureAwait(false);
                        sessionTcs?.TrySetResult(null);
                    } catch (OperationCanceledException) {
                        FinalizeUiPublishAwaiter(sessionTcs, preferCancel: true);
                    } catch (Exception ex) {
                        sessionTcs?.TrySetException(ex);
                    }
                }

                if (publishOptions) {
                    try {
                        await RunOnUiThreadAsync(() => PublishOptionsStateCoreAsync()).ConfigureAwait(false);
                        optionsTcs?.TrySetResult(null);
                    } catch (OperationCanceledException) {
                        FinalizeUiPublishAwaiter(optionsTcs, preferCancel: true);
                    } catch (Exception ex) {
                        optionsTcs?.TrySetException(ex);
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

        if (preferCancel && _shutdownRequested) {
            tcs.TrySetCanceled();
            return;
        }

        tcs.TrySetResult(null);
    }

    private static void CancelUiPublishAwaiter(TaskCompletionSource<object?>? tcs) {
        tcs?.TrySetCanceled();
    }

    private void CancelQueuedUiPublishes() {
        TaskCompletionSource<object?>? pendingSession;
        TaskCompletionSource<object?>? pendingOptions;
        CancellationTokenSource? pumpCts;

        lock (_uiPublishSync) {
            pendingSession = _pendingSessionStatePublishTcs;
            pendingOptions = _pendingOptionsStatePublishTcs;
            pumpCts = _uiPublishPumpCts;
            _pendingSessionStatePublish = false;
            _pendingOptionsStatePublish = false;
            _pendingSessionStatePublishTcs = null;
            _pendingOptionsStatePublishTcs = null;
            _uiPublishPumpCts = null;
            _uiPublishPumpRunning = false;
        }

        // Queue teardown is a cancellation boundary: pending awaiters should observe cancellation.
        CancelUiPublishAwaiter(pendingSession);
        CancelUiPublishAwaiter(pendingOptions);

        if (pumpCts is null) {
            return;
        }

        try {
            pumpCts.Cancel();
        } finally {
            pumpCts.Dispose();
        }
    }

    private object[] BuildConversationState() {
        if (_conversations.Count == 0) {
            return Array.Empty<object>();
        }

        var ordered = new List<ConversationRuntime>(_conversations);
        ordered.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        var list = new List<object>(ordered.Count);
        foreach (var conversation in ordered) {
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
                title = string.IsNullOrWhiteSpace(conversation.Title) ? DefaultConversationTitle : conversation.Title,
                messageCount = conversation.Messages.Count,
                preview,
                isActive = string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase),
                updatedLocal = updatedLocal.ToString(_timestampFormat, CultureInfo.InvariantCulture)
            });
        }

        return list.ToArray();
    }

    private static object[] BuildPackState(ToolPackInfoDto[] packs) {
        var list = new List<object>(packs.Length);
        foreach (var pack in packs) {
            var normalizedPackId = NormalizePackId(pack.Id);
            list.Add(new {
                id = string.IsNullOrWhiteSpace(normalizedPackId) ? pack.Id : normalizedPackId,
                name = ResolvePackDisplayName(normalizedPackId, pack.Name),
                tier = pack.Tier.ToString(),
                enabled = pack.Enabled,
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
        return normalized switch {
            "system" => "ComputerX",
            "ad" => "ADPlayground",
            "testimox" => "TestimoX",
            _ => string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName.Trim()
        };
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
                category = string.IsNullOrWhiteSpace(category) ? InferToolCategory(name) : category,
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

    private async Task SetActivityAsync(string? text) {
        if (!_webViewReady) {
            return;
        }

        var json = JsonSerializer.Serialize(text ?? string.Empty);
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetActivity(" + json + ");").AsTask()).ConfigureAwait(false);
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
            "tool_completed" when toolLabel.Length > 0 =>
                status.DurationMs is not null
                    ? toolLabel + " done (" + FormatDuration(status.DurationMs.Value) + ")"
                    : toolLabel + " done",
            "tool_recovered" when toolLabel.Length > 0 => toolLabel + " recovered with safe defaults",
            _ => string.IsNullOrWhiteSpace(status.Status)
                ? "Working..."
                : char.ToUpperInvariant(status.Status[0]) + status.Status[1..]
        };
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
