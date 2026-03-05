using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxTurnTimelineEvents = 64;
    private const string TurnPhaseAccepted = "accepted";
    private const string TurnPhaseQueued = "queued";
    private const string TurnPhaseLaneWait = "lane_wait";
    private const string TurnPhaseContextReady = "context_ready";
    private const string TurnPhaseModelPlan = "model_plan";
    private const string TurnPhaseToolExecute = "tool_execute";
    private const string TurnPhaseReview = "review";
    private const string TurnPhaseDone = "done";
    private const string TurnPhaseError = "error";
    private const string TurnPhaseTimeout = "timeout";
    private readonly object _turnTimelineSync = new();
    private string? _capturedTurnTimelineRequestId;
    private List<TurnTimelineEventDto>? _capturedTurnTimelineEvents;

    private async Task TryWriteDeltaAsync(StreamWriter writer, string requestId, string threadId, string delta) {
        try {
            await WriteAsync(writer, new ChatDeltaMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Text = delta
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort streaming; ignore pipe failures.
        }

        try {
            await WriteAsync(writer, new ChatAssistantProvisionalMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Text = delta
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort streaming; ignore pipe failures.
        }
    }

    private async Task TryWriteInterimResultAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string text,
        string? stage,
        int toolCallsCount,
        int toolOutputsCount) {
        var normalizedText = (text ?? string.Empty).Trim();
        if (normalizedText.Length == 0) {
            return;
        }

        var normalizedStage = (stage ?? string.Empty).Trim();
        if (normalizedStage.Length == 0) {
            normalizedStage = null;
        }

        try {
            await WriteAsync(writer, new ChatInterimResultMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Text = normalizedText,
                Stage = normalizedStage,
                ToolCallsCount = Math.Max(0, toolCallsCount),
                ToolOutputsCount = Math.Max(0, toolOutputsCount)
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort streaming; ignore pipe failures.
        }
    }

    private async Task TryWriteStatusAsync(StreamWriter writer, string requestId, string threadId, string status, string? toolName = null,
        string? toolCallId = null, long? durationMs = null, string? message = null) {
        CaptureTurnTimelineEvent(requestId, status, toolName, toolCallId, durationMs, message);
        try {
            await WriteAsync(writer, new ChatStatusMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = requestId,
                ThreadId = threadId,
                Status = status,
                ToolName = toolName,
                ToolCallId = toolCallId,
                DurationMs = durationMs,
                Message = message
            }, CancellationToken.None).ConfigureAwait(false);
        } catch {
            // Best-effort; ignore pipe failures.
        }
    }

    private int CountTrailingPhaseLoopEvents(string requestId) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return 0;
        }

        var normalized = requestId.Trim();
        lock (_turnTimelineSync) {
            if (!string.Equals(_capturedTurnTimelineRequestId, normalized, StringComparison.Ordinal)
                || _capturedTurnTimelineEvents is null
                || _capturedTurnTimelineEvents.Count == 0) {
                return 0;
            }

            var count = 0;
            for (var i = _capturedTurnTimelineEvents.Count - 1; i >= 0; i--) {
                var ev = _capturedTurnTimelineEvents[i];
                var status = (ev.Status ?? string.Empty).Trim();
                if (status.Length == 0) {
                    continue;
                }

                if (string.Equals(status, ChatStatusCodes.PhasePlan, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.PhaseReview, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.PhaseHeartbeat, StringComparison.OrdinalIgnoreCase)) {
                    count++;
                    continue;
                }

                if (string.Equals(status, ChatStatusCodes.ToolCall, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolRunning, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolCompleted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolRoundStarted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolRoundCompleted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolReplayCompacted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.PhaseExecute, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolBatchStarted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolBatchProgress, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolBatchHeartbeat, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolBatchRecovering, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolBatchRecovered, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, ChatStatusCodes.ToolBatchCompleted, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }
            }

            return count;
        }
    }

    private void BeginTurnTimelineCapture(string requestId) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        lock (_turnTimelineSync) {
            _capturedTurnTimelineRequestId = requestId.Trim();
            _capturedTurnTimelineEvents = new List<TurnTimelineEventDto>(capacity: 24);
        }
    }

    private void EndTurnTimelineCapture(string requestId) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        var normalized = requestId.Trim();
        lock (_turnTimelineSync) {
            if (!string.Equals(_capturedTurnTimelineRequestId, normalized, StringComparison.Ordinal)) {
                return;
            }

            _capturedTurnTimelineRequestId = null;
            _capturedTurnTimelineEvents = null;
        }
    }

    private TurnTimelineEventDto[]? SnapshotTurnTimelineEvents(string requestId) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return null;
        }

        var normalized = requestId.Trim();
        lock (_turnTimelineSync) {
            if (!string.Equals(_capturedTurnTimelineRequestId, normalized, StringComparison.Ordinal)
                || _capturedTurnTimelineEvents is null
                || _capturedTurnTimelineEvents.Count == 0) {
                return null;
            }

            return _capturedTurnTimelineEvents.ToArray();
        }
    }

    internal static IReadOnlyList<TurnPhaseTimingDto>? BuildTurnPhaseTimings(IReadOnlyList<TurnTimelineEventDto>? timelineEvents) {
        if (timelineEvents is null || timelineEvents.Count == 0) {
            return null;
        }

        var aggregates = new Dictionary<string, (long DurationMs, int EventCount)>(StringComparer.OrdinalIgnoreCase);
        string? activeHeartbeatPhase = null;
        for (var i = 0; i < timelineEvents.Count; i++) {
            var status = (timelineEvents[i].Status ?? string.Empty).Trim();
            if (status.Length == 0) {
                continue;
            }

            var phase = MapTimelineStatusToTurnPhase(status, activeHeartbeatPhase);
            if (phase is null) {
                continue;
            }

            var aggregate = aggregates.TryGetValue(phase, out var existing)
                ? existing
                : (DurationMs: 0L, EventCount: 0);
            aggregate.EventCount++;
            if (i + 1 < timelineEvents.Count) {
                var elapsed = timelineEvents[i + 1].AtUtc - timelineEvents[i].AtUtc;
                if (elapsed.Ticks > 0) {
                    aggregate.DurationMs += (long)Math.Max(0, elapsed.TotalMilliseconds);
                }
            }

            aggregates[phase] = aggregate;
            if (IsLongRunningTurnPhase(phase)) {
                activeHeartbeatPhase = phase;
            } else if (string.Equals(phase, TurnPhaseDone, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(phase, TurnPhaseError, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(phase, TurnPhaseTimeout, StringComparison.OrdinalIgnoreCase)) {
                activeHeartbeatPhase = null;
            }
        }

        if (aggregates.Count == 0) {
            return null;
        }

        var orderedPhases = new[] {
            TurnPhaseAccepted,
            TurnPhaseQueued,
            TurnPhaseLaneWait,
            TurnPhaseContextReady,
            TurnPhaseModelPlan,
            TurnPhaseToolExecute,
            TurnPhaseReview,
            TurnPhaseDone,
            TurnPhaseError,
            TurnPhaseTimeout
        };

        var results = new List<TurnPhaseTimingDto>(orderedPhases.Length);
        for (var i = 0; i < orderedPhases.Length; i++) {
            var phase = orderedPhases[i];
            if (!aggregates.TryGetValue(phase, out var aggregate)) {
                continue;
            }

            results.Add(new TurnPhaseTimingDto {
                Phase = phase,
                DurationMs = aggregate.DurationMs,
                EventCount = aggregate.EventCount
            });
        }

        return results.Count == 0 ? null : results;
    }

    private void CaptureTurnTimelineEvent(string requestId, string status, string? toolName, string? toolCallId, long? durationMs, string? message) {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(status)) {
            return;
        }

        var normalizedRequestId = requestId.Trim();
        var normalizedStatus = status.Trim();
        if (normalizedStatus.Length == 0) {
            return;
        }

        var normalizedToolName = NormalizeTimelineValue(toolName);
        var normalizedToolCallId = NormalizeTimelineValue(toolCallId);
        var normalizedMessage = NormalizeTimelineMessage(message);
        lock (_turnTimelineSync) {
            if (!string.Equals(_capturedTurnTimelineRequestId, normalizedRequestId, StringComparison.Ordinal)
                || _capturedTurnTimelineEvents is null) {
                return;
            }

            if (_capturedTurnTimelineEvents.Count > 0) {
                var previous = _capturedTurnTimelineEvents[^1];
                if (string.Equals(previous.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(previous.ToolName ?? string.Empty, normalizedToolName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(previous.ToolCallId ?? string.Empty, normalizedToolCallId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(previous.Message ?? string.Empty, normalizedMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
            }

            _capturedTurnTimelineEvents.Add(new TurnTimelineEventDto {
                Status = normalizedStatus,
                ToolName = normalizedToolName,
                ToolCallId = normalizedToolCallId,
                DurationMs = durationMs,
                Message = normalizedMessage,
                AtUtc = DateTime.UtcNow
            });
            while (_capturedTurnTimelineEvents.Count > MaxTurnTimelineEvents) {
                _capturedTurnTimelineEvents.RemoveAt(0);
            }
        }
    }

    private static string? NormalizeTimelineMessage(string? value) {
        var normalized = NormalizeTimelineValue(value);
        if (normalized is null) {
            return null;
        }

        const int maxTimelineMessageChars = 280;
        if (normalized.Length > maxTimelineMessageChars) {
            normalized = normalized[..maxTimelineMessageChars].TrimEnd() + "...";
        }

        return normalized;
    }

    private static string? NormalizeTimelineValue(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsLongRunningTurnPhase(string phase) {
        return string.Equals(phase, TurnPhaseModelPlan, StringComparison.OrdinalIgnoreCase)
               || string.Equals(phase, TurnPhaseToolExecute, StringComparison.OrdinalIgnoreCase)
               || string.Equals(phase, TurnPhaseReview, StringComparison.OrdinalIgnoreCase);
    }

    private static string? MapTimelineStatusToTurnPhase(string status, string? activeHeartbeatPhase) {
        if (string.Equals(status, ChatStatusCodes.Accepted, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseAccepted;
        }

        if (string.Equals(status, ChatStatusCodes.TurnQueued, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseQueued;
        }

        if (string.Equals(status, ChatStatusCodes.ExecutionLaneWaiting, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.ExecutionLaneAcquired, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseLaneWait;
        }

        if (string.Equals(status, ChatStatusCodes.ContextReady, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseContextReady;
        }

        if (string.Equals(status, ChatStatusCodes.PhasePlan, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.Thinking, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.ModelSelected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.Routing, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.RoutingMeta, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.RoutingTool, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseModelPlan;
        }

        if (string.Equals(status, ChatStatusCodes.PhaseExecute, StringComparison.OrdinalIgnoreCase)
            || IsToolExecutionTimelineStatus(status)) {
            return TurnPhaseToolExecute;
        }

        if (string.Equals(status, ChatStatusCodes.PhaseReview, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, ChatStatusCodes.NoResultWatchdogTriggered, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseReview;
        }

        if (string.Equals(status, ChatStatusCodes.PhaseHeartbeat, StringComparison.OrdinalIgnoreCase)
            && IsLongRunningTurnPhase(activeHeartbeatPhase ?? string.Empty)) {
            return activeHeartbeatPhase;
        }

        if (string.Equals(status, ChatStatusCodes.Done, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseDone;
        }

        if (string.Equals(status, ChatStatusCodes.Error, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseError;
        }

        if (string.Equals(status, ChatStatusCodes.Timeout, StringComparison.OrdinalIgnoreCase)) {
            return TurnPhaseTimeout;
        }

        return null;
    }

    private static bool IsToolExecutionTimelineStatus(string status) {
        return string.Equals(status, ChatStatusCodes.ToolCall, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolRunning, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolHeartbeat, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolCompleted, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolCanceled, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolRecovered, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolBatchStarted, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolBatchProgress, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolBatchHeartbeat, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolBatchRecovering, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolBatchRecovered, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolBatchCompleted, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolRoundStarted, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolRoundCompleted, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolReplayCompacted, StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, ChatStatusCodes.ToolRoundLimitReached, StringComparison.OrdinalIgnoreCase);
    }
}
