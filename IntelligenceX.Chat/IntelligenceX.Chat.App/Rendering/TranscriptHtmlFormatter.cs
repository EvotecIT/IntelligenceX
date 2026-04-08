using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.App.Markdown;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Renders transcript messages into chat-shell HTML.
/// </summary>
internal static class TranscriptHtmlFormatter {
    private const int MaxAssistantTurnTraceEntries = 8;
    private const string AssistantDraftBadgeText = "Draft/Thinking";
    private const string AssistantToolBadgeText = "Tool Activity";
    private const string ExecutionContractMarker = "ix:execution-contract:v1";
    private const string CopyButtonIconSvg =
        "<svg width='14' height='14' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><rect x='9' y='9' width='13' height='13' rx='2'/><path d='M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1'/></svg>";
    private static readonly Regex AssistantOutcomePrefixRegex = new(
        @"^\[(?<kind>[a-zA-Z0-9 _-]+)\](?:[ \t]+(?<headline>[^\r\n]*))?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PendingActionLineRegex = new(
        @"^\s*(?<index>\d+)\.\s+(?<label>.+?)\s+\((?:`)?(?<command>/act\s+(?<id>[^\s)`]+))(?:`)?\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PendingActionHeadingRegex = new(
        @"^\s*You can run one of these follow-up actions:\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineCodeFallbackRegex = new(
        @"(?<!`)`(?<code>[^`\r\n]+?)`(?!`)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PreBlockRegex = new(
        @"<pre\b[\s\S]*?</pre>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StandaloneHashParagraphBeforeHeadingRegex = new(
        @"<p>\s*#\s*</p>\s*(?=<h[1-6]\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Backward-compatible overload for transcript entries without model metadata.
    /// </summary>
    public static string Format(
        IEnumerable<(string Role, string Text, DateTime Time)> messages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        ArgumentNullException.ThrowIfNull(messages);
        return Format(
            ProjectLegacyMessages(messages),
            timestampFormat,
            markdownOptions,
            messageDecorations: null,
            showAssistantTurnTrace: true,
            showAssistantDraftBubbles: true);
    }

    /// <summary>
    /// Renders a single message body for export without rebuilding the full chat-shell row chrome.
    /// </summary>
    public static string FormatSingleMessageForExport(
        string role,
        string text,
        MarkdownRendererOptions markdownOptions) {
        ArgumentNullException.ThrowIfNull(markdownOptions);

        var normalizedText = TranscriptMarkdownPreparation.PrepareMessageBody(role, text);
        if (string.IsNullOrWhiteSpace(normalizedText)) {
            return string.Empty;
        }

        if (TryRenderOutcomeCallout(role, normalizedText, markdownOptions, out var calloutHtml)) {
            return calloutHtml;
        }

        var actionExtraction = string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase)
            ? ExtractPendingActionsForRendering(normalizedText)
            : new PendingActionExtraction(normalizedText, Array.Empty<PendingActionRenderItem>());
        var bodyHtml = string.IsNullOrWhiteSpace(actionExtraction.CleanedText)
            ? string.Empty
            : RenderBodyHtml(actionExtraction.CleanedText, markdownOptions);
        if (actionExtraction.Actions.Count > 0) {
            bodyHtml = AppendPendingActionChips(bodyHtml, actionExtraction.Actions);
        }

        return bodyHtml;
    }

