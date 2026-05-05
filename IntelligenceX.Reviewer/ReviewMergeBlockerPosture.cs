using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewMergeBlockerPosture {
    public IReadOnlyList<ReviewSummaryFinding> Findings { get; private init; } = Array.Empty<ReviewSummaryFinding>();
    public bool FindingsHitLimit { get; private init; }
    public bool FindingsParseIncomplete { get; private init; }
    public bool HasAnyMergeBlockerSection { get; private init; }
    public bool HasMergeBlockers { get; private init; }
    public int OpenFindingCount { get; private init; }

    public bool MissingRequiredSections =>
        !HasAnyMergeBlockerSection && (Settings.MergeBlockerRequireSectionMatch || Settings.MergeBlockerRequireAllSections);

    private ReviewSettings Settings { get; init; } = new();

    public static ReviewMergeBlockerPosture Build(string? reviewBody, ReviewSettings settings, int maxItems) {
        ArgumentNullException.ThrowIfNull(settings);

        var findings = ReviewSummaryParser.ExtractMergeBlockerFindings(reviewBody, settings, maxItems,
            out var hitLimit, out var parseIncomplete);
        return new ReviewMergeBlockerPosture {
            Settings = settings,
            Findings = findings,
            FindingsHitLimit = hitLimit,
            FindingsParseIncomplete = parseIncomplete,
            HasAnyMergeBlockerSection = ReviewSummaryParser.HasAnyMergeBlockerSection(reviewBody, settings),
            HasMergeBlockers = ReviewSummaryParser.HasMergeBlockers(reviewBody, settings),
            OpenFindingCount = findings.Count(finding =>
                string.Equals(finding.Status, "open", StringComparison.OrdinalIgnoreCase))
        };
    }

    public bool HasHistoryMergeBlockers() {
        if (!HasMergeBlockers) {
            return false;
        }

        return Findings.Count != 0 || FindingsParseIncomplete || !HasAnyMergeBlockerSection;
    }

    public string ResolveHistoryRecommendation() {
        if (FindingsParseIncomplete) {
            return "manual-review";
        }

        return HasHistoryMergeBlockers() ? "needs-work" : "approve";
    }

    public string FormatHistoryMergeBlockerStatus() {
        var hasHistoryMergeBlockers = HasHistoryMergeBlockers();
        if (Findings.Count == 0) {
            if (FindingsParseIncomplete) {
                return "unknown; merge-blocker lines were present but could not be normalized.";
            }

            return hasHistoryMergeBlockers
                ? "present, but markdown items could not be normalized."
                : "none.";
        }

        return FindingsParseIncomplete
            ? $"{Findings.Count} normalized item(s), but additional merge-blocker lines could not be normalized."
            : $"{Findings.Count} normalized item(s).";
    }
}
