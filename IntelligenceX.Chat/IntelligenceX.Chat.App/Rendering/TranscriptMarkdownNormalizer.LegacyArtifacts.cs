using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Rendering;

internal static partial class TranscriptMarkdownNormalizer {
    private static bool RequiresLegacyTranscriptRepair(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        return LegacyRepairSignalRegex.IsMatch(text)
               || text.Contains("****", StringComparison.Ordinal)
               || text.IndexOf("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase) >= 0
               || LegacyToolHeadingBulletRegex.IsMatch(text)
               || LegacyToolSlugHeadingRegex.IsMatch(text)
               || StandaloneHashSeparatorBeforeHeadingSignalRegex.IsMatch(text)
               || text.Contains("**Result\n", StringComparison.Ordinal)
               || ContainsLegacyJsonVisualFenceCandidate(text);
    }

    private static string StripInternalTransportMarkers(string text) {
        if (string.IsNullOrEmpty(text)
            || text.IndexOf("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase) < 0) {
            return text;
        }

        return CachedToolEvidenceMarkerLineRegex.Replace(text, string.Empty);
    }

    private static string NormalizeLegacyToolHeadingArtifacts(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var rewritten = new List<string>(lines.Length);
        var changed = false;

        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            var bulletMatch = LegacyToolHeadingBulletRegex.Match(current);
            if (bulletMatch.Success) {
                rewritten.Add(bulletMatch.Groups["heading"].Value.Trim());
                changed = true;
                continue;
            }

            var slugMatch = LegacyToolSlugHeadingRegex.Match(current);
            if (slugMatch.Success && TryFindNextNonEmptyLine(lines, i + 1, out var nextIndex)) {
                var next = lines[nextIndex] ?? string.Empty;
                if (IsMarkdownHeadingLine(next)) {
                    changed = true;
                    continue;
                }
            }

            rewritten.Add(current);
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", rewritten);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static string RemoveStandaloneHashSeparatorsBeforeHeadings(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf('#', StringComparison.Ordinal) < 0) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var rewritten = new List<string>(lines.Length);
        var changed = false;

        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            if (StandaloneSingleHashSeparatorRegex.IsMatch(current)
                && TryFindNextNonEmptyLine(lines, i + 1, out var nextIndex)
                && IsMarkdownHeadingLine(lines[nextIndex] ?? string.Empty)) {
                changed = true;
                continue;
            }

            rewritten.Add(current);
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", rewritten);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }

    private static bool IsMarkdownHeadingLine(string line) {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 4 || trimmed[0] != '#') {
            return false;
        }

        var depth = 0;
        while (depth < trimmed.Length && trimmed[depth] == '#') {
            depth++;
        }

        return depth is >= 2 and <= 6
               && depth < trimmed.Length
               && char.IsWhiteSpace(trimmed[depth]);
    }

    private static bool TryFindNextNonEmptyLine(string[] lines, int startIndex, out int index) {
        for (var i = startIndex; i < lines.Length; i++) {
            if (!string.IsNullOrWhiteSpace(lines[i])) {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static string RepairBrokenTwoLineStrongLeadIns(string text) {
        if (string.IsNullOrEmpty(text) || text.IndexOf("**", StringComparison.Ordinal) < 0) {
            return text;
        }

        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var rewritten = new List<string>(lines.Length);
        var changed = false;

        for (var i = 0; i < lines.Length; i++) {
            var current = lines[i] ?? string.Empty;
            if (i + 1 < lines.Length) {
                var currentMatch = BrokenTwoLineStrongLeadInRegex.Match(current);
                if (currentMatch.Success) {
                    var next = lines[i + 1] ?? string.Empty;
                    var closingIndex = next.IndexOf("**", StringComparison.Ordinal);
                    if (closingIndex > 0) {
                        var label = currentMatch.Groups["label"].Value.Trim().TrimEnd(':');
                        var body = next[..closingIndex].Trim();
                        var tail = next[(closingIndex + 2)..].Trim();
                        if (label.Length > 0
                            && body.Length > 0
                            && !StructuralMarkdownLineRegex.IsMatch(body)) {
                            var merged = currentMatch.Groups["indent"].Value
                                         + "**" + label + ":** "
                                         + body
                                         + (tail.Length == 0 ? string.Empty : " " + tail);
                            rewritten.Add(merged);
                            changed = true;
                            i++;
                            continue;
                        }
                    }
                }
            }

            rewritten.Add(current);
        }

        if (!changed) {
            return text;
        }

        var rebuilt = string.Join("\n", rewritten);
        return hasCrLf ? rebuilt.Replace("\n", "\r\n", StringComparison.Ordinal) : rebuilt;
    }
}
