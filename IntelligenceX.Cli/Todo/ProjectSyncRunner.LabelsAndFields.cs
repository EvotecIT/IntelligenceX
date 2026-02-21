using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class ProjectSyncRunner {
    internal static IReadOnlyList<string> BuildLabelsForEntry(ProjectSyncEntry entry) {
        var labels = new List<string>();
        var isPullRequest = entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase);
        var isIssue = entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase);

        if (ShouldApplyCategoryLabel(entry) &&
            ProjectLabelCatalog.TryMapCategoryLabel(entry.Category ?? string.Empty, out var categoryLabel)) {
            labels.Add(categoryLabel);
        }

        foreach (var tag in entry.Tags) {
            if (ShouldApplyTagLabel(entry, tag) &&
                ProjectLabelCatalog.TryMapTagLabel(tag, out var tagLabel)) {
                labels.Add(tagLabel);
            }
        }

        var visionLabel = MapVisionLabel(entry.VisionFit);
        if (!string.IsNullOrWhiteSpace(visionLabel) && isPullRequest) {
            labels.Add(visionLabel);
        }

        var relatedTopCandidate = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .FirstOrDefault();

        var effectiveMatchedIssueUrl = !string.IsNullOrWhiteSpace(entry.MatchedIssueUrl)
            ? entry.MatchedIssueUrl
            : relatedTopCandidate?.Url;
        var effectiveMatchedIssueConfidence = entry.MatchedIssueConfidence ??
                                              relatedTopCandidate?.Confidence;

        if (!string.IsNullOrWhiteSpace(effectiveMatchedIssueUrl) && isPullRequest) {
            if (effectiveMatchedIssueConfidence.HasValue &&
                effectiveMatchedIssueConfidence.Value >= HighConfidenceIssueMatchLabelThreshold) {
                labels.Add("ix/match:linked-issue");
            } else {
                labels.Add("ix/match:needs-review");
            }
        }

        var relatedPullRequestTopCandidate = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .FirstOrDefault();

        var effectiveMatchedPullRequestUrl = !string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl)
            ? entry.MatchedPullRequestUrl
            : relatedPullRequestTopCandidate?.Url;
        var effectiveMatchedPullRequestConfidence = entry.MatchedPullRequestConfidence ??
                                                    relatedPullRequestTopCandidate?.Confidence;

        if (!string.IsNullOrWhiteSpace(effectiveMatchedPullRequestUrl) && isIssue) {
            if (effectiveMatchedPullRequestConfidence.HasValue &&
                effectiveMatchedPullRequestConfidence.Value >= HighConfidencePullRequestMatchLabelThreshold) {
                labels.Add("ix/match:linked-pr");
            } else {
                labels.Add("ix/match:needs-review-pr");
            }
        }

        var decisionLabel = MapSuggestedDecisionLabel(entry.SuggestedDecision);
        if (!string.IsNullOrWhiteSpace(decisionLabel) && isPullRequest) {
            labels.Add(decisionLabel);
        }

        if (IsLowSignalQuality(entry)) {
            labels.Add("ix/signal:low");
        }

        if (!string.IsNullOrWhiteSpace(entry.DuplicateCluster)) {
            labels.Add("ix/duplicate:clustered");
        }

        return labels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldApplyCategoryLabel(ProjectSyncEntry entry) {
        if (!entry.CategoryConfidence.HasValue) {
            // Backward-compatible default for legacy triage artifacts without confidence fields.
            return true;
        }

        return entry.CategoryConfidence.Value >= CategoryLabelConfidenceThreshold;
    }

    private static bool ShouldApplyTagLabel(ProjectSyncEntry entry, string tag) {
        if (string.IsNullOrWhiteSpace(tag)) {
            return false;
        }

        if (entry.TagConfidences is null ||
            !entry.TagConfidences.TryGetValue(tag, out var confidence)) {
            // Backward-compatible default for legacy triage artifacts without confidence fields.
            return true;
        }

        return confidence >= TagLabelConfidenceThreshold;
    }

    private static (IReadOnlyList<string> Categories, IReadOnlyList<string> Tags) BuildLabelTaxonomyForEntries(
        IReadOnlyList<ProjectSyncEntry> entries) {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            foreach (var label in BuildLabelsForEntry(entry)) {
                if (label.StartsWith("ix/category:", StringComparison.OrdinalIgnoreCase)) {
                    categories.Add(label["ix/category:".Length..]);
                    continue;
                }

                if (label.StartsWith("ix/tag:", StringComparison.OrdinalIgnoreCase)) {
                    tags.Add(label["ix/tag:".Length..]);
                }
            }
        }

        return (
            categories.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
            tags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static string BuildRelatedIssuesFieldValue(ProjectSyncEntry entry, int maxIssues) {
        var limit = Math.Max(1, Math.Min(maxIssues, 10));
        var related = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();
        if (related.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, related.Select(candidate =>
            $"#{candidate.Number.ToString(CultureInfo.InvariantCulture)} | {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {candidate.Url}"));
    }

    internal static string BuildMatchReasonFieldValue(string? reason) {
        if (string.IsNullOrWhiteSpace(reason)) {
            return string.Empty;
        }

        return NormalizeCommentReason(reason);
    }

    internal static string BuildTagConfidenceSummaryFieldValue(ProjectSyncEntry entry, int maxTags) {
        var limit = Math.Max(1, Math.Min(maxTags, 20));
        if (entry.TagConfidences is null || entry.TagConfidences.Count == 0) {
            return string.Empty;
        }

        var normalized = entry.TagConfidences
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, double>(pair.Key.Trim(), Math.Clamp(pair.Value, 0, 1)))
            .ToList();
        if (normalized.Count == 0) {
            return string.Empty;
        }

        var tagSet = entry.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = tagSet.Count > 0
            ? normalized.Where(pair => tagSet.Contains(pair.Key)).ToList()
            : normalized;
        if (selected.Count == 0) {
            selected = normalized;
        }

        var summaryLines = selected
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(pair => $"{pair.Key}: {pair.Value.ToString("0.00", CultureInfo.InvariantCulture)}")
            .ToList();
        if (summaryLines.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, summaryLines);
    }

    internal static string BuildSignalQualityNotesFieldValue(ProjectSyncEntry entry, int maxReasons) {
        var limit = Math.Max(1, Math.Min(maxReasons, 10));
        var reasons = (entry.SignalQualityReasons ?? Array.Empty<string>())
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
        if (reasons.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, reasons);
    }

    internal static string BuildRelatedPullRequestsFieldValue(ProjectSyncEntry entry, int maxPullRequests) {
        var limit = Math.Max(1, Math.Min(maxPullRequests, 10));
        var related = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
            .Where(candidate => candidate.Number > 0 && !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();

        if (related.Count == 0 &&
            !string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl) &&
            entry.MatchedPullRequestConfidence.HasValue) {
            var (_, matchedNumber) = ParseKindAndNumberFromUrl(entry.MatchedPullRequestUrl);
            if (matchedNumber > 0) {
                related.Add(new RelatedPullRequestCandidate(
                    Number: matchedNumber,
                    Url: entry.MatchedPullRequestUrl,
                    Confidence: entry.MatchedPullRequestConfidence.Value,
                    Reason: "issue-side matched pull request"
                ));
            }
        }

        if (related.Count == 0) {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, related.Select(candidate =>
            $"PR #{candidate.Number.ToString(CultureInfo.InvariantCulture)} | {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {candidate.Url}"));
    }

    private static string? MapVisionLabel(string? visionFit) {
        return visionFit?.ToLowerInvariant() switch {
            "aligned" => "ix/vision:aligned",
            "needs-human-review" => "ix/vision:needs-review",
            "likely-out-of-scope" => "ix/vision:out-of-scope",
            _ => null
        };
    }

    private static string? MapSuggestedDecisionLabel(string? suggestedDecision) {
        return suggestedDecision?.ToLowerInvariant() switch {
            "accept" => "ix/decision:accept",
            "defer" => "ix/decision:defer",
            "reject" => "ix/decision:reject",
            "merge-candidate" => "ix/decision:merge-candidate",
            _ => null
        };
    }

    private static bool TryResolveOptionId(ProjectV2Client.ProjectField field, string optionName, out string optionId) {
        optionId = string.Empty;
        foreach (var option in field.OptionsByName) {
            if (option.Key.Equals(optionName, StringComparison.OrdinalIgnoreCase)) {
                optionId = option.Value;
                return true;
            }
        }
        return false;
    }

}
