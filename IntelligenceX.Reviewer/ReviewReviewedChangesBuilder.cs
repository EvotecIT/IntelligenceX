using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class ReviewReviewedChangesBuilder {
    private const int MaxRows = 20;

    public static string BuildCommentBlock(IReadOnlyList<PullRequestFile> reviewFiles,
        IReadOnlyList<PullRequestFile> promptFiles, bool reviewFailed) {
        if (reviewFiles.Count == 0) {
            return string.Empty;
        }

        var promptFileMap = promptFiles
            .GroupBy(static file => NormalizePath(file.Filename), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rows = reviewFiles.Take(MaxRows).ToArray();
        var omittedRows = Math.Max(0, reviewFiles.Count - rows.Length);
        var omittedFromPrompt = reviewFiles.Count(file => !promptFileMap.ContainsKey(NormalizePath(file.Filename)));

        var sb = new StringBuilder();
        sb.AppendLine("## Reviewed Changes 📋");
        sb.AppendLine();
        if (reviewFailed) {
            sb.AppendLine("> Review provider failed before a completed assessment; this table reflects collected diff context only.");
            sb.AppendLine();
        }
        sb.AppendLine("| File | Status | Change | IX checked |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var file in rows) {
            var normalized = NormalizePath(file.Filename);
            promptFileMap.TryGetValue(normalized, out var promptFile);
            sb.Append("| `");
            sb.Append(EscapeTable(file.Filename));
            sb.Append("` | ");
            sb.Append(EscapeTable(FormatStatus(file.Status)));
            sb.Append(" | ");
            sb.Append(EscapeTable(DescribeChange(file.Patch)));
            sb.Append(" | ");
            sb.Append(EscapeTable(DescribeReviewScope(file, promptFile)));
            sb.AppendLine(" |");
        }
        if (omittedRows > 0) {
            sb.AppendLine();
            sb.AppendLine(omittedRows == 1
                ? "_1 additional file omitted from this table._"
                : $"_{omittedRows} additional files omitted from this table._");
        }
        if (omittedFromPrompt > 0) {
            sb.AppendLine();
            sb.AppendLine(omittedFromPrompt == 1
                ? "_1 selected file was outside the LLM prompt file budget._"
                : $"_{omittedFromPrompt} selected files were outside the LLM prompt file budget._");
        }

        return sb.ToString().TrimEnd();
    }

    private static string DescribeReviewScope(PullRequestFile sourceFile, PullRequestFile? promptFile) {
        if (promptFile is null) {
            return "not included in prompt";
        }

        if (string.IsNullOrWhiteSpace(sourceFile.Patch)) {
            return "path/status only";
        }

        if (string.IsNullOrWhiteSpace(promptFile.Patch)) {
            return "path/status only";
        }

        return string.Equals(sourceFile.Patch, promptFile.Patch, StringComparison.Ordinal)
            ? "diff patch"
            : "trimmed diff patch";
    }

    private static string DescribeChange(string? patch) {
        if (string.IsNullOrWhiteSpace(patch)) {
            return "patch unavailable";
        }

        var added = 0;
        var deleted = 0;
        var hunks = 0;
        var lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                hunks++;
                continue;
            }
            if (line.StartsWith("+++", StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal)) {
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal)) {
                added++;
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal)) {
                deleted++;
            }
        }

        var hunkLabel = hunks == 1 ? "1 hunk" : $"{hunks} hunks";
        return $"+{added}/-{deleted} across {hunkLabel}";
    }

    private static string FormatStatus(string? status) {
        if (string.IsNullOrWhiteSpace(status)) {
            return "unknown";
        }

        var trimmed = status.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1).ToLowerInvariant();
    }

    private static string NormalizePath(string path) =>
        (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static string EscapeTable(string value) =>
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
}
