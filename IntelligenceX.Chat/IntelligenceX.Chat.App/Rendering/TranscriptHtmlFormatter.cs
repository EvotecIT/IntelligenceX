using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Encodings.Web;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Renders transcript messages into chat-shell HTML.
/// </summary>
internal static class TranscriptHtmlFormatter {
    private const string CopyButtonIconSvg =
        "<svg width='14' height='14' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><rect x='9' y='9' width='13' height='13' rx='2'/><path d='M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1'/></svg>";
    private static readonly Regex InlineCodeSpanRegex = new("`([^`]+)`", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SoftWrappedStrongRegex = new(
        @"\*\*(?<left>[^\r\n*]{1,80})\r?\n(?<right>[^\r\n*]{1,80})\*\*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AssistantOutcomePrefixRegex = new(
        @"^\[(?<kind>[a-z_]+)\]\s*(?<headline>[^\r\n]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Builds transcript HTML for the chat shell.
    /// </summary>
    /// <param name="messages">Role/text/time transcript entries.</param>
    /// <param name="timestampFormat">Timestamp format.</param>
    /// <param name="markdownOptions">Markdown renderer options.</param>
    /// <returns>HTML fragment.</returns>
    public static string Format(
        IEnumerable<(string Role, string Text, DateTime Time)> messages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(markdownOptions);

        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat;
        var encoder = HtmlEncoder.Default;
        var html = new StringBuilder();
        string? previousRoleClass = null;
        var messageIndex = 0;

        foreach (var message in messages) {
            if (string.IsNullOrWhiteSpace(message.Text)) {
                messageIndex++;
                continue;
            }

            var role = ResolveRoleStyle(message.Role);
            var isContinuation = string.Equals(previousRoleClass, role.RoleClass, StringComparison.Ordinal);
            var bodyHtml = RenderBodyHtml(message.Text, markdownOptions);
            var bubbleClass = "bubble";
            if (TryRenderAssistantOutcomeCallout(message.Role, message.Text, markdownOptions, out var calloutHtml)) {
                bodyHtml = calloutHtml;
                bubbleClass = "bubble bubble-callout";
            }
            var time = message.Time.ToString(format, CultureInfo.InvariantCulture);

            html.Append("<div class='msg-row ").Append(role.RoleClass);
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
            html.Append("'>").Append(encoder.Encode(role.DisplayName)).Append(" &middot; ").Append(encoder.Encode(time)).Append("</div>")
                .Append("<div class='").Append(bubbleClass).Append("'>").Append(bodyHtml).Append("</div>")
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

    private static bool TryRenderAssistantOutcomeCallout(
        string role,
        string text,
        MarkdownRendererOptions markdownOptions,
        out string html) {
        html = string.Empty;
        if (!string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
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
        var headlineRaw = match.Groups["headline"].Value.Trim();
        if (!IsAssistantOutcomeKind(kindRaw)) {
            return false;
        }

        var headline = headlineRaw.Length == 0
            ? GetAssistantOutcomeDefaultTitle(kindRaw)
            : headlineRaw;
        var detail = raw[match.Length..].Trim();
        var toneClass = GetAssistantOutcomeToneClass(kindRaw);
        var badge = GetAssistantOutcomeBadge(kindRaw);
        var iconSvg = GetAssistantOutcomeIconSvg(kindRaw);
        var encoder = HtmlEncoder.Default;

        var sb = new StringBuilder();
        sb.Append("<section class='outcome-card ").Append(toneClass).Append("'>")
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

    private static bool IsAssistantOutcomeKind(string kind) {
        return kind.Equals("error", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("limit", StringComparison.OrdinalIgnoreCase)
               || kind.Equals("warning", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetAssistantOutcomeDefaultTitle(string kind) {
        if (kind.Equals("canceled", StringComparison.OrdinalIgnoreCase)) {
            return "Turn canceled";
        }
        if (kind.Equals("limit", StringComparison.OrdinalIgnoreCase)) {
            return "Limit reached";
        }
        if (kind.Equals("warning", StringComparison.OrdinalIgnoreCase)) {
            return "Warning";
        }

        return "Request failed";
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

        return "<svg width='14' height='14' viewBox='0 0 16 16' fill='none' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'><circle cx='8' cy='8' r='6.2'/><path d='M5.7 5.7l4.6 4.6M10.3 5.7l-4.6 4.6'/></svg>";
    }

    private static string RenderBodyHtml(string text, MarkdownRendererOptions markdownOptions) {
        try {
            var normalized = NormalizeMarkdownForRenderer(text);
            return MarkdownRenderer.RenderBodyHtml(normalized, markdownOptions);
        } catch {
            return "<article class='markdown-body'><p>" + WebUtility.HtmlEncode(text) + "</p></article>";
        }
    }

    private static string NormalizeMarkdownForRenderer(string? text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return value;
        }

        // Model/tool output sometimes hard-wraps short bold labels across lines.
        value = SoftWrappedStrongRegex.Replace(value, static match => {
            var left = match.Groups["left"].Value.Trim();
            var right = match.Groups["right"].Value.Trim();
            if (left.Length == 0 || right.Length == 0) {
                return match.Value;
            }
            return "**" + left + " " + right + "**";
        });

        // Keep inline code on one line for strict markdown parsers.
        return InlineCodeSpanRegex.Replace(value, static match => {
            var body = match.Groups[1].Value;
            if (body.IndexOfAny(new[] { '\r', '\n' }) < 0) {
                return match.Value;
            }

            var compact = body.Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return compact.Length == 0 ? "``" : "`" + compact + "`";
        });
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

    private readonly record struct RoleStyle(string RoleClass, string DisplayName, string Avatar);
}
