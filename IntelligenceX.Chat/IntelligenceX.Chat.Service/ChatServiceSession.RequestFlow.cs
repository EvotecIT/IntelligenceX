using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

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

    private async Task HandleListToolsAsync(StreamWriter writer, string requestId, CancellationToken cancellationToken) {
        var defs = _registry.GetDefinitions();
        var tools = new ToolDefinitionDto[defs.Count];
        for (var i = 0; i < defs.Count; i++) {
            var parametersJson = defs[i].Parameters is null ? "{}" : JsonLite.Serialize(defs[i].Parameters);
            var required = ExtractRequiredArguments(parametersJson);
            var parameters = ExtractToolParameters(parametersJson, required);
            var packId = string.Empty;
            if (_toolPackIdsByToolName.TryGetValue(defs[i].Name, out var registeredPackId)) {
                packId = NormalizePackId(registeredPackId);
            }

            string? packName = null;
            string? packDescription = null;
            ToolPackSourceKind? packSourceKind = null;
            if (packId.Length > 0 && _packDisplayNamesById.TryGetValue(packId, out var resolvedPackName)) {
                packName = resolvedPackName;
            }
            if (packId.Length > 0 && _packDescriptionsById.TryGetValue(packId, out var resolvedPackDescription)) {
                packDescription = resolvedPackDescription;
            }
            if (packId.Length > 0 && _packSourceKindsById.TryGetValue(packId, out var resolvedPackSourceKind)) {
                packSourceKind = resolvedPackSourceKind;
            }

            tools[i] = new ToolDefinitionDto {
                Name = defs[i].Name,
                Description = defs[i].Description ?? string.Empty,
                DisplayName = ResolveToolDisplayName(defs[i]),
                Category = InferToolCategory(packId, ResolveToolCategory(defs[i])),
                Tags = defs[i].Tags.Count == 0 ? null : defs[i].Tags.ToArray(),
                PackId = packId.Length == 0 ? null : packId,
                PackName = string.IsNullOrWhiteSpace(packName) ? null : packName,
                PackDescription = string.IsNullOrWhiteSpace(packDescription) ? null : packDescription,
                PackSourceKind = packSourceKind,
                ParametersJson = parametersJson,
                RequiredArguments = required,
                Parameters = parameters
            };
        }
        await WriteAsync(writer, new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = requestId,
            Tools = tools
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleInvokeToolAsync(StreamWriter writer, InvokeToolRequest request, CancellationToken cancellationToken) {
        var toolName = (request.ToolName ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolName is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        JsonObject? arguments = null;
        if (!string.IsNullOrWhiteSpace(request.ArgumentsJson)) {
            try {
                var parsed = JsonLite.Parse(request.ArgumentsJson!);
                arguments = parsed?.AsObject();
                if (parsed is not null && arguments is null) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = "argumentsJson must be a JSON object.",
                        Code = "invalid_argument"
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }
            } catch (Exception ex) {
                await WriteAsync(writer, new ErrorMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    Error = $"Invalid argumentsJson: {ex.Message}",
                    Code = "invalid_json"
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var timeoutSeconds = request.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        if (timeoutSeconds < 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "toolTimeoutSeconds must be a non-negative integer.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var call = new ToolCall(
            callId: request.RequestId + ":invoke",
            name: toolName,
            input: request.ArgumentsJson,
            arguments: arguments,
            raw: new JsonObject());
        var output = await ExecuteToolAsync(call, timeoutSeconds, cancellationToken).ConfigureAwait(false);

        await WriteAsync(writer, new InvokeToolResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            ToolName = toolName,
            Output = output
        }, cancellationToken).ConfigureAwait(false);
    }

}
