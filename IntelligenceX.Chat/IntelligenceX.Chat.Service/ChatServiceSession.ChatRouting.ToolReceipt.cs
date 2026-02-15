using System;
using System.Collections.Generic;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private const string ToolReceiptCorrectionMarker = "ix:tool-receipt-correction:v1";
    private const int ToolReceiptCorrectionMaxUserRequestChars = 2000;
    private const int ToolReceiptCorrectionMaxDraftChars = 3200;

    private static bool ShouldAttemptToolReceiptCorrection(string userRequest, string assistantDraft, IReadOnlyList<ToolDefinition> tools, int priorToolCalls,
        int priorToolOutputs, int assistantDraftToolCalls) {
        if (priorToolCalls > 0 || priorToolOutputs > 0 || assistantDraftToolCalls > 0) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > 3200) {
            return false;
        }

        // Guard against feedback loops where the assistant echoes correction prompts.
        if (draft.Contains(ToolReceiptCorrectionMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionCorrectionMarker, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // High-signal receipt fragments that should not appear unless we actually ran tools.
        if (LooksLikeToolReceiptOutput(draft)) {
            return true;
        }

        if (tools is null || tools.Count == 0) {
            return false;
        }

        return DraftBindsToolNameToResults(draft, tools);
    }

    private static bool LooksLikeToolReceiptOutput(string text) {
        return text.IndexOf("exit code", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("exited with code", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("stdout", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("stderr", StringComparison.OrdinalIgnoreCase) >= 0;
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
        var left = Math.Max(0, toolNameIndex - 80);
        var rightExclusive = Math.Min(text.Length, toolNameIndex + toolNameLength + 120);
        var windowLength = rightExclusive - left;
        if (windowLength <= 0) {
            return false;
        }

        // Minimal claim cues (avoid broad language heuristics).
        if (text.IndexOf("returned", left, windowLength, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        if (text.IndexOf("output", left, windowLength, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        if (text.IndexOf("I ran", left, windowLength, StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("we ran", left, windowLength, StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("I called", left, windowLength, StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("we called", left, windowLength, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        // "tool_name: { ... }" or "tool_name: [ ... ]" is a strong result binding signal.
        var after = toolNameIndex + toolNameLength;
        var scan = after;
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
                if (ch == '{' || ch == '[' || char.IsDigit(ch)) {
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
            return "(empty)";
        }

        var trimmed = text.Trim();
        if (maxChars <= 0 || trimmed.Length <= maxChars) {
            return trimmed;
        }

        return trimmed.Substring(0, maxChars) + "\n(truncated)";
    }
}
