using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Formats structured tool-run envelopes into transcript markdown.
/// </summary>
internal static class ToolRunMarkdownFormatter {
    /// <summary>
    /// Builds markdown for tool calls and outputs.
    /// </summary>
    /// <param name="tools">Tool run payload.</param>
    /// <param name="resolveToolDisplayName">Display-name resolver callback.</param>
    /// <returns>Markdown summary for transcript.</returns>
    public static string Format(ToolRunDto tools, Func<string?, string> resolveToolDisplayName) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resolveToolDisplayName);

        var markdown = new MarkdownComposer()
            .Paragraph("**Tool outputs:**")
            .BlankLine();

        var namesByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var call in tools.Calls) {
            namesByCallId[call.CallId] = resolveToolDisplayName(call.Name);
        }

        foreach (var output in tools.Outputs) {
            var toolLabel = namesByCallId.TryGetValue(output.CallId, out var name)
                ? name
                : $"Call {output.CallId}";
            var hasError = !string.IsNullOrWhiteSpace(output.Error) || !string.IsNullOrWhiteSpace(output.ErrorCode) || output.Ok == false;

            markdown.Heading(toolLabel, 4);
            if (hasError) {
                AppendFailureContract(markdown, output);
            }

            var summary = NormalizeSummaryMarkdown(output.SummaryMarkdown, toolLabel);
            if (ShouldIncludeSummary(summary, hasError)) {
                markdown.Raw(summary);
            } else if (!hasError) {
                markdown.Paragraph("completed");
            }

            markdown.BlankLine();
        }

        return markdown.Build();
    }

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
        foreach (var rawLine in lines) {
            var line = rawLine.TrimEnd();
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

    private static void AppendFailureContract(MarkdownComposer markdown, ToolOutputDto output) {
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
            markdown.Quote("failure contract: " + string.Join(" | ", detailParts));
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
