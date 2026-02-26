using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxTurnTimelineEvents = 64;
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
}