    /// <summary>
    /// Builds transcript HTML for the chat shell.
    /// </summary>
    /// <param name="messages">Role/text/time transcript entries.</param>
    /// <param name="timestampFormat">Timestamp format.</param>
    /// <param name="markdownOptions">Markdown renderer options.</param>
    /// <param name="messageDecorations">Optional per-message decorations keyed by transcript index.</param>
    /// <param name="showAssistantTurnTrace">When true, renders assistant turn trace details under assistant bubbles.</param>
    /// <param name="showAssistantDraftBubbles">When false, hides provisional assistant draft bubbles from the transcript.</param>
    /// <returns>HTML fragment.</returns>
    public static string Format(
        IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions,
        IReadOnlyDictionary<int, TranscriptMessageDecoration>? messageDecorations = null,
        bool showAssistantTurnTrace = true,
        bool showAssistantDraftBubbles = true) {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(markdownOptions);

        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat;
        var encoder = HtmlEncoder.Default;
        var html = new StringBuilder();
        string? previousRoleClass = null;
        var messageIndex = 0;

        foreach (var message in messages) {
            var normalizedText = TranscriptMarkdownPreparation.PrepareMessageBody(message.Role, message.Text);
            if (string.IsNullOrWhiteSpace(normalizedText)) {
                messageIndex++;
                continue;
            }

            var role = ResolveRoleStyle(message.Role);
            var actionExtraction = string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                ? ExtractPendingActionsForRendering(normalizedText)
                : new PendingActionExtraction(normalizedText, Array.Empty<PendingActionRenderItem>());
            var bodyHtml = string.IsNullOrWhiteSpace(actionExtraction.CleanedText)
                ? string.Empty
                : RenderBodyHtml(actionExtraction.CleanedText, markdownOptions);
            TranscriptMessageDecoration? decoration = null;
            messageDecorations?.TryGetValue(messageIndex, out decoration);
            var isAssistantDraft = decoration is not null
                                   && decoration.IsProvisional
                                   && string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase);
            var assistantChannel = decoration?.Channel ?? AssistantBubbleChannelKind.Final;
            if (isAssistantDraft && assistantChannel == AssistantBubbleChannelKind.Final) {
                assistantChannel = AssistantBubbleChannelKind.DraftThinking;
            }
            var isAssistantToolActivity = assistantChannel == AssistantBubbleChannelKind.ToolActivity
                                          && string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase);
            var hideContinuationMeta = !string.Equals(role.RoleClass, "system", StringComparison.Ordinal)
                                       && !isAssistantDraft
                                       && !isAssistantToolActivity;
            var isContinuation = hideContinuationMeta
                && string.Equals(previousRoleClass, role.RoleClass, StringComparison.Ordinal);
            if (!showAssistantDraftBubbles && isAssistantDraft) {
                messageIndex++;
                continue;
            }
            if (actionExtraction.Actions.Count > 0) {
                bodyHtml = AppendPendingActionChips(bodyHtml, actionExtraction.Actions);
            }
            var bubbleClass = "bubble";
            if (isAssistantDraft) {
                bubbleClass += " bubble-provisional";
            }
            if (isAssistantToolActivity) {
                bubbleClass += " bubble-tool-activity";
            }
            if (TryRenderOutcomeCallout(message.Role, normalizedText, markdownOptions, out var calloutHtml)) {
                bodyHtml = calloutHtml;
                bubbleClass = "bubble bubble-callout";
            }
            var time = message.Time.ToString(format, CultureInfo.InvariantCulture);
            var modelLabel = BuildModelBadgeLabel(message.Role, message.Model);
            var showModelBadge = modelLabel.Length > 0;

            html.Append("<div class='msg-row ").Append(role.RoleClass);
            if (isAssistantDraft) {
                html.Append(" assistant-draft");
            }
            if (isAssistantToolActivity) {
                html.Append(" assistant-tool-activity");
            }
            if (isContinuation) {
                html.Append(" cont");
            }
            html.Append("'>")
                .Append("<div class='avatar'>").Append(encoder.Encode(role.Avatar)).Append("</div>")
                .Append("<div class='msg'>")
                .Append("<div class='meta");
            if (isContinuation) {
                html.Append(" hidden");
            }
            html.Append("'>").Append(encoder.Encode(role.DisplayName)).Append(" &middot; ").Append(encoder.Encode(time));
            if (isAssistantDraft) {
                html.Append(" <span class='assistant-draft-meta-pill'>").Append(encoder.Encode(AssistantDraftBadgeText)).Append("</span>");
            } else if (isAssistantToolActivity) {
                html.Append(" <span class='assistant-tool-meta-pill'>").Append(encoder.Encode(AssistantToolBadgeText)).Append("</span>");
            }
            html.Append("</div>")
                .Append("<div class='").Append(bubbleClass).Append("'>").Append(bodyHtml).Append("</div>");
            if (showModelBadge) {
                html.Append("<div class='bubble-meta'><span class='bubble-model-chip' title='Model used for this response'>")
                    .Append(encoder.Encode(modelLabel))
                    .Append("</span></div>");
            }
            if (showAssistantTurnTrace && TryBuildAssistantTurnTraceHtml(message.Role, decoration, out var traceHtml)) {
                html.Append(traceHtml);
            }
            html
                .Append("<div class='msg-actions'><button class='msg-copy-btn' data-msg-index='").Append(messageIndex).Append("' title='Copy message'>")
                .Append(CopyButtonIconSvg)
                .Append("</button></div>")
                .Append("</div>")
                .AppendLine("</div>");

