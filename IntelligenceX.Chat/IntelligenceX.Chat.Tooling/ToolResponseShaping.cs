using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared response-shaping and redaction helpers for chat host/service runtimes.
/// </summary>
public static class ToolResponseShaping {
    private static readonly Regex EmailLikeTokenRegex = new(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Builds response-shaping guidance text, or null when no shaping constraints are active.
    /// </summary>
    /// <param name="maxTableRows">Max table rows (0 = unlimited).</param>
    /// <param name="maxSample">Max sample items (0 = unlimited).</param>
    /// <param name="redact">When true, redact output guidance is included.</param>
    /// <returns>Guidance text or null.</returns>
    public static string? BuildSessionResponseShapingInstructions(int maxTableRows, int maxSample, bool redact) {
        if (maxTableRows <= 0 && maxSample <= 0 && !redact) {
            return null;
        }

        var lines = new List<string> {
            "## Session Response Shaping",
            "Follow these display constraints for all assistant responses:"
        };
        if (maxTableRows > 0) {
            lines.Add($"- Max table rows: {maxTableRows} (show a preview, then offer to paginate/refine).");
        }
        if (maxSample > 0) {
            lines.Add($"- Max sample items: {maxSample} (for long lists, show a sample and counts).");
        }
        if (redact) {
            lines.Add("- Redaction: redact emails/UPNs in assistant output. Prefer summaries over raw identifiers.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Appends shaping guidance to base instructions when shaping constraints are active.
    /// </summary>
    /// <param name="instructions">Base instructions.</param>
    /// <param name="maxTableRows">Max table rows (0 = unlimited).</param>
    /// <param name="maxSample">Max sample items (0 = unlimited).</param>
    /// <param name="redact">When true, redact output guidance is included.</param>
    /// <returns>Instructions with optional shaping guidance appended.</returns>
    public static string? AppendSessionResponseShapingInstructions(string? instructions, int maxTableRows, int maxSample, bool redact) {
        var shaping = BuildSessionResponseShapingInstructions(maxTableRows, maxSample, redact);
        if (string.IsNullOrWhiteSpace(shaping)) {
            return instructions;
        }
        if (string.IsNullOrWhiteSpace(instructions)) {
            return shaping;
        }

        return instructions + Environment.NewLine + Environment.NewLine + shaping;
    }

    /// <summary>
    /// Best-effort redaction of email/UPN-like tokens.
    /// </summary>
    /// <param name="text">Input text.</param>
    /// <returns>Redacted text.</returns>
    public static string RedactEmailLikeTokens(string? text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }

        return EmailLikeTokenRegex.Replace(text, "[redacted_email]");
    }
}
