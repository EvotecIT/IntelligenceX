using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int CarryoverHostHintMultiHostThreshold = 2;
    private readonly record struct StructuredNextActionSnapshot(
        string ToolName,
        string ArgumentsJson,
        ActionMutability Mutability,
        long SeenUtcTicks);
    private readonly record struct StructuredNextActionAutoReplaySnapshot(
        string Signature,
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

        if (HasEquivalentToolCallArguments(toolCalls, nextTool, toolDefinition, normalizedArguments)) {
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
        PersistStructuredNextActionSnapshot(normalizedThreadId, snapshot);
    }

    private bool TryBuildCarryoverStructuredNextActionToolCall(
        string threadId,
        string userRequest,
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

        if (HasCarryoverHostHintMismatch(userRequest, normalizedArguments)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_host_hint_mismatch";
            return false;
        }

        if (ShouldBlockSingleHostStructuredReplayForScopeShift(
                normalizedThreadId,
                userRequest,
                normalizedArguments)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_scope_shift_requires_fresh_plan";
            return false;
        }

        var serializedArguments = JsonLite.Serialize(normalizedArguments);
        if (ShouldBlockRepeatedCarryoverAutoReplay(
                normalizedThreadId,
                userRequest,
                snapshot.ToolName,
                serializedArguments,
                normalizedArguments)) {
            RemoveStructuredNextActionCarryover(normalizedThreadId);
            reason = "carryover_replay_requires_new_context";
            return false;
        }

        RememberCarryoverAutoReplay(normalizedThreadId, snapshot.ToolName, serializedArguments);
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

        var loadedFromSnapshot = false;
        lock (_toolRoutingContextLock) {
            if (!_structuredNextActionByThreadId.TryGetValue(normalizedThreadId, out snapshot)) {
                snapshot = default;
                loadedFromSnapshot = true;
            }
        }

        if (loadedFromSnapshot) {
            if (!TryLoadStructuredNextActionSnapshot(normalizedThreadId, out snapshot)) {
                return false;
            }

            lock (_toolRoutingContextLock) {
                _structuredNextActionByThreadId[normalizedThreadId] = snapshot;
                TrimWeightedRoutingContextsNoLock();
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
            snapshot = snapshot with { SeenUtcTicks = now.Ticks };
            _structuredNextActionByThreadId[normalizedThreadId] = snapshot;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistStructuredNextActionSnapshot(normalizedThreadId, snapshot);

        reason = "carryover_ready";
        return true;
    }

    private bool ShouldBlockRepeatedCarryoverAutoReplay(
        string normalizedThreadId,
        string userRequest,
        string toolName,
        string serializedArguments,
        JsonObject normalizedArguments) {
        var replaySignature = BuildCarryoverAutoReplaySignature(toolName, serializedArguments);
        if (replaySignature.Length == 0) {
            return false;
        }

        var userHostHints = CollectHostHintsFromUserRequest(userRequest);
        if (userHostHints.Length > 0) {
            var carryoverTargets = ExtractHostScopedTargets(normalizedArguments);
            if (ShouldTreatCarryoverHostHintsAsMultiHostMismatch(userHostHints, carryoverTargets)) {
                return true;
            }

            for (var i = 0; i < carryoverTargets.Length; i++) {
                if (HostMatchesAnyCandidate(carryoverTargets[i], userHostHints)) {
                    return false;
                }
            }
        }

        lock (_toolRoutingContextLock) {
            if (!_structuredNextActionAutoReplayByThreadId.TryGetValue(normalizedThreadId, out var priorReplay)) {
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(priorReplay.SeenUtcTicks, out var seenUtc)
                || DateTime.UtcNow - seenUtc > StructuredNextActionContextMaxAge) {
                _structuredNextActionAutoReplayByThreadId.Remove(normalizedThreadId);
                return false;
            }

            return string.Equals(priorReplay.Signature, replaySignature, StringComparison.Ordinal);
        }
    }

    private void RememberCarryoverAutoReplay(string normalizedThreadId, string toolName, string serializedArguments) {
        var replaySignature = BuildCarryoverAutoReplaySignature(toolName, serializedArguments);
        if (replaySignature.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _structuredNextActionAutoReplayByThreadId[normalizedThreadId] = new StructuredNextActionAutoReplaySnapshot(
                Signature: replaySignature,
                SeenUtcTicks: DateTime.UtcNow.Ticks);
            TrimWeightedRoutingContextsNoLock();
        }
    }

    private static string BuildCarryoverAutoReplaySignature(string toolName, string serializedArguments) {
        var normalizedTool = (toolName ?? string.Empty).Trim();
        var normalizedArguments = (serializedArguments ?? string.Empty).Trim();
        if (normalizedTool.Length == 0 || normalizedArguments.Length == 0) {
            return string.Empty;
        }

        return normalizedTool.ToLowerInvariant() + "|" + normalizedArguments;
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
        RemoveStructuredNextActionSnapshot(normalizedThreadId);
    }

    private static bool HasEquivalentToolCallArguments(
        IReadOnlyList<ToolCallDto> toolCalls,
        string toolName,
        ToolDefinition toolDefinition,
        JsonObject normalizedArguments) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0 || toolCalls.Count == 0) {
            return false;
        }

        var normalizedArgumentsJson = JsonLite.Serialize(normalizedArguments);
        for (var i = toolCalls.Count - 1; i >= 0; i--) {
            var call = toolCalls[i];
            var callName = (call.Name ?? string.Empty).Trim();
            if (!string.Equals(callName, normalizedToolName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!TryParseStructuredNextActionArguments(
                    call.ArgumentsJson,
                    toolDefinition,
                    out var existingArguments,
                    out _)) {
                continue;
            }

            if (string.Equals(JsonLite.Serialize(existingArguments), normalizedArgumentsJson, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasCarryoverHostHintMismatch(string userRequest, JsonObject normalizedArguments) {
        var userHostHints = CollectHostHintsFromUserRequest(userRequest);
        if (userHostHints.Length == 0) {
            return false;
        }

        var carryoverTargets = ExtractHostScopedTargets(normalizedArguments);
        if (carryoverTargets.Length == 0) {
            return false;
        }

        if (ShouldTreatCarryoverHostHintsAsMultiHostMismatch(userHostHints, carryoverTargets)) {
            return true;
        }

        for (var i = 0; i < carryoverTargets.Length; i++) {
            if (HostMatchesAnyCandidate(carryoverTargets[i], userHostHints)) {
                return false;
            }
        }

        return true;
    }

    private bool ShouldBlockSingleHostStructuredReplayForScopeShift(
        string normalizedThreadId,
        string userRequest,
        JsonObject normalizedArguments) {
        var request = NormalizeContextualFollowUpRequest(userRequest);
        if (request.Length == 0
            || request.Length > FollowUpShapeShortCharLimit
            || ContainsQuestionSignal(request)
            || LooksLikeActionSelectionPayload(request)) {
            return false;
        }

        // Keep one-token acknowledgements ("continue", "run") eligible for carryover replay.
        // Scope-shift follow-ups like "other dcs" still carry enough context to require fresh planning.
        var requestTokens = ExtractMeaningfulTokensForContext(request, maxTokens: 12);
        if (requestTokens.Count < 2) {
            return false;
        }

        var carryoverTargets = ExtractHostScopedTargets(normalizedArguments);
        if (carryoverTargets.Length == 0 || carryoverTargets.Length >= CarryoverHostHintMultiHostThreshold) {
            return false;
        }

        var userHostHints = CollectHostHintsFromUserRequest(request);
        if (userHostHints.Length > 0) {
            for (var i = 0; i < carryoverTargets.Length; i++) {
                if (HostMatchesAnyCandidate(carryoverTargets[i], userHostHints)) {
                    return false;
                }
            }
        }

        var knownThreadHosts = CollectThreadHostCandidatesForScopeShiftReplayGuard(normalizedThreadId);
        if (knownThreadHosts.Length < CarryoverHostHintMultiHostThreshold) {
            return false;
        }

        // Carryover replay is still scoped to a single remembered host while thread context
        // already includes multi-host evidence and the user gave a contextual compact follow-up.
        return true;
    }

    private string[] CollectThreadHostCandidatesForScopeShiftReplayGuard(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return Array.Empty<string>();
        }

        TryHydrateThreadToolEvidenceFromSnapshot(normalizedThreadId);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_threadToolEvidenceLock) {
            if (!_threadToolEvidenceByThreadId.TryGetValue(normalizedThreadId, out var bySignature) || bySignature.Count == 0) {
                return Array.Empty<string>();
            }

            foreach (var entry in bySignature.Values) {
                CollectHostCandidatesFromSerializedJson(entry.ArgumentsJson, candidates);
                CollectHostCandidatesFromSerializedJson(entry.Output, candidates);
            }
        }

        return candidates.Count == 0 ? Array.Empty<string>() : candidates.ToArray();
    }

    private static bool ShouldTreatCarryoverHostHintsAsMultiHostMismatch(
        IReadOnlyList<string> userHostHints,
        IReadOnlyList<string> carryoverTargets) {
        if (userHostHints is null || carryoverTargets is null) {
            return false;
        }

        return userHostHints.Count >= CarryoverHostHintMultiHostThreshold
               && carryoverTargets.Count > 0
               && carryoverTargets.Count < CarryoverHostHintMultiHostThreshold;
    }

    private static string BuildCarryoverHostHintInput(string userRequest, string assistantDraft) {
        var request = (userRequest ?? string.Empty).Trim();
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0) {
            return request;
        }

        // Keep host-hint enrichment bounded and language-neutral by carrying only recent draft text.
        if (draft.Length > 2048) {
            draft = draft[..2048];
        }

        if (request.Length == 0) {
            return draft;
        }

        return request + "\n" + draft;
    }

    internal static string BuildCarryoverHostHintInputForTesting(string userRequest, string assistantDraft) {
        return BuildCarryoverHostHintInput(userRequest, assistantDraft);
    }
}