            previousRoleClass = role.RoleClass;
            messageIndex++;
        }

        return html.ToString();
    }

    private static bool TryBuildAssistantTurnTraceHtml(string role, TranscriptMessageDecoration? decoration, out string html) {
        html = string.Empty;
        if (!string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase) || decoration is null) {
            return false;
        }

        var timeline = BuildAssistantTraceTimelineForRendering(decoration.Timeline);
        var hasTimeline = timeline.Count > 0;
        if (!decoration.IsProvisional && !hasTimeline) {
            return false;
        }

        var encoder = HtmlEncoder.Default;
        var channel = decoration.Channel;
        if (decoration.IsProvisional && channel == AssistantBubbleChannelKind.Final) {
            channel = AssistantBubbleChannelKind.DraftThinking;
        }
        var summaryLabel = channel switch {
            AssistantBubbleChannelKind.DraftThinking => "Draft trace",
            AssistantBubbleChannelKind.ToolActivity => "Tool trace",
            _ => hasTimeline ? "Turn trace" : "Live stream"
        };
        var countLabel = hasTimeline ? timeline.Count.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var detailsOpen = channel is AssistantBubbleChannelKind.DraftThinking or AssistantBubbleChannelKind.ToolActivity
            ? " open"
            : string.Empty;
        var sb = new StringBuilder();
        sb.Append("<details class='assistant-turn-trace'").Append(detailsOpen).Append(">")
            .Append("<summary class='assistant-turn-trace-summary'>");
        if (channel is AssistantBubbleChannelKind.DraftThinking or AssistantBubbleChannelKind.ToolActivity) {
            var liveLabel = channel == AssistantBubbleChannelKind.ToolActivity ? "Tool" : "Live";
            sb.Append("<span class='assistant-turn-live-pill'>").Append(encoder.Encode(liveLabel)).Append("</span>");
        }
        sb.Append("<span class='assistant-turn-trace-title'>").Append(encoder.Encode(summaryLabel)).Append("</span>");
        if (countLabel.Length > 0) {
            sb.Append("<span class='assistant-turn-trace-count'>").Append(encoder.Encode(countLabel)).Append("</span>");
        }
        sb.Append("</summary>");

        if (hasTimeline) {
            sb.Append("<ol class='assistant-turn-trace-list'>");
            for (var i = 0; i < timeline.Count; i++) {
                sb.Append("<li>").Append(encoder.Encode(timeline[i])).Append("</li>");
            }
            sb.Append("</ol>");
        }

        sb.Append("</details>");
        html = sb.ToString();
        return true;
    }

    private static IReadOnlyList<string> BuildAssistantTraceTimelineForRendering(IReadOnlyList<string>? timeline) {
        if (timeline is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(Math.Min(timeline.Count, MaxAssistantTurnTraceEntries));
        for (var i = timeline.Count - 1; i >= 0 && normalized.Count < MaxAssistantTurnTraceEntries; i--) {
            var item = (timeline[i] ?? string.Empty).Trim();
            if (item.Length == 0) {
                continue;
            }

            normalized.Add(item);
        }

        normalized.Reverse();
        return normalized.Count == 0 ? Array.Empty<string>() : normalized;
    }

    private static IEnumerable<(string Role, string Text, DateTime Time, string? Model)> ProjectLegacyMessages(IEnumerable<(string Role, string Text, DateTime Time)> messages) {
        foreach (var message in messages) {
            yield return (message.Role, message.Text, message.Time, null);
        }
    }

    private static string BuildModelBadgeLabel(string role, string? model) {
        var normalizedRole = (role ?? string.Empty).Trim();
        if (!normalizedRole.Equals("Assistant", StringComparison.OrdinalIgnoreCase)
            && !normalizedRole.Equals("Tools", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalizedModel = (model ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            return string.Empty;
        }

        return normalizedModel;
    }

    private static bool TryRenderOutcomeCallout(
        string role,
        string text,
        MarkdownRendererOptions markdownOptions,
        out string html) {
        html = string.Empty;
        if (!IsOutcomeRole(role)) {
            return false;
        }

        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0) {
            return false;
        }

        var match = AssistantOutcomePrefixRegex.Match(raw);
        if (!match.Success) {
            return false;
        }

        var kindRaw = match.Groups["kind"].Value.Trim();
        var normalizedKind = NormalizeOutcomeKind(kindRaw);
        var headlineRaw = match.Groups["headline"].Value.Trim();
        if (!IsAssistantOutcomeKind(normalizedKind)) {
            return false;
        }

        var headline = headlineRaw.Length == 0
            ? GetOutcomeDefaultTitle(normalizedKind, role)
            : headlineRaw;
        var detail = normalizedKind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)
            ? PrepareExecutionBlockedOutcomeDetail(raw[match.Length..])
            : TranscriptMarkdownPreparation.PrepareOutcomeDetailBody(raw[match.Length..]);
        var toneClass = GetAssistantOutcomeToneClass(normalizedKind);
        var badge = GetAssistantOutcomeBadge(normalizedKind);
        var iconSvg = GetAssistantOutcomeIconSvg(normalizedKind);
        var encoder = HtmlEncoder.Default;
        var kindCssClass = "outcome-kind-" + normalizedKind.Replace('_', '-');
        var roleCssClass = string.Equals(role, "System", StringComparison.OrdinalIgnoreCase)
            ? "outcome-role-system"
            : "outcome-role-assistant";

        var sb = new StringBuilder();
        sb.Append("<section class='outcome-card ").Append(toneClass).Append(' ').Append(kindCssClass).Append(' ').Append(roleCssClass).Append("'>")
            .Append("<div class='outcome-head'>")
            .Append("<div class='outcome-main'>")
            .Append("<span class='outcome-icon' aria-hidden='true'>").Append(iconSvg).Append("</span>")
            .Append("<span class='outcome-title'>").Append(encoder.Encode(headline)).Append("</span>")
            .Append("</div>")
            .Append("<span class='outcome-badge'>").Append(encoder.Encode(badge)).Append("</span>")
            .Append("</div>");

        if (detail.Length > 0) {
            sb.Append("<div class='outcome-body'>")
                .Append(RenderBodyHtml(detail, markdownOptions))
                .Append("</div>");
        }

        sb.Append("</section>");
        html = sb.ToString();
        return true;
    }

    private static string NormalizeOutcomeKind(string kind) {
        return (kind ?? string.Empty)
            .Trim()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool IsAssistantOutcomeKind(string kind) {
        return kind.Equals("error", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("limit", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("warning", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("startup", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOutcomeRole(string role) {
        return string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase)
               || string.Equals(role, "System", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOutcomeDefaultTitle(string kind, string role) {
        var isSystemRole = string.Equals(role, "System", StringComparison.OrdinalIgnoreCase);
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System canceled" : "Turn canceled";
        }
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System limit reached" : "Limit reached";
        }
        if (kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System warning" : "Warning";
        }
        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "Startup diagnostics" : "Startup";
        }
        if (kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)) {
            return isSystemRole ? "System action blocked" : "Execution blocked";
        }
        if (kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) {
            return "Cached evidence fallback";
        }

        return isSystemRole ? "System error" : "Request failed";
    }

    private static string GetAssistantOutcomeBadge(string kind) {
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) {
            return "Canceled";
        }
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)) {
            return "Limit";
        }
        if (kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return "Warning";
        }
        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)) {
            return "Startup";
        }
        if (kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)) {
            return "Blocked";
        }
        if (kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) {
            return "Cached";
        }

        return "Error";
    }

    private static string GetAssistantOutcomeToneClass(string kind) {
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) {
            return "outcome-neutral";
        }
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return "outcome-warn";
        }
        if (kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)) {
            return "outcome-neutral";
        }
        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) {
            return "outcome-neutral";
        }

        return "outcome-error";
    }

    private static string GetAssistantOutcomeIconSvg(string kind) {
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) {
            return "<svg width='14' height='14' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'><circle cx='8' cy='8' r='6.2'/><path d='M5.2 8h5.6'/></svg>";
        }
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return "<svg width='14' height='14' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'><circle cx='8' cy='8' r='6.2'/><path d='M8 4.6v4.4'/><circle cx='8' cy='11.7' r='0.8' fill='currentColor' stroke='none'/></svg>";
        }
        if (kind.Equals("startup", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("cached_evidence_fallback", StringComparison.OrdinalIgnoreCase)) {
            return "<svg width='14' height='14' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'><circle cx='8' cy='8' r='6.2'/><path d='M8 6v2.8'/><path d='M8 10.7h.01'/></svg>";
        }
        if (kind.Equals("execution_blocked", StringComparison.OrdinalIgnoreCase)) {
            return "<svg width='14' height='14' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'><rect x='3.4' y='7' width='9.2' height='6.1' rx='1.8'/><path d='M5.4 7V5.6A2.6 2.6 0 018 3a2.6 2.6 0 012.6 2.6V7'/><path d='M8 8.9v1.9'/></svg>";
        }

        return "<svg width='14' height='14' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'><circle cx='8' cy='8' r='6.2'/><path d='M5.7 5.7l4.6 4.6M10.3 5.7l-4.6 4.6'/></svg>";
    }

    private static string PrepareExecutionBlockedOutcomeDetail(string rawDetail) {
        var lines = (rawDetail ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        string summary = string.Empty;
        string selectedAction = string.Empty;
        string actionCommand = string.Empty;
        string reasonCode = string.Empty;
        var body = new StringBuilder();
        var bodyStarted = false;

        for (var i = 0; i < lines.Length; i++) {
            var rawLine = lines[i] ?? string.Empty;
            var line = rawLine.Trim();

            if (line.IndexOf(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase) >= 0) {
                line = line.Replace(ExecutionContractMarker, string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Trim(' ', ':', '-', '\t');
                rawLine = line;
                if (line.Length == 0 && !bodyStarted) {
                    continue;
                }
            }

            if (summary.Length == 0) {
                if (line.Length == 0) {
                    continue;
                }

                summary = line;
                continue;
            }

            if (line.StartsWith("Selected action request:", StringComparison.OrdinalIgnoreCase)) {
                selectedAction = line["Selected action request:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Action:", StringComparison.OrdinalIgnoreCase)) {
                actionCommand = line["Action:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("Reason code:", StringComparison.OrdinalIgnoreCase)) {
                reasonCode = line["Reason code:".Length..].Trim();
                if (reasonCode.Length == 0) {
                    for (var j = i + 1; j < lines.Length; j++) {
                        var next = lines[j].Trim();
                        if (next.Length == 0) {
                            continue;
                        }

                        reasonCode = next;
                        i = j;
                        break;
                    }
                }
                continue;
            }

            if (line.Length == 0 && !bodyStarted) {
                continue;
            }

            if (bodyStarted) {
                body.AppendLine();
            }
            body.Append(rawLine);
            bodyStarted = true;
        }

        var detail = new StringBuilder();
        if (summary.Length > 0) {
            detail.Append(summary);
        }

        var hasMetadata = selectedAction.Length > 0 || actionCommand.Length > 0 || reasonCode.Length > 0;
        if (hasMetadata) {
            if (detail.Length > 0) {
                detail.AppendLine().AppendLine();
            }

            if (selectedAction.Length > 0) {
                AppendExecutionBlockedMetadataLine(detail, "Selected action", selectedAction);
            }
            if (actionCommand.Length > 0) {
                AppendExecutionBlockedMetadataLine(detail, "Action command", actionCommand);
            }
            if (reasonCode.Length > 0) {
                AppendExecutionBlockedMetadataLine(detail, "Reason code", reasonCode);
            }
        }

        var preservedBody = body.ToString().Trim();
        if (preservedBody.Length > 0) {
            if (detail.Length > 0) {
                detail.AppendLine().AppendLine();
            }

            detail.Append(preservedBody);
        }

        return TranscriptMarkdownPreparation.PrepareOutcomeDetailBody(detail.ToString());
    }

    private static void AppendExecutionBlockedMetadataLine(StringBuilder detail, string label, string value) {
        var normalizedValue = NormalizeExecutionBlockedMetadataValue(value);
        if (normalizedValue.Length == 0) {
            return;
        }

        detail.Append("- ")
            .Append(label)
            .Append(": ")
            .Append(BuildExecutionBlockedMetadataCodeSpan(normalizedValue))
            .AppendLine();
    }

    private static string NormalizeExecutionBlockedMetadataValue(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var pendingWhitespace = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsControl(ch)) {
                pendingWhitespace = sb.Length > 0;
                continue;
            }

            if (char.IsWhiteSpace(ch)) {
                pendingWhitespace = sb.Length > 0;
                continue;
            }

            if (pendingWhitespace) {
                sb.Append(' ');
                pendingWhitespace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static string BuildExecutionBlockedMetadataCodeSpan(string value) {
        if (string.IsNullOrEmpty(value)) {
            return "``";
        }

        var longestBacktickRun = 0;
        var currentRun = 0;
        for (var i = 0; i < value.Length; i++) {
            if (value[i] == '`') {
                currentRun++;
                if (currentRun > longestBacktickRun) {
                    longestBacktickRun = currentRun;
                }
            } else {
                currentRun = 0;
            }
        }

        var fence = new string('`', Math.Max(1, longestBacktickRun + 1));
        var requiresPadding = value.Length > 0
                              && (char.IsWhiteSpace(value[0])
                                  || char.IsWhiteSpace(value[^1])
                                  || value[0] == '`'
                                  || value[^1] == '`');
        return requiresPadding
            ? fence + " " + value + " " + fence
            : fence + value + fence;
    }

    private static string RenderBodyHtml(string text, MarkdownRendererOptions markdownOptions) {
        try {
            var html = MarkdownRenderer.RenderBodyHtml(text, markdownOptions);
            html = RemoveStandaloneHashParagraphsBeforeHeadings(html);
            return EnsureInlineCodeHtml(html);
        } catch {
            return EnsureInlineCodeHtml("<article class='markdown-body'><p>" + WebUtility.HtmlEncode(text) + "</p></article>");
        }
    }

    private static string RemoveStandaloneHashParagraphsBeforeHeadings(string html) {
        if (string.IsNullOrEmpty(html) || html.IndexOf("<p", StringComparison.OrdinalIgnoreCase) < 0) {
            return html;
        }

        return StandaloneHashParagraphBeforeHeadingRegex.Replace(html, string.Empty);
    }

    private static PendingActionExtraction ExtractPendingActionsForRendering(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return new PendingActionExtraction(string.Empty, Array.Empty<PendingActionRenderItem>());
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var kept = new List<string>(lines.Length);
        var actions = new List<PendingActionRenderItem>();
        var inFence = false;
        var fenceMarker = '\0';
        var fenceRunLength = 0;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i] ?? string.Empty;
            var trimmed = line.Trim();
            if (TryReadFenceRun(line, out var runMarker, out var runLength, out var runSuffix)) {
                if (!inFence) {
                    inFence = true;
                    fenceMarker = runMarker;
                    fenceRunLength = runLength;
                } else if (runMarker == fenceMarker
                           && runLength >= fenceRunLength
                           && string.IsNullOrWhiteSpace(runSuffix)) {
                    inFence = false;
                    fenceMarker = '\0';
                    fenceRunLength = 0;
                }

                kept.Add(line);
                continue;
            }

            if (!inFence) {
                var match = PendingActionLineRegex.Match(trimmed);
                if (match.Success) {
                    if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)) {
                        ordinal = actions.Count + 1;
                    }

                    var label = match.Groups["label"].Value.Trim();
                    var command = match.Groups["command"].Value.Trim();
                    var id = match.Groups["id"].Value.Trim();
                    if (label.Length > 0 && command.Length > 0 && id.Length > 0) {
                        actions.Add(new PendingActionRenderItem(ordinal, label, command, id));
                        continue;
                    }
                }

                if (actions.Count > 0 && PendingActionHeadingRegex.IsMatch(trimmed)) {
                    continue;
                }
            }

            kept.Add(line);
        }

        if (actions.Count > 0) {
            kept.RemoveAll(line => PendingActionHeadingRegex.IsMatch((line ?? string.Empty).Trim()));
        }

        var cleanedText = Regex.Replace(string.Join('\n', kept).Trim(), @"\n{3,}", "\n\n");
        return new PendingActionExtraction(cleanedText, actions);
    }

    private static bool TryReadFenceRun(string line, out char marker, out int runLength, out string suffix) {
        marker = '\0';
        runLength = 0;
        suffix = string.Empty;
        if (line is null) {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.Length < 3) {
            return false;
        }

        var first = trimmed[0];
        if (first != '`' && first != '~') {
            return false;
        }

        var i = 0;
        while (i < trimmed.Length && trimmed[i] == first) {
            i++;
        }

        if (i < 3) {
            return false;
        }

        marker = first;
        runLength = i;
        suffix = trimmed.Substring(i);
        return true;
    }

    private static string AppendPendingActionChips(string bodyHtml, IReadOnlyList<PendingActionRenderItem> actions) {
        if (actions is null || actions.Count == 0) {
            return bodyHtml;
        }

        var encoder = HtmlEncoder.Default;
        var sb = new StringBuilder(bodyHtml ?? string.Empty);
        sb.Append("<section class='ix-action-cta'><div class='ix-action-cta-title'>Follow-up actions</div><div class='ix-action-cta-list'>");
        for (var i = 0; i < actions.Count; i++) {
            var action = actions[i];
            sb.Append("<button type='button' class='ix-action-btn' data-act-cmd='")
                .Append(encoder.Encode(action.Command))
                .Append("' data-act-id='")
                .Append(encoder.Encode(action.Id))
                .Append("' aria-label='Run follow-up action ")
                .Append(action.Ordinal.ToString(CultureInfo.InvariantCulture))
                .Append("'>")
                .Append("<span class='ix-action-ordinal'>")
                .Append(action.Ordinal.ToString(CultureInfo.InvariantCulture))
                .Append(".</span>")
                .Append("<span class='ix-action-label'>")
                .Append(encoder.Encode(action.Label))
                .Append("</span>")
                .Append("<code class='ix-action-command'>")
                .Append(encoder.Encode(action.Command))
                .Append("</code>")
                .Append("</button>");
        }
        sb.Append("</div></section>");
        return sb.ToString();
    }

    private static string EnsureInlineCodeHtml(string html) {
        if (string.IsNullOrEmpty(html) || html.IndexOf('`', StringComparison.Ordinal) < 0) {
            return html;
        }

        var preBlocks = PreBlockRegex.Matches(html);
        if (preBlocks.Count == 0) {
            return InlineCodeFallbackRegex.Replace(html, match => "<code>" + match.Groups["code"].Value + "</code>");
        }

        var sb = new StringBuilder(html.Length + 32);
        var cursor = 0;
        for (var i = 0; i < preBlocks.Count; i++) {
            var pre = preBlocks[i];
            if (pre.Index > cursor) {
                var segment = html[cursor..pre.Index];
                sb.Append(InlineCodeFallbackRegex.Replace(segment, match => "<code>" + match.Groups["code"].Value + "</code>"));
            }

            sb.Append(pre.Value);
            cursor = pre.Index + pre.Length;
        }

        if (cursor < html.Length) {
            var tail = html[cursor..];
            sb.Append(InlineCodeFallbackRegex.Replace(tail, match => "<code>" + match.Groups["code"].Value + "</code>"));
        }

        return sb.ToString();
    }

    private static RoleStyle ResolveRoleStyle(string? role) {
        if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase)) {
            return new RoleStyle("user", "You", "U");
        }

        if (string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
            return new RoleStyle("assistant", "IntelligenceX", "IX");
        }

        if (string.Equals(role, "Tools", StringComparison.OrdinalIgnoreCase)) {
            return new RoleStyle("tools", "Tools", "T");
        }

        return new RoleStyle("system", "System", "S");
    }

    private readonly record struct PendingActionRenderItem(int Ordinal, string Label, string Command, string Id);
    private readonly record struct PendingActionExtraction(string CleanedText, IReadOnlyList<PendingActionRenderItem> Actions);
    private readonly record struct RoleStyle(string RoleClass, string DisplayName, string Avatar);
}
