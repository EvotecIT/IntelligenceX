using System;
using System.Text;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.ExportArtifacts;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Applies App-specific timing and legacy repair before delegating to explicit OfficeIMO transcript-preparation contracts.
/// </summary>
internal static class TranscriptMarkdownPreparation {
    private static readonly Regex StructuredMemoryFenceRegex = new(
        @"(?is)(?:```|~~~)\s*(?:ix_memory|ix_memory_note)\b[\s\S]*?(?:(?:```|~~~)|(?=(?:\\?\[Answer progression plan\\?\]|ix\s*:\s*answer-plan\s*:\s*v1(?:\b|(?=[a-z_]))|$)))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnswerPlanMarkerRegex = new(
        @"ix\s*:\s*answer-plan\s*:\s*v1(?:\b|(?=[a-z_]))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MermaidEdgeStatementStartRegex = new(
        @"(?<!\S)[A-Za-z_][A-Za-z0-9_-]*\s+(?:-->|---|-.->|==>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] AnswerPlanKeys = [
        "user_goal:",
        "resolved_so_far:",
        "unresolved_now:",
        "carry_forward_unresolved_focus:",
        "carry_forward_reason:",
        "requires_live_execution:",
        "missing_live_evidence:",
        "preferred_pack_ids:",
        "preferred_tool_names:",
        "preferred_deferred_work_capability_ids:",
        "allow_cached_evidence_reuse:",
        "prefer_cached_evidence_reuse:",
        "cached_evidence_reuse_reason:",
        "primary_artifact:",
        "requested_artifact_already_visible_above:",
        "requested_artifact_visibility_reason:",
        "repeats_prior_visible_content:",
        "prior_visible_delta_reason:",
        "reuse_prior_visuals:",
        "reuse_reason:",
        "repeat_adds_new_information:",
        "repeat_novelty_reason:",
        "advances_current_ask:",
        "advance_reason:"
    ];

