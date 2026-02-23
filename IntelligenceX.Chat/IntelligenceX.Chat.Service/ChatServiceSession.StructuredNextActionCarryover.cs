using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private readonly record struct StructuredNextActionSnapshot(
        string ToolName,
        string ArgumentsJson,
        ActionMutability Mutability,
        long SeenUtcTicks);

    private void RememberStructuredNextActionCarryover(
        string threadId,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        if (toolCalls.Count == 0 || toolOutputs.Count == 0) {
            return;
        }

        if (!TryExtractStructuredNextAction(
                toolDefinitions,
                toolCalls,
                toolOutputs,
                out _,
                out var nextTool,
                out var argumentsJson,
                out _,
                out var nextActionMutability)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            return;
        }

        if (!TryGetToolDefinitionByName(toolDefinitions, nextTool, out var toolDefinition)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            return;
        }

        var mutability = ResolveStructuredNextActionMutability(
            declaredMutability: nextActionMutability,
            toolName: nextTool,
            toolDefinition: toolDefinition,
            mutatingToolHintsByName: mutatingToolHintsByName);
        if (mutability != ActionMutability.ReadOnly) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            return;
        }

        if (!TryParseStructuredNextActionArguments(argumentsJson, toolDefinition, out var normalizedArguments, out _)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            return;
        }

        var snapshot = new StructuredNextActionSnapshot(
            ToolName: nextTool,
            ArgumentsJson: JsonLite.Serialize(normalizedArguments),
            Mutability: ActionMutability.ReadOnly,
            SeenUtcTicks: DateTime.UtcNow.Ticks);
        lock (_toolRoutingContextLock) {
            _structuredNextActionByThreadId[normalizedThreadId] = snapshot;
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private bool TryBuildCarryoverStructuredNextActionToolCall(
        string threadId,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "not_eligible";

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            reason = "missing_thread_id";
            return false;
        }

        if (!TryGetStructuredNextActionCarryover(normalizedThreadId, out var snapshot, out reason)) {
            return false;
        }

        if (!TryGetToolDefinitionByName(toolDefinitions, snapshot.ToolName, out var toolDefinition)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_tool_not_available";
            return false;
        }

        var mutability = ResolveStructuredNextActionMutability(
            declaredMutability: snapshot.Mutability,
            toolName: snapshot.ToolName,
            toolDefinition: toolDefinition,
            mutatingToolHintsByName: mutatingToolHintsByName);
        if (mutability == ActionMutability.Unknown) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_mutability_unknown";
            return false;
        }

        if (mutability == ActionMutability.Mutating) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_mutating_not_autorun";
            return false;
        }

        if (!TryParseStructuredNextActionArguments(snapshot.ArgumentsJson, toolDefinition, out var normalizedArguments, out var argumentReason)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_" + argumentReason;
            return false;
        }

        var serializedArguments = JsonLite.Serialize(normalizedArguments);
        var callId = "host_carryover_next_action_" + Guid.NewGuid().ToString("N");
        var raw = new JsonObject()
            .Add("type", "tool_call")
            .Add("call_id", callId)
            .Add("name", snapshot.ToolName)
            .Add("arguments", serializedArguments);
        toolCall = new ToolCall(
            callId: callId,
            name: snapshot.ToolName,
            input: serializedArguments,
            arguments: normalizedArguments,
            raw: raw);
        reason = "carryover_structured_next_action_readonly_autorun";
        return true;
    }

    private static ActionMutability ResolveStructuredNextActionMutability(
        ActionMutability declaredMutability,
        string toolName,
        ToolDefinition toolDefinition,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName) {
        var mutability = declaredMutability;
        if (mutability == ActionMutability.Unknown) {
            if (mutatingToolHintsByName is not null && mutatingToolHintsByName.TryGetValue(toolName, out var hintedMutating)) {
                mutability = hintedMutating ? ActionMutability.Mutating : ActionMutability.ReadOnly;
            } else if (toolDefinition.WriteGovernance is not null) {
                mutability = toolDefinition.WriteGovernance.IsWriteCapable
                    ? ActionMutability.Mutating
                    : ActionMutability.ReadOnly;
            } else {
                var inferred = ClassifyMutatingCapabilityFromDefinition(toolDefinition);
                if (inferred.HasValue) {
                    mutability = inferred.Value ? ActionMutability.Mutating : ActionMutability.ReadOnly;
                }
            }
        }

        return mutability;
    }

    private bool TryGetStructuredNextActionCarryover(string normalizedThreadId, out StructuredNextActionSnapshot snapshot, out string reason) {
        snapshot = default;
        reason = "carryover_missing";

        lock (_toolRoutingContextLock) {
            if (!_structuredNextActionByThreadId.TryGetValue(normalizedThreadId, out snapshot)) {
                return false;
            }
        }

        if (!TryGetUtcDateTimeFromTicks(snapshot.SeenUtcTicks, out var seenUtc)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_ticks_invalid";
            return false;
        }

        var now = DateTime.UtcNow;
        if (seenUtc > now) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_in_future";
            return false;
        }

        if (now - seenUtc > StructuredNextActionContextMaxAge) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_expired";
            return false;
        }

        lock (_toolRoutingContextLock) {
            _structuredNextActionByThreadId[normalizedThreadId] = snapshot with { SeenUtcTicks = now.Ticks };
            TrimWeightedRoutingContextsNoLock();
        }

        reason = "carryover_ready";
        return true;
    }

    private void RemoveStructuredNextActionCarryover(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _structuredNextActionByThreadId.Remove(normalizedThreadId);
            TrimWeightedRoutingContextsNoLock();
        }
    }
}
