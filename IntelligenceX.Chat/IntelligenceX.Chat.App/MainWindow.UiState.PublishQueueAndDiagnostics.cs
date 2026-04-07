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
        lock (_serviceSessionPublishSync) {
            _serviceSessionPublishPending = false;
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

    private void RequestServiceDrivenSessionPublish() {
        var requestedUtcTicks = DateTime.UtcNow.Ticks;
        Interlocked.Increment(ref _serviceSessionPublishRequestedCount);
        Interlocked.Exchange(ref _serviceSessionPublishLastRequestedUtcTicks, requestedUtcTicks);

        lock (_serviceSessionPublishSync) {
            if (_shutdownRequested) {
                return;
            }

            _serviceSessionPublishPending = true;
            if (_serviceSessionPublishScheduled) {
                Interlocked.Increment(ref _serviceSessionPublishCoalescedCount);
                return;
            }

            _serviceSessionPublishScheduled = true;
        }

        _ = Task.Run(async () => {
            while (true) {
                TimeSpan delay = TimeSpan.Zero;
                lock (_serviceSessionPublishSync) {
                    if (_shutdownRequested) {
                        _serviceSessionPublishScheduled = false;
                        _serviceSessionPublishPending = false;
                        break;
                    }

                    if (!_serviceSessionPublishPending) {
                        _serviceSessionPublishScheduled = false;
                        break;
                    }

                    _serviceSessionPublishPending = false;

                    if (_serviceSessionPublishLastUtcTicks > 0) {
                        var elapsedTicks = DateTime.UtcNow.Ticks - _serviceSessionPublishLastUtcTicks;
                        if (elapsedTicks < 0) {
                            elapsedTicks = 0;
                        }

                        var minIntervalTicks = ServiceDrivenSessionPublishMinInterval.Ticks;
                        if (elapsedTicks < minIntervalTicks) {
                            delay = TimeSpan.FromTicks(minIntervalTicks - elapsedTicks);
                        }
                    }
                }

                try {
                    if (delay > TimeSpan.Zero) {
                        var delayMs = Math.Max(1L, (long)Math.Round(delay.TotalMilliseconds));
                        Interlocked.Increment(ref _serviceSessionPublishDelayedCount);
                        Interlocked.Exchange(ref _serviceSessionPublishLastDelayMs, delayMs);
                        UpdateMaxInterlocked(ref _serviceSessionPublishMaxDelayMs, delayMs);
                        await Task.Delay(delay).ConfigureAwait(false);
                    }

                    if (_shutdownRequested) {
                        continue;
                    }

                    await PublishSessionStateAsync().ConfigureAwait(false);
                    Interlocked.Increment(ref _serviceSessionPublishExecutedCount);
                    Interlocked.Exchange(ref _serviceSessionPublishLastPublishedUtcTicks, DateTime.UtcNow.Ticks);
                } catch (OperationCanceledException) {
                    // Shutdown may cancel pending publish work.
                } catch (Exception ex) {
                    Interlocked.Increment(ref _serviceSessionPublishFailedCount);
                    if (VerboseServiceLogs || _debugMode) {
                        await AppendSystemBestEffortAsync("Service-driven session publish failed: " + ex.Message).ConfigureAwait(false);
                    }
                } finally {
                    lock (_serviceSessionPublishSync) {
                        _serviceSessionPublishLastUtcTicks = DateTime.UtcNow.Ticks;
                    }
                }
            }
        });
    }

    private object BuildServiceSessionPublishDiagnosticsState() {
        bool scheduled;
        bool pending;
        long schedulerLastUtcTicks;
        lock (_serviceSessionPublishSync) {
            scheduled = _serviceSessionPublishScheduled;
            pending = _serviceSessionPublishPending;
            schedulerLastUtcTicks = _serviceSessionPublishLastUtcTicks;
        }

        return new {
            requested = Interlocked.Read(ref _serviceSessionPublishRequestedCount),
            coalesced = Interlocked.Read(ref _serviceSessionPublishCoalescedCount),
            executed = Interlocked.Read(ref _serviceSessionPublishExecutedCount),
            failed = Interlocked.Read(ref _serviceSessionPublishFailedCount),
            delayed = Interlocked.Read(ref _serviceSessionPublishDelayedCount),
            lastDelayMs = Interlocked.Read(ref _serviceSessionPublishLastDelayMs),
            maxDelayMs = Interlocked.Read(ref _serviceSessionPublishMaxDelayMs),
            scheduled,
            pending,
            lastRequestedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _serviceSessionPublishLastRequestedUtcTicks)),
            lastPublishedLocal = FormatUtcTicksAsLocalTimestamp(Interlocked.Read(ref _serviceSessionPublishLastPublishedUtcTicks)),
            lastSchedulerTickLocal = FormatUtcTicksAsLocalTimestamp(schedulerLastUtcTicks)
        };
    }

    private string FormatUtcTicksAsLocalTimestamp(long utcTicks) {
        if (utcTicks <= 0) {
            return string.Empty;
        }

        var utc = new DateTime(utcTicks, DateTimeKind.Utc);
        return utc.ToLocalTime().ToString(_timestampFormat, CultureInfo.InvariantCulture);
    }

    private static void UpdateMaxInterlocked(ref long target, long candidate) {
        while (true) {
            var current = Interlocked.Read(ref target);
            if (candidate <= current) {
                return;
            }

            if (Interlocked.CompareExchange(ref target, candidate, current) == current) {
                return;
            }
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
                threadId = string.IsNullOrWhiteSpace(conversation.ThreadId) ? null : conversation.ThreadId.Trim(),
                runtimeLabel = string.IsNullOrWhiteSpace(conversation.RuntimeLabel) ? null : conversation.RuntimeLabel.Trim(),
                modelLabel = string.IsNullOrWhiteSpace(conversation.ModelLabel) ? null : conversation.ModelLabel.Trim(),
                modelOverride = string.IsNullOrWhiteSpace(conversation.ModelOverride) ? null : conversation.ModelOverride.Trim(),
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
            var normalizedPackId = NormalizeRuntimePackId(pack.Id);
            list.Add(new {
                id = string.IsNullOrWhiteSpace(normalizedPackId) ? pack.Id : normalizedPackId,
                name = ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPackId, pack.Name),
                description = string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim(),
                tier = pack.Tier.ToString(),
                enabled = pack.Enabled,
                activationState = ToolActivationStates.NormalizeOrDefault(pack.ActivationState, pack.Enabled),
                canActivateOnDemand = pack.CanActivateOnDemand,
                disabledReason = string.IsNullOrWhiteSpace(pack.DisabledReason) ? null : pack.DisabledReason.Trim(),
                isDangerous = pack.IsDangerous,
                autonomySummary = BuildPackAutonomySummaryState(pack.AutonomySummary),
                sourceKind = pack.SourceKind switch {
                    ToolPackSourceKind.Builtin => "builtin",
                    ToolPackSourceKind.ClosedSource => "closed_source",
                    _ => "open_source"
                }
            });
        }
        return list.ToArray();
    }

    private static object[] BuildPluginState(PluginInfoDto[] plugins) {
        if (plugins.Length == 0) {
            return Array.Empty<object>();
        }

        var ordered = new List<PluginInfoDto>(plugins.Length);
        ordered.AddRange(plugins);
        ordered.Sort(static (a, b) => {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0) {
                return byName;
            }

            return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        var list = new List<object>(ordered.Count);
        foreach (var plugin in ordered) {
            var normalizedPluginId = NormalizeRuntimePackId(plugin.Id);
            var displayName = string.IsNullOrWhiteSpace(plugin.Name)
                ? ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPluginId, fallbackName: plugin.Id)
                : plugin.Name.Trim();
            list.Add(new {
                id = string.IsNullOrWhiteSpace(normalizedPluginId) ? plugin.Id : normalizedPluginId,
                name = displayName,
                version = string.IsNullOrWhiteSpace(plugin.Version) ? null : plugin.Version.Trim(),
                origin = string.IsNullOrWhiteSpace(plugin.Origin) ? null : plugin.Origin.Trim(),
                sourceKind = plugin.SourceKind switch {
                    ToolPackSourceKind.Builtin => "builtin",
                    ToolPackSourceKind.ClosedSource => "closed_source",
                    _ => "open_source"
                },
                defaultEnabled = plugin.DefaultEnabled,
                enabled = plugin.Enabled,
                disabledReason = string.IsNullOrWhiteSpace(plugin.DisabledReason) ? null : plugin.DisabledReason.Trim(),
                isDangerous = plugin.IsDangerous,
                packIds = plugin.PackIds ?? Array.Empty<string>(),
                rootPath = string.IsNullOrWhiteSpace(plugin.RootPath) ? null : plugin.RootPath.Trim(),
                skillDirectories = plugin.SkillDirectories ?? Array.Empty<string>(),
                skillIds = plugin.SkillIds ?? Array.Empty<string>()
            });
        }

        return list.ToArray();
    }

    private static object? BuildPackAutonomySummaryState(ToolPackAutonomySummaryDto? summary) {
        if (summary is null) {
            return null;
        }

        return new {
            totalTools = Math.Max(0, summary.TotalTools),
            remoteCapableTools = Math.Max(0, summary.RemoteCapableTools),
            remoteCapableToolNames = summary.RemoteCapableToolNames ?? Array.Empty<string>(),
            setupAwareTools = Math.Max(0, summary.SetupAwareTools),
            setupAwareToolNames = summary.SetupAwareToolNames ?? Array.Empty<string>(),
            handoffAwareTools = Math.Max(0, summary.HandoffAwareTools),
            handoffAwareToolNames = summary.HandoffAwareToolNames ?? Array.Empty<string>(),
            recoveryAwareTools = Math.Max(0, summary.RecoveryAwareTools),
            recoveryAwareToolNames = summary.RecoveryAwareToolNames ?? Array.Empty<string>(),
            crossPackHandoffTools = Math.Max(0, summary.CrossPackHandoffTools),
            crossPackHandoffToolNames = summary.CrossPackHandoffToolNames ?? Array.Empty<string>(),
            crossPackTargetPacks = summary.CrossPackTargetPacks ?? Array.Empty<string>()
        };
    }

    private static string ResolvePackDisplayName(string? id, string? fallbackName) {
        return ToolPackMetadataNormalizer.ResolveDisplayName(id, fallbackName);
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
        var hiddenWithoutCatalogCount = 0;
        foreach (var name in names) {
            // Persisted disabled-tool entries can exist before the runtime publishes the
            // current tool catalog. Keep those states internally, but hide them from the
            // Options -> Tools UI until we have live catalog metadata for that tool.
            if (!_toolDisplayNames.ContainsKey(name)) {
                hiddenWithoutCatalogCount++;
                continue;
            }

            _toolDescriptions.TryGetValue(name, out var description);
            _toolDisplayNames.TryGetValue(name, out var displayName);
            _toolCategories.TryGetValue(name, out var category);
            _toolTags.TryGetValue(name, out var tags);
            _toolPackIds.TryGetValue(name, out var packId);
            _toolPackNames.TryGetValue(name, out var packName);
            _toolParameters.TryGetValue(name, out var parameters);
            _toolWriteCapabilities.TryGetValue(name, out var isWriteCapable);
            _toolExecutionAwareness.TryGetValue(name, out var isExecutionAware);
            _toolExecutionContractIds.TryGetValue(name, out var executionContractId);
            var hasExecutionScope = _toolExecutionScopes.TryGetValue(name, out var executionScope);
            var hasLocalExecution = _toolSupportsLocalExecution.TryGetValue(name, out var supportsLocalExecution);
            var hasRemoteExecution = _toolSupportsRemoteExecution.TryGetValue(name, out var supportsRemoteExecution);
            _toolStates.TryGetValue(name, out var enabled);
            _toolRoutingConfidence.TryGetValue(name, out var routingConfidence);
            _toolRoutingReason.TryGetValue(name, out var routingReason);
            _toolRoutingScore.TryGetValue(name, out var routingScore);
            _toolCatalogDefinitions.TryGetValue(name, out var toolDefinition);
            var normalizedPackId = NormalizeRuntimePackId(packId);
            var normalizedPackName = ResolvePackDisplayName(normalizedPackId, packName);
            var parameterState = BuildToolParameterState(parameters);
            if (!hasExecutionScope) {
                executionScope = ResolveToolExecutionScope(null, supportsLocalExecution, supportsRemoteExecution);
            }
            if (!hasLocalExecution) {
                supportsLocalExecution = !string.Equals(executionScope, "remote_only", StringComparison.OrdinalIgnoreCase);
            }
            if (!hasRemoteExecution) {
                supportsRemoteExecution = string.Equals(executionScope, "remote_only", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(executionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase);
            }
            list.Add(new {
                name,
                displayName = string.IsNullOrWhiteSpace(displayName) ? FormatToolDisplayName(name) : displayName,
                description = description ?? string.Empty,
                category = string.IsNullOrWhiteSpace(category) ? "other" : category,
                packId = string.IsNullOrWhiteSpace(normalizedPackId) ? null : normalizedPackId,
                packName = string.IsNullOrWhiteSpace(normalizedPackName) ? null : normalizedPackName,
                tags = tags ?? Array.Empty<string>(),
                parameters = parameterState,
                isWriteCapable,
                isExecutionAware,
                executionContractId = string.IsNullOrWhiteSpace(executionContractId) ? null : executionContractId,
                executionScope = string.IsNullOrWhiteSpace(toolDefinition?.ExecutionScope)
                    ? (string.IsNullOrWhiteSpace(executionScope) ? "local_only" : executionScope)
                    : toolDefinition.ExecutionScope,
                supportsLocalExecution,
                supportsRemoteExecution,
                packDescription = string.IsNullOrWhiteSpace(toolDefinition?.PackDescription) ? null : toolDefinition.PackDescription,
                packSourceKind = toolDefinition?.PackSourceKind switch {
                    ToolPackSourceKind.Builtin => "builtin",
                    ToolPackSourceKind.ClosedSource => "closed_source",
                    ToolPackSourceKind.OpenSource => "open_source",
                    _ => null
                },
                isPackInfoTool = toolDefinition?.IsPackInfoTool == true,
                isEnvironmentDiscoverTool = toolDefinition?.IsEnvironmentDiscoverTool == true,
                supportsTargetScoping = toolDefinition?.SupportsTargetScoping == true,
                targetScopeArguments = toolDefinition?.TargetScopeArguments ?? Array.Empty<string>(),
                supportsRemoteHostTargeting = toolDefinition?.SupportsRemoteHostTargeting == true,
                remoteHostArguments = toolDefinition?.RemoteHostArguments ?? Array.Empty<string>(),
                isSetupAware = toolDefinition?.IsSetupAware == true,
                setupToolName = string.IsNullOrWhiteSpace(toolDefinition?.SetupToolName) ? null : toolDefinition.SetupToolName,
                isHandoffAware = toolDefinition?.IsHandoffAware == true,
                handoffTargetPackIds = toolDefinition?.HandoffTargetPackIds ?? Array.Empty<string>(),
                handoffTargetToolNames = toolDefinition?.HandoffTargetToolNames ?? Array.Empty<string>(),
                isRecoveryAware = toolDefinition?.IsRecoveryAware == true,
                supportsTransientRetry = toolDefinition?.SupportsTransientRetry == true,
                maxRetryAttempts = Math.Max(0, toolDefinition?.MaxRetryAttempts ?? 0),
                recoveryToolNames = toolDefinition?.RecoveryToolNames ?? Array.Empty<string>(),
                requiredArguments = toolDefinition?.RequiredArguments ?? Array.Empty<string>(),
                routingConfidence = string.IsNullOrWhiteSpace(routingConfidence) ? null : routingConfidence,
                routingReason = string.IsNullOrWhiteSpace(routingReason) ? null : routingReason,
                routingScore = _toolRoutingScore.ContainsKey(name) ? Math.Round(routingScore, 3) : (double?)null,
                enabled
            });
        }

        var previousHiddenCount = Interlocked.Exchange(ref _toolStateHiddenWithoutCatalogLastCount, hiddenWithoutCatalogCount);
        if ((_debugMode || VerboseServiceLogs) && previousHiddenCount != hiddenWithoutCatalogCount) {
            StartupLog.Write(
                "BuildToolState hidden_without_catalog count="
                + hiddenWithoutCatalogCount.ToString(CultureInfo.InvariantCulture));
        }

        return list.ToArray();
    }

    private int CountToolsHiddenWithoutCatalog() {
        if (_toolStates.Count == 0) {
            return 0;
        }

        var hiddenCount = 0;
        var names = new List<string>(_toolStates.Keys);
        for (var i = 0; i < names.Count; i++) {
            if (!_toolDisplayNames.ContainsKey(names[i])) {
                hiddenCount++;
            }
        }

        return hiddenCount;
    }

}
