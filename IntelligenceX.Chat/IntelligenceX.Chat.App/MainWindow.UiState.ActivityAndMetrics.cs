using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private async Task SetActivityAsync(string? text, IReadOnlyList<string>? timeline = null) {
        if (!_webViewReady) {
            lock (_uiPublishSync) {
                _lastActivityScriptPayload = null;
            }
            return;
        }

        var textJson = JsonSerializer.Serialize(text ?? string.Empty);
        var timelineJson = JsonSerializer.Serialize(timeline ?? Array.Empty<string>());
        var scriptPayload = textJson + "|" + timelineJson;
        lock (_uiPublishSync) {
            if (string.Equals(_lastActivityScriptPayload, scriptPayload, StringComparison.Ordinal)) {
                return;
            }
            _lastActivityScriptPayload = scriptPayload;
        }

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetActivity(" + textJson + "," + timelineJson + ");").AsTask())
                .ConfigureAwait(false);
        } catch {
            lock (_uiPublishSync) {
                if (string.Equals(_lastActivityScriptPayload, scriptPayload, StringComparison.Ordinal)) {
                    _lastActivityScriptPayload = null;
                }
            }
            throw;
        }
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
            string? activeRequestId;
            lock (_activeTurnLifecycleSync) {
                activeRequestId = _activeTurnRequestId;
            }

            var hasSnapshot = TryGetTurnWatchdogProgressSnapshot(activeRequestId, out var watchdogSnapshot);
            var elapsedAnchorUtc = hasSnapshot ? watchdogSnapshot.DispatchStartedUtc : startedUtc;
            var elapsed = DateTime.UtcNow - elapsedAnchorUtc;
            var hintThreshold = hasSnapshot
                ? ResolveTurnWatchdogHintThreshold(
                    hasFirstStatus: watchdogSnapshot.HasFirstStatus,
                    hasFirstDelta: watchdogSnapshot.HasFirstDelta)
                : TurnWatchdogHintThreshold;
            if (elapsed < hintThreshold) {
                continue;
            }

            var watchdogProgressLabel = hasSnapshot
                ? BuildTurnWatchdogProgressLabel(
                    hasFirstStatus: watchdogSnapshot.HasFirstStatus,
                    hasModelSelected: watchdogSnapshot.HasModelSelected,
                    hasFirstToolRunning: watchdogSnapshot.HasFirstToolRunning,
                    hasFirstDelta: watchdogSnapshot.HasFirstDelta,
                    firstStatusCode: watchdogSnapshot.FirstStatusCode)
                : string.Empty;
            var baseActivity = !string.IsNullOrWhiteSpace(watchdogProgressLabel)
                ? watchdogProgressLabel
                : string.IsNullOrWhiteSpace(_latestServiceActivityText)
                ? "Working..."
                : _latestServiceActivityText;
            var elapsedSeconds = Math.Max(1, (int)Math.Round(elapsed.TotalSeconds));
            var watchdogText = $"{baseActivity} ({elapsedSeconds}s elapsed - press Stop to cancel)";
            await SetActivityAsync(watchdogText, SnapshotActivityTimeline()).ConfigureAwait(false);
            if (ShouldUpdateTurnWatchdogStatus(_statusText)) {
                await SetStatusAsync(BuildTurnWatchdogStatusText(baseActivity, elapsedSeconds), SessionStatusTone.Warn).ConfigureAwait(false);
            }
        }
    }

    internal static TimeSpan ResolveTurnWatchdogHintThreshold(bool hasFirstStatus, bool hasFirstDelta) {
        if (!hasFirstStatus) {
            return TurnWatchdogAwaitingAckHintThreshold;
        }

        if (!hasFirstDelta) {
            return TurnWatchdogAwaitingFirstTokenHintThreshold;
        }

        return TurnWatchdogHintThreshold;
    }

    internal static string BuildTurnWatchdogProgressLabel(
        bool hasFirstStatus,
        bool hasModelSelected,
        bool hasFirstToolRunning,
        bool hasFirstDelta,
        string? firstStatusCode) {
        if (hasFirstDelta) {
            return "Streaming response...";
        }

        if (hasFirstToolRunning) {
            return "Tool execution is running...";
        }

        if (hasModelSelected) {
            return "Model selected. Waiting for first token...";
        }

        if (hasFirstStatus) {
            return "Runtime acknowledged request. Waiting for first token...";
        }

        var normalizedStatusCode = (firstStatusCode ?? string.Empty).Trim();
        if (normalizedStatusCode.Length > 0) {
            return "Waiting for runtime acknowledgement (" + normalizedStatusCode + ")...";
        }

        return "Waiting for runtime acknowledgement...";
    }

    internal static string BuildTurnWatchdogStatusText(string baseActivity, int elapsedSeconds) {
        var normalizedActivity = (baseActivity ?? string.Empty).Trim();
        if (normalizedActivity.Length == 0) {
            normalizedActivity = "Working...";
        }

        var boundedElapsedSeconds = Math.Max(1, elapsedSeconds);
        return "Runtime processing request... "
               + normalizedActivity
               + " ("
               + boundedElapsedSeconds.ToString(CultureInfo.InvariantCulture)
               + "s elapsed - press Stop to cancel)";
    }

    private static bool ShouldUpdateTurnWatchdogStatus(string currentStatusText) {
        var normalized = (currentStatusText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return true;
        }

        if (normalized.StartsWith("Canceling", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (normalized.StartsWith("Last turn failed:", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    private string FormatActivityText(ChatStatusMessage status) {
        if (string.Equals(status.Status, ChatStatusCodes.RoutingTool, StringComparison.OrdinalIgnoreCase)) {
            var routingToolLabel = string.IsNullOrWhiteSpace(status.ToolName)
                ? "tool"
                : ResolveToolActivityName(status.ToolName!);
            if (TryParseRoutingInsightPayload(status.Message, out var routingConfidence, out _)) {
                return $"Routing {routingToolLabel} ({routingConfidence})";
            }
            return $"Routing {routingToolLabel}...";
        }

        if (string.Equals(status.Status, ChatStatusCodes.RoutingMeta, StringComparison.OrdinalIgnoreCase)) {
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
            ChatStatusCodes.Thinking => "Thinking...",
            "tool_call" when toolLabel.Length > 0 => "Preparing " + toolLabel + "...",
            ChatStatusCodes.ToolRunning when toolLabel.Length > 0 => "Running " + toolLabel + "...",
            ChatStatusCodes.ToolHeartbeat when toolLabel.Length > 0 =>
                status.DurationMs is not null
                    ? toolLabel + " still running (" + FormatDuration(status.DurationMs.Value) + ")"
                    : toolLabel + " still running...",
            ChatStatusCodes.ToolCompleted when toolLabel.Length > 0 =>
                status.DurationMs is not null
                    ? toolLabel + " done (" + FormatDuration(status.DurationMs.Value) + ")"
                    : toolLabel + " done",
            ChatStatusCodes.ToolCanceled when toolLabel.Length > 0 => toolLabel + " canceled",
            ChatStatusCodes.ToolRecovered when toolLabel.Length > 0 => toolLabel + " recovered with safe defaults",
            ChatStatusCodes.ToolParallelMode => "Parallel mode changed for this turn...",
            ChatStatusCodes.ToolParallelForced => "Parallel mode forced for mutating tools...",
            ChatStatusCodes.ToolParallelSafetyOff => "Using sequential mode for mutating tools...",
            ChatStatusCodes.ToolBatchStarted => "Starting parallel tool batch...",
            ChatStatusCodes.ToolBatchProgress => "Parallel tool batch in progress...",
            ChatStatusCodes.ToolBatchHeartbeat => "Parallel tool batch still running...",
            ChatStatusCodes.ToolBatchRecovering => "Recovering transient tool failures...",
            ChatStatusCodes.ToolBatchRecovered => "Recovery pass complete",
            ChatStatusCodes.ToolBatchCompleted => "Parallel tool batch complete",
            ChatStatusCodes.ToolRoundStarted => "Starting tool round...",
            ChatStatusCodes.ToolRoundCompleted => "Tool round complete",
            ChatStatusCodes.ToolRoundLimitReached => "Tool round limit reached for this turn",
            ChatStatusCodes.ToolRoundCapApplied => "Applied safe tool-round cap for this turn",
            ChatStatusCodes.ReviewPassesClamped => "Applied safe review-pass cap for this turn",
            ChatStatusCodes.ModelHeartbeatClamped => "Applied safe model-heartbeat cap for this turn",
            ChatStatusCodes.PhasePlan => "Planning...",
            ChatStatusCodes.PhaseExecute => "Executing plan...",
            ChatStatusCodes.PhaseReview => "Reviewing...",
            ChatStatusCodes.PhaseHeartbeat => "Still working...",
            ChatStatusCodes.NoResultWatchdogTriggered => "No-result watchdog triggered",
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

    private bool AppendActivityTimeline(ChatStatusMessage status, string activityText) {
        var label = BuildActivityTimelineLabel(status, activityText);
        if (label.Length == 0) {
            return false;
        }

        var changed = false;
        lock (_turnDiagnosticsSync) {
            if (_activityTimeline.Count > 0
                && string.Equals(_activityTimeline[^1], label, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            _activityTimeline.Add(label);
            while (_activityTimeline.Count > MaxActivityTimelineEntries) {
                _activityTimeline.RemoveAt(0);
            }
            changed = true;
        }

        return changed;
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

        var autonomyCounters = BuildAutonomyCounterState(snapshot.AutonomyCounters);
        return new {
            completedLocal = EnsureUtc(snapshot.CompletedUtc).ToLocalTime().ToString(_timestampFormat, CultureInfo.InvariantCulture),
            durationMs = snapshot.DurationMs,
            ttftMs = snapshot.TtftMs,
            queueWaitMs = snapshot.QueueWaitMs,
            authProbeMs = snapshot.AuthProbeMs,
            connectMs = snapshot.ConnectMs,
            ensureThreadMs = snapshot.EnsureThreadMs,
            weightedSubsetSelectionMs = snapshot.WeightedSubsetSelectionMs,
            resolveModelMs = snapshot.ResolveModelMs,
            firstStatusMs = snapshot.DispatchToFirstStatusMs,
            modelSelectedMs = snapshot.DispatchToModelSelectedMs,
            firstToolRunningMs = snapshot.DispatchToFirstToolRunningMs,
            firstDeltaMs = snapshot.DispatchToFirstDeltaMs,
            lastDeltaMs = snapshot.DispatchToLastDeltaMs,
            streamDurationMs = snapshot.StreamDurationMs,
            toolCalls = snapshot.ToolCallsCount,
            toolRounds = snapshot.ToolRounds,
            projectionFallbacks = snapshot.ProjectionFallbackCount,
            outcome = snapshot.Outcome,
            errorCode = snapshot.ErrorCode ?? string.Empty,
            promptTokens = snapshot.PromptTokens,
            completionTokens = snapshot.CompletionTokens,
            totalTokens = snapshot.TotalTokens,
            cachedPromptTokens = snapshot.CachedPromptTokens,
            reasoningTokens = snapshot.ReasoningTokens,
            autonomyCounters,
            model = snapshot.Model ?? string.Empty,
            transport = snapshot.Transport ?? string.Empty,
            endpointHost = snapshot.EndpointHost ?? string.Empty
        };
    }

    private static object[] BuildAutonomyCounterState(IReadOnlyList<TurnCounterMetricDto>? counters) {
        if (counters is null || counters.Count == 0) {
            return Array.Empty<object>();
        }

        var items = new List<object>(counters.Count);
        for (var i = 0; i < counters.Count; i++) {
            var current = counters[i];
            var name = (current.Name ?? string.Empty).Trim();
            if (name.Length == 0 || current.Count <= 0) {
                continue;
            }

            items.Add(new {
                name,
                count = current.Count
            });
        }

        return items.Count == 0 ? Array.Empty<object>() : items.ToArray();
    }

    private string BuildActivityTimelineLabel(ChatStatusMessage status, string activityText) {
        var toolLabel = string.IsNullOrWhiteSpace(status.ToolName) ? string.Empty : ResolveToolActivityName(status.ToolName!);
        var normalizedStatus = (status.Status ?? string.Empty).Trim().ToLowerInvariant();
        var label = normalizedStatus switch {
            ChatStatusCodes.Thinking => "thinking",
            ChatStatusCodes.RoutingTool when toolLabel.Length > 0 => "route " + toolLabel,
            ChatStatusCodes.RoutingMeta => "route strategy",
            ChatStatusCodes.ToolCall when toolLabel.Length > 0 => "prepare " + toolLabel,
            ChatStatusCodes.ToolRunning when toolLabel.Length > 0 => "run " + toolLabel,
            ChatStatusCodes.ToolHeartbeat when toolLabel.Length > 0 => "run " + toolLabel,
            ChatStatusCodes.ToolCompleted when toolLabel.Length > 0 => "done " + toolLabel,
            ChatStatusCodes.ToolCanceled when toolLabel.Length > 0 => "cancel " + toolLabel,
            ChatStatusCodes.ToolRecovered when toolLabel.Length > 0 => "recover " + toolLabel,
            ChatStatusCodes.ToolParallelMode => "mode parallel",
            ChatStatusCodes.ToolParallelForced => "mode forced",
            ChatStatusCodes.ToolParallelSafetyOff => "safety serialized",
            ChatStatusCodes.ToolBatchStarted => "batch start",
            ChatStatusCodes.ToolBatchProgress => "batch progress",
            ChatStatusCodes.ToolBatchHeartbeat => "batch wait",
            ChatStatusCodes.ToolBatchRecovering => "batch recovery",
            ChatStatusCodes.ToolBatchRecovered => "batch recovered",
            ChatStatusCodes.ToolBatchCompleted => "batch completed",
            ChatStatusCodes.ToolRoundStarted => "round start",
            ChatStatusCodes.ToolRoundCompleted => "round complete",
            ChatStatusCodes.ToolRoundLimitReached => "round limit",
            ChatStatusCodes.ToolRoundCapApplied => "round cap",
            ChatStatusCodes.ReviewPassesClamped => "review cap",
            ChatStatusCodes.ModelHeartbeatClamped => "heartbeat cap",
            ChatStatusCodes.PhasePlan => "plan",
            ChatStatusCodes.PhaseExecute => "execute",
            ChatStatusCodes.PhaseReview => "review",
            ChatStatusCodes.PhaseHeartbeat => "phase wait",
            ChatStatusCodes.NoResultWatchdogTriggered => "watchdog no-result",
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
}
