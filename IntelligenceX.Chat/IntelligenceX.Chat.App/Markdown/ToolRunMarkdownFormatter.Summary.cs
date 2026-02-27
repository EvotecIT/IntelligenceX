using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Markdown;

internal static partial class ToolRunMarkdownFormatter {
    private static bool ShouldIncludeSummary(string summary, bool hasError) {
        if (string.IsNullOrWhiteSpace(summary)) {
            return false;
        }

        if (!hasError) {
            return true;
        }

        // In error cases, only keep summaries that add real diagnostic value.
        var usefulLines = 0;
        var lines = summary.Split('\n', StringSplitOptions.None);
        foreach (var rawLine in lines) {
            var line = rawLine.Trim();
            if (line.Length == 0 || IsPipeOnlyLine(line)) {
                continue;
            }

            if (line.StartsWith('#') || line.Equals("count", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (int.TryParse(line, out _)) {
                continue;
            }

            usefulLines++;
            if (usefulLines >= 2) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSummaryMarkdown(string? summaryMarkdown, string toolLabel) {
        if (string.IsNullOrWhiteSpace(summaryMarkdown)) {
            return string.Empty;
        }

        var lines = summaryMarkdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        var sb = new StringBuilder();
        var previousBlank = false;
        var inFence = false;
        var fenceMarker = '\0';
        var fenceRunLength = 0;
        foreach (var rawLine in lines) {
            var line = rawLine.TrimEnd();
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

                sb.AppendLine(line);
                previousBlank = false;
                continue;
            }

            if (inFence) {
                sb.AppendLine(line);
                previousBlank = false;
                continue;
            }

            if (line.Length == 0) {
                if (!previousBlank && sb.Length > 0) {
                    sb.AppendLine();
                }
                previousBlank = true;
                continue;
            }

            if (IsPipeOnlyLine(line)) {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal)) {
                var heading = line[4..].Trim();
                if (heading.Equals(toolLabel, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            }

            sb.AppendLine(line);
            previousBlank = false;
        }

        return sb.ToString().TrimEnd();
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

    private static bool IsPipeOnlyLine(string line) {
        var hasPipe = false;
        for (var i = 0; i < line.Length; i++) {
            var ch = line[i];
            if (ch == '|') {
                hasPipe = true;
                continue;
            }

            if (!char.IsWhiteSpace(ch)) {
                return false;
            }
        }

        return hasPipe;
    }

    private static void AppendFailureDescriptor(MarkdownComposer markdown, ToolOutputDto output) {
        var detailParts = new List<string>();
        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        var errorMessage = (output.Error ?? string.Empty).Trim();

        if (errorCode.Length > 0) {
            detailParts.Add("code: `" + errorCode + "`");
        }
        if (output.IsTransient.HasValue) {
            detailParts.Add("retryable: " + (output.IsTransient.Value ? "yes" : "no"));
        }
        if (detailParts.Count > 0) {
            markdown.Quote("failure descriptor: " + string.Join(" | ", detailParts));
        }

        if (errorMessage.Length > 0) {
            markdown.Quote("error: " + errorMessage);
        } else if (errorCode.Length > 0) {
            markdown.Quote("error: Tool failed with code `" + errorCode + "`.");
        }

        if (output.Hints is { Length: > 0 }) {
            markdown.Paragraph("hints:");
            for (var i = 0; i < output.Hints.Length; i++) {
                var hint = (output.Hints[i] ?? string.Empty).Trim();
                if (hint.Length > 0) {
                    markdown.Bullet(hint);
                }
            }
        }
    }
}
