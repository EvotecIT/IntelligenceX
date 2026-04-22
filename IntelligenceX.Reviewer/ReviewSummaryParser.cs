using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IntelligenceX.Reviewer;

internal readonly record struct ReviewSummaryItem(string Section, string Text);
internal readonly record struct ReviewSummaryFinding(string Fingerprint, string Section, string Text, string Status);

internal static class ReviewSummaryParser {
    public static bool TryGetReviewedCommit(string? body, out string? commit) {
        commit = null;
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }

        var marker = ReviewFormatter.ReviewedCommitMarker;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) {
                continue;
            }
            var slice = line.Substring(index + marker.Length);
            var start = slice.IndexOf('`');
            if (start < 0) {
                continue;
            }
            slice = slice.Substring(start + 1);
            var end = slice.IndexOf('`');
            if (end < 0) {
                continue;
            }
            var token = slice.Substring(0, end).Trim();
            if (token.Length == 0) {
                continue;
            }
            commit = token;
            return true;
        }

        return false;
    }

    internal static bool HasMergeBlockers(string? body, ReviewSettings? settings = null) {
        if (string.IsNullOrWhiteSpace(body)) {
            return true;
        }

        body = ReviewFormatter.NormalizeSectionLayout(body);

        var configuredSections = settings?.ResolveMergeBlockerSections()
                                 ?? new[] { "todo list", "critical issues" };
        var mergeBlockerSections = ReviewSettings.NormalizeMergeBlockerSections(configuredSections);
        if (mergeBlockerSections.Count == 0) {
            mergeBlockerSections = new[] { "todo list", "critical issues" };
        }

        var sectionText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentSection = string.Empty;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) {
                currentSection = MatchConfiguredSection(trimmed, mergeBlockerSections);
                if (currentSection.Length > 0) {
                    seenSections.Add(currentSection);
                }
                continue;
            }

            if (currentSection.Length > 0) {
                sectionText.TryGetValue(currentSection, out var existing);
                sectionText[currentSection] = $"{existing}\n{trimmed}";
            }
        }

        var requireSectionMatch = settings?.MergeBlockerRequireSectionMatch ?? true;
        if (seenSections.Count == 0) {
            return requireSectionMatch;
        }
        if ((settings?.MergeBlockerRequireAllSections ?? true) &&
            seenSections.Count < mergeBlockerSections.Count) {
            return true;
        }

        foreach (var section in seenSections) {
            sectionText.TryGetValue(section, out var content);
            if (SectionHasMergeBlockerItems(content ?? string.Empty)) {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<ReviewSummaryItem> ExtractMergeBlockerItems(string? body, ReviewSettings? settings = null,
        int maxItems = 10) {
        var items = new List<ReviewSummaryItem>();
        if (string.IsNullOrWhiteSpace(body) || maxItems <= 0) {
            return items;
        }

        body = ReviewFormatter.NormalizeSectionLayout(body);
        var configuredSections = settings?.ResolveMergeBlockerSections()
                                 ?? new[] { "todo list", "critical issues" };
        var mergeBlockerSections = ReviewSettings.NormalizeMergeBlockerSections(configuredSections);
        if (mergeBlockerSections.Count == 0) {
            mergeBlockerSections = new[] { "todo list", "critical issues" };
        }

        var currentSection = string.Empty;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) {
                currentSection = MatchConfiguredSection(trimmed, mergeBlockerSections);
                continue;
            }

            if (currentSection.Length == 0) {
                continue;
            }

            if (!TryNormalizeItem(trimmed, out var normalizedItem)) {
                continue;
            }

            items.Add(new ReviewSummaryItem(currentSection, normalizedItem));
            if (items.Count >= maxItems) {
                break;
            }
        }

        return items;
    }

    internal static IReadOnlyList<ReviewSummaryFinding> ExtractMergeBlockerFindings(string? body, ReviewSettings? settings = null,
        int maxItems = 10) {
        return ExtractMergeBlockerFindings(body, settings, maxItems, out _);
    }

    internal static IReadOnlyList<ReviewSummaryFinding> ExtractMergeBlockerFindings(string? body, ReviewSettings? settings,
        int maxItems, out bool hitLimit) {
        return ExtractMergeBlockerFindings(body, settings, maxItems, out hitLimit, out _);
    }

    internal static IReadOnlyList<ReviewSummaryFinding> ExtractMergeBlockerFindings(string? body, ReviewSettings? settings,
        int maxItems, out bool hitLimit, out bool parseIncomplete) {
        var findings = new List<ReviewSummaryFinding>();
        hitLimit = false;
        parseIncomplete = false;
        if (string.IsNullOrWhiteSpace(body) || maxItems <= 0) {
            return findings;
        }

        body = ReviewFormatter.NormalizeSectionLayout(body);
        var configuredSections = settings?.ResolveMergeBlockerSections()
                                 ?? new[] { "todo list", "critical issues" };
        var mergeBlockerSections = ReviewSettings.NormalizeMergeBlockerSections(configuredSections);
        if (mergeBlockerSections.Count == 0) {
            mergeBlockerSections = new[] { "todo list", "critical issues" };
        }

        var currentSection = string.Empty;
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) {
                currentSection = MatchConfiguredSection(trimmed, mergeBlockerSections);
                continue;
            }

            if (currentSection.Length == 0) {
                continue;
            }

            if (!TryNormalizeFinding(trimmed, currentSection, out var finding)) {
                if (LooksLikeMergeBlockerEntry(trimmed)) {
                    parseIncomplete = true;
                }
                continue;
            }

            if (findings.Count >= maxItems) {
                hitLimit = true;
                break;
            }

            findings.Add(finding);
        }

        return findings;
    }

    private static string NormalizeHeader(string header) {
        var trimmed = header.Trim();
        while (trimmed.StartsWith("#", StringComparison.Ordinal)) {
            trimmed = trimmed.Substring(1).TrimStart();
        }

        var sb = new StringBuilder(trimmed.Length);
        var pendingSpace = false;
        foreach (var ch in trimmed.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                if (pendingSpace && sb.Length > 0) {
                    sb.Append(' ');
                }
                sb.Append(ch);
                pendingSpace = false;
            } else {
                pendingSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static string MatchConfiguredSection(string header, IReadOnlyList<string> configuredSections) {
        var normalizedHeader = NormalizeHeader(header);
        foreach (var section in configuredSections) {
            var normalizedSection = NormalizeHeader(section);
            if (normalizedHeader.Equals(normalizedSection, StringComparison.OrdinalIgnoreCase) ||
                normalizedHeader.StartsWith(normalizedSection + " ", StringComparison.OrdinalIgnoreCase)) {
                return section;
            }
        }
        return string.Empty;
    }

    private static bool SectionHasMergeBlockerItems(string section) {
        if (string.IsNullOrWhiteSpace(section)) {
            return false;
        }

        using var reader = new StringReader(section);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) {
                continue;
            }
            if (trimmed.StartsWith("<!--", StringComparison.Ordinal)) {
                continue;
            }
            if (IsNoneLine(trimmed)) {
                continue;
            }
            if (IsPlaceholderLine(trimmed)) {
                continue;
            }
            if (trimmed.StartsWith("*Rationale:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("*Why", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal)) {
                return !IsNoneLine(trimmed) && !IsPlaceholderLine(trimmed);
            }
            if (trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
                // Checked checklist items represent already-addressed work and should not block merge.
                continue;
            }
            if (trimmed.StartsWith("-", StringComparison.Ordinal)) {
                return !IsNoneLine(trimmed) && !IsPlaceholderLine(trimmed);
            }
        }

        return false;
    }

    private static bool TryNormalizeItem(string line, out string normalizedItem) {
        normalizedItem = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        return TryNormalizeFinding(line, "finding", out _, out normalizedItem, out _);
    }

    private static bool TryNormalizeFinding(string line, string section, out ReviewSummaryFinding finding) {
        if (TryNormalizeFinding(line, section, out finding, out _, out _)) {
            return true;
        }

        finding = default;
        return false;
    }

    private static bool TryNormalizeFinding(string line, string section, out ReviewSummaryFinding finding,
        out string normalizedItem, out string status) {
        finding = default;
        normalizedItem = string.Empty;
        status = string.Empty;
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("<!--", StringComparison.Ordinal)) {
            return false;
        }
        if (IsNoneLine(trimmed) || IsPlaceholderLine(trimmed)) {
            return false;
        }
        if (trimmed.StartsWith("*Rationale:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("*Why", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (trimmed.StartsWith("- [ ]", StringComparison.Ordinal)) {
            normalizedItem = trimmed.Substring(5).Trim();
            status = "open";
        } else if (trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
            normalizedItem = trimmed.Substring(5).Trim();
            status = "resolved";
        } else if (trimmed.StartsWith("-", StringComparison.Ordinal)) {
            normalizedItem = trimmed.Substring(1).Trim();
            status = "open";
        } else {
            return false;
        }

        if (normalizedItem.Length == 0 || IsNoneLine(normalizedItem) || IsPlaceholderLine(normalizedItem)) {
            return false;
        }

        finding = new ReviewSummaryFinding(CreateFindingFingerprint(section, normalizedItem), section, normalizedItem, status);
        return true;
    }

    private static bool LooksLikeMergeBlockerEntry(string line) {
        if (string.IsNullOrWhiteSpace(line)) {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("<!--", StringComparison.Ordinal)) {
            return false;
        }
        if (IsNoneLine(trimmed) || IsPlaceholderLine(trimmed)) {
            return false;
        }
        if (trimmed.StartsWith("*Rationale:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("*Why", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        if (trimmed.StartsWith("-", StringComparison.Ordinal) ||
            IsStarredBulletEntry(trimmed) ||
            trimmed.StartsWith("[ ]", StringComparison.Ordinal) ||
            trimmed.StartsWith("[x]", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (!char.IsDigit(trimmed[0])) {
            return false;
        }

        var markerIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
        return markerIndex > 0 && markerIndex <= 2;
    }

    private static bool IsStarredBulletEntry(string line) {
        if (!line.StartsWith("*", StringComparison.Ordinal) || line.Length < 2 || !char.IsWhiteSpace(line[1])) {
            return false;
        }

        var index = 1;
        while (index < line.Length && char.IsWhiteSpace(line[index])) {
            index++;
        }

        return index < line.Length;
    }

    private static string CreateFindingFingerprint(string section, string text) {
        var sectionSlug = Slugify(section, 18);
        var textSlug = Slugify(text, 36);
        var hash = ComputeFnv1a32($"{NormalizeHeader(section)}\n{text.Trim()}");
        return $"{sectionSlug}-{textSlug}-{hash:x8}";
    }

    private static string Slugify(string value, int maxChars) {
        if (string.IsNullOrWhiteSpace(value) || maxChars <= 0) {
            return "item";
        }

        var chars = new char[maxChars];
        var length = 0;
        var lastWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch)) {
                if (length >= maxChars) {
                    break;
                }
                chars[length++] = ch;
                lastWasDash = false;
                continue;
            }

            if (length == 0 || lastWasDash) {
                continue;
            }
            if (length >= maxChars) {
                break;
            }
            chars[length++] = '-';
            lastWasDash = true;
        }

        while (length > 0 && chars[length - 1] == '-') {
            length--;
        }

        return length == 0 ? "item" : new string(chars, 0, length);
    }

    private static uint ComputeFnv1a32(string value) {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in value) {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }

    private static bool IsNoneLine(string line) {
        var normalized = line.Trim().Trim('*').Trim();
        if (normalized.StartsWith("- [ ]", StringComparison.Ordinal)) {
            normalized = normalized.Substring(5).Trim();
        } else if (normalized.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(5).Trim();
        } else if (normalized.StartsWith("-", StringComparison.Ordinal)) {
            normalized = normalized.Substring(1).Trim();
        }

        return string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "none.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderLine(string line) {
        var normalized = line.Trim().Trim('*').Trim();
        return string.Equals(normalized, "(if any)", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "if any", StringComparison.OrdinalIgnoreCase);
    }
}
