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
}
