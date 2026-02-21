using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
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
            authProbeMs = snapshot.AuthProbeMs,
            connectMs = snapshot.ConnectMs,
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
            model = snapshot.Model ?? string.Empty,
            transport = snapshot.Transport ?? string.Empty,
            endpointHost = snapshot.EndpointHost ?? string.Empty
        };
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
