using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Reviewer;

internal static class AnalysisSummaryBuilder {
    private const int MaxMessageChars = 200;

    public static string BuildSummary(IReadOnlyList<AnalysisFinding> findings, AnalysisResultsSettings results,
        AnalysisLoadReport? loadReport = null) {
        if (results is null || !results.Summary) {
            return string.Empty;
        }
        findings ??= Array.Empty<AnalysisFinding>();
        if (findings.Count == 0) {
            var emptyLines = new List<string> {
                "### Static analysis",
                loadReport is not null && loadReport.ResolvedInputFiles == 0
                    ? "Findings: unavailable (no analysis result files matched configured inputs)"
                    : "Findings: 0 (no issues at or above configured severity)"
            };
            return string.Join("\n", emptyLines).TrimEnd();
        }

        var errorCount = 0;
        var warningCount = 0;
        var infoCount = 0;
        foreach (var finding in findings) {
            switch (AnalysisSeverity.Normalize(finding.Severity)) {
                case "error":
                    errorCount++;
                    break;
                case "warning":
                    warningCount++;
                    break;
                case "info":
                    infoCount++;
                    break;
            }
        }

        var header = "### Static analysis";
        var totals = new List<string>();
        if (errorCount > 0) {
            totals.Add($"error: {errorCount}");
        }
        if (warningCount > 0) {
            totals.Add($"warning: {warningCount}");
        }
        if (infoCount > 0) {
            totals.Add($"info: {infoCount}");
        }
        var totalLine = totals.Count == 0
            ? $"Findings: {findings.Count}"
            : $"Findings: {findings.Count} ({string.Join(", ", totals)})";

        var maxItems = results.SummaryMaxItems <= 0 ? findings.Count : results.SummaryMaxItems;
        var ordered = OrderFindings(findings).Take(maxItems).ToList();

        var lines = new List<string> { header, totalLine, string.Empty };
        foreach (var finding in ordered) {
            var location = FormatLocation(finding);
            var rule = string.IsNullOrWhiteSpace(finding.RuleId) ? string.Empty : $" ({finding.RuleId})";
            var message = TrimMessage(finding.Message);
            var severity = AnalysisSeverity.Normalize(finding.Severity);
            lines.Add($"- [{severity}] `{location}`{rule} {message}");
        }

        if (findings.Count > ordered.Count) {
            lines.Add(string.Empty);
            lines.Add($"Showing first {ordered.Count} of {findings.Count} findings.");
        }

        return string.Join("\n", lines).TrimEnd();
    }

    public static IReadOnlyList<InlineReviewComment> BuildInlineComments(IReadOnlyList<AnalysisFinding> findings,
        AnalysisResultsSettings results) {
        if (findings is null || findings.Count == 0 || results is null || results.MaxInline <= 0) {
            return Array.Empty<InlineReviewComment>();
        }

        var limit = results.MaxInline;
        var ordered = OrderFindings(findings);
        var list = new List<InlineReviewComment>();
        foreach (var finding in ordered) {
            if (list.Count >= limit) {
                break;
            }
            if (string.IsNullOrWhiteSpace(finding.Path) || finding.Line <= 0) {
                continue;
            }
            var severity = AnalysisSeverity.Normalize(finding.Severity);
            var message = TrimMessage(finding.Message);
            if (string.IsNullOrWhiteSpace(message)) {
                continue;
            }
            var rule = string.IsNullOrWhiteSpace(finding.RuleId) ? string.Empty : $" (rule {finding.RuleId})";
            var body = $"Static analysis ({severity}): {message}{rule}";
            list.Add(new InlineReviewComment(finding.Path, finding.Line, body));
        }

        return list;
    }

    private static IEnumerable<AnalysisFinding> OrderFindings(IReadOnlyList<AnalysisFinding> findings) {
        return findings
            .OrderByDescending(finding => AnalysisSeverity.Rank(finding.Severity))
            .ThenBy(finding => finding.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.Line);
    }

    private static string FormatLocation(AnalysisFinding finding) {
        if (finding.Line > 0) {
            return $"{finding.Path}:{finding.Line}";
        }
        return finding.Path;
    }

    private static string TrimMessage(string? message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return string.Empty;
        }
        var trimmed = message.Trim();
        if (trimmed.Length <= MaxMessageChars) {
            return trimmed;
        }
        return trimmed.Substring(0, MaxMessageChars) + "...";
    }
}
