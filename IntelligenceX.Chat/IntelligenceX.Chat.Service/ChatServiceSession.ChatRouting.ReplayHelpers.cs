using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static bool SupportsSyntheticHostReplayItems(OpenAITransportKind transport) {
        // Synthetic host replay items (custom_tool_call/custom_tool_call_output) are
        // only reliable on compatible HTTP transports. Native/AppServer/Copilot
        // runtimes may reject host-generated call ids.
        return transport == OpenAITransportKind.CompatibleHttp;
    }

    private const int DefaultMaxReplayToolOutputCharsPerCall = 6_000;
    private const int DefaultMaxReplayToolOutputCharsTotal = 16_000;
    private const int SmallContextMaxReplayToolOutputCharsPerCall = 2_500;
    private const int SmallContextMaxReplayToolOutputCharsTotal = 7_000;
    private const int MediumContextMaxReplayToolOutputCharsPerCall = 4_000;
    private const int MediumContextMaxReplayToolOutputCharsTotal = 11_000;
    private const int LargeContextMaxReplayToolOutputCharsPerCall = 8_000;
    private const int LargeContextMaxReplayToolOutputCharsTotal = 22_000;
    private const string HostReplayReviewMarker = "ix:host-replay-review:v1";
    private const string HostDomainBootstrapContinuationMarker = "ix:host-domain-bootstrap-continuation:v1";
    private const string ReplayOutputCompactionMarker = "ix:replay-output-compacted:v1";
    private const string ReplayOutputBudgetStatusMarker = "ix:replay-output-budget:v1";
    private const string ReplayOutputBudgetStatusWhere = "tool_replay_input";
    private const string ReplayOutputBudgetStatusReason = "output_budget_compaction";

    private readonly record struct PriorToolCallContract(string Name, string ArgumentsJson);
    private readonly record struct ReplayToolOutputSelection(string Output, bool MatchedRawCallId);
    private readonly record struct ReplayOutputCompactionBudget(
        int MaxOutputCharsPerCall,
        int MaxOutputCharsTotal,
        long? EffectiveContextLength,
        bool ContextAwareBudgetApplied);
    private readonly record struct ReplayOutputCompactionStats(
        int ReplayedCallCount,
        int OriginalTotalChars,
        int CompactedTotalChars,
        int CompactedCallCount);

    private static readonly ReplayOutputCompactionBudget DefaultReplayOutputCompactionBudget = new(
        MaxOutputCharsPerCall: DefaultMaxReplayToolOutputCharsPerCall,
        MaxOutputCharsTotal: DefaultMaxReplayToolOutputCharsTotal,
        EffectiveContextLength: null,
        ContextAwareBudgetApplied: false);

    private ReplayOutputCompactionBudget ResolveReplayOutputCompactionBudgetForTurn(string? selectedModel) {
        var effectiveContextLength = ResolveEffectiveModelContextLength(selectedModel);
        if (!effectiveContextLength.HasValue) {
            return DefaultReplayOutputCompactionBudget;
        }

        var (maxOutputCharsPerCall, maxOutputCharsTotal) = ResolveContextAwareReplayOutputCharBudgets(effectiveContextLength.Value);
        return new ReplayOutputCompactionBudget(
            MaxOutputCharsPerCall: maxOutputCharsPerCall,
            MaxOutputCharsTotal: maxOutputCharsTotal,
            EffectiveContextLength: effectiveContextLength.Value,
            ContextAwareBudgetApplied: true);
    }

    private static (int MaxOutputCharsPerCall, int MaxOutputCharsTotal) ResolveContextAwareReplayOutputCharBudgets(
        long effectiveContextLength) {
        if (effectiveContextLength <= 0) {
            return (DefaultMaxReplayToolOutputCharsPerCall, DefaultMaxReplayToolOutputCharsTotal);
        }

        if (effectiveContextLength <= 8_192) {
            return (SmallContextMaxReplayToolOutputCharsPerCall, SmallContextMaxReplayToolOutputCharsTotal);
        }

        if (effectiveContextLength <= 16_384) {
            return (MediumContextMaxReplayToolOutputCharsPerCall, MediumContextMaxReplayToolOutputCharsTotal);
        }

        if (effectiveContextLength <= 32_768) {
            return (DefaultMaxReplayToolOutputCharsPerCall, DefaultMaxReplayToolOutputCharsTotal);
        }

        return (LargeContextMaxReplayToolOutputCharsPerCall, LargeContextMaxReplayToolOutputCharsTotal);
    }

    private static bool ShouldEmitReplayOutputCompactionStatus(ReplayOutputCompactionStats stats) {
        return stats.CompactedCallCount > 0 && stats.CompactedTotalChars < stats.OriginalTotalChars;
    }

    private static string BuildReplayOutputCompactionStatusMessage(
        ReplayOutputCompactionBudget budget,
        ReplayOutputCompactionStats stats) {
        var contextLength = budget.EffectiveContextLength.HasValue ? budget.EffectiveContextLength.Value.ToString() : "unknown";
        var contextAware = budget.ContextAwareBudgetApplied ? "true" : "false";
        var contextTier = ResolveReplayOutputCompactionContextTier(budget.EffectiveContextLength);
        return $"[{ReplayOutputBudgetStatusMarker} where={ReplayOutputBudgetStatusWhere} reason={ReplayOutputBudgetStatusReason} compacted_calls={stats.CompactedCallCount} replayed_calls={stats.ReplayedCallCount} original_chars={stats.OriginalTotalChars} kept_chars={stats.CompactedTotalChars} per_call_budget={budget.MaxOutputCharsPerCall} total_budget={budget.MaxOutputCharsTotal} context_aware={contextAware} context_tier={contextTier} context_length={contextLength}]";
    }

    private static string ResolveReplayOutputCompactionContextTier(long? effectiveContextLength) {
        if (!effectiveContextLength.HasValue || effectiveContextLength.Value <= 0) {
            return "unknown";
        }

        if (effectiveContextLength.Value <= 8_192) {
            return "small";
        }

        if (effectiveContextLength.Value <= 16_384) {
            return "medium";
        }

        if (effectiveContextLength.Value <= 32_768) {
            return "default";
        }

        return "large";
    }

    private static string ResolveToolOutputCallId(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        string? rawOutputCallId,
        int outputIndex) {
        _ = TryResolveToolOutputCallId(
            extractedCalls,
            extractedCallsById,
            rawOutputCallId,
            outputIndex,
            out var normalizedOutputCallId,
            out _);
        return normalizedOutputCallId;
    }

    private static bool TryResolveToolOutputCallId(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        string? rawOutputCallId,
        int outputIndex,
        out string normalizedOutputCallId,
        out bool matchedRawCallId) {
        normalizedOutputCallId = string.Empty;
        matchedRawCallId = false;
        var directOutputCallId = (rawOutputCallId ?? string.Empty).Trim();
        if (directOutputCallId.Length > 0 && extractedCallsById.ContainsKey(directOutputCallId)) {
            normalizedOutputCallId = directOutputCallId;
            matchedRawCallId = true;
            return true;
        }

        if (outputIndex >= 0 && outputIndex < extractedCalls.Count) {
            var fallbackCallId = (extractedCalls[outputIndex].CallId ?? string.Empty).Trim();
            if (fallbackCallId.Length > 0) {
                normalizedOutputCallId = fallbackCallId;
                return true;
            }
        }

        if (extractedCallsById.Count == 1) {
            foreach (var pair in extractedCallsById) {
                var singleCallId = (pair.Key ?? string.Empty).Trim();
                if (singleCallId.Length > 0) {
                    normalizedOutputCallId = singleCallId;
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<string, ToolOutputDto> BuildLatestToolOutputsByCallId(IReadOnlyList<ToolOutputDto> outputs) {
        var byCallId = new Dictionary<string, ToolOutputDto>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < outputs.Count; i++) {
            var callId = (outputs[i].CallId ?? string.Empty).Trim();
            if (callId.Length == 0) {
                continue;
            }

            byCallId[callId] = outputs[i];
        }

        return byCallId;
    }

    private static Dictionary<string, PriorToolCallContract> BuildLatestToolCallContractsByCallId(IReadOnlyList<ToolCallDto> calls) {
        var byCallId = new Dictionary<string, PriorToolCallContract>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var callId = (calls[i].CallId ?? string.Empty).Trim();
            if (callId.Length == 0) {
                continue;
            }

            var callName = (calls[i].Name ?? string.Empty).Trim();
            var argumentsJson = NormalizeArgumentsJsonForReplayContract(calls[i].ArgumentsJson);
            byCallId[callId] = new PriorToolCallContract(callName, argumentsJson);
        }

        return byCallId;
    }

    private static string NormalizeArgumentsJsonForReplayContract(string? argumentsJson) {
        var value = (argumentsJson ?? string.Empty).Trim();
        if (value.Length == 0) {
            return "{}";
        }

        try {
            var parsed = JsonLite.Parse(value);
            if (parsed is null) {
                return "{}";
            }

            return JsonLite.Serialize(parsed);
        } catch {
            return value;
        }
    }

    private static bool CallMatchesReplayRecoveredContract(ToolCall call, PriorToolCallContract priorContract) {
        var currentName = (call.Name ?? string.Empty).Trim();
        if (priorContract.Name.Length > 0
            && currentName.Length > 0
            && !string.Equals(currentName, priorContract.Name, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var currentArgumentsJson = call.Arguments is null
            ? "{}"
            : NormalizeArgumentsJsonForReplayContract(JsonLite.Serialize(call.Arguments));
        return string.Equals(currentArgumentsJson, priorContract.ArgumentsJson, StringComparison.Ordinal);
    }

    private static bool TryGetReplayRecoveredOutputForCall(ToolCall call, IReadOnlyDictionary<string, ToolOutputDto> outputsByCallId,
        IReadOnlyDictionary<string, PriorToolCallContract> priorCallsByCallId,
        out ToolOutputDto replayRecoveredOutput) {
        var callId = (call.CallId ?? string.Empty).Trim();
        if (callId.Length > 0
            && outputsByCallId.TryGetValue(callId, out var priorOutput)
            && (!priorCallsByCallId.TryGetValue(callId, out var priorCall) || CallMatchesReplayRecoveredContract(call, priorCall))) {
            replayRecoveredOutput = priorOutput with { CallId = callId };
            return true;
        }

        replayRecoveredOutput = default!;
        return false;
    }

    private static IReadOnlyList<ToolOutputDto> MergeToolRoundReplayOutputs(IReadOnlyList<ToolOutputDto> executed,
        IReadOnlyList<ToolOutputDto> replayRecoveredOutputs) {
        if (replayRecoveredOutputs.Count == 0) {
            return executed;
        }

        var merged = new List<ToolOutputDto>(executed.Count + replayRecoveredOutputs.Count);
        merged.AddRange(executed);
        merged.AddRange(replayRecoveredOutputs);
        return merged;
    }

    private static ChatInput BuildToolRoundReplayInput(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        IReadOnlyList<ToolOutputDto> outputs) {
        return BuildToolRoundReplayInputWithBudget(
            extractedCalls,
            extractedCallsById,
            outputs,
            DefaultReplayOutputCompactionBudget,
            out _);
    }

    private static ChatInput BuildToolRoundReplayInputWithBudget(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        IReadOnlyList<ToolOutputDto> outputs,
        ReplayOutputCompactionBudget compactionBudget,
        out ReplayOutputCompactionStats compactionStats) {
        var next = new ChatInput();
        var replayedCallIdsInOrder = new List<string>();
        var replayedCallsById = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
        var selectedOutputsByCallId = new Dictionary<string, ReplayToolOutputSelection>(StringComparer.OrdinalIgnoreCase);
        for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++) {
            var output = outputs[outputIndex];
            if (!TryResolveToolOutputCallId(
                    extractedCalls,
                    extractedCallsById,
                    output.CallId,
                    outputIndex,
                    out var normalizedOutputCallId,
                    out var matchedRawCallId)) {
                continue;
            }

            if (normalizedOutputCallId.Length == 0) {
                continue;
            }

            if (!extractedCallsById.TryGetValue(normalizedOutputCallId, out var executedCall)) {
                continue;
            }

            if (replayedCallsById.TryAdd(normalizedOutputCallId, executedCall)) {
                replayedCallIdsInOrder.Add(normalizedOutputCallId);
            }

            var candidateOutput = new ReplayToolOutputSelection(
                Output: output.Output ?? string.Empty,
                MatchedRawCallId: matchedRawCallId);
            if (!selectedOutputsByCallId.TryGetValue(normalizedOutputCallId, out var existingOutput)) {
                selectedOutputsByCallId[normalizedOutputCallId] = candidateOutput;
                continue;
            }

            if (candidateOutput.MatchedRawCallId || !existingOutput.MatchedRawCallId) {
                selectedOutputsByCallId[normalizedOutputCallId] = candidateOutput;
            }
        }

        selectedOutputsByCallId = CompactReplayOutputsByBudget(
            replayedCallIdsInOrder,
            selectedOutputsByCallId,
            compactionBudget,
            out compactionStats);

        for (var replayIndex = 0; replayIndex < replayedCallIdsInOrder.Count; replayIndex++) {
            var replayCallId = replayedCallIdsInOrder[replayIndex];
            if (!replayedCallsById.TryGetValue(replayCallId, out var replayCall)) {
                continue;
            }

            next.AddToolCall(
                replayCallId,
                replayCall.Name,
                replayCall.Input);
            if (selectedOutputsByCallId.TryGetValue(replayCallId, out var selectedOutput)) {
                next.AddToolOutput(replayCallId, selectedOutput.Output);
            }
        }

        return next;
    }

    private static Dictionary<string, ReplayToolOutputSelection> CompactReplayOutputsByBudget(
        IReadOnlyList<string> replayedCallIdsInOrder,
        IReadOnlyDictionary<string, ReplayToolOutputSelection> selectedOutputsByCallId,
        ReplayOutputCompactionBudget compactionBudget,
        out ReplayOutputCompactionStats compactionStats) {
        var remainingChars = Math.Max(0, compactionBudget.MaxOutputCharsTotal);
        var maxOutputCharsPerCall = Math.Max(0, compactionBudget.MaxOutputCharsPerCall);
        var replayedCallCount = 0;
        var originalTotalChars = 0;
        var compactedTotalChars = 0;
        var compactedCallCount = 0;
        var compactedByCallId = new Dictionary<string, ReplayToolOutputSelection>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < replayedCallIdsInOrder.Count; i++) {
            var callId = replayedCallIdsInOrder[i];
            if (string.IsNullOrWhiteSpace(callId) || !selectedOutputsByCallId.TryGetValue(callId, out var selectedOutput)) {
                continue;
            }

            replayedCallCount++;

            var originalOutput = selectedOutput.Output ?? string.Empty;
            originalTotalChars += originalOutput.Length;

            string compactedOutput;
            if (remainingChars <= 0 || maxOutputCharsPerCall <= 0) {
                compactedOutput = string.Empty;
            } else {
                var maxOutputChars = Math.Min(maxOutputCharsPerCall, remainingChars);
                compactedOutput = CompactReplayOutputText(originalOutput, maxOutputChars);
            }

            compactedByCallId[callId] = selectedOutput with { Output = compactedOutput };
            compactedTotalChars += compactedOutput.Length;
            if (compactedOutput.Length < originalOutput.Length) {
                compactedCallCount++;
            }

            remainingChars = Math.Max(0, remainingChars - compactedOutput.Length);
        }

        compactionStats = new ReplayOutputCompactionStats(
            ReplayedCallCount: replayedCallCount,
            OriginalTotalChars: originalTotalChars,
            CompactedTotalChars: compactedTotalChars,
            CompactedCallCount: compactedCallCount);
        return compactedByCallId;
    }

    private static string CompactReplayOutputText(string output, int maxOutputChars) {
        var source = output ?? string.Empty;
        if (maxOutputChars <= 0) {
            return string.Empty;
        }

        if (source.Length <= maxOutputChars) {
            return source;
        }

        var markerLine = $"[{ReplayOutputCompactionMarker} original_chars={source.Length} kept_chars={maxOutputChars}]";
        var budgetForContent = maxOutputChars - markerLine.Length - 2;
        if (budgetForContent < 16) {
            var headOnlyLength = Math.Max(0, maxOutputChars - markerLine.Length - 1);
            if (headOnlyLength <= 0) {
                return markerLine.Length > maxOutputChars ? markerLine[..maxOutputChars] : markerLine;
            }

            var headOnly = source[..Math.Min(source.Length, headOnlyLength)];
            return headOnly + "\n" + markerLine;
        }

        var prefixLength = budgetForContent / 2;
        var suffixLength = budgetForContent - prefixLength;
        var prefix = source[..prefixLength];
        var suffix = source[^suffixLength..];
        return BuildReplayCompactedOutputEnvelope(prefix + suffix, source.Length, maxOutputChars, prefix, suffix);
    }

    private static string BuildReplayCompactedOutputEnvelope(string output, int originalLength, int maxOutputChars, string? prefix = null,
        string? suffix = null) {
        var markerLine = $"[{ReplayOutputCompactionMarker} original_chars={originalLength} kept_chars={maxOutputChars}]";
        if (prefix is null || suffix is null) {
            return output.Length == 0 ? markerLine : output + "\n" + markerLine;
        }

        return prefix + "\n" + markerLine + "\n" + suffix;
    }

    private static ChatInput BuildHostReplayReviewInput(
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs,
        bool supportsSyntheticReplayItems,
        out string? promptTextForOrdering) {
        promptTextForOrdering = null;
        if (supportsSyntheticReplayItems
            && TryBuildSyntheticHostReplayInput(executedCall, outputs, out var syntheticInput)) {
            return syntheticInput;
        }

        promptTextForOrdering = BuildNativeHostReplayReviewPrompt(executedCall, outputs);
        return ChatInput.FromText(promptTextForOrdering);
    }

    private static ChatInput BuildHostReplayContinuationInput(
        string promptText,
        string? orderingPromptText,
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs,
        bool supportsSyntheticReplayItems,
        out string? promptTextForOrdering) {
        var normalizedPromptText = string.IsNullOrWhiteSpace(promptText)
            ? string.Empty
            : promptText.Trim();
        promptTextForOrdering = string.IsNullOrWhiteSpace(orderingPromptText)
            ? null
            : orderingPromptText.Trim();
        if (promptTextForOrdering is null && normalizedPromptText.Length > 0) {
            promptTextForOrdering = normalizedPromptText;
        }

        if (supportsSyntheticReplayItems
            && TryBuildSyntheticHostReplayInputWithPrelude(
                normalizedPromptText.Length > 0 ? normalizedPromptText : promptTextForOrdering,
                executedCall,
                outputs,
                out var syntheticInput)) {
            return syntheticInput;
        }

        return ChatInput.FromText(BuildNativeHostReplayContinuationPrompt(
            normalizedPromptText.Length > 0 ? normalizedPromptText : promptTextForOrdering,
            executedCall,
            outputs));
    }

    private static bool TryBuildSyntheticHostReplayInput(
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs,
        out ChatInput input) {
        input = null!;

        var executedCallId = (executedCall.CallId ?? string.Empty).Trim();
        if (executedCallId.Length == 0 || outputs.Count == 0) {
            return false;
        }

        for (var i = 0; i < outputs.Count; i++) {
            var outputCallId = (outputs[i].CallId ?? string.Empty).Trim();
            if (outputCallId.Length == 0) {
                continue;
            }

            if (!string.Equals(outputCallId, executedCallId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        var nextInput = new ChatInput();
        nextInput.AddToolCall(executedCallId, executedCall.Name, executedCall.Input);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var outputCallId = (output.CallId ?? string.Empty).Trim();
            nextInput.AddToolOutput(outputCallId.Length == 0 ? executedCallId : outputCallId, output.Output);
        }

        input = nextInput;
        return true;
    }

    private static bool TryBuildSyntheticHostReplayInputWithPrelude(
        string? promptText,
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs,
        out ChatInput input) {
        input = null!;
        var executedCallId = (executedCall.CallId ?? string.Empty).Trim();
        if (executedCallId.Length == 0 || outputs.Count == 0) {
            return false;
        }

        for (var i = 0; i < outputs.Count; i++) {
            var outputCallId = (outputs[i].CallId ?? string.Empty).Trim();
            if (outputCallId.Length == 0) {
                continue;
            }

            if (!string.Equals(outputCallId, executedCallId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        var nextInput = new ChatInput();
        if (!string.IsNullOrWhiteSpace(promptText)) {
            nextInput.AddText(promptText);
        }

        nextInput.AddToolCall(executedCallId, executedCall.Name, executedCall.Input);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var outputCallId = (output.CallId ?? string.Empty).Trim();
            nextInput.AddToolOutput(outputCallId.Length == 0 ? executedCallId : outputCallId, output.Output);
        }

        input = nextInput;
        return true;
    }

    private static string BuildNativeHostReplayReviewPrompt(ToolCall executedCall, IReadOnlyList<ToolOutputDto> outputs) {
        var sb = new StringBuilder();
        sb.AppendLine(HostReplayReviewMarker);
        sb.AppendLine("A read-only follow-up action was already executed by the host runtime.");
        sb.AppendLine("Continue from the evidence below and provide the user-facing answer.");
        sb.AppendLine("Do not ask to rerun this same action and do not require synthetic tool call replay.");
        sb.AppendLine();
        AppendHostReplayEvidence(sb, executedCall, outputs);
        if (outputs.Count == 0) {
            sb.AppendLine("If results are missing, explain the blocker briefly and request only the minimum next input.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("Return the concise final answer with evidence.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildNativeHostReplayContinuationPrompt(
        string? promptText,
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs) {
        var normalizedPromptText = (promptText ?? string.Empty).Trim();
        if (normalizedPromptText.Length == 0) {
            return BuildNativeHostReplayReviewPrompt(executedCall, outputs);
        }

        var sb = new StringBuilder();
        sb.AppendLine(normalizedPromptText);
        sb.AppendLine();
        AppendHostReplayEvidence(sb, executedCall, outputs);
        if (outputs.Count == 0) {
            sb.AppendLine("If results are missing, explain the blocker briefly and request only the minimum next input.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("Continue from the original request now.");
        return sb.ToString().TrimEnd();
    }

    private static string BuildHostDomainBootstrapContinuationPrompt(
        string userRequest,
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs) {
        const int maxUserRequestChars = 1_200;

        var sb = new StringBuilder();
        sb.AppendLine(HostDomainBootstrapContinuationMarker);
        sb.AppendLine("A host bootstrap tool already ran to discover AD scope for the user's request.");
        sb.AppendLine("Treat the bootstrap evidence as setup context only, then continue the original task.");
        sb.AppendLine("Do not stop after merely restating the bootstrap output.");
        sb.AppendLine("Do not rerun this same bootstrap unless the evidence below is unusable.");
        sb.AppendLine("Prefer read-only operational tools that answer the user's request now that scope is known.");
        sb.AppendLine();
        sb.AppendLine("original_user_request:");
        sb.AppendLine(TruncateForHostReplayPrompt(userRequest, maxUserRequestChars));
        sb.AppendLine();
        AppendHostReplayEvidence(sb, executedCall, outputs);
        sb.AppendLine("If enough scope is available, run the next read-only AD tool now.");
        sb.AppendLine("If scope is still insufficient, say exactly what is missing.");
        return sb.ToString().TrimEnd();
    }

    private static void AppendHostReplayEvidence(
        StringBuilder sb,
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs) {
        const int maxOutputsInPrompt = 3;
        const int maxOutputCharsPerItem = 3_000;
        const int maxOutputCharsTotal = 9_000;
        const int maxInputChars = 2_000;

        var toolName = (executedCall.Name ?? string.Empty).Trim();
        var callId = (executedCall.CallId ?? string.Empty).Trim();
        sb.AppendLine("executed_tool: " + (toolName.Length == 0 ? "<unknown>" : toolName));
        sb.AppendLine("executed_call_id: " + (callId.Length == 0 ? "<unknown>" : callId));

        var inputJson = TruncateForHostReplayPrompt(executedCall.Input, maxInputChars);
        if (inputJson.Length > 0) {
            sb.AppendLine("executed_input_json:");
            sb.AppendLine("```json");
            sb.AppendLine(inputJson);
            sb.AppendLine("```");
        }

        if (outputs.Count == 0) {
            sb.AppendLine("tool_results: none");
            return;
        }

        sb.AppendLine("tool_results:");
        var emittedOutputChars = 0;
        var outputCount = Math.Min(outputs.Count, maxOutputsInPrompt);
        for (var i = 0; i < outputCount; i++) {
            var output = outputs[i];
            var outputCallId = (output.CallId ?? string.Empty).Trim();
            var errorCode = (output.ErrorCode ?? string.Empty).Trim();
            var error = (output.Error ?? string.Empty).Trim();

            sb.Append("result[").Append(i + 1).Append("] ");
            sb.Append("call_id=").Append(outputCallId.Length == 0 ? "<unknown>" : outputCallId);
            sb.Append(" ok=").Append(output.Ok == true ? "true" : "false");
            if (errorCode.Length > 0) {
                sb.Append(" error_code=").Append(errorCode);
            }
            if (error.Length > 0) {
                sb.Append(" error=").Append(TruncateForHostReplayPrompt(error, 240));
            }
            sb.AppendLine();

            var remainingBudget = maxOutputCharsTotal - emittedOutputChars;
            if (remainingBudget <= 0) {
                sb.AppendLine("output: <omitted due to prompt budget>");
                continue;
            }

            var itemBudget = Math.Min(maxOutputCharsPerItem, remainingBudget);
            var outputText = TruncateForHostReplayPrompt(output.Output, itemBudget);
            emittedOutputChars += outputText.Length;
            if (outputText.Length == 0) {
                sb.AppendLine("output: <empty>");
                continue;
            }

            sb.AppendLine("output:");
            sb.AppendLine("```");
            sb.AppendLine(outputText);
            sb.AppendLine("```");
        }

        if (outputs.Count > outputCount) {
            sb.AppendLine("additional_results_omitted: " + (outputs.Count - outputCount));
        }
    }

    private static string TruncateForHostReplayPrompt(string? value, int maxChars) {
        if (maxChars <= 0) {
            return string.Empty;
        }

        var text = (value ?? string.Empty).Trim();
        if (text.Length <= maxChars) {
            return text;
        }

        if (maxChars < 64) {
            return text.Substring(0, maxChars);
        }

        var omitted = text.Length - maxChars;
        return text.Substring(0, maxChars) + Environment.NewLine + $"...[truncated {omitted} chars]";
    }

}
