using System;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private bool TryGetPartialTurnFailureNotice(ConversationRuntime conversation, AssistantTurnOutcome outcome, out string notice) {
        notice = string.Empty;
        if (!_assistantStreamingState.HasReceivedDelta()) {
            return false;
        }

        if (!TryGetLastAssistantText(conversation, out var assistantText)) {
            return false;
        }

        var normalizedAssistant = (assistantText ?? string.Empty).Trim();
        if (normalizedAssistant.Length == 0 || StartsWithOutcomeMarker(normalizedAssistant)) {
            return false;
        }

        notice = BuildPartialTurnFailureNoticeText(outcome);
        _assistantStreamingState.ClearReceivedDelta();
        return true;
    }

    internal static string BuildPartialTurnFailureNoticeText(AssistantTurnOutcome outcome) {
        return outcome.Kind switch {
            AssistantTurnOutcomeKind.ToolRoundLimit =>
                "Partial response shown above. The turn hit the tool safety limit before completion. "
                + "Reply naturally to proceed, or narrow scope (one DC / one OU).",
            AssistantTurnOutcomeKind.UsageLimit =>
                "Partial response shown above. The turn then hit your account usage limit. "
                + "Switch account or try again later.",
            AssistantTurnOutcomeKind.Canceled =>
                "Partial response shown above. Turn was canceled before completion.",
            AssistantTurnOutcomeKind.Disconnected =>
                "Partial response shown above. Connection dropped before the turn could finish.",
            AssistantTurnOutcomeKind.Error =>
                BuildPartialTurnErrorNoticeText(outcome.Detail),
            _ =>
                "Partial response shown above. The turn ended before completion."
        };
    }

    private static string BuildPartialTurnErrorNoticeText(string? detail) {
        var summary = NormalizePartialTurnFailureDetail(detail, maxChars: 220);
        var code = TryExtractPartialTurnFailureCode(detail);
        if (code.Length == 0 && summary.Length == 0) {
            return "Partial response shown above. The turn ended before completion.";
        }

        if (code.Length == 0) {
            return "Partial response shown above. The turn ended before completion. " + summary;
        }

        if (summary.Length == 0) {
            return "Partial response shown above. The turn ended before completion (" + code + ").";
        }

        var suffix = "(" + code + ")";
        if (summary.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
            summary = summary[..^suffix.Length].TrimEnd(' ', '.', ':', ';', '-', ',');
        }

        if (summary.Length == 0) {
            return "Partial response shown above. The turn ended before completion (" + code + ").";
        }

        return "Partial response shown above. The turn ended before completion (" + code + "). " + summary;
    }

    private static string NormalizePartialTurnFailureDetail(string? detail, int maxChars) {
        var text = (detail ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        var firstLineEnd = text.IndexOfAny(new[] { '\r', '\n' });
        if (firstLineEnd >= 0) {
            text = text[..firstLineEnd].Trim();
        }

        if (text.Length <= maxChars) {
            return text;
        }

        return text[..maxChars].TrimEnd() + "...";
    }

    private static string TryExtractPartialTurnFailureCode(string? detail) {
        var text = (detail ?? string.Empty).Trim();
        if (text.Length == 0) {
            return string.Empty;
        }

        var close = text.LastIndexOf(')');
        if (close == text.Length - 1) {
            var open = text.LastIndexOf('(', close);
            if (open >= 0 && open + 1 < close) {
                var candidate = text[(open + 1)..close].Trim();
                if (LooksLikePartialTurnFailureCode(candidate)) {
                    return candidate;
                }
            }
        }

        const string marker = "reason code:";
        var markerIndex = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) {
            return string.Empty;
        }

        var value = text[(markerIndex + marker.Length)..].Trim();
        var lineEnd = value.IndexOfAny(new[] { '\r', '\n', '.', ';', ',', ' ' });
        if (lineEnd > 0) {
            value = value[..lineEnd].Trim();
        }

        return LooksLikePartialTurnFailureCode(value) ? value : string.Empty;
    }

    private static bool LooksLikePartialTurnFailureCode(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 3 or > 80) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.') {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryGetLastAssistantText(ConversationRuntime conversation, out string text) {
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            var entry = conversation.Messages[i];
            if (!string.Equals(entry.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            text = entry.Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    internal static bool ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
        bool activeTurnReceivedDelta,
        string? finalAssistantText,
        string? streamedAssistantText,
        out string notice) {
        notice = string.Empty;
        if (!activeTurnReceivedDelta) {
            return false;
        }

        var finalText = (finalAssistantText ?? string.Empty).Trim();
        if (!IsNoTextWarningText(finalText)) {
            return false;
        }

        var streamed = (streamedAssistantText ?? string.Empty).Trim();
        if (streamed.Length == 0 || StartsWithOutcomeMarker(streamed) || IsNoTextWarningText(streamed)) {
            return false;
        }

        notice = "Runtime warning: no final response envelope was produced. Kept the partial streamed response shown above.";
        return true;
    }

    internal static bool IsNoTextWarningText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.StartsWith("[warning] No response text was produced", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldAppendFinalAssistantAfterInterim(string? finalAssistantText, string? interimAssistantText) {
        var finalText = NormalizeAssistantSnapshotForAppendDecision(finalAssistantText);
        if (finalText.Length == 0) {
            return false;
        }

        var interimText = NormalizeAssistantSnapshotForAppendDecision(interimAssistantText);
        if (interimText.Length == 0) {
            return true;
        }

        if (string.Equals(finalText, interimText, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (AreNearDuplicateAssistantSnapshots(finalText, interimText)) {
            return false;
        }

        return true;
    }

    internal static bool ShouldSuppressConsecutiveAssistantDuplicate(string? candidateAssistantText, string? previousAssistantText) {
        var candidate = NormalizeAssistantSnapshotForAppendDecision(candidateAssistantText);
        if (candidate.Length == 0) {
            return false;
        }

        var previous = NormalizeAssistantSnapshotForAppendDecision(previousAssistantText);
        if (previous.Length == 0) {
            return false;
        }

        if (string.Equals(candidate, previous, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (AreNearDuplicateAssistantSnapshots(candidate, previous)
            || AreNearDuplicateAssistantSnapshots(previous, candidate)) {
            return true;
        }

        return false;
    }

    internal static bool ShouldAppendAssistantSnapshot(string? candidateAssistantText, string? previousRole, string? previousAssistantText) {
        if (NormalizeAssistantSnapshotForAppendDecision(candidateAssistantText).Length == 0) {
            return false;
        }

        if (!string.Equals((previousRole ?? string.Empty).Trim(), "Assistant", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return !ShouldSuppressConsecutiveAssistantDuplicate(
            candidateAssistantText: candidateAssistantText,
            previousAssistantText: previousAssistantText);
    }

    internal static bool ShouldRenderFinalAssistantAsSeparateBubbleAfterInterim() {
        return false;
    }

    internal static bool ShouldAppendFinalAssistantAfterStreamedDraft(
        bool activeTurnReceivedDelta,
        bool activeTurnInterimResultSeen,
        string? finalAssistantText,
        string? streamedAssistantText) {
        if (!activeTurnReceivedDelta || activeTurnInterimResultSeen) {
            return false;
        }

        var normalizedStreamedDraft = NormalizeAssistantSnapshotForAppendDecision(streamedAssistantText);
        if (normalizedStreamedDraft.Length == 0) {
            // Guardrail: if this turn reports delta activity but no usable draft text is present,
            // preserve final-message append behavior so we do not risk clobbering prior assistant rows.
            return NormalizeAssistantSnapshotForAppendDecision(finalAssistantText).Length > 0;
        }

        // Streamed draft bubbles should converge into a single finalized assistant entry.
        // Appending a second "final" bubble after streaming causes near-duplicate transcript
        // rows and confusing exports; finalize by replacing the draft instead.
        return false;
    }

    private static string NormalizeAssistantSnapshotForAppendDecision(string? text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var normalized = new System.Text.StringBuilder(value.Length);
        var previousSpace = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsWhiteSpace(ch)) {
                if (!previousSpace) {
                    normalized.Append(' ');
                    previousSpace = true;
                }
                continue;
            }

            previousSpace = false;
            normalized.Append(ch);
        }

        var compact = normalized.ToString().Trim();
        while (compact.Length > 0) {
            var tail = compact[^1];
            if (IsAssistantSnapshotTerminalPunctuation(tail)) {
                compact = compact[..^1].TrimEnd();
                continue;
            }

            break;
        }

        return compact;
    }

    private static bool IsAssistantSnapshotTerminalPunctuation(char value) {
        return value is '.'
            or '!'
            or '?'
            or ':'
            or ';'
            or ','
            or '\u3002' // 。
            or '\uFF01' // ！
            or '\uFF1F' // ？
            or '\uFF1A' // ：
            or '\uFF1B' // ；
            or '\uFF0C' // ，
            or '\u061F' // ؟
            or '\u060C'; // ،
    }

    private static bool AreNearDuplicateAssistantSnapshots(string finalText, string interimText) {
        if (finalText.Length == 0 || interimText.Length == 0) {
            return false;
        }

        if (finalText.StartsWith(interimText, StringComparison.OrdinalIgnoreCase)) {
            return finalText.Length - interimText.Length <= InterimFinalNearDuplicateSuffixThresholdChars;
        }

        if (interimText.StartsWith(finalText, StringComparison.OrdinalIgnoreCase)) {
            return interimText.Length - finalText.Length <= InterimFinalNearDuplicateSuffixThresholdChars;
        }

        return false;
    }

    private static string CollapseRepeatedExecutionContractBlockers(ConversationRuntime conversation, string assistantText) {
        if (!TryParseExecutionContractBlocker(assistantText, out var reasonCode, out var actionId)) {
            return assistantText;
        }

        if (!TryFindRecentExecutionContractBlocker(
                conversation,
                currentAssistantText: assistantText,
                actionIdHint: actionId,
                reasonHint: reasonCode,
                previousReasonCode: out var previousReasonCode,
                previousActionId: out var previousActionId)) {
            return assistantText;
        }

        if (actionId.Length == 0) {
            actionId = previousActionId;
        }

        if (reasonCode.Length == 0) {
            reasonCode = previousReasonCode;
        }
        if (reasonCode.Length == 0) {
            reasonCode = "no_tool_calls_after_retries";
        }

        var actionHint = actionId.Length > 0 ? $"Action: /act {actionId}" + Environment.NewLine + Environment.NewLine : string.Empty;
        return $$"""
            [Execution blocked]
            {{ExecutionContractMarker}}
            Still blocked; no new tool output was produced in this retry.

            {{actionHint}}Reason code: {{reasonCode}}

            Retry with narrower scope (single DC/domain), or use Stop if this turn is looping without new tool output.
            """;
    }

    private static bool TryFindRecentExecutionContractBlocker(ConversationRuntime conversation, string? currentAssistantText, string? actionIdHint, string? reasonHint,
        out string previousReasonCode, out string previousActionId) {
        previousReasonCode = string.Empty;
        previousActionId = string.Empty;

        var normalizedCurrentAssistantText = NormalizeExecutionContractBlockerText(currentAssistantText);
        var normalizedActionHint = (actionIdHint ?? string.Empty).Trim();
        var normalizedReasonHint = (reasonHint ?? string.Empty).Trim();

        var scanned = 0;
        var skippedCurrentBlocker = false;
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            var entry = conversation.Messages[i];
            if (!string.Equals(entry.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            scanned++;
            if (scanned > MaxExecutionContractHistoryScan) {
                break;
            }

            if (!TryParseExecutionContractBlocker(entry.Text, out var candidateReason, out var candidateAction)) {
                continue;
            }

            var normalizedCandidateAction = (candidateAction ?? string.Empty).Trim();
            var normalizedCandidateReason = (candidateReason ?? string.Empty).Trim();
            if (!skippedCurrentBlocker && normalizedCurrentAssistantText.Length > 0) {
                var normalizedEntryText = NormalizeExecutionContractBlockerText(entry.Text);
                if (normalizedEntryText.Length > 0
                    && string.Equals(normalizedEntryText, normalizedCurrentAssistantText, StringComparison.OrdinalIgnoreCase)) {
                    skippedCurrentBlocker = true;
                    continue;
                }
            }

            if (normalizedActionHint.Length > 0) {
                if (!string.Equals(normalizedActionHint, normalizedCandidateAction, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            } else if (normalizedReasonHint.Length > 0 && normalizedCandidateAction.Length == 0) {
                if (!string.Equals(normalizedReasonHint, normalizedCandidateReason, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            }

            previousReasonCode = normalizedCandidateReason;
            previousActionId = normalizedCandidateAction;
            return true;
        }

        return false;
    }

    private static string NormalizeExecutionContractBlockerText(string? text) {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static bool TryParseExecutionContractBlocker(string text, out string reasonCode, out string actionId) {
        reasonCode = string.Empty;
        actionId = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.IndexOf(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        var lines = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (line.StartsWith("Reason code:", StringComparison.OrdinalIgnoreCase)) {
                reasonCode = line["Reason code:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("id:", StringComparison.OrdinalIgnoreCase)) {
                actionId = line["id:".Length..].Trim();
            }
        }

        return true;
    }

    private static bool StartsWithOutcomeMarker(string text) {
        return text.StartsWith("[error]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[warning]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[limit]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[canceled]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[execution blocked]", StringComparison.OrdinalIgnoreCase);
    }

}