    public static string NormalizePersistedTranscriptText(string? role, string text, out bool repaired) {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase)) {
            repaired = false;
            return text;
        }

        if (!TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(text, out var normalized)) {
            normalized = text;
        } else {
            repaired = true;
            return normalized;
        }

        var fullyNormalized = PrepareMessageBody(role, text);
        if (!string.Equals(fullyNormalized, text, StringComparison.Ordinal)) {
            repaired = true;
            return fullyNormalized;
        }

        repaired = false;
        return text;
    }

    public static string PrepareMessageBody(string? role, string? text) =>
        TranscriptMarkdownContract.PrepareMessageBody(NormalizeMessageBodyCore(role, text));

    public static string PrepareMessageBody(string? text) =>
        TranscriptMarkdownContract.PrepareMessageBody(NormalizeMessageBodyCore(text));

    public static string PrepareOutcomeDetailBody(string? text) =>
        TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(NormalizeMessageBodyCore(text)).Trim();

    public static string PrepareTranscriptMarkdownForExport(string? markdown) {
        return TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(NormalizeMessageBodyCore(markdown));
    }

    public static string PrepareTranscriptMarkdownForPortableExport(string? markdown) {
        return TranscriptMarkdownContract.PrepareTranscriptMarkdownForPortableExport(NormalizeMessageBodyCore(markdown));
    }

    public static string PrepareStreamingPreview(string? text) =>
        TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(
            SanitizeRuntimeOnlyArtifacts(text ?? string.Empty));

    private static string NormalizeMessageBodyCore(string? role, string? text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return string.Empty;
        }

        if (ShouldStripRuntimeOnlyArtifacts(role)) {
            value = SanitizeRuntimeOnlyArtifacts(value);
        }

        return TranscriptMarkdownNormalizer.NormalizeForRendering(value);
    }

    private static string NormalizeMessageBodyCore(string? text) {
        var value = text ?? string.Empty;
        return TranscriptMarkdownNormalizer.NormalizeForRendering(value);
    }

    private static bool ShouldStripRuntimeOnlyArtifacts(string? role) {
        var normalizedRole = (role ?? string.Empty).Trim();
        return normalizedRole.Equals("Assistant", StringComparison.OrdinalIgnoreCase)
               || normalizedRole.Equals("System", StringComparison.OrdinalIgnoreCase)
               || normalizedRole.Equals("Tools", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeRuntimeOnlyArtifacts(string text) {
        if (text.Length == 0) {
            return string.Empty;
        }

        var sanitized = StructuredMemoryFenceRegex.Replace(text, string.Empty);
        sanitized = StripAnswerPlanBlocks(sanitized);
        sanitized = RepairMermaidBlocks(sanitized);
        return sanitized.Trim();
    }

    private static string StripAnswerPlanBlocks(string text) {
        if (text.Length == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length) {
            if (!TryFindAnswerPlanBlock(text, position, out var start, out var end)) {
                builder.Append(text, position, text.Length - position);
                break;
            }

            if (start > position) {
                builder.Append(text, position, start - position);
            }

            position = Math.Max(end, start);
        }

        return builder.ToString();
    }

    private static bool TryFindAnswerPlanBlock(string text, int searchStart, out int start, out int end) {
        start = -1;
        end = -1;

        if (searchStart < 0 || searchStart >= text.Length) {
            return false;
        }

        var markerMatch = AnswerPlanMarkerRegex.Match(text, searchStart);
        if (!markerMatch.Success) {
            return false;
        }

        start = FindAnswerPlanStart(text, markerMatch.Index);
        end = FindAnswerPlanEnd(text, markerMatch.Index + markerMatch.Length);
        return end > start;
    }

    private static int FindAnswerPlanStart(string text, int markerIndex) {
        var headerIndex = text.LastIndexOf("[Answer progression plan]", markerIndex, StringComparison.OrdinalIgnoreCase);
        var escapedHeaderIndex = text.LastIndexOf(@"\[Answer progression plan\]", markerIndex, StringComparison.OrdinalIgnoreCase);
        var candidate = Math.Max(headerIndex, escapedHeaderIndex);
        if (candidate < 0) {
            return markerIndex;
        }

        return candidate;
    }

    private static int FindAnswerPlanEnd(string text, int markerEnd) {
        var afterLastKey = markerEnd;
        foreach (var key in AnswerPlanKeys) {
            var keyIndex = text.IndexOf(key, afterLastKey, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) {
                break;
            }

            afterLastKey = keyIndex + key.Length;
        }

        var nextBoundary = FindNextVisibleBoundary(text, afterLastKey);
        if (afterLastKey == markerEnd) {
            var blankLineBoundary = FindBlankLineBoundary(text, markerEnd);
            if (blankLineBoundary > markerEnd) {
                return blankLineBoundary;
            }
        }

        var recoveredVisibleTextStart = FindCollapsedVisibleTextBoundary(text, afterLastKey, nextBoundary);
        if (recoveredVisibleTextStart > afterLastKey) {
            return recoveredVisibleTextStart;
        }

        if (nextBoundary < text.Length) {
            return nextBoundary;
        }

        return text.Length;
    }

    private static int FindNextVisibleBoundary(string text, int start) {
        var blankLineBoundary = FindBlankLineBoundary(text, start);
        var nextFence = IndexOfAny(text, start, "```", "~~~");
        var nextHeading = IndexOfAny(text, start, "\n#", "\r\n#");
        var nextSystemHeading = IndexOfAny(text, start, "\n## ", "\r\n## ");
        var nextBoundary = text.Length;

        if (blankLineBoundary >= 0) {
            nextBoundary = Math.Min(nextBoundary, blankLineBoundary);
        }

        if (nextFence >= 0) {
            nextBoundary = Math.Min(nextBoundary, nextFence);
        }

        if (nextHeading >= 0) {
            nextBoundary = Math.Min(nextBoundary, nextHeading + 1);
        }

        if (nextSystemHeading >= 0) {
            nextBoundary = Math.Min(nextBoundary, nextSystemHeading + 1);
        }

        return nextBoundary;
    }

    private static int FindBlankLineBoundary(string text, int start) {
        var blankLineIndex = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        var blankLineIndexCrLf = text.IndexOf("\r\n\r\n", start, StringComparison.Ordinal);
        if (blankLineIndex < 0) {
            return blankLineIndexCrLf < 0 ? -1 : blankLineIndexCrLf + 4;
        }

        if (blankLineIndexCrLf < 0) {
            return blankLineIndex + 2;
        }

        return Math.Min(blankLineIndex + 2, blankLineIndexCrLf + 4);
    }

    private static int FindCollapsedVisibleTextBoundary(string text, int searchStart, int maxExclusive) {
        var limit = Math.Min(text.Length, Math.Max(searchStart, maxExclusive));
        for (var i = searchStart + 8; i < limit; i++) {
            if (!char.IsLetter(text[i]) || !char.IsUpper(text[i])) {
                continue;
            }

            var previous = text[i - 1];
            if (char.IsLower(previous) || char.IsDigit(previous) || previous is '.' or '!' or '?' or ')') {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfAny(string text, int startIndex, params string[] values) {
        var bestIndex = -1;
        for (var i = 0; i < values.Length; i++) {
            var candidate = text.IndexOf(values[i], startIndex, StringComparison.Ordinal);
            if (candidate < 0) {
                continue;
            }

            if (bestIndex < 0 || candidate < bestIndex) {
                bestIndex = candidate;
            }
        }

        return bestIndex;
    }

    private static string RepairMermaidBlocks(string text) {
        if (text.IndexOf("mermaid", StringComparison.OrdinalIgnoreCase) < 0) {
            return text;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length + 32);
        var inMermaid = false;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];

            if (!inMermaid && TryParseMermaidFenceStart(line, out var inlineMermaidContent)) {
                builder.AppendLine("```mermaid");
                if (inlineMermaidContent.Length > 0) {
                    builder.AppendLine(inlineMermaidContent);
                }

                inMermaid = true;
                continue;
            }

            if (!inMermaid) {
                builder.AppendLine(line);
                continue;
            }

            if (IsFenceOnlyLine(line)) {
                builder.AppendLine("```");
                inMermaid = false;
                continue;
            }

            if (TrySplitInlineFenceClose(line, out var contentBeforeClose)) {
                if (contentBeforeClose.Length > 0) {
                    AppendNormalizedMermaidLine(builder, contentBeforeClose);
                }

                builder.AppendLine("```");
                inMermaid = false;
                continue;
            }

            if (line.Trim().Length > 0 && !LooksLikeMermaidLine(line)) {
                builder.AppendLine("```");
                builder.AppendLine(line);
                inMermaid = false;
                continue;
            }

            AppendNormalizedMermaidLine(builder, line);
        }

        if (inMermaid) {
            builder.AppendLine("```");
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static void AppendNormalizedMermaidLine(StringBuilder builder, string line) {
        if (TrySplitCompactMermaidDirectiveLine(line, out var headerLine, out var contentLine)) {
            builder.AppendLine(headerLine);
            AppendNormalizedMermaidLine(builder, contentLine);
            return;
        }

        if (TrySplitCollapsedMermaidEndLine(line, out var endLine, out var remainderLine)) {
            builder.AppendLine(endLine);
            AppendNormalizedMermaidLine(builder, remainderLine);
            return;
        }

        if (TrySplitCollapsedMermaidEdgeStatements(line, out var firstStatement, out var remainingStatements)) {
            builder.AppendLine(firstStatement);
            AppendNormalizedMermaidLine(builder, remainingStatements);
            return;
        }

        builder.AppendLine(line);
    }

    private static bool TryParseMermaidFenceStart(string line, out string inlineMermaidContent) {
        inlineMermaidContent = string.Empty;

        var trimmedStart = line.TrimStart();
        if (!trimmedStart.StartsWith("```", StringComparison.Ordinal)
            && !trimmedStart.StartsWith("~~~", StringComparison.Ordinal)) {
            return false;
        }

        var afterFence = trimmedStart[3..].TrimStart();
        if (!afterFence.StartsWith("mermaid", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        inlineMermaidContent = afterFence["mermaid".Length..].TrimStart();
        return true;
    }

    private static bool IsFenceOnlyLine(string line) {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && (trimmed == "```" || trimmed == "~~~");
    }

    private static bool TrySplitInlineFenceClose(string line, out string contentBeforeClose) {
        contentBeforeClose = string.Empty;
        var trimmedEnd = line.TrimEnd();
        if (trimmedEnd.Length <= 3) {
            return false;
        }

        if (!trimmedEnd.EndsWith("```", StringComparison.Ordinal)
            && !trimmedEnd.EndsWith("~~~", StringComparison.Ordinal)) {
            return false;
        }

        contentBeforeClose = trimmedEnd[..^3].TrimEnd();
        return true;
    }

    private static bool LooksLikeMermaidLine(string line) {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) {
            return true;
        }

        if (trimmed.StartsWith("%%", StringComparison.Ordinal)
            || trimmed.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("graph", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("journey", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("pie", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("timeline", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("gitGraph", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("xychart-beta", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("sankey", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("architecture", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("block", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("packet", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("radar", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("kanban", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("treemap", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("subgraph", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("direction", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("style ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("classDef", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("class ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("linkStyle", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("click ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("accTitle", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("accDescr", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("end", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return trimmed.Contains("-->", StringComparison.Ordinal)
               || trimmed.Contains("---", StringComparison.Ordinal)
               || trimmed.Contains("-.->", StringComparison.Ordinal)
               || trimmed.Contains("==>", StringComparison.Ordinal)
               || trimmed.Contains("@{", StringComparison.Ordinal)
               || trimmed.Contains('[')
               || trimmed.Contains('{')
               || trimmed.Contains('(');
    }

    private static bool TrySplitCompactMermaidDirectiveLine(string line, out string headerLine, out string contentLine) {
        headerLine = string.Empty;
        contentLine = string.Empty;

        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var leadingWhitespaceLength = line.Length - line.TrimStart().Length;
        var leadingWhitespace = leadingWhitespaceLength > 0 ? line[..leadingWhitespaceLength] : string.Empty;
        var trimmed = line.TrimStart();

        if (!TrySplitCompactMermaidDirectiveCore(trimmed, "flowchart", out var directive, out var remainder)
            && !TrySplitCompactMermaidDirectiveCore(trimmed, "graph", out directive, out remainder)) {
            return false;
        }

        headerLine = leadingWhitespace + directive;
        contentLine = leadingWhitespace + remainder;
        return true;
    }

    private static bool TrySplitCompactMermaidDirectiveCore(
        string line,
        string keyword,
        out string directive,
        out string remainder) {
        directive = string.Empty;
        remainder = string.Empty;

        if (!line.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var index = keyword.Length;
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        var directionStart = index;
        while (index < line.Length && char.IsLetter(line[index])) {
            index++;
        }

        if (index <= directionStart || index >= line.Length || !char.IsWhiteSpace(line[index])) {
            return false;
        }

        var direction = line[directionStart..index];
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        if (index >= line.Length) {
            return false;
        }

        directive = keyword + " " + direction;
        remainder = line[index..];
        return true;
    }

    private static bool TrySplitCollapsedMermaidEndLine(string line, out string endLine, out string remainderLine) {
        endLine = string.Empty;
        remainderLine = string.Empty;

        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var leadingWhitespaceLength = line.Length - line.TrimStart().Length;
        var leadingWhitespace = leadingWhitespaceLength > 0 ? line[..leadingWhitespaceLength] : string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("end ", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var remainder = trimmed["end".Length..].TrimStart();
        if (remainder.Length == 0) {
            return false;
        }

        endLine = leadingWhitespace + "end";
        remainderLine = leadingWhitespace + remainder;
        return true;
    }

    private static bool TrySplitCollapsedMermaidEdgeStatements(
        string line,
        out string firstStatement,
        out string remainingStatements) {
        firstStatement = string.Empty;
        remainingStatements = string.Empty;

        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var leadingWhitespaceLength = line.Length - line.TrimStart().Length;
        var leadingWhitespace = leadingWhitespaceLength > 0 ? line[..leadingWhitespaceLength] : string.Empty;
        var trimmed = line.TrimStart();
        var matches = MermaidEdgeStatementStartRegex.Matches(trimmed);
        if (matches.Count < 2) {
            return false;
        }

        var splitIndex = matches[1].Index;
        if (splitIndex <= 0 || splitIndex >= trimmed.Length) {
            return false;
        }

        var first = trimmed[..splitIndex].TrimEnd();
        var remainder = trimmed[splitIndex..].TrimStart();
        if (first.Length == 0 || remainder.Length == 0) {
            return false;
        }

        firstStatement = leadingWhitespace + first;
        remainingStatements = leadingWhitespace + remainder;
        return true;
    }
}
