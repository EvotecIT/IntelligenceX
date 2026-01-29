using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IntelligenceX.Reviewer;

internal sealed class InlineReviewComment {
    public InlineReviewComment(string path, int line, string body) {
        Path = path;
        Line = line;
        Body = body;
    }

    public string Path { get; }
    public int Line { get; }
    public string Body { get; }
}

internal sealed class InlineSectionResult {
    public InlineSectionResult(string body, IReadOnlyList<InlineReviewComment> comments, bool hadInlineSection) {
        Body = body;
        Comments = comments;
        HadInlineSection = hadInlineSection;
    }

    public string Body { get; }
    public IReadOnlyList<InlineReviewComment> Comments { get; }
    public bool HadInlineSection { get; }
}

internal static class ReviewInlineParser {
    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase) {
        "summary",
        "critical issues",
        "other issues",
        "tests / coverage",
        "tests/coverage",
        "tests & coverage",
        "next steps",
        "recommendations"
    };

    private static readonly Regex ListPrefix = new(@"^\s*(?:\d+[\)\.\:]\s*|\-\s*|\*\s*)",
        RegexOptions.Compiled);

    public static InlineSectionResult Extract(string reviewBody, int maxComments) {
        if (string.IsNullOrWhiteSpace(reviewBody)) {
            return new InlineSectionResult(reviewBody, Array.Empty<InlineReviewComment>(), false);
        }

        var normalized = reviewBody.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        var start = FindInlineSectionStart(lines);
        if (start < 0) {
            return new InlineSectionResult(normalized, Array.Empty<InlineReviewComment>(), false);
        }

        var end = FindInlineSectionEnd(lines, start + 1);
        var comments = ParseInlineComments(lines, start + 1, end, maxComments);
        var stripped = StripSection(lines, start, end);

        return new InlineSectionResult(stripped, comments, true);
    }

    private static int FindInlineSectionStart(string[] lines) {
        for (var i = 0; i < lines.Length; i++) {
            if (IsInlineHeader(lines[i])) {
                return i;
            }
        }
        return -1;
    }

    private static int FindInlineSectionEnd(string[] lines, int startIndex) {
        for (var i = startIndex; i < lines.Length; i++) {
            if (IsInlineHeader(lines[i])) {
                continue;
            }
            if (IsSectionHeader(lines[i])) {
                return i;
            }
        }
        return lines.Length;
    }

    private static IReadOnlyList<InlineReviewComment> ParseInlineComments(string[] lines, int start, int end,
        int maxComments) {
        if (maxComments <= 0) {
            return Array.Empty<InlineReviewComment>();
        }

        var results = new List<InlineReviewComment>();
        var index = start;
        while (index < end) {
            if (!TryParseCommentHeader(lines[index], out var path, out var line, out var inlineBody)) {
                index++;
                continue;
            }

            index++;
            var bodyLines = new List<string>();
            while (index < end && !TryParseCommentHeader(lines[index], out _, out _, out _)) {
                if (IsSectionHeader(lines[index])) {
                    break;
                }
                bodyLines.Add(lines[index]);
                index++;
            }

            var body = string.Join("\n", bodyLines).Trim();
            if (string.IsNullOrWhiteSpace(body)) {
                body = inlineBody ?? string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(body)) {
                results.Add(new InlineReviewComment(path, line, body));
            }

            if (results.Count >= maxComments) {
                break;
            }
        }

        return results;
    }

    private static bool TryParseCommentHeader(string line, out string path, out int lineNumber, out string? inlineBody) {
        path = string.Empty;
        lineNumber = 0;
        inlineBody = null;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var cleaned = ListPrefix.Replace(line.Trim(), string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) {
            return false;
        }

        if (cleaned.StartsWith("`", StringComparison.Ordinal) && cleaned.EndsWith("`", StringComparison.Ordinal)) {
            cleaned = cleaned.Trim('`').Trim();
        }

        var hashIndex = cleaned.LastIndexOf("#L", StringComparison.OrdinalIgnoreCase);
        if (hashIndex > -1) {
            var rawPath = cleaned.Substring(0, hashIndex).Trim();
            var rawLine = cleaned.Substring(hashIndex + 2).Trim();
            return TryParsePathLine(rawPath, rawLine, out path, out lineNumber, out inlineBody);
        }

        var colonIndex = cleaned.LastIndexOf(':');
        if (colonIndex < 0) {
            return false;
        }

        var pathPart = cleaned.Substring(0, colonIndex).Trim();
        var linePart = cleaned.Substring(colonIndex + 1).Trim();
        return TryParsePathLine(pathPart, linePart, out path, out lineNumber, out inlineBody);
    }

    private static bool TryParsePathLine(string rawPath, string rawLine, out string path, out int lineNumber,
        out string? inlineBody) {
        path = string.Empty;
        lineNumber = 0;
        inlineBody = null;

        if (string.IsNullOrWhiteSpace(rawPath) || string.IsNullOrWhiteSpace(rawLine)) {
            return false;
        }

        var trimmedLine = rawLine.Trim();
        var digits = new string(trimmedLine.TakeWhile(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits) || !int.TryParse(digits, out lineNumber) || lineNumber <= 0) {
            return false;
        }

        path = rawPath.Replace('\\', '/').Trim();
        if (path.StartsWith("./", StringComparison.Ordinal)) {
            path = path.Substring(2);
        }

        var remainder = trimmedLine.Substring(digits.Length).Trim();
        if (!string.IsNullOrWhiteSpace(remainder)) {
            if (remainder.StartsWith("-", StringComparison.Ordinal) ||
                remainder.StartsWith(":", StringComparison.Ordinal)) {
                remainder = remainder.Substring(1).Trim();
            }
            if (!string.IsNullOrWhiteSpace(remainder)) {
                inlineBody = remainder;
            }
        }

        return !string.IsNullOrWhiteSpace(path);
    }

    private static string StripSection(string[] lines, int start, int end) {
        var before = lines.Take(start);
        var after = lines.Skip(end);
        return string.Join("\n", before.Concat(after)).Trim();
    }

    private static bool IsInlineHeader(string line) {
        var header = NormalizeHeader(line);
        return header.StartsWith("inline comments", StringComparison.OrdinalIgnoreCase) ||
               header.StartsWith("inline comment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSectionHeader(string line) {
        var header = NormalizeHeader(line);
        if (string.IsNullOrWhiteSpace(header)) {
            return false;
        }
        if (header.StartsWith("inline comments", StringComparison.OrdinalIgnoreCase) ||
            header.StartsWith("inline comment", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return KnownSections.Contains(header);
    }

    private static string NormalizeHeader(string line) {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return string.Empty;
        }
        if (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.TrimStart('#').Trim();
        }
        if (trimmed.EndsWith(":", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
        }
        return trimmed;
    }
}
