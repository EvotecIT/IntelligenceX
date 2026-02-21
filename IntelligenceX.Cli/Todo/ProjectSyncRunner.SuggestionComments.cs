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
    internal static IReadOnlyDictionary<int, string> BuildPullRequestIssueSuggestionComments(
        IReadOnlyList<ProjectSyncEntry> entries,
        double minConfidence,
        int maxIssuesPerPullRequest) {
        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var pullRequestToCandidates = new Dictionary<int, Dictionary<int, RelatedIssueCandidate>>();

        static void AddPullRequestCandidate(
            IDictionary<int, Dictionary<int, RelatedIssueCandidate>> pullRequestMap,
            int pullRequestNumber,
            RelatedIssueCandidate candidate) {
            if (pullRequestNumber <= 0 || candidate.Number <= 0 || string.IsNullOrWhiteSpace(candidate.Url)) {
                return;
            }

            if (!pullRequestMap.TryGetValue(pullRequestNumber, out var issueMap)) {
                issueMap = new Dictionary<int, RelatedIssueCandidate>();
                pullRequestMap[pullRequestNumber] = issueMap;
            }

            if (issueMap.TryGetValue(candidate.Number, out var existing) &&
                existing.Confidence >= candidate.Confidence) {
                return;
            }

            issueMap[candidate.Number] = candidate;
        }

        foreach (var entry in entries) {
            if (entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && entry.Number > 0) {
                var relatedIssues = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
                    .Where(candidate => candidate.Number > 0 &&
                                        candidate.Confidence >= threshold &&
                                        !string.IsNullOrWhiteSpace(candidate.Url))
                    .ToList();

                foreach (var candidate in relatedIssues) {
                    AddPullRequestCandidate(pullRequestToCandidates, entry.Number, new RelatedIssueCandidate(
                        Number: candidate.Number,
                        Url: candidate.Url,
                        Confidence: candidate.Confidence,
                        Reason: candidate.Reason
                    ));
                }

                if (!string.IsNullOrWhiteSpace(entry.MatchedIssueUrl) &&
                    entry.MatchedIssueConfidence.HasValue &&
                    entry.MatchedIssueConfidence.Value >= threshold) {
                    var (kind, issueNumber) = ParseKindAndNumberFromUrl(entry.MatchedIssueUrl);
                    if (kind.Equals("issue", StringComparison.OrdinalIgnoreCase) && issueNumber > 0) {
                        AddPullRequestCandidate(pullRequestToCandidates, entry.Number, new RelatedIssueCandidate(
                            Number: issueNumber,
                            Url: entry.MatchedIssueUrl,
                            Confidence: entry.MatchedIssueConfidence.Value,
                            Reason: "matched issue"
                        ));
                    }
                }

                continue;
            }

            if (!entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            var relatedPullRequests = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
                .Where(candidate => candidate.Number > 0 &&
                                    candidate.Confidence >= threshold &&
                                    !string.IsNullOrWhiteSpace(candidate.Url))
                .ToList();

            foreach (var candidate in relatedPullRequests) {
                AddPullRequestCandidate(pullRequestToCandidates, candidate.Number, new RelatedIssueCandidate(
                    Number: entry.Number,
                    Url: entry.Url,
                    Confidence: candidate.Confidence,
                    Reason: candidate.Reason
                ));
            }

            if (!string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl) &&
                entry.MatchedPullRequestConfidence.HasValue &&
                entry.MatchedPullRequestConfidence.Value >= threshold) {
                var (kind, pullRequestNumber) = ParseKindAndNumberFromUrl(entry.MatchedPullRequestUrl);
                if (kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && pullRequestNumber > 0) {
                    AddPullRequestCandidate(pullRequestToCandidates, pullRequestNumber, new RelatedIssueCandidate(
                        Number: entry.Number,
                        Url: entry.Url,
                        Confidence: entry.MatchedPullRequestConfidence.Value,
                        Reason: "issue-side matched pull request"
                    ));
                }
            }
        }

        var comments = new Dictionary<int, string>();
        foreach (var pullRequestCandidates in pullRequestToCandidates) {
            var comment = BuildIssueMatchSuggestionComment(
                pullRequestCandidates.Key,
                pullRequestCandidates.Value.Values.ToList(),
                threshold,
                maxIssuesPerPullRequest);
            if (!string.IsNullOrWhiteSpace(comment)) {
                comments[pullRequestCandidates.Key] = comment;
            }
        }

        return comments;
    }

    internal static IReadOnlyList<int> BuildStaleSuggestionCommentTargets(
        IReadOnlyList<ProjectSyncEntry> entries,
        string kind,
        IReadOnlyDictionary<int, string> activeComments) {
        if (string.IsNullOrWhiteSpace(kind) || entries.Count == 0) {
            return Array.Empty<int>();
        }

        var activeNumbers = new HashSet<int>(activeComments.Keys);
        return entries
            .Where(entry => entry.Number > 0 &&
                            entry.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                            !activeNumbers.Contains(entry.Number))
            .Select(entry => entry.Number)
            .Distinct()
            .OrderBy(number => number)
            .ToList();
    }

    internal static string? BuildIssueMatchSuggestionComment(ProjectSyncEntry entry, double minConfidence, int maxIssues) {
        if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
            return null;
        }

        return BuildIssueMatchSuggestionComment(
            entry.Number,
            entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>(),
            minConfidence,
            maxIssues);
    }

    internal static string? BuildIssueMatchSuggestionComment(
        int pullRequestNumber,
        IReadOnlyList<RelatedIssueCandidate> candidates,
        double minConfidence,
        int maxIssues) {
        if (pullRequestNumber <= 0 || candidates.Count == 0) {
            return null;
        }

        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var limit = Math.Max(1, Math.Min(maxIssues, 10));
        var related = candidates
            .Where(candidate => candidate.Number > 0 &&
                                candidate.Confidence >= threshold &&
                                !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();
        if (related.Count == 0) {
            return null;
        }

        var lines = new List<string> {
            IssueSuggestionCommentManager.CommentMarker,
            "### IntelligenceX Related Issue Suggestions",
            string.Empty,
            $"Automated match candidates (confidence >= {threshold.ToString("0.00", CultureInfo.InvariantCulture)}). Please verify before linking/closing.",
            string.Empty
        };

        foreach (var candidate in related) {
            var reason = NormalizeCommentReason(candidate.Reason);
            lines.Add($"- #{candidate.Number} ({candidate.Url}) - confidence {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} - {reason}");
        }

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    internal static IReadOnlyDictionary<int, string> BuildIssueBacklinkSuggestionComments(
        IReadOnlyList<ProjectSyncEntry> entries,
        double minConfidence,
        int maxPullRequestsPerIssue) {
        var threshold = Math.Clamp(minConfidence, 0.0, 1.0);
        var issueToCandidates = new Dictionary<int, Dictionary<int, RelatedPullRequestCandidate>>();

        static void AddIssueCandidate(
            IDictionary<int, Dictionary<int, RelatedPullRequestCandidate>> issueMap,
            int issueNumber,
            RelatedPullRequestCandidate candidate) {
            if (issueNumber <= 0 || candidate.Number <= 0 || string.IsNullOrWhiteSpace(candidate.Url)) {
                return;
            }

            if (!issueMap.TryGetValue(issueNumber, out var pullRequestMap)) {
                pullRequestMap = new Dictionary<int, RelatedPullRequestCandidate>();
                issueMap[issueNumber] = pullRequestMap;
            }

            if (pullRequestMap.TryGetValue(candidate.Number, out var existing) &&
                existing.Confidence >= candidate.Confidence) {
                return;
            }

            pullRequestMap[candidate.Number] = candidate;
        }

        foreach (var entry in entries) {
            if (entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && entry.Number > 0) {
                var related = (entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>())
                    .Where(candidate => candidate.Number > 0 &&
                                        candidate.Confidence >= threshold &&
                                        !string.IsNullOrWhiteSpace(candidate.Url))
                    .ToList();
                foreach (var candidate in related) {
                    AddIssueCandidate(issueToCandidates, candidate.Number, new RelatedPullRequestCandidate(
                        Number: entry.Number,
                        Url: entry.Url,
                        Confidence: candidate.Confidence,
                        Reason: candidate.Reason
                    ));
                }
                continue;
            }

            if (!entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            var relatedPullRequests = (entry.RelatedPullRequests ?? Array.Empty<RelatedPullRequestCandidate>())
                .Where(candidate => candidate.Number > 0 &&
                                    candidate.Confidence >= threshold &&
                                    !string.IsNullOrWhiteSpace(candidate.Url))
                .ToList();

            foreach (var candidate in relatedPullRequests) {
                AddIssueCandidate(issueToCandidates, entry.Number, new RelatedPullRequestCandidate(
                    Number: candidate.Number,
                    Url: candidate.Url,
                    Confidence: candidate.Confidence,
                    Reason: candidate.Reason
                ));
            }

            if (!string.IsNullOrWhiteSpace(entry.MatchedPullRequestUrl) &&
                entry.MatchedPullRequestConfidence.HasValue &&
                entry.MatchedPullRequestConfidence.Value >= threshold) {
                var (kind, pullRequestNumber) = ParseKindAndNumberFromUrl(entry.MatchedPullRequestUrl);
                if (kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) && pullRequestNumber > 0) {
                    AddIssueCandidate(issueToCandidates, entry.Number, new RelatedPullRequestCandidate(
                        Number: pullRequestNumber,
                        Url: entry.MatchedPullRequestUrl,
                        Confidence: entry.MatchedPullRequestConfidence.Value,
                        Reason: "issue-side matched pull request"
                    ));
                }
            }
        }

        var comments = new Dictionary<int, string>();
        foreach (var issueCandidates in issueToCandidates) {
            var candidates = issueCandidates.Value.Values.ToList();
            var comment = BuildIssueBacklinkSuggestionComment(
                issueCandidates.Key,
                candidates,
                threshold,
                maxPullRequestsPerIssue);
            if (!string.IsNullOrWhiteSpace(comment)) {
                comments[issueCandidates.Key] = comment;
            }
        }

        return comments;
    }

    internal static string? BuildIssueBacklinkSuggestionComment(
        int issueNumber,
        IReadOnlyList<RelatedPullRequestCandidate> candidates,
        double minConfidence,
        int maxPullRequests) {
        if (issueNumber <= 0 || candidates.Count == 0) {
            return null;
        }

        var limit = Math.Max(1, Math.Min(maxPullRequests, 10));
        var filtered = candidates
            .Where(candidate => candidate.Number > 0 &&
                                candidate.Confidence >= minConfidence &&
                                !string.IsNullOrWhiteSpace(candidate.Url))
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(limit)
            .ToList();
        if (filtered.Count == 0) {
            return null;
        }

        var lines = new List<string> {
            IssueSuggestionCommentManager.IssueBacklinkCommentMarker,
            $"### IntelligenceX Related Pull Request Suggestions for #{issueNumber.ToString(CultureInfo.InvariantCulture)}",
            string.Empty,
            $"Automated PR candidates (confidence >= {minConfidence.ToString("0.00", CultureInfo.InvariantCulture)}). Please verify before linking/closing.",
            string.Empty
        };

        foreach (var candidate in filtered) {
            var reason = NormalizeCommentReason(candidate.Reason);
            lines.Add($"- PR #{candidate.Number} ({candidate.Url}) - confidence {candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} - {reason}");
        }

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static string NormalizeCommentReason(string reason) {
        if (string.IsNullOrWhiteSpace(reason)) {
            return "token similarity";
        }

        var compact = string.Join(" ", reason
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (compact.Length <= 140) {
            return compact;
        }
        return compact[..137] + "...";
    }

}
