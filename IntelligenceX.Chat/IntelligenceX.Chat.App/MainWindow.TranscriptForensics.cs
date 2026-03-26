using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private async Task ExportTranscriptForensicsAsync() {
        try {
            var conversation = GetActiveConversation();
            if (conversation.Messages.Count == 0) {
                AppendSystem("Transcript forensics export skipped: active conversation is empty.");
                return;
            }

            var baseName = string.IsNullOrWhiteSpace(conversation.ThreadId) ? conversation.Id : conversation.ThreadId!;
            var pickedPath = await ShowTranscriptForensicsSavePickerAsync(baseName).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pickedPath)) {
                return;
            }

            var persistedState = await _stateStore.GetAsync(_appProfileName, CancellationToken.None).ConfigureAwait(false);
            var persistedConversation = FindPersistedConversationState(
                persistedState,
                conversation.Id,
                conversation.ThreadId);
            var bundle = TranscriptForensicsExporter.Build(
                _appProfileName,
                _stateStore.DatabasePath,
                _timestampFormat,
                _markdownOptions,
                conversation.Id,
                conversation.Title,
                conversation.ThreadId,
                conversation.Messages,
                persistedConversation?.Messages,
                BuildRuntimeToolingSupportSnapshot(),
                BuildTranscriptForensicsTurnDiagnosticsSnapshot());

            var outputPath = ResolveTranscriptForensicsOutputPath(pickedPath);
            TranscriptForensicsExporter.Export(outputPath, bundle);
            AppendSystem("Exported transcript forensics: " + outputPath);
        } catch (Exception ex) {
            StartupLog.Write("Transcript forensics export failed: " + ex);
            AppendSystem("Transcript forensics export failed: " + ex.Message);
        }
    }

    private async Task<string?> ShowTranscriptForensicsSavePickerAsync(string? title) {
        string? selectedPath = null;
        await RunOnUiThreadAsync(async () => {
            var picker = new FileSavePicker {
                SuggestedStartLocation = PickerLocationId.Downloads,
                SuggestedFileName = BuildSuggestedExportFileName((title ?? "transcript") + "-forensics", "json"),
                DefaultFileExtension = ".json"
            };

            picker.FileTypeChoices.Add("JSON File", new List<string> { ".json" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero) {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            selectedPath = file?.Path;
        }).ConfigureAwait(false);
        return selectedPath;
    }

    internal static string ResolveTranscriptForensicsOutputPath(string selectedPath) {
        var fullPath = Path.GetFullPath(selectedPath);
        if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.ChangeExtension(fullPath, ".json");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    internal static ChatConversationState? FindPersistedConversationState(
        ChatAppState? state,
        string liveConversationId,
        string? liveThreadId) {
        if (state?.Conversations is not { Count: > 0 }) {
            return null;
        }

        for (var i = 0; i < state.Conversations.Count; i++) {
            var conversation = state.Conversations[i];
            if (string.Equals(conversation.Id, liveConversationId, StringComparison.Ordinal)) {
                return conversation;
            }
        }

        if (!string.IsNullOrWhiteSpace(liveThreadId)) {
            ChatConversationState? matchedConversation = null;
            for (var i = 0; i < state.Conversations.Count; i++) {
                var conversation = state.Conversations[i];
                if (string.Equals(conversation.ThreadId, liveThreadId, StringComparison.Ordinal)) {
                    if (matchedConversation is not null) {
                        return null;
                    }

                    matchedConversation = conversation;
                }
            }

            return matchedConversation;
        }

        return null;
    }

    internal RuntimeToolingSupportSnapshot? BuildRuntimeToolingSupportSnapshot() {
        return RuntimeToolingSupportSnapshotBuilder.Build(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogPlugins,
            _toolCatalogCapabilitySnapshot);
    }

    internal TranscriptForensicsTurnDiagnosticsSnapshot? BuildTranscriptForensicsTurnDiagnosticsSnapshot() {
        TurnMetricsSnapshot? turnMetricsSnapshot;
        string[] activityTimeline;
        RoutingPromptExposureSnapshot[] routingPromptExposureHistory;
        lock (_turnDiagnosticsSync) {
            turnMetricsSnapshot = _lastTurnMetrics;
            activityTimeline = _activityTimeline.Count == 0 ? Array.Empty<string>() : _activityTimeline.ToArray();
            routingPromptExposureHistory = _routingPromptExposureHistory.Count == 0
                ? Array.Empty<RoutingPromptExposureSnapshot>()
                : _routingPromptExposureHistory.ToArray();
        }

        if (turnMetricsSnapshot is null
            && activityTimeline.Length == 0
            && routingPromptExposureHistory.Length == 0) {
            return null;
        }

        var autonomyCounters = new List<TranscriptForensicsAutonomyCounterSnapshot>();
        if (turnMetricsSnapshot is not null && turnMetricsSnapshot.AutonomyCounters is { Count: > 0 }) {
            for (var i = 0; i < turnMetricsSnapshot.AutonomyCounters.Count; i++) {
                var counter = turnMetricsSnapshot.AutonomyCounters[i];
                var name = (counter.Name ?? string.Empty).Trim();
                if (name.Length == 0 || counter.Count <= 0) {
                    continue;
                }

                autonomyCounters.Add(new TranscriptForensicsAutonomyCounterSnapshot {
                    Name = name,
                    Count = counter.Count
                });
            }
        }

        var routingExposureHistory = new List<TranscriptForensicsRoutingPromptExposureSnapshot>(routingPromptExposureHistory.Length);
        for (var i = 0; i < routingPromptExposureHistory.Length; i++) {
            var snapshot = routingPromptExposureHistory[i];
            routingExposureHistory.Add(new TranscriptForensicsRoutingPromptExposureSnapshot {
                RequestId = string.IsNullOrWhiteSpace(snapshot.RequestId) ? null : snapshot.RequestId,
                ThreadId = string.IsNullOrWhiteSpace(snapshot.ThreadId) ? null : snapshot.ThreadId,
                Strategy = snapshot.Strategy,
                SelectedToolCount = snapshot.SelectedToolCount,
                TotalToolCount = snapshot.TotalToolCount,
                Reordered = snapshot.Reordered,
                TopToolNames = snapshot.TopToolNames.Length == 0 ? new List<string>() : new List<string>(snapshot.TopToolNames)
            });
        }

        return new TranscriptForensicsTurnDiagnosticsSnapshot {
            ActivityTimeline = activityTimeline.Length == 0 ? new List<string>() : new List<string>(activityTimeline),
            RoutingPromptExposureHistory = routingExposureHistory,
            LastTurnMetrics = turnMetricsSnapshot is null
                ? null
                : new TranscriptForensicsTurnMetricsSnapshot {
                    RequestId = turnMetricsSnapshot.RequestId,
                    CompletedUtc = EnsureUtc(turnMetricsSnapshot.CompletedUtc),
                    DurationMs = turnMetricsSnapshot.DurationMs,
                    TtftMs = turnMetricsSnapshot.TtftMs,
                    QueueWaitMs = turnMetricsSnapshot.QueueWaitMs,
                    AuthProbeMs = turnMetricsSnapshot.AuthProbeMs,
                    ConnectMs = turnMetricsSnapshot.ConnectMs,
                    EnsureThreadMs = turnMetricsSnapshot.EnsureThreadMs,
                    WeightedSubsetSelectionMs = turnMetricsSnapshot.WeightedSubsetSelectionMs,
                    ResolveModelMs = turnMetricsSnapshot.ResolveModelMs,
                    DispatchToFirstStatusMs = turnMetricsSnapshot.DispatchToFirstStatusMs,
                    DispatchToModelSelectedMs = turnMetricsSnapshot.DispatchToModelSelectedMs,
                    DispatchToFirstToolRunningMs = turnMetricsSnapshot.DispatchToFirstToolRunningMs,
                    DispatchToFirstDeltaMs = turnMetricsSnapshot.DispatchToFirstDeltaMs,
                    DispatchToLastDeltaMs = turnMetricsSnapshot.DispatchToLastDeltaMs,
                    StreamDurationMs = turnMetricsSnapshot.StreamDurationMs,
                    ToolCallsCount = turnMetricsSnapshot.ToolCallsCount,
                    ToolRounds = turnMetricsSnapshot.ToolRounds,
                    ProjectionFallbackCount = turnMetricsSnapshot.ProjectionFallbackCount,
                    Outcome = turnMetricsSnapshot.Outcome,
                    ErrorCode = string.IsNullOrWhiteSpace(turnMetricsSnapshot.ErrorCode) ? null : turnMetricsSnapshot.ErrorCode,
                    PromptTokens = turnMetricsSnapshot.PromptTokens,
                    CompletionTokens = turnMetricsSnapshot.CompletionTokens,
                    TotalTokens = turnMetricsSnapshot.TotalTokens,
                    CachedPromptTokens = turnMetricsSnapshot.CachedPromptTokens,
                    ReasoningTokens = turnMetricsSnapshot.ReasoningTokens,
                    Model = string.IsNullOrWhiteSpace(turnMetricsSnapshot.Model) ? null : turnMetricsSnapshot.Model,
                    RequestedModel = string.IsNullOrWhiteSpace(turnMetricsSnapshot.RequestedModel) ? null : turnMetricsSnapshot.RequestedModel,
                    Transport = string.IsNullOrWhiteSpace(turnMetricsSnapshot.Transport) ? null : turnMetricsSnapshot.Transport,
                    EndpointHost = string.IsNullOrWhiteSpace(turnMetricsSnapshot.EndpointHost) ? null : turnMetricsSnapshot.EndpointHost,
                    AutonomyCounters = autonomyCounters
                }
        };
    }
}
