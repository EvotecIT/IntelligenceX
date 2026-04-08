using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal const string PhaseHeartbeatSuppressionReasonIo = "io";
    internal const string PhaseHeartbeatSuppressionReasonCanceled = "canceled";
    internal const string PhaseHeartbeatSuppressionReasonHeartbeatCanceled = "heartbeat-canceled";
    internal const string PhaseHeartbeatSuppressionReasonRequestCanceled = "request-canceled";

    private readonly record struct ProactiveVisualizationPolicy(
        bool AllowNewVisuals,
        bool DraftHasVisuals,
        bool RequestHasVisualContract,
        bool HasPreferredVisualOverride,
        string PreferredVisualType,
        string PreferredVisualSource,
        bool HasPreferredVisualPriority,
        int PreferredVisualPriority,
        bool HasMaxNewVisualsOverride,
        int MaxNewVisuals);
    private const int PreferredVisualSourceScoreRenderHint = 300;
    private const int PreferredVisualSourceScoreSummaryMarkdown = 200;
    private const int PreferredVisualSourceScoreOutputPayload = 100;
    private const int RenderHintVisualTypePriorityDefault = 10;
    private const int RenderHintVisualTypePriorityTable = 100;
    private const int RenderHintVisualTypePriorityMermaid = 200;
    private const int RenderHintVisualTypePriorityChart = 300;
    private const int RenderHintVisualTypePriorityNetwork = 400;
    private const int RenderHintPriorityMin = -100000;
    private const int RenderHintPriorityMax = 100000;
    private const int NoTextToolOutputRetryPromptMaxEvidenceItems = 6;
    private const int NoTextToolOutputRetryPromptMaxArgumentPairs = 4;
    private const int NoTextToolOutputRetryPromptMaxArgumentValueChars = 64;

    internal static string ResolveAssistantTextBeforeNoTextFallback(
        string assistantDraft,
        string lastNonEmptyAssistantDraft,
        bool hasToolActivity) {
        var normalizedAssistantDraft = assistantDraft ?? string.Empty;
        var current = normalizedAssistantDraft.Trim();
        if (current.Length > 0) {
            return normalizedAssistantDraft;
        }

        if (!hasToolActivity) {
            return normalizedAssistantDraft;
        }

        var prior = (lastNonEmptyAssistantDraft ?? string.Empty).Trim();
        if (prior.Length == 0
            || prior.StartsWith("[warning] No response text was produced", StringComparison.OrdinalIgnoreCase)
            || LooksLikeRuntimeControlPayloadArtifact(prior)) {
            return normalizedAssistantDraft;
        }

        return prior;
    }

    internal static string ResolveAssistantTextFromToolOutputsFallback(
        string assistantDraft,
        IReadOnlyList<ToolCallDto?> toolCalls,
        IReadOnlyList<ToolOutputDto?> toolOutputs) {
        var normalizedAssistantDraft = assistantDraft ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedAssistantDraft)) {
            return normalizedAssistantDraft;
        }

        if (toolOutputs is null || toolOutputs.Count == 0) {
            return normalizedAssistantDraft;
        }

        var toolNamesByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (toolCalls is { Count: > 0 }) {
            for (var i = 0; i < toolCalls.Count; i++) {
                ToolCallDto? call = toolCalls[i];
                if (call is null) {
                    continue;
                }

                var callId = (call.CallId ?? string.Empty).Trim();
                var toolName = (call.Name ?? string.Empty).Trim();
                if (callId.Length == 0 || toolName.Length == 0) {
                    continue;
                }

                toolNamesByCallId[callId] = toolName;
            }
        }

        var bulletLines = new List<string>(capacity: Math.Min(3, toolOutputs.Count));
        var usableOutputCount = 0;
        for (var i = 0; i < toolOutputs.Count; i++) {
            ToolOutputDto? output = toolOutputs[i];
            if (output is null) {
                continue;
            }

            usableOutputCount++;
            if (bulletLines.Count >= 3) {
                continue;
            }

            var summary = BuildToolOutputFallbackSummary(output);
            if (summary.Length == 0) {
                continue;
            }

            var callId = (output.CallId ?? string.Empty).Trim();
            var toolName = toolNamesByCallId.TryGetValue(callId, out var name)
                ? name
                : "tool";
            bulletLines.Add("- `" + toolName + "`: " + summary);
        }

        if (bulletLines.Count == 0) {
            return normalizedAssistantDraft;
        }

        var remainingCount = Math.Max(0, usableOutputCount - bulletLines.Count);
        var builder = new StringBuilder();
        builder.AppendLine("Recovered findings from executed tools (model returned no text):");
        for (var i = 0; i < bulletLines.Count; i++) {
            builder.AppendLine(bulletLines[i]);
        }

        if (remainingCount > 0) {
            builder.Append("... and ")
                .Append(remainingCount.ToString())
                .Append(" more tool output(s).");
        }

        return builder.ToString().TrimEnd();
    }

    internal static string ResolveAssistantTextFromRequestedArtifactToolOutputsFallback(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolOutputDto?> toolOutputs) {
        var normalizedAssistantDraft = assistantDraft ?? string.Empty;
        var requestedArtifactIntent = ResolveRequestedArtifactIntent(userRequest);
        if (!requestedArtifactIntent.RequiresArtifact
            || IsRequestedArtifactSatisfied(requestedArtifactIntent, normalizedAssistantDraft)
            || toolOutputs is null
            || toolOutputs.Count == 0) {
            return normalizedAssistantDraft;
        }

        for (var i = toolOutputs.Count - 1; i >= 0; i--) {
            var output = toolOutputs[i];
            if (output?.Ok is not true) {
                continue;
            }

            var summary = (output.SummaryMarkdown ?? string.Empty).Trim();
            if (summary.Length == 0 || !IsRequestedArtifactSatisfied(requestedArtifactIntent, summary)) {
                continue;
            }

            return summary;
        }

        return normalizedAssistantDraft;
    }

    internal static string BuildNoTextToolOutputSynthesisPrompt(
        string userRequest,
        IReadOnlyList<ToolCallDto?> toolCalls,
        IReadOnlyList<ToolOutputDto?> toolOutputs) {
        var requestText = TrimForPrompt(userRequest, 520);
        var requestedArtifactIntent = ResolveRequestedArtifactIntent(userRequest);
        var requestedArtifactRequirementLine = requestedArtifactIntent.RequiresArtifact
            ? BuildRequestedArtifactRequirementLine(requestedArtifactIntent)
            : string.Empty;
        var evidenceBuilder = new StringBuilder();
        var toolMetadataByCallId = new Dictionary<string, (string ToolName, string ArgumentSummary)>(StringComparer.OrdinalIgnoreCase);
        if (toolCalls is { Count: > 0 }) {
            for (var i = 0; i < toolCalls.Count; i++) {
                var call = toolCalls[i];
                if (call is null) {
                    continue;
                }

                var callId = (call.CallId ?? string.Empty).Trim();
                var toolName = (call.Name ?? string.Empty).Trim();
                if (callId.Length == 0 || toolName.Length == 0) {
                    continue;
                }

                toolMetadataByCallId[callId] = (
                    ToolName: toolName,
                    ArgumentSummary: BuildToolCallArgumentSummary(call.ArgumentsJson));
            }
        }

        var appendedCount = 0;
        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i];
            if (output is null) {
                continue;
            }

            if (appendedCount >= NoTextToolOutputRetryPromptMaxEvidenceItems) {
                break;
            }

            var summary = BuildToolOutputFallbackSummary(output);
            if (summary.Length == 0) {
                continue;
            }

            var callId = (output.CallId ?? string.Empty).Trim();
            var toolName = "tool";
            var argumentSummary = string.Empty;
            if (toolMetadataByCallId.TryGetValue(callId, out var metadata)) {
                toolName = metadata.ToolName;
                argumentSummary = metadata.ArgumentSummary;
            }

            var status = output.Ok is false ? "error" : "ok";
            evidenceBuilder.Append("- ")
                .Append(toolName);
            if (argumentSummary.Length > 0) {
                evidenceBuilder.Append(" [")
                    .Append(argumentSummary)
                    .Append("]");
            }

            evidenceBuilder.Append(" (")
                .Append(status)
                .Append("): ")
                .Append(summary)
                .AppendLine();
            appendedCount++;
        }

        if (evidenceBuilder.Length == 0) {
            evidenceBuilder.AppendLine("- Tool outputs were present but no concise summaries were available.");
        }

        return $$"""
            [No-text tool-output recovery]
            Tool execution completed but the assistant draft is empty. Produce the final user-facing answer from the executed tool evidence below.

            User request:
            {{requestText}}

            Executed tool evidence:
            {{evidenceBuilder.ToString().TrimEnd()}}

            Requirements:
            - Use only the executed tool evidence above.
            - Keep the response concise and direct.
            {{requestedArtifactRequirementLine}}
            - Do not call tools again.
            - If evidence is incomplete, state the exact missing evidence briefly.
            - Do not emit internal markers or control payload text.
            Return only the final assistant response text.
            """;
    }

    private static string BuildToolCallArgumentSummary(string? argumentsJson) {
        var normalized = (argumentsJson ?? string.Empty).Trim();
        if (normalized.Length == 0 || !LooksLikeJsonPayload(normalized)) {
            return string.Empty;
        }

        try {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return string.Empty;
            }

            var pairs = new List<string>(NoTextToolOutputRetryPromptMaxArgumentPairs);
            foreach (var property in doc.RootElement.EnumerateObject()) {
                if (pairs.Count >= NoTextToolOutputRetryPromptMaxArgumentPairs) {
                    break;
                }

                var key = CollapseWhitespace((property.Name ?? string.Empty).Trim());
                if (key.Length == 0) {
                    continue;
                }

                if (!TryFormatCompactArgumentValue(property.Value, out var compactValue)) {
                    continue;
                }

                pairs.Add(key + "=" + compactValue);
            }

            if (pairs.Count == 0) {
                return string.Empty;
            }

            return "args: " + string.Join(", ", pairs);
        } catch (JsonException) {
            return string.Empty;
        }
    }

    private static bool TryFormatCompactArgumentValue(JsonElement value, out string compactValue) {
        compactValue = string.Empty;
        string raw;
        switch (value.ValueKind) {
            case JsonValueKind.String:
                raw = (value.GetString() ?? string.Empty).Trim();
                break;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                raw = value.ToString();
                break;
            default:
                return false;
        }

        if (raw.Length == 0) {
            return false;
        }

        compactValue = TruncateToolOutputSummary(raw, NoTextToolOutputRetryPromptMaxArgumentValueChars);
        return compactValue.Length > 0;
    }

    private static string BuildToolOutputFallbackSummary(ToolOutputDto output) {
        var summaryMarkdown = TruncateToolOutputSummary((output.SummaryMarkdown ?? string.Empty).Trim(), maxChars: 220);
        if (summaryMarkdown.Length > 0) {
            return summaryMarkdown;
        }

        var errorText = TruncateToolOutputSummary((output.Error ?? string.Empty).Trim(), maxChars: 220);
        if (errorText.Length > 0) {
            return "error: " + errorText;
        }

        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Length > 0) {
            return "error code " + errorCode;
        }

        var raw = (output.Output ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return output.Ok is false ? "returned an empty error payload." : "completed successfully.";
        }

        if (TryExtractPreferredJsonSummary(raw, out var jsonSummary)) {
            return TruncateToolOutputSummary(jsonSummary, maxChars: 220);
        }

        if (LooksLikeJsonPayload(raw)) {
            return output.Ok is false ? "returned structured error output." : "returned structured output.";
        }

        return TruncateToolOutputSummary(raw, maxChars: 220);
    }

    private static bool TryExtractPreferredJsonSummary(string raw, out string summary) {
        summary = string.Empty;
        if (!LooksLikeJsonPayload(raw)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (TryGetJsonString(root, "summary_markdown", out summary)
                || TryGetJsonString(root, "summary", out summary)
                || TryGetJsonString(root, "message", out summary)
                || TryGetJsonString(root, "error", out summary)) {
                return true;
            }

            if (root.TryGetProperty("failure", out var failure)
                && failure.ValueKind == JsonValueKind.Object
                && (TryGetJsonString(failure, "message", out summary)
                    || TryGetJsonString(failure, "code", out summary))) {
                return true;
            }

            if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array) {
                summary = "returned " + rows.GetArrayLength().ToString() + " row(s).";
                return true;
            }

            return false;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool TryGetJsonString(JsonElement obj, string propertyName, out string value) {
        value = string.Empty;
        if (!obj.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String) {
            return false;
        }

        var candidate = (node.GetString() ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool LooksLikeJsonPayload(string text) {
        var value = (text ?? string.Empty).TrimStart();
        return value.StartsWith("{") || value.StartsWith("[");
    }

    private static string TruncateToolOutputSummary(string text, int maxChars) {
        var normalized = CollapseWhitespace((text ?? string.Empty).Trim());
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length <= maxChars || maxChars <= 4) {
            return normalized;
        }

        return normalized.Substring(0, maxChars - 3).TrimEnd() + "...";
    }

    internal static string BuildProactiveFollowUpReviewPrompt(string userRequest, string assistantDraft) {
        return BuildProactiveFollowUpReviewPrompt(userRequest, assistantDraft, toolOutputs: null);
    }

    internal static string BuildProactiveFollowUpReviewPrompt(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolOutputDto>? toolOutputs) {
        var requestText = TrimForPrompt(userRequest, 520);
        var draftText = TrimForPrompt(ResolveReviewedAssistantDraft(assistantDraft).VisibleText, 1800);
        var requestedArtifactIntent = ResolveRequestedArtifactIntent(userRequest);
        var visualPolicy = ResolveProactiveVisualizationPolicy(userRequest, assistantDraft, toolOutputs);
        var allowNewVisualsText = visualPolicy.AllowNewVisuals ? "true" : "false";
        var draftHasVisualsText = visualPolicy.DraftHasVisuals ? "true" : "false";
        var requestHasVisualContractText = visualPolicy.RequestHasVisualContract ? "true" : "false";
        var preferredVisualTypeText = visualPolicy.HasPreferredVisualOverride
            ? visualPolicy.PreferredVisualType
            : "auto";
        var preferredVisualSourceText = visualPolicy.HasPreferredVisualOverride
            ? visualPolicy.PreferredVisualSource
            : "none";
        var preferredVisualPriorityText = visualPolicy.HasPreferredVisualPriority
            ? visualPolicy.PreferredVisualPriority.ToString(CultureInfo.InvariantCulture)
            : "n/a";
        var maxNewVisualsText = visualPolicy.HasMaxNewVisualsOverride
            ? visualPolicy.MaxNewVisuals.ToString(CultureInfo.InvariantCulture)
            : visualPolicy.AllowNewVisuals
                ? "1"
                : "0";
        var hasSpecificPreferredVisualType = visualPolicy.HasPreferredVisualOverride
            && !string.Equals(visualPolicy.PreferredVisualType, "auto", StringComparison.OrdinalIgnoreCase);
        var supportedVisualBlocks = GetSupportedProactiveVisualBlockListText();
        var visualRequirementLine = visualPolicy.AllowNewVisuals && visualPolicy.MaxNewVisuals > 0
            ? $"- If allow_new_visuals is true, include at most {visualPolicy.MaxNewVisuals} new visual block(s) and only when it materially compresses complex evidence."
            : $"- If allow_new_visuals is false, do not introduce new {supportedVisualBlocks} blocks in this proactive rewrite.";
        var preferredVisualRequirementLine = visualPolicy.AllowNewVisuals && hasSpecificPreferredVisualType
            ? "- If preferred_visual is set, prefer that visual format for any newly introduced visual block unless another supported format is clearly better."
            : string.Empty;
        var requestedArtifactRequirementLine = requestedArtifactIntent.RequiresArtifact
            ? BuildRequestedArtifactRequirementLine(requestedArtifactIntent)
            : string.Empty;
        return $$"""
            [Proactive follow-up review]
            {{ProactiveFollowUpMarker}}
            Expand the response with proactive intelligence based on current tool findings.

            [Proactive visualization guidance]
            {{ProactiveVisualizationMarker}}
            allow_new_visuals: {{allowNewVisualsText}}
            draft_has_visuals: {{draftHasVisualsText}}
            request_has_visual_contract: {{requestHasVisualContractText}}
            preferred_visual: {{preferredVisualTypeText}}
            preferred_visual_source: {{preferredVisualSourceText}}
            preferred_visual_priority: {{preferredVisualPriorityText}}
            max_new_visuals: {{maxNewVisualsText}}

            User request:
            {{requestText}}

            Current assistant draft:
            {{draftText}}

            {{BuildAnswerPlanInstructions()}}

            Requirements:
            - Keep all existing factual findings that are already supported by tool output.
            - Keep the response natural and conversational, not scripted.
            - Add proactive follow-ups only when they provide real value (typically 1-3 key items).
            - Prefer concise prose/bullets by default; keep visual blocks optional.
            - Use visuals only when they materially improve clarity over plain markdown.
            {{visualRequirementLine}}
            {{preferredVisualRequirementLine}}
            {{requestedArtifactRequirementLine}}
            - Preserve existing visual blocks when they are already present and still accurate.
            - When listing checks/fixes, make each item actionable and specific.
            - Include "why it matters" context when the impact is not obvious, but do not force that label on every line.
            - Vary structure naturally across turns; avoid repeating rigid templates.
            - If confidence is uncertain, say what evidence is missing and how to collect it.
            - Prefer proactive checks that can catch hidden regressions, not just obvious follow-ups.
            - Do not invent tool outputs or claim completed actions that were not executed.
            After the answer-plan block, return only the revised assistant response text.
            """;
    }

    private static ProactiveVisualizationPolicy ResolveProactiveVisualizationPolicy(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolOutputDto>? toolOutputs) {
        var requestHasProactiveVisualizationMarker = ContainsProactiveVisualizationMarker(userRequest);
        var requestHasVisualContractSignal = ContainsVisualContractSignal(userRequest);
        var requestHasVisualRequestSignal = TryResolvePreferredVisualTypeFromVisualRequestSignal(userRequest, out _);
        var hasStructuredOverrides = TryReadProactiveVisualizationOverridesFromRequestText(userRequest, out var hasAllowNewVisualsOverride,
            out var allowNewVisualsFromOverride, out var hasPreferredVisualOverride, out var preferredVisualType,
            out var hasMaxNewVisualsOverride, out var maxNewVisualsOverride);
        var inferredPreferredVisualTypeFromRequest = string.Empty;
        var hasInferredPreferredVisualTypeFromRequest = !hasPreferredVisualOverride
                                                        && TryResolvePreferredVisualTypeFromVisualRequestSignal(userRequest, out inferredPreferredVisualTypeFromRequest);
        var hasPreferredVisualDirectiveFromRequest = hasPreferredVisualOverride || hasInferredPreferredVisualTypeFromRequest;
        var effectivePreferredVisualType = hasPreferredVisualOverride
            ? preferredVisualType
            : hasInferredPreferredVisualTypeFromRequest
                ? inferredPreferredVisualTypeFromRequest
                : string.Empty;
        var preferredVisualSource = hasPreferredVisualDirectiveFromRequest ? "request" : "none";
        var hasPreferredVisualPriority = false;
        var preferredVisualPriority = 0;
        var hasExplicitAutoPreferredVisualOverride = hasPreferredVisualOverride
            && string.Equals(preferredVisualType, "auto", StringComparison.OrdinalIgnoreCase);
        var requestHasVisualContract = requestHasProactiveVisualizationMarker || requestHasVisualContractSignal || requestHasVisualRequestSignal || hasStructuredOverrides;
        var hasSpecificPreferredVisualTypeFromRequest = hasPreferredVisualDirectiveFromRequest
            && !string.Equals(effectivePreferredVisualType, "auto", StringComparison.OrdinalIgnoreCase);
        var baseAllowNewVisuals = hasAllowNewVisualsOverride
            ? allowNewVisualsFromOverride
            : hasExplicitAutoPreferredVisualOverride
                ? false
            : hasSpecificPreferredVisualTypeFromRequest
                ? true
            : hasMaxNewVisualsOverride
                    ? maxNewVisualsOverride > 0
                    : requestHasVisualRequestSignal;
        var hasInferredPreferredVisualTypeFromToolOutputs = false;
        if (baseAllowNewVisuals
            && requestHasVisualContract
            && !hasPreferredVisualDirectiveFromRequest
            && TryResolvePreferredVisualTypeFromToolOutputs(
                toolOutputs,
                out var inferredPreferredVisualTypeFromToolOutputs,
                out var inferredPreferredVisualPriorityFromToolOutputs,
                out var hasInferredPreferredVisualPriorityFromToolOutputs)
            && !string.IsNullOrWhiteSpace(inferredPreferredVisualTypeFromToolOutputs)) {
            effectivePreferredVisualType = inferredPreferredVisualTypeFromToolOutputs;
            preferredVisualSource = "tool_outputs";
            hasPreferredVisualPriority = hasInferredPreferredVisualPriorityFromToolOutputs;
            preferredVisualPriority = inferredPreferredVisualPriorityFromToolOutputs;
            hasInferredPreferredVisualTypeFromToolOutputs = true;
        }

        var hasInferredPreferredVisualTypeFromDraft = false;
        if (baseAllowNewVisuals
            && requestHasVisualContract
            && !hasPreferredVisualDirectiveFromRequest
            && !hasInferredPreferredVisualTypeFromToolOutputs
            && TryResolvePreferredVisualTypeFromVisualContractSignal(assistantDraft, out var inferredPreferredVisualTypeFromDraft)
            && !string.IsNullOrWhiteSpace(inferredPreferredVisualTypeFromDraft)) {
            effectivePreferredVisualType = inferredPreferredVisualTypeFromDraft;
            preferredVisualSource = "draft";
            hasInferredPreferredVisualTypeFromDraft = true;
        }

        var hasPreferredVisualDirective = hasPreferredVisualDirectiveFromRequest
                                          || hasInferredPreferredVisualTypeFromToolOutputs
                                          || hasInferredPreferredVisualTypeFromDraft;
        var maxNewVisuals = hasMaxNewVisualsOverride
            ? Math.Clamp(maxNewVisualsOverride, 0, MaxSupportedProactiveVisualBlocks)
            : baseAllowNewVisuals
                ? 1
                : 0;
        var allowNewVisuals = baseAllowNewVisuals && maxNewVisuals > 0;
        var draftHasVisuals = ContainsVisualContractSignal(assistantDraft);
        return new ProactiveVisualizationPolicy(
            AllowNewVisuals: allowNewVisuals,
            DraftHasVisuals: draftHasVisuals,
            RequestHasVisualContract: requestHasVisualContract,
            HasPreferredVisualOverride: hasPreferredVisualDirective,
            PreferredVisualType: effectivePreferredVisualType,
            PreferredVisualSource: preferredVisualSource,
            HasPreferredVisualPriority: hasPreferredVisualPriority,
            PreferredVisualPriority: preferredVisualPriority,
            HasMaxNewVisualsOverride: hasMaxNewVisualsOverride,
            MaxNewVisuals: maxNewVisuals);
    }

    private static string BuildRequestedArtifactRequirementLine(RequestedArtifactIntent requestedArtifactIntent) {
        if (!requestedArtifactIntent.RequiresArtifact) {
            return string.Empty;
        }

        var preferredVisualType = string.IsNullOrWhiteSpace(requestedArtifactIntent.PreferredVisualType)
            || string.Equals(requestedArtifactIntent.PreferredVisualType, AutoVisualType, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : requestedArtifactIntent.PreferredVisualType.Trim();
        var preferredVisualClause = preferredVisualType.Length > 0
            ? $" Prefer `{preferredVisualType}` when it fits the evidence."
            : string.Empty;

        if (requestedArtifactIntent.WantsTable && requestedArtifactIntent.WantsVisual) {
            return "- The user explicitly asked for both a compact table and a visual. Produce both when current evidence supports them; otherwise say clearly which artifact is unsupported and why instead of ignoring the request." + preferredVisualClause;
        }

        if (requestedArtifactIntent.WantsTable) {
            return "- The user explicitly asked for a compact table. Include it when current evidence supports it; if it cannot be produced from current evidence, say that explicitly instead of ignoring the request. Do not add unrelated diagram/chart/network blocks when the user only asked for a table.";
        }

        return "- The user explicitly asked for a visual artifact. Include a supported visual block when current evidence supports it; if it cannot be produced from current evidence, say that explicitly instead of ignoring the request." + preferredVisualClause;
    }

    private static bool TryResolvePreferredVisualTypeFromToolOutputs(
        IReadOnlyList<ToolOutputDto>? toolOutputs,
        out string preferredVisualType,
        out int preferredVisualPriority,
        out bool hasPreferredVisualPriority) {
        preferredVisualType = string.Empty;
        preferredVisualPriority = 0;
        hasPreferredVisualPriority = false;
        if (toolOutputs is null || toolOutputs.Count == 0) {
            return false;
        }

        var bestSourceScore = int.MinValue;
        var bestRecencyScore = -1;
        var bestPreferredVisualPriority = 0;
        var bestHasPreferredVisualPriority = false;
        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i];
            if (output is null) {
                continue;
            }

            if (TryResolvePreferredVisualTypeFromToolOutputRenderHints(
                    output.RenderJson,
                    out var preferredVisualFromRenderHints,
                    out var preferredVisualPriorityFromRenderHints,
                    out var hasPreferredVisualPriorityFromRenderHints)
                && !string.IsNullOrWhiteSpace(preferredVisualFromRenderHints)
                && TrySetPreferredVisualTypeCandidate(
                    candidateVisualType: preferredVisualFromRenderHints,
                    candidatePriority: preferredVisualPriorityFromRenderHints,
                    hasCandidatePriority: hasPreferredVisualPriorityFromRenderHints,
                    sourceScore: PreferredVisualSourceScoreRenderHint,
                    recencyIndex: i,
                    ref preferredVisualType,
                    ref bestSourceScore,
                    ref bestRecencyScore,
                    ref bestPreferredVisualPriority,
                    ref bestHasPreferredVisualPriority)) {
                continue;
            }

            if (TryResolvePreferredVisualTypeFromVisualContractSignal(output.SummaryMarkdown, out var preferredVisualFromSummary)
                && !string.IsNullOrWhiteSpace(preferredVisualFromSummary)
                && TrySetPreferredVisualTypeCandidate(
                    candidateVisualType: preferredVisualFromSummary,
                    candidatePriority: 0,
                    hasCandidatePriority: false,
                    sourceScore: PreferredVisualSourceScoreSummaryMarkdown,
                    recencyIndex: i,
                    ref preferredVisualType,
                    ref bestSourceScore,
                    ref bestRecencyScore,
                    ref bestPreferredVisualPriority,
                    ref bestHasPreferredVisualPriority)) {
                continue;
            }

            if (TryResolvePreferredVisualTypeFromToolOutputPayload(output.Output, out var preferredVisualFromOutput)
                && !string.IsNullOrWhiteSpace(preferredVisualFromOutput)) {
                TrySetPreferredVisualTypeCandidate(
                    candidateVisualType: preferredVisualFromOutput,
                    candidatePriority: 0,
                    hasCandidatePriority: false,
                    sourceScore: PreferredVisualSourceScoreOutputPayload,
                    recencyIndex: i,
                    ref preferredVisualType,
                    ref bestSourceScore,
                    ref bestRecencyScore,
                    ref bestPreferredVisualPriority,
                    ref bestHasPreferredVisualPriority);
            }
        }

        preferredVisualPriority = bestPreferredVisualPriority;
        hasPreferredVisualPriority = bestHasPreferredVisualPriority;
        return !string.IsNullOrWhiteSpace(preferredVisualType);
    }

    private static bool TrySetPreferredVisualTypeCandidate(
        string candidateVisualType,
        int candidatePriority,
        bool hasCandidatePriority,
        int sourceScore,
        int recencyIndex,
        ref string preferredVisualType,
        ref int bestSourceScore,
        ref int bestRecencyScore,
        ref int bestPreferredVisualPriority,
        ref bool bestHasPreferredVisualPriority) {
        var normalizedVisualType = (candidateVisualType ?? string.Empty).Trim();
        if (normalizedVisualType.Length == 0) {
            return false;
        }

        var normalizedRecency = Math.Max(0, recencyIndex);
        if (sourceScore < bestSourceScore) {
            return false;
        }

        if (sourceScore == bestSourceScore && normalizedRecency < bestRecencyScore) {
            return false;
        }

        preferredVisualType = normalizedVisualType;
        bestSourceScore = sourceScore;
        bestRecencyScore = normalizedRecency;
        bestPreferredVisualPriority = candidatePriority;
        bestHasPreferredVisualPriority = hasCandidatePriority;
        return true;
    }

    private static bool TryResolvePreferredVisualTypeFromToolOutputRenderHints(
        string? renderJson,
        out string preferredVisualType,
        out int preferredVisualPriority,
        out bool hasPreferredVisualPriority) {
        preferredVisualType = string.Empty;
        preferredVisualPriority = 0;
        hasPreferredVisualPriority = false;
        var payload = (renderJson ?? string.Empty).Trim();
        if (payload.Length < 2) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object) {
                return TryResolvePreferredVisualTypeFromRenderHint(
                    document.RootElement,
                    out preferredVisualType,
                    out preferredVisualPriority,
                    out hasPreferredVisualPriority);
            }

            if (document.RootElement.ValueKind != JsonValueKind.Array) {
                return false;
            }

            var bestPriority = int.MinValue;
            var bestVisualType = string.Empty;
            var bestVisualHintPriority = 0;
            var hasBestVisualHintPriority = false;
            foreach (var hint in document.RootElement.EnumerateArray()) {
                if (hint.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                if (!TryResolvePreferredVisualTypeFromRenderHint(
                        hint,
                        out var candidateVisualType,
                        out var candidateVisualHintPriority,
                        out var hasCandidateVisualHintPriority)
                    || string.IsNullOrWhiteSpace(candidateVisualType)) {
                    continue;
                }

                if (candidateVisualHintPriority <= bestPriority) {
                    continue;
                }

                bestPriority = candidateVisualHintPriority;
                bestVisualType = candidateVisualType;
                bestVisualHintPriority = candidateVisualHintPriority;
                hasBestVisualHintPriority = hasCandidateVisualHintPriority;
            }

            if (string.IsNullOrWhiteSpace(bestVisualType)) {
                return false;
            }

            preferredVisualType = bestVisualType;
            preferredVisualPriority = bestVisualHintPriority;
            hasPreferredVisualPriority = hasBestVisualHintPriority;
            return true;
        } catch (JsonException) {
            preferredVisualType = string.Empty;
            preferredVisualPriority = 0;
            hasPreferredVisualPriority = false;
            return false;
        }
    }

    private static int ResolveRenderHintVisualPriority(string preferredVisualType) {
        return preferredVisualType switch {
            NetworkVisualType => RenderHintVisualTypePriorityNetwork,
            ChartVisualType => RenderHintVisualTypePriorityChart,
            MermaidVisualType => RenderHintVisualTypePriorityMermaid,
            TableVisualType => RenderHintVisualTypePriorityTable,
            _ => RenderHintVisualTypePriorityDefault
        };
    }

    private static int ResolveRenderHintPriority(JsonElement renderHint, string preferredVisualType) {
        if (!TryReadJsonInt32PropertyIgnoreCase(renderHint, "priority", out var explicitPriority)) {
            return ResolveRenderHintVisualPriority(preferredVisualType);
        }

        return Math.Clamp(explicitPriority, RenderHintPriorityMin, RenderHintPriorityMax);
    }

    private static bool TryResolvePreferredVisualTypeFromRenderHint(
        JsonElement renderHint,
        out string preferredVisualType,
        out int preferredVisualPriority,
        out bool hasPreferredVisualPriority) {
        preferredVisualType = string.Empty;
        preferredVisualPriority = 0;
        hasPreferredVisualPriority = false;
        if (renderHint.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!TryReadJsonStringPropertyIgnoreCase(renderHint, "kind", out var kind)) {
            return false;
        }

        if (TryResolvePreferredVisualTypeToken(kind.AsSpan(), out preferredVisualType)) {
            preferredVisualPriority = ResolveRenderHintPriority(renderHint, preferredVisualType);
            hasPreferredVisualPriority = true;
            return true;
        }

        if (!kind.Equals("code", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryReadJsonStringPropertyIgnoreCase(renderHint, "language", out var language)) {
            return false;
        }

        if (!TryResolvePreferredVisualTypeToken(language.AsSpan(), out preferredVisualType)) {
            return false;
        }

        preferredVisualPriority = ResolveRenderHintPriority(renderHint, preferredVisualType);
        hasPreferredVisualPriority = true;
        return true;
    }

    private static bool TryReadJsonStringPropertyIgnoreCase(
        JsonElement obj,
        string propertyName,
        out string value) {
        value = string.Empty;
        if (obj.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        foreach (var property in obj.EnumerateObject()) {
            if (!property.NameEquals(propertyName)
                && !string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String) {
                return false;
            }

            var candidate = (property.Value.GetString() ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                return false;
            }

            value = candidate;
            return true;
        }

        return false;
    }

    private static bool TryReadJsonInt32PropertyIgnoreCase(
        JsonElement obj,
        string propertyName,
        out int value) {
        value = 0;
        if (obj.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(propertyName)) {
            return false;
        }

        foreach (var property in obj.EnumerateObject()) {
            if (!property.NameEquals(propertyName)
                && !string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number) {
                if (property.Value.TryGetInt32(out var numericValue)) {
                    value = numericValue;
                    return true;
                }

                if (property.Value.TryGetInt64(out var longValue)) {
                    value = longValue > int.MaxValue ? int.MaxValue : longValue < int.MinValue ? int.MinValue : (int)longValue;
                    return true;
                }

                return false;
            }

            if (property.Value.ValueKind != JsonValueKind.String) {
                return false;
            }

            var numericText = (property.Value.GetString() ?? string.Empty).Trim();
            if (numericText.Length == 0
                || !int.TryParse(numericText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue)) {
                return false;
            }

            value = parsedValue;
            return true;
        }

        return false;
    }

    private static bool TryResolvePreferredVisualTypeFromToolOutputPayload(
        string? outputPayload,
        out string preferredVisualType) {
        preferredVisualType = string.Empty;
        var payload = (outputPayload ?? string.Empty).Trim();
        if (payload.Length < 2) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(payload);
            return TryResolvePreferredVisualTypeFromJsonElement(document.RootElement, out preferredVisualType);
        } catch (JsonException) {
            preferredVisualType = string.Empty;
            return false;
        }
    }

    private static bool ContainsProactiveVisualizationMarker(string? text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return false;
        }

        var marker = ProactiveVisualizationMarker.AsSpan();
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty) {
            var lineBreakIndex = remaining.IndexOfAny('\r', '\n');
            ReadOnlySpan<char> line;
            if (lineBreakIndex < 0) {
                line = remaining;
                remaining = ReadOnlySpan<char>.Empty;
            } else {
                line = remaining.Slice(0, lineBreakIndex);
                var nextIndex = lineBreakIndex + 1;
                if (nextIndex < remaining.Length && remaining[lineBreakIndex] == '\r' && remaining[nextIndex] == '\n') {
                    nextIndex++;
                }

                remaining = remaining.Slice(nextIndex);
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var suffix = trimmed.Slice(marker.Length).TrimStart();
            if (suffix.IsEmpty
                || suffix[0] == '#'
                || suffix[0] == ';'
                || (suffix[0] == '/' && suffix.Length > 1 && suffix[1] == '/')) {
                return true;
            }
        }

        return false;
    }

    internal Task RunPhaseProgressLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask) {
        ValidatePhaseProgressLoopArgs(writer, requestId, threadId, phaseTask);
        return RunPhaseProgressLoopCoreAsync(
            writer,
            requestId,
            threadId,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            cancellationToken,
            phaseTask,
            heartbeatTaskFactory: null);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Task RunPhaseProgressLoopForTestingAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask,
        Func<CancellationToken, Task> heartbeatTaskFactory) {
        ValidatePhaseProgressLoopArgs(writer, requestId, threadId, phaseTask);
        if (heartbeatTaskFactory is null) {
            throw new ArgumentNullException(nameof(heartbeatTaskFactory));
        }

        return RunPhaseProgressLoopCoreAsync(
            writer,
            requestId,
            threadId,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            cancellationToken,
            phaseTask,
            heartbeatTaskFactory);
    }

    private static void ValidatePhaseProgressLoopArgs(StreamWriter writer, string requestId, string threadId, Task phaseTask) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(phaseTask);
    }

    private async Task RunPhaseProgressLoopCoreAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string phaseStatus,
        string? phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds,
        CancellationToken cancellationToken,
        Task phaseTask,
        Func<CancellationToken, Task>? heartbeatTaskFactory) {
        var status = string.IsNullOrWhiteSpace(phaseStatus) ? "thinking" : phaseStatus.Trim();
        if (!string.IsNullOrWhiteSpace(phaseMessage)) {
            await TryWriteStatusAsync(writer, requestId, threadId, status: status, message: phaseMessage).ConfigureAwait(false);
        }

        if (heartbeatSeconds <= 0) {
            await phaseTask.ConfigureAwait(false);
            return;
        }

        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, heartbeatSeconds));
        var sw = Stopwatch.StartNew();
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new PeriodicTimer(heartbeatInterval);
        var heartbeatTask = heartbeatTaskFactory is null
            ? RunPhaseHeartbeatLoopAsync(
                writer,
                requestId,
                threadId,
                heartbeatLabel,
                sw,
                phaseTask,
                timer,
                heartbeatCts.Token)
            : heartbeatTaskFactory(heartbeatCts.Token);
        await Task.WhenAny(phaseTask, heartbeatTask).ConfigureAwait(false);
        heartbeatCts.Cancel();
        Exception? heartbeatFailure = null;
        try {
            await heartbeatTask.ConfigureAwait(false);
        } catch (Exception ex) {
            heartbeatFailure = ex;
        }

        await phaseTask.ConfigureAwait(false);
        FinalizePhaseHeartbeatFailure(heartbeatFailure, status, requestId, threadId, heartbeatCts.Token, cancellationToken);
    }

    internal static void FinalizePhaseHeartbeatFailure(
        Exception? heartbeatFailure,
        string phaseStatus,
        string requestId,
        string threadId,
        CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken) {
        if (heartbeatFailure is null) {
            return;
        }

        var suppressionReason = GetPhaseHeartbeatSuppressionReason(heartbeatFailure, heartbeatCancellationToken, cancellationToken);
        if (suppressionReason is not null) {
            Trace.TraceWarning(
                $"Phase heartbeat loop suppressed failure after phase completion: phase={phaseStatus}; request={requestId}; thread={threadId}; " +
                $"reason={suppressionReason}; error={heartbeatFailure}");
            return;
        }

        ExceptionDispatchInfo.Capture(heartbeatFailure).Throw();
    }

    internal static bool ShouldSuppressPhaseHeartbeatFailure(Exception heartbeatFailure, CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(heartbeatFailure);
        return GetPhaseHeartbeatSuppressionReason(heartbeatFailure, heartbeatCancellationToken, cancellationToken) is not null;
    }

    internal static string? GetPhaseHeartbeatSuppressionReason(Exception heartbeatFailure, CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(heartbeatFailure);
        if (heartbeatFailure is IOException) {
            return PhaseHeartbeatSuppressionReasonIo;
        }

        if (heartbeatFailure is not OperationCanceledException canceledException) {
            return null;
        }

        var failureToken = canceledException.CancellationToken;
        if (!failureToken.CanBeCanceled) {
            return heartbeatCancellationToken.IsCancellationRequested || cancellationToken.IsCancellationRequested
                ? PhaseHeartbeatSuppressionReasonCanceled
                : null;
        }

        // The heartbeat loop should throw OCE with either the linked heartbeat token
        // or the outer request token. Treat other canceled tokens as unexpected.
        if (failureToken == heartbeatCancellationToken && heartbeatCancellationToken.IsCancellationRequested) {
            return PhaseHeartbeatSuppressionReasonHeartbeatCanceled;
        }

        if (failureToken == cancellationToken && cancellationToken.IsCancellationRequested) {
            return PhaseHeartbeatSuppressionReasonRequestCanceled;
        }

        return null;
    }

    private async Task RunPhaseHeartbeatLoopAsync(
        StreamWriter writer,
        string requestId,
        string threadId,
        string heartbeatLabel,
        Stopwatch sw,
        Task phaseTask,
        PeriodicTimer timer,
        CancellationToken cancellationToken) {
        try {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                if (phaseTask.IsCompleted) {
                    break;
                }

                var elapsedSeconds = Math.Max(1, (int)Math.Round(sw.Elapsed.TotalSeconds));
                await TryWriteStatusAsync(
                        writer,
                        requestId,
                        threadId,
                        status: ChatStatusCodes.PhaseHeartbeat,
                        durationMs: sw.ElapsedMilliseconds,
                        message: $"{heartbeatLabel}... ({elapsedSeconds}s)")
                    .ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected when the model phase completes or the turn is canceled.
        }
    }

    internal async Task<TurnInfo> RunModelPhaseWithProgressAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        string requestId,
        string threadId,
        ChatInput input,
        ChatOptions options,
        CancellationToken cancellationToken,
        string phaseStatus,
        string phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds) {
        // Defensive rebind: planner routing may temporarily switch threads.
        // Reassert the active conversation thread before each model phase.
        var normalizedPhaseStatus = string.IsNullOrWhiteSpace(phaseStatus) ? ChatStatusCodes.Thinking : phaseStatus.Trim();
        if (!string.IsNullOrWhiteSpace(threadId)) {
            var requestedThreadId = threadId.Trim();
            var reboundThreadId = ResolveRecoveredThreadAlias(requestedThreadId);
            await TryWriteStatusAsync(
                    writer,
                    requestId,
                    threadId,
                    status: normalizedPhaseStatus,
                    message: "Preparing model phase context...")
                .ConfigureAwait(false);
            try {
                await client.UseThreadAsync(reboundThreadId, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ShouldRecoverMissingTransportThread(ex)) {
                var recoveredThread = await client.StartNewThreadAsync(options.Model, cancellationToken: cancellationToken).ConfigureAwait(false);
                var recoveredThreadId = (recoveredThread.Id ?? string.Empty).Trim();
                if (recoveredThreadId.Length > 0) {
                    await client.UseThreadAsync(recoveredThreadId, cancellationToken).ConfigureAwait(false);
                    RememberRecoveredThreadAlias(requestedThreadId, recoveredThreadId);
                    if (!string.Equals(reboundThreadId, requestedThreadId, StringComparison.Ordinal)) {
                        RememberRecoveredThreadAlias(reboundThreadId, recoveredThreadId);
                    }

                    await TryWriteStatusAsync(
                            writer,
                            requestId,
                            recoveredThreadId,
                            status: normalizedPhaseStatus,
                            message: "Recovered missing conversation context. Continuing model phase...")
                        .ConfigureAwait(false);
                }
            }
        }

        var chatTask = ChatWithToolSchemaRecoveryAsync(client, input, options, cancellationToken);
        await RunPhaseProgressLoopAsync(
                writer,
                requestId,
                threadId,
                phaseStatus,
                phaseMessage,
                heartbeatLabel,
                heartbeatSeconds,
                cancellationToken,
                chatTask)
            .ConfigureAwait(false);
        return await chatTask.ConfigureAwait(false);
    }

    internal Task<TurnInfo> RunReviewOnlyModelPhaseWithProgressAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        string requestId,
        string threadId,
        ChatInput input,
        ChatOptions options,
        CancellationToken cancellationToken,
        string phaseStatus,
        string phaseMessage,
        string heartbeatLabel,
        int heartbeatSeconds) {
        // Review-only passes are in-thread rewrites of the current draft and must never execute tools.
        return RunModelPhaseWithProgressAsync(
            client,
            writer,
            requestId,
            threadId,
            input,
            CopyChatOptionsWithoutTools(options, newThreadOverride: false),
            cancellationToken,
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds);
    }

    private static ChatOptions CopyChatOptions(ChatOptions options, bool? newThreadOverride = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var copy = options.Clone();
        if (newThreadOverride.HasValue) {
            copy.NewThread = newThreadOverride.Value;
        }
        return copy;
    }

    internal static ChatOptions CopyChatOptionsWithoutTools(ChatOptions options, bool? newThreadOverride = null) {
        var copy = CopyChatOptions(options, newThreadOverride);
        copy.Tools = null;
        copy.ToolChoice = null;
        copy.ParallelToolCalls = false;
        return copy;
    }
}
