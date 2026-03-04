using System;
using System.Collections.Generic;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private const string ToolReceiptCorrectionMarker = "ix:tool-receipt-correction:v1";
    private const string ToolReceiptNoExecutionDisclosure = "Tool receipt: no tools were run in this turn.";
    private const int ToolReceiptCorrectionMaxUserRequestChars = 2000;
    private const int ToolReceiptCorrectionMaxDraftChars = 3200;

    private static bool ShouldAttemptToolReceiptCorrection(string userRequest, string assistantDraft, IReadOnlyList<ToolDefinition> tools, int priorToolCalls,
        int priorToolOutputs, int assistantDraftToolCalls) {
        if (priorToolCalls > 0 || priorToolOutputs > 0 || assistantDraftToolCalls > 0) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > ToolReceiptCorrectionMaxDraftChars) {
            return false;
        }

        // Guard against feedback loops where the assistant echoes correction prompts.
        if (draft.Contains(ToolReceiptCorrectionMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionCorrectionMarker, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (tools is null || tools.Count == 0) {
            return false;
        }

        return DraftBindsToolNameToResults(draft, tools);
    }

    private static bool DraftBindsToolNameToResults(string assistantDraft, IReadOnlyList<ToolDefinition> tools) {
        // Bound work: tool lists can be large when routing is disabled; keep this per-turn check stable.
        var limit = Math.Min(tools.Count, 64);
        for (var i = 0; i < limit; i++) {
            var name = (tools[i]?.Name ?? string.Empty).Trim();
            if (name.Length < 3) {
                continue;
            }

            var startIndex = 0;
            while (true) {
                var idx = assistantDraft.IndexOf(name, startIndex, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) {
                    break;
                }

                if (HasWordBoundaries(assistantDraft, idx, name.Length)
                    && LooksLikeToolResultContext(assistantDraft, idx, name.Length)) {
                    return true;
                }

                startIndex = idx + 1;
                if (startIndex >= assistantDraft.Length) {
                    break;
                }
            }
        }

        return false;
    }

    private static bool DraftMentionsKnownToolName(string assistantDraft, IReadOnlyList<ToolDefinition> tools) {
        var limit = Math.Min(tools.Count, 64);
        for (var i = 0; i < limit; i++) {
            var name = (tools[i]?.Name ?? string.Empty).Trim();
            if (name.Length < 3) {
                continue;
            }

            var startIndex = 0;
            while (true) {
                var idx = assistantDraft.IndexOf(name, startIndex, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) {
                    break;
                }

                if (HasWordBoundaries(assistantDraft, idx, name.Length)) {
                    return true;
                }

                startIndex = idx + 1;
                if (startIndex >= assistantDraft.Length) {
                    break;
                }
            }
        }

        return false;
    }

    private static bool HasWordBoundaries(string text, int index, int length) {
        if (index < 0 || length <= 0 || index + length > text.Length) {
            return false;
        }

        var beforeOk = index == 0 || !IsToolNameChar(text[index - 1]);
        var afterIndex = index + length;
        var afterOk = afterIndex >= text.Length || !IsToolNameChar(text[afterIndex]);
        return beforeOk && afterOk;
    }

    private static bool IsToolNameChar(char ch) {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.';
    }

    private static bool LooksLikeToolResultContext(string text, int toolNameIndex, int toolNameLength) {
        var afterToolName = toolNameIndex + toolNameLength;
        if (afterToolName < 0 || afterToolName > text.Length) {
            return false;
        }

        // Language-neutral: only trust structural receipts (tool name directly bound to a JSON-like payload).
        // Example: "eventlog_live_query: { ... }" or "eventlog_live_query: [ ... ]".
        var scan = afterToolName;
        var consumedWhitespace = 0;
        while (scan < text.Length && consumedWhitespace < 3 && char.IsWhiteSpace(text[scan])) {
            scan++;
            consumedWhitespace++;
        }

        if (scan < text.Length && text[scan] == ':') {
            var look = scan + 1;
            var maxLook = Math.Min(text.Length, look + 72);
            while (look < maxLook && char.IsWhiteSpace(text[look])) {
                look++;
            }

            if (look < maxLook) {
                var ch = text[look];
                // JSON-ish payloads are strong binding signals; avoid triggering on common numeric prose like "port: 80".
                if (ch == '{' || ch == '[') {
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildToolReceiptCorrectionPrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        return $$"""
            [Tool receipt correction]
            {{ToolReceiptCorrectionMarker}}
            The previous assistant draft implied that tools were executed, but this turn has zero tool calls and zero tool outputs.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Revise the response:
            - Do not claim that you ran/called/executed tools unless you actually call them via tool-calling.
            - If tools are needed, call them now.
            - If tools are not needed, answer normally but explicitly state that no tools were run in this turn.
            """;
    }

    private static string TrimForPrompt(string? text, int maxChars) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        // Use Span-based trimming to avoid allocating a large intermediate string (this can be hit on correction
        // paths when the assistant draft is big).
        var span = text.AsSpan();
        var start = 0;
        var end = span.Length;
        while (start < end && char.IsWhiteSpace(span[start])) {
            start++;
        }
        while (end > start && char.IsWhiteSpace(span[end - 1])) {
            end--;
        }

        span = span.Slice(start, end - start);
        if (span.Length == 0) {
            return string.Empty;
        }

        if (maxChars <= 0 || span.Length <= maxChars) {
            return span.ToString();
        }

        return span.Slice(0, maxChars).ToString() + "\n(truncated)";
    }

    private static string AppendNoToolExecutionDisclosureIfNeeded(
        string assistantDraft,
        IReadOnlyList<ToolDefinition> tools,
        int priorToolCalls,
        int priorToolOutputs) {
        var draft = (assistantDraft ?? string.Empty).TrimEnd();
        if (draft.Length == 0 || priorToolCalls > 0 || priorToolOutputs > 0) {
            return draft;
        }

        if (draft.Contains(ToolReceiptNoExecutionDisclosure, StringComparison.OrdinalIgnoreCase)) {
            return draft;
        }

        if (tools is null || tools.Count == 0 || !DraftMentionsKnownToolName(draft, tools)) {
            return draft;
        }

        return draft + Environment.NewLine + Environment.NewLine + ToolReceiptNoExecutionDisclosure;
    }
}
