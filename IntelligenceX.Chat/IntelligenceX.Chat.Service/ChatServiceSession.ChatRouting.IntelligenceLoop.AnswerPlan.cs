using System;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static readonly Regex AnswerPlanMarkerRegex = new(
        @"ix\s*:\s*answer-plan\s*:\s*v1(?:\b|(?=[a-z_]))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal readonly record struct TurnAnswerPlan(
        bool HasPlan,
        string UserGoal,
        string ResolvedSoFar,
        string UnresolvedNow,
        bool CarryForwardUnresolvedFocus,
        string CarryForwardReason,
        bool RequiresLiveExecution,
        string MissingLiveEvidence,
        string[] PreferredPackIds,
        string[] PreferredToolNames,
        string[] PreferredDeferredWorkCapabilityIds,
        bool AllowCachedEvidenceReuse,
        bool PreferCachedEvidenceReuse,
        string CachedEvidenceReuseReason,
        string PrimaryArtifact,
        bool RequestedArtifactAlreadyVisibleAbove,
        string RequestedArtifactVisibilityReason,
        bool RepeatsPriorVisibleContent,
        string PriorVisibleDeltaReason,
        bool ReusePriorVisuals,
        string ReuseReason,
        bool RepeatAddsNewInformation,
        string RepeatNoveltyReason,
        bool AdvancesCurrentAsk,
        string AdvanceReason) {
        internal static TurnAnswerPlan None() =>
            new(
                HasPlan: false,
                UserGoal: string.Empty,
                ResolvedSoFar: string.Empty,
                UnresolvedNow: string.Empty,
                CarryForwardUnresolvedFocus: false,
                CarryForwardReason: string.Empty,
                RequiresLiveExecution: false,
                MissingLiveEvidence: string.Empty,
                PreferredPackIds: Array.Empty<string>(),
                PreferredToolNames: Array.Empty<string>(),
                PreferredDeferredWorkCapabilityIds: Array.Empty<string>(),
                AllowCachedEvidenceReuse: false,
                PreferCachedEvidenceReuse: false,
                CachedEvidenceReuseReason: string.Empty,
                PrimaryArtifact: string.Empty,
                RequestedArtifactAlreadyVisibleAbove: false,
                RequestedArtifactVisibilityReason: string.Empty,
                RepeatsPriorVisibleContent: false,
                PriorVisibleDeltaReason: string.Empty,
                ReusePriorVisuals: false,
                ReuseReason: string.Empty,
                RepeatAddsNewInformation: true,
                RepeatNoveltyReason: string.Empty,
                AdvancesCurrentAsk: true,
                AdvanceReason: string.Empty);
    }

    internal readonly record struct ReviewedAssistantDraft(
        string VisibleText,
        TurnAnswerPlan AnswerPlan);

    internal static ReviewedAssistantDraft ResolveReviewedAssistantDraft(string? assistantDraft) {
        var text = assistantDraft ?? string.Empty;
        if (text.Length == 0) {
            return new ReviewedAssistantDraft(string.Empty, TurnAnswerPlan.None());
        }

        if (!TryExtractTurnAnswerPlan(text, out var answerPlan, out var blockStart, out var blockLength)) {
            return new ReviewedAssistantDraft(text, TurnAnswerPlan.None());
        }

        var sanitized = StripRange(text, blockStart, blockLength);
        return new ReviewedAssistantDraft(sanitized, answerPlan);
    }

    private static string BuildAnswerPlanInstructions() {
        return $$"""
            [Answer progression plan]
            {{AnswerPlanMarker}}
            user_goal: <one short line>
            resolved_so_far: <one short line>
            unresolved_now: <one short line>
            carry_forward_unresolved_focus: true|false
            carry_forward_reason: <short line or none>
            requires_live_execution: true|false
            missing_live_evidence: <short line or none>
            preferred_pack_ids: <csv pack ids or none>
            preferred_tool_names: <csv tool names or none>
            preferred_deferred_work_capability_ids: <csv deferred capability ids or none>
            allow_cached_evidence_reuse: true|false
            prefer_cached_evidence_reuse: true|false
            cached_evidence_reuse_reason: <short line or none>
            primary_artifact: prose|table|diagram|chart|network|none
            requested_artifact_already_visible_above: true|false
            requested_artifact_visibility_reason: <short line or none>
            repeats_prior_visible_content: true|false
            prior_visible_delta_reason: <short line or none>
            reuse_prior_visuals: true|false
            reuse_reason: <short line or none>
            repeat_adds_new_information: true|false
            repeat_novelty_reason: <short line or none>
            advances_current_ask: true|false
            advance_reason: <one short line>

            Compare against assistant content already visible earlier in the thread, not just this draft.
            Set carry_forward_unresolved_focus to false when this turn resolves the prior follow-up gap and nothing narrower should stay active.
            If carry_forward_unresolved_focus is true, unresolved_now must name only the next remaining gap, not the old umbrella task.
            Set requires_live_execution to true when the current ask still needs fresh tool execution instead of capability explanation or cached evidence.
            Use missing_live_evidence to name the fresh evidence that is still missing (for example cert status, disk state, memory usage).
            preferred_pack_ids and preferred_tool_names should be compact planning hints for the next execution attempt.
            preferred_deferred_work_capability_ids should name runtime-registered deferred follow-up capabilities like email, reporting, or notification when that deliverable format matters for the next step.
            Set allow_cached_evidence_reuse to true only when recent read-only evidence remains acceptable for this exact continuation.
            Set prefer_cached_evidence_reuse to true only when this turn should explicitly reuse the latest fresh read-only evidence snapshot on a compact continuation instead of rerunning tools.
            Start your output with this exact answer-plan block, then a blank line, then the revised assistant response text for the user.
            The answer-plan block is runtime-only metadata and will be removed before display.
            """;
    }

    private static bool TryExtractTurnAnswerPlan(
        string? text,
        out TurnAnswerPlan answerPlan,
        out int blockStart,
        out int blockLength) {
        answerPlan = TurnAnswerPlan.None();
        blockStart = 0;
        blockLength = 0;

        var content = text ?? string.Empty;
        if (content.Length == 0) {
            return false;
        }

        var markerMatch = AnswerPlanMarkerRegex.Match(content);
        if (!markerMatch.Success) {
            return false;
        }
        var markerIndex = markerMatch.Index;

        blockStart = FindAnswerPlanBlockStart(content, markerIndex);
        var position = blockStart;
        var sawMarker = false;
        var userGoal = string.Empty;
        var resolvedSoFar = string.Empty;
        var unresolvedNow = string.Empty;
        var carryForwardReason = string.Empty;
        var missingLiveEvidence = string.Empty;
        var preferredPackIds = Array.Empty<string>();
        var preferredToolNames = Array.Empty<string>();
        var preferredDeferredWorkCapabilityIds = Array.Empty<string>();
        var cachedEvidenceReuseReason = string.Empty;
        var primaryArtifact = string.Empty;
        var requestedArtifactVisibilityReason = string.Empty;
        var priorVisibleDeltaReason = string.Empty;
        var reuseReason = string.Empty;
        var repeatNoveltyReason = string.Empty;
        var advanceReason = string.Empty;
        var carryForwardUnresolvedFocus = false;
        var requiresLiveExecution = false;
        var allowCachedEvidenceReuse = false;
        var preferCachedEvidenceReuse = false;
        var requestedArtifactAlreadyVisibleAbove = false;
        var repeatsPriorVisibleContent = false;
        var reusePriorVisuals = false;
        var repeatAddsNewInformation = true;
        var advancesCurrentAsk = true;
        var hasCarryForwardUnresolvedFocus = false;
        var hasRequiresLiveExecution = false;
        var hasAllowCachedEvidenceReuse = false;
        var hasPreferCachedEvidenceReuse = false;
        var hasRequestedArtifactAlreadyVisibleAbove = false;
        var hasRepeatsPriorVisibleContent = false;
        var hasReusePriorVisuals = false;
        var hasRepeatAddsNewInformation = false;
        var hasAdvancesCurrentAsk = false;

        while (position < content.Length) {
            var lineOffset = position;
            position = ReadNextLine(content.AsSpan(), position, out var line);
            var trimmed = line.Trim();

            if (!sawMarker) {
                if (ContainsAnswerPlanMarker(trimmed)) {
                    sawMarker = true;
                }

                continue;
            }

            if (trimmed.IsEmpty) {
                blockLength = position - blockStart;
                break;
            }

            if (LooksLikeStructuredSectionHeader(trimmed)) {
                blockLength = lineOffset - blockStart;
                break;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "user_goal", out var userGoalValue)) {
                userGoal = userGoalValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "resolved_so_far", out var resolvedValue)) {
                resolvedSoFar = resolvedValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "unresolved_now", out var unresolvedValue)) {
                unresolvedNow = unresolvedValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "carry_forward_reason", out var carryForwardReasonValue)) {
                carryForwardReason = carryForwardReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "missing_live_evidence", out var missingLiveEvidenceValue)) {
                missingLiveEvidence = missingLiveEvidenceValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_pack_ids", out var preferredPackIdsValue)) {
                preferredPackIds = NormalizeStructuredMetadataCsv(preferredPackIdsValue, NormalizePackId, maxItems: 8);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_tool_names", out var preferredToolNamesValue)) {
                preferredToolNames = NormalizeStructuredMetadataCsv(
                    preferredToolNamesValue,
                    static value => NormalizeToolNameForAnswerPlan(value),
                    maxItems: 8);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "preferred_deferred_work_capability_ids", out var preferredDeferredWorkCapabilityIdsValue)) {
                preferredDeferredWorkCapabilityIds = NormalizeStructuredMetadataCsv(
                    preferredDeferredWorkCapabilityIdsValue,
                    static value => NormalizeDeferredWorkCapabilityId(value),
                    maxItems: 6);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "cached_evidence_reuse_reason", out var cachedEvidenceReuseReasonValue)) {
                cachedEvidenceReuseReason = cachedEvidenceReuseReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "primary_artifact", out var artifactValue)) {
                primaryArtifact = NormalizeStructuredArtifactKind(artifactValue);
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "requested_artifact_visibility_reason", out var requestedArtifactVisibilityReasonValue)) {
                requestedArtifactVisibilityReason = requestedArtifactVisibilityReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "prior_visible_delta_reason", out var priorVisibleDeltaReasonValue)) {
                priorVisibleDeltaReason = priorVisibleDeltaReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "reuse_reason", out var reuseReasonValue)) {
                reuseReason = reuseReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "repeat_novelty_reason", out var repeatNoveltyReasonValue)) {
                repeatNoveltyReason = repeatNoveltyReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredKeyValueLine(trimmed, "advance_reason", out var advanceReasonValue)) {
                advanceReason = advanceReasonValue.ToString();
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "reuse_prior_visuals", out var parsedReusePriorVisuals)) {
                reusePriorVisuals = parsedReusePriorVisuals;
                hasReusePriorVisuals = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "carry_forward_unresolved_focus", out var parsedCarryForwardUnresolvedFocus)) {
                carryForwardUnresolvedFocus = parsedCarryForwardUnresolvedFocus;
                hasCarryForwardUnresolvedFocus = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "requires_live_execution", out var parsedRequiresLiveExecution)) {
                requiresLiveExecution = parsedRequiresLiveExecution;
                hasRequiresLiveExecution = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "allow_cached_evidence_reuse", out var parsedAllowCachedEvidenceReuse)) {
                allowCachedEvidenceReuse = parsedAllowCachedEvidenceReuse;
                hasAllowCachedEvidenceReuse = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "prefer_cached_evidence_reuse", out var parsedPreferCachedEvidenceReuse)) {
                preferCachedEvidenceReuse = parsedPreferCachedEvidenceReuse;
                hasPreferCachedEvidenceReuse = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "requested_artifact_already_visible_above", out var parsedRequestedArtifactAlreadyVisibleAbove)) {
                requestedArtifactAlreadyVisibleAbove = parsedRequestedArtifactAlreadyVisibleAbove;
                hasRequestedArtifactAlreadyVisibleAbove = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "repeats_prior_visible_content", out var parsedRepeatsPriorVisibleContent)) {
                repeatsPriorVisibleContent = parsedRepeatsPriorVisibleContent;
                hasRepeatsPriorVisibleContent = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "repeat_adds_new_information", out var parsedRepeatAddsNewInformation)) {
                repeatAddsNewInformation = parsedRepeatAddsNewInformation;
                hasRepeatAddsNewInformation = true;
                continue;
            }

            if (TryParseStructuredBooleanLine(trimmed, "advances_current_ask", out var parsedAdvancesCurrentAsk)) {
                advancesCurrentAsk = parsedAdvancesCurrentAsk;
                hasAdvancesCurrentAsk = true;
                continue;
            }

            blockLength = lineOffset - blockStart;
            break;
        }

        if (blockLength == 0) {
            blockLength = content.Length - blockStart;
        }

        if (!sawMarker) {
            return false;
        }

        var effectiveAllowCachedEvidenceReuse = hasAllowCachedEvidenceReuse
            ? allowCachedEvidenceReuse
            : hasPreferCachedEvidenceReuse && preferCachedEvidenceReuse;

        answerPlan = new TurnAnswerPlan(
            HasPlan: true,
            UserGoal: userGoal,
            ResolvedSoFar: resolvedSoFar,
            UnresolvedNow: unresolvedNow,
            CarryForwardUnresolvedFocus: hasCarryForwardUnresolvedFocus ? carryForwardUnresolvedFocus : NormalizeStructuredMetadataText(unresolvedNow).Length > 0,
            CarryForwardReason: NormalizeStructuredMetadataText(carryForwardReason),
            RequiresLiveExecution: hasRequiresLiveExecution && requiresLiveExecution,
            MissingLiveEvidence: NormalizeStructuredMetadataText(missingLiveEvidence),
            PreferredPackIds: preferredPackIds,
            PreferredToolNames: preferredToolNames,
            PreferredDeferredWorkCapabilityIds: preferredDeferredWorkCapabilityIds,
            AllowCachedEvidenceReuse: effectiveAllowCachedEvidenceReuse,
            PreferCachedEvidenceReuse: hasPreferCachedEvidenceReuse && preferCachedEvidenceReuse,
            CachedEvidenceReuseReason: NormalizeStructuredMetadataText(cachedEvidenceReuseReason),
            PrimaryArtifact: primaryArtifact,
            RequestedArtifactAlreadyVisibleAbove: hasRequestedArtifactAlreadyVisibleAbove && requestedArtifactAlreadyVisibleAbove,
            RequestedArtifactVisibilityReason: NormalizeStructuredMetadataText(requestedArtifactVisibilityReason),
            RepeatsPriorVisibleContent: hasRepeatsPriorVisibleContent && repeatsPriorVisibleContent,
            PriorVisibleDeltaReason: NormalizeStructuredMetadataText(priorVisibleDeltaReason),
            ReusePriorVisuals: hasReusePriorVisuals && reusePriorVisuals,
            ReuseReason: NormalizeStructuredMetadataText(reuseReason),
            RepeatAddsNewInformation: !hasRepeatAddsNewInformation || repeatAddsNewInformation,
            RepeatNoveltyReason: NormalizeStructuredMetadataText(repeatNoveltyReason),
            AdvancesCurrentAsk: !hasAdvancesCurrentAsk || advancesCurrentAsk,
            AdvanceReason: NormalizeStructuredMetadataText(advanceReason));
        return true;
    }

    private static bool ContainsAnswerPlanMarker(ReadOnlySpan<char> line) {
        return line.Length > 0 && AnswerPlanMarkerRegex.IsMatch(line.ToString());
    }

    private static bool TryParseStructuredBooleanLine(ReadOnlySpan<char> line, string key, out bool value) {
        value = false;
        if (!TryParseStructuredKeyValueLine(line, key, out var parsedValue)) {
            return false;
        }

        if (parsedValue.Equals("true", StringComparison.OrdinalIgnoreCase)) {
            value = true;
            return true;
        }

        if (parsedValue.Equals("false", StringComparison.OrdinalIgnoreCase)) {
            value = false;
            return true;
        }

        return false;
    }

    private static string NormalizeStructuredArtifactKind(ReadOnlySpan<char> value) {
        var normalized = NormalizeCompactToken(value);
        return normalized switch {
            "table" => "table",
            "diagram" => "diagram",
            "chart" => "chart",
            "network" => "network",
            "none" => "none",
            "prose" => "prose",
            _ => string.Empty
        };
    }

    private static string NormalizeStructuredMetadataText(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "n/a", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        return normalized;
    }

    private static string[] NormalizeStructuredMetadataCsv(
        ReadOnlySpan<char> value,
        Func<string, string> normalizeItem,
        int maxItems) {
        if (value.IsEmpty || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var raw = NormalizeStructuredMetadataText(value.ToString());
        if (raw.Length == 0) {
            return Array.Empty<string>();
        }

        var items = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(Math.Min(items.Length, maxItems));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Length && normalized.Count < maxItems; i++) {
            var candidate = normalizeItem(items[i]);
            if (candidate.Length == 0 || !seen.Add(candidate)) {
                continue;
            }

            normalized.Add(candidate);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    private static string NormalizeToolNameForAnswerPlan(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[normalized.Length];
        var written = 0;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-') {
                buffer[written++] = char.ToLowerInvariant(ch);
            }
        }

        if (written == 0) {
            return string.Empty;
        }

        return new string(buffer[..written]).Replace('-', '_');
    }

    private static int FindAnswerPlanBlockStart(string text, int markerIndex) {
        if (markerIndex <= 0) {
            return 0;
        }

        var start = markerIndex;
        while (start > 0 && text[start - 1] != '\n' && text[start - 1] != '\r') {
            start--;
        }

        var headerStart = start;
        if (headerStart > 0) {
            var previousLineEnd = headerStart - 1;
            while (previousLineEnd >= 0 && (text[previousLineEnd] == '\r' || text[previousLineEnd] == '\n')) {
                previousLineEnd--;
            }

            if (previousLineEnd >= 0) {
                var previousLineStart = previousLineEnd;
                while (previousLineStart > 0 && text[previousLineStart - 1] != '\n' && text[previousLineStart - 1] != '\r') {
                    previousLineStart--;
                }

                var previousLine = text.AsSpan(previousLineStart, previousLineEnd - previousLineStart + 1).Trim();
                if (LooksLikeStructuredSectionHeader(previousLine)) {
                    return previousLineStart;
                }
            }
        }

        return start;
    }

    private static string StripRange(string text, int start, int length) {
        if (string.IsNullOrEmpty(text) || length <= 0 || start < 0 || start >= text.Length) {
            return text ?? string.Empty;
        }

        var effectiveLength = Math.Min(length, text.Length - start);
        var builder = new StringBuilder(text.Length);
        if (start > 0) {
            builder.Append(text, 0, start);
        }

        var suffixStart = start + effectiveLength;
        while (suffixStart < text.Length && (text[suffixStart] == '\r' || text[suffixStart] == '\n')) {
            suffixStart++;
        }

        if (suffixStart < text.Length) {
            builder.Append(text, suffixStart, text.Length - suffixStart);
        }

        return builder.ToString().Trim();
    }
}
