using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class TriageIndexRunner {
    private static List<TriageIndexItem> BuildItems(IReadOnlyList<RawPullRequest> pullRequests, IReadOnlyList<RawIssue> issues) {
        var items = new List<TriageIndexItem>(pullRequests.Count + issues.Count);
        foreach (var pr in pullRequests) {
            var normalizedTitle = NormalizeText(pr.Title);
            var titleTokens = Tokenize(pr.Title);
            var contextTokens = Tokenize($"{pr.Title}\n{pr.Body}");
            var signals = new PullRequestSignals(pr.IsDraft, pr.Mergeable, pr.ReviewDecision, pr.StatusCheckState, pr.ChangedFiles,
                pr.Additions, pr.Deletions, pr.Comments, pr.Commits, pr.Author);
            items.Add(new TriageIndexItem(
                Id: $"pr#{pr.Number}",
                Kind: "pull_request",
                Number: pr.Number,
                Title: pr.Title,
                Url: pr.Url,
                UpdatedAtUtc: pr.UpdatedAtUtc,
                Labels: pr.Labels,
                NormalizedTitle: normalizedTitle,
                TitleTokens: titleTokens,
                ContextTokens: contextTokens,
                PullRequest: signals,
                Issue: null
            ));
        }

        foreach (var issue in issues) {
            var normalizedTitle = NormalizeText(issue.Title);
            var titleTokens = Tokenize(issue.Title);
            var contextTokens = Tokenize($"{issue.Title}\n{issue.Body}");
            var signals = new IssueSignals(issue.Comments, issue.Author);
            items.Add(new TriageIndexItem(
                Id: $"issue#{issue.Number}",
                Kind: "issue",
                Number: issue.Number,
                Title: issue.Title,
                Url: issue.Url,
                UpdatedAtUtc: issue.UpdatedAtUtc,
                Labels: issue.Labels,
                NormalizedTitle: normalizedTitle,
                TitleTokens: titleTokens,
                ContextTokens: contextTokens,
                PullRequest: null,
                Issue: signals
            ));
        }

        return items;
    }

    private static Dictionary<string, ItemEnrichment> BuildItemEnrichments(
        string repo,
        IReadOnlyList<TriageIndexItem> items,
        IReadOnlyList<RawPullRequest> pullRequests,
        IReadOnlyList<RawIssue> issues) {
        var enrichments = new Dictionary<string, ItemEnrichment>(StringComparer.OrdinalIgnoreCase);
        var issueItems = items
            .Where(item => item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pullRequestItems = items
            .Where(item => item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var rawPrByNumber = pullRequests.ToDictionary(pr => pr.Number);
        var rawIssueByNumber = issues.ToDictionary(issue => issue.Number);

        foreach (var item in items) {
            var inference = InferCategoryAndTagsWithConfidence(item);

            IReadOnlyList<RelatedIssueCandidate> relatedIssues = Array.Empty<RelatedIssueCandidate>();
            IReadOnlyList<RelatedPullRequestCandidate> relatedPullRequests = Array.Empty<RelatedPullRequestCandidate>();
            string? matchedIssueUrl = null;
            double? matchedIssueConfidence = null;
            string? matchedPullRequestUrl = null;
            double? matchedPullRequestConfidence = null;

            if (item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
                if (rawPrByNumber.TryGetValue(item.Number, out var rawPr)) {
                    relatedIssues = MatchPullRequestToIssues(repo, rawPr.Title, rawPr.Body, issueItems);
                } else {
                    relatedIssues = MatchPullRequestToIssues(repo, item.Title, string.Join(' ', item.ContextTokens), issueItems);
                }

                if (relatedIssues.Count > 0) {
                    matchedIssueUrl = relatedIssues[0].Url;
                    matchedIssueConfidence = relatedIssues[0].Confidence;
                }
            } else if (item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase)) {
                if (rawIssueByNumber.TryGetValue(item.Number, out var rawIssue)) {
                    relatedPullRequests = MatchIssueToPullRequests(repo, rawIssue.Title, rawIssue.Body, pullRequestItems);
                } else {
                    relatedPullRequests = MatchIssueToPullRequests(repo, item.Title, string.Join(' ', item.ContextTokens), pullRequestItems);
                }

                if (relatedPullRequests.Count > 0) {
                    matchedPullRequestUrl = relatedPullRequests[0].Url;
                    matchedPullRequestConfidence = relatedPullRequests[0].Confidence;
                }
            }

            enrichments[item.Id] = new ItemEnrichment(
                Category: inference.Category,
                CategoryConfidence: inference.CategoryConfidence,
                Tags: inference.Tags,
                TagConfidences: inference.TagConfidences,
                MatchedIssueUrl: matchedIssueUrl,
                MatchedIssueConfidence: matchedIssueConfidence,
                RelatedIssues: relatedIssues,
                MatchedPullRequestUrl: matchedPullRequestUrl,
                MatchedPullRequestConfidence: matchedPullRequestConfidence,
                RelatedPullRequests: relatedPullRequests
            );
        }

        return enrichments;
    }

    internal sealed record CategoryTagInference(
        string Category,
        double CategoryConfidence,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, double> TagConfidences
    );

    internal static (string Category, IReadOnlyList<string> Tags) InferCategoryAndTags(TriageIndexItem item) {
        var inference = InferCategoryAndTagsWithConfidence(item);
        return (inference.Category, inference.Tags);
    }

    internal static SignalQualityAssessment AssessSignalQuality(TriageIndexItem item, ItemEnrichment? enrichment) {
        var score = 40.0;
        var reasons = new List<string>();

        var titleTokenCount = item.TitleTokens.Count;
        if (titleTokenCount >= 6) {
            score += 18;
            reasons.Add("Title contains strong intent detail.");
        } else if (titleTokenCount >= 3) {
            score += 10;
            reasons.Add("Title contains basic intent detail.");
        } else {
            score -= 10;
            reasons.Add("Title is too short for reliable intent inference.");
        }

        var contextTokenCount = item.ContextTokens.Count;
        if (contextTokenCount >= 20) {
            score += 18;
            reasons.Add("Description/context is detailed.");
        } else if (contextTokenCount >= 10) {
            score += 10;
            reasons.Add("Description/context has moderate detail.");
        } else {
            score -= 12;
            reasons.Add("Description/context is sparse.");
        }

        if (item.Labels.Count >= 2) {
            score += 8;
            reasons.Add("Labels provide extra classification evidence.");
        } else if (item.Labels.Count == 1) {
            score += 4;
            reasons.Add("Single label provides limited evidence.");
        } else {
            score -= 5;
            reasons.Add("No labels present.");
        }

        if (item.PullRequest is not null) {
            if (item.PullRequest.ChangedFiles > 0) {
                score += 4;
                reasons.Add("PR change metadata is present.");
            } else {
                score -= 6;
                reasons.Add("PR change metadata is missing.");
            }

            if (!string.IsNullOrWhiteSpace(item.PullRequest.ReviewDecision)) {
                score += 4;
            } else {
                score -= 3;
            }

            if (!string.IsNullOrWhiteSpace(item.PullRequest.StatusCheckState)) {
                score += 4;
            } else {
                score -= 3;
            }

            if (item.PullRequest.Commits > 0) {
                score += 3;
            } else {
                score -= 2;
            }
        } else if (item.Issue is not null) {
            if (item.Issue.Comments >= 2) {
                score += 6;
                reasons.Add("Issue discussion provides additional context.");
            } else if (item.Issue.Comments == 0) {
                score -= 3;
                reasons.Add("Issue has no discussion context.");
            }
        }

        if (enrichment is not null) {
            if (enrichment.CategoryConfidence >= 0.75) {
                score += 8;
                reasons.Add("Category confidence is high.");
            } else if (enrichment.CategoryConfidence >= 0.60) {
                score += 4;
            } else if (enrichment.CategoryConfidence < 0.50) {
                score -= 5;
                reasons.Add("Category confidence is low.");
            }

            var topMatchConfidence = item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)
                ? enrichment.MatchedIssueConfidence
                : enrichment.MatchedPullRequestConfidence;
            if (topMatchConfidence.HasValue) {
                if (topMatchConfidence.Value >= 0.80) {
                    score += 8;
                    reasons.Add("Top related-link confidence is high.");
                } else if (topMatchConfidence.Value >= 0.55) {
                    score += 4;
                }
            }
        }

        score = Math.Round(Math.Clamp(score, 0, 100), 2, MidpointRounding.AwayFromZero);
        var level = score >= 75
            ? "high"
            : score >= 50
                ? "medium"
                : "low";

        return new SignalQualityAssessment(level, score, reasons);
    }

    internal static PullRequestOperationalSignals? AssessPullRequestOperationalSignals(
        TriageIndexItem item,
        DateTimeOffset nowUtc) {
        if (item.PullRequest is null ||
            !item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var signals = item.PullRequest;
        var changedFiles = Math.Max(0, signals.ChangedFiles);
        var changeVolume = Math.Max(0, signals.Additions) + Math.Max(0, signals.Deletions);
        var ageDays = Math.Max(0, (nowUtc - item.UpdatedAtUtc).TotalDays);

        var sizeBand = (changedFiles, changeVolume) switch {
            (<= 3, <= 80) => "xsmall",
            (<= 10, <= 300) => "small",
            (<= 30, <= 900) => "medium",
            (<= 80, <= 2500) => "large",
            _ => "xlarge"
        };

        var blocked = signals.IsDraft ||
                      signals.Mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
                      signals.Mergeable.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
                      signals.ReviewDecision.Equals("CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase) ||
                      signals.StatusCheckState.Equals("FAILURE", StringComparison.OrdinalIgnoreCase) ||
                      signals.StatusCheckState.Equals("ERROR", StringComparison.OrdinalIgnoreCase);

        var mergeReadiness = blocked
            ? "blocked"
            : signals.Mergeable.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase) &&
              signals.ReviewDecision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase) &&
              signals.StatusCheckState.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
                ? "ready"
                : "needs-review";

        var churnRisk = changedFiles >= ChurnHighChangedFilesThreshold ||
                        changeVolume >= ChurnHighChangeVolumeThreshold ||
                        signals.Comments >= ChurnHighCommentsThreshold ||
                        signals.Commits >= ChurnHighCommitsThreshold
            ? "high"
            : changedFiles >= ChurnMediumChangedFilesThreshold ||
              changeVolume >= ChurnMediumChangeVolumeThreshold ||
              signals.Comments >= ChurnMediumCommentsThreshold ||
              signals.Commits >= ChurnMediumCommitsThreshold
                ? "medium"
                : "low";
        if ((signals.Mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
             signals.Mergeable.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)) &&
            churnRisk.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            churnRisk = "medium";
        }

        var freshness = ageDays switch {
            <= 1 => "fresh",
            <= 7 => "recent",
            <= 30 => "aging",
            _ => "stale"
        };

        var checkHealth = signals.StatusCheckState.Trim().ToUpperInvariant() switch {
            "SUCCESS" => "healthy",
            "NEUTRAL" => "healthy",
            "SKIPPED" => "healthy",
            "PENDING" => "pending",
            "EXPECTED" => "pending",
            "IN_PROGRESS" => "pending",
            "QUEUED" => "pending",
            "FAILURE" => "failing",
            "ERROR" => "failing",
            "CANCELLED" => "failing",
            "TIMED_OUT" => "failing",
            "ACTION_REQUIRED" => "failing",
            "STARTUP_FAILURE" => "failing",
            _ => "unknown"
        };

        var reviewLatency = ageDays switch {
            <= ReviewLatencyLowAgeDaysThreshold => "low",
            <= ReviewLatencyMediumAgeDaysThreshold => "medium",
            _ => "high"
        };
        if (mergeReadiness.Equals("ready", StringComparison.OrdinalIgnoreCase)) {
            reviewLatency = ageDays switch {
                <= ReadyReviewLatencyLowAgeDaysThreshold => "low",
                <= ReadyReviewLatencyMediumAgeDaysThreshold => "medium",
                _ => "high"
            };
        } else if (mergeReadiness.Equals("blocked", StringComparison.OrdinalIgnoreCase) &&
                   reviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase) &&
                   ageDays > 2) {
            reviewLatency = "medium";
        }
        if (checkHealth.Equals("pending", StringComparison.OrdinalIgnoreCase) &&
            ageDays > PendingReviewLatencyHighAgeDaysThreshold) {
            reviewLatency = "high";
        }
        if ((signals.Comments >= ChurnHighCommentsThreshold || signals.Commits >= ChurnHighCommitsThreshold) &&
            !reviewLatency.Equals("high", StringComparison.OrdinalIgnoreCase)) {
            reviewLatency = "high";
        } else if ((signals.Comments >= ChurnMediumCommentsThreshold || signals.Commits >= ChurnMediumCommitsThreshold) &&
                   reviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            reviewLatency = "medium";
        }

        var mergeConflictRisk = signals.Mergeable.Trim().ToUpperInvariant() switch {
            "CONFLICTING" => "high",
            "UNKNOWN" => sizeBand is "xlarge" ||
                         churnRisk.Equals("high", StringComparison.OrdinalIgnoreCase) ||
                         ageDays > ConflictRiskHighAgeDaysThreshold
                ? "high"
                : sizeBand is "large" ||
                  ageDays > 7 ||
                  checkHealth is "pending" or "failing"
                    ? "medium"
                    : "low",
            "MERGEABLE" => sizeBand is "xlarge" &&
                           churnRisk.Equals("high", StringComparison.OrdinalIgnoreCase) &&
                           ageDays > ConflictRiskHighAgeDaysThreshold
                ? "high"
                : sizeBand is "large" or "xlarge" ||
                  churnRisk is "medium" or "high" ||
                  ageDays > ConflictRiskMediumAgeDaysThreshold ||
                  checkHealth is "pending" or "failing"
                    ? "medium"
                    : "low",
            _ => sizeBand is "xlarge" || churnRisk.Equals("high", StringComparison.OrdinalIgnoreCase)
                ? "high"
                : sizeBand is "large" || ageDays > 10
                    ? "medium"
                    : "low"
        };

        return new PullRequestOperationalSignals(sizeBand, churnRisk, mergeReadiness, freshness, checkHealth, reviewLatency,
            mergeConflictRisk);
    }

    internal static CategoryTagInference InferCategoryAndTagsWithConfidence(TriageIndexItem item) {
        var tokens = new HashSet<string>(item.ContextTokens, StringComparer.OrdinalIgnoreCase);
        var titleTokens = new HashSet<string>(item.TitleTokens, StringComparer.OrdinalIgnoreCase);
        var labelTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in item.TitleTokens) {
            tokens.Add(token);
        }

        foreach (var label in item.Labels) {
            foreach (var token in Tokenize(label)) {
                tokens.Add(token);
                labelTokens.Add(token);
            }
        }

        static int CountMatches(HashSet<string> source, IReadOnlyList<string> candidates) {
            var matches = 0;
            foreach (var candidate in candidates) {
                if (source.Contains(candidate)) {
                    matches++;
                }
            }
            return matches;
        }

        var securityTokens = new[] { "security", "vulnerability", "vulnerabilities", "auth", "authorization", "xss", "injection", "cve", "secret", "secrets" };
        var bugTokens = new[] { "bug", "bugs", "error", "errors", "failure", "failures", "exception", "exceptions", "crash", "regression", "defect", "defects" };
        var performanceTokens = new[] { "performance", "perf", "latency", "throughput", "memory", "cpu" };
        var docsTokens = new[] { "docs", "doc", "documentation", "readme", "wiki", "changelog" };
        var testingTokens = new[] { "test", "tests", "testing", "unittest", "integration", "e2e" };
        var ciTokens = new[] { "ci", "pipeline", "workflows", "workflow", "actions", "github", "build" };
        var maintenanceTokens = new[] { "refactor", "cleanup", "chore", "maintenance", "bump", "upgrade", "dependency", "dependencies", "deps" };
        var featureTokens = new[] { "feature", "features", "enhancement", "enhancements" };
        var apiTokens = new[] { "api", "apis" };
        var uxTokens = new[] { "ui", "ux", "frontend", "website" };
        var dependencyTokens = new[] { "dependency", "dependencies", "deps", "nuget", "package", "packages" };

        var securityMatches = CountMatches(tokens, securityTokens);
        var bugMatches = CountMatches(tokens, bugTokens);
        var performanceMatches = CountMatches(tokens, performanceTokens);
        var docsMatches = CountMatches(tokens, docsTokens);
        var testingMatches = CountMatches(tokens, testingTokens);
        var ciMatches = CountMatches(tokens, ciTokens);
        var maintenanceMatches = CountMatches(tokens, maintenanceTokens);
        var featureMatches = CountMatches(tokens, featureTokens);
        var apiMatches = CountMatches(tokens, apiTokens);
        var uxMatches = CountMatches(tokens, uxTokens);
        var dependencyMatches = CountMatches(tokens, dependencyTokens);

        var isSecurity = securityMatches > 0;
        var isBug = bugMatches > 0;
        var isPerformance = performanceMatches > 0;
        var isDocs = docsMatches > 0;
        var isTesting = testingMatches > 0;
        var isCi = ciMatches > 0;
        var isMaintenance = maintenanceMatches > 0;

        var category = isSecurity ? "security"
            : isBug ? "bug"
            : isPerformance ? "performance"
            : isDocs ? "documentation"
            : isTesting ? "testing"
            : isCi ? "ci"
            : isMaintenance ? "maintenance"
            : featureMatches > 0 ? "feature"
            : "feature";

        double ComputeConfidence(int matchCount, bool hasLabelEvidence, bool hasTitleEvidence, double baseConfidence) {
            var confidence = baseConfidence;
            if (matchCount > 0) {
                confidence += 0.08;
                confidence += Math.Min(0.16, (matchCount - 1) * 0.04);
            }
            if (hasLabelEvidence) {
                confidence += 0.10;
            }
            if (hasTitleEvidence) {
                confidence += 0.07;
            }

            return Math.Round(Math.Clamp(confidence, 0.35, 0.98), 2, MidpointRounding.AwayFromZero);
        }

        int ResolveCategoryMatches() {
            return category switch {
                "security" => securityMatches,
                "bug" => bugMatches,
                "performance" => performanceMatches,
                "documentation" => docsMatches,
                "testing" => testingMatches,
                "ci" => ciMatches,
                "maintenance" => maintenanceMatches,
                _ => featureMatches
            };
        }

        IReadOnlyList<string> ResolveCategoryCandidates() {
            return category switch {
                "security" => securityTokens,
                "bug" => bugTokens,
                "performance" => performanceTokens,
                "documentation" => docsTokens,
                "testing" => testingTokens,
                "ci" => ciTokens,
                "maintenance" => maintenanceTokens,
                _ => featureTokens
            };
        }

        var categoryCandidates = ResolveCategoryCandidates();
        var categoryConfidence = ComputeConfidence(
            ResolveCategoryMatches(),
            CountMatches(labelTokens, categoryCandidates) > 0,
            CountMatches(titleTokens, categoryCandidates) > 0,
            category.Equals("feature", StringComparison.OrdinalIgnoreCase) ? 0.48 : 0.60);

        var tags = new List<string>();
        var tagConfidenceByName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void AddTag(string tag, int matchCount, IReadOnlyList<string> evidenceTokens, double baseConfidence) {
            tags.Add(tag);
            var confidence = ComputeConfidence(
                matchCount,
                CountMatches(labelTokens, evidenceTokens) > 0,
                CountMatches(titleTokens, evidenceTokens) > 0,
                baseConfidence);

            if (tagConfidenceByName.TryGetValue(tag, out var existing)) {
                tagConfidenceByName[tag] = Math.Max(existing, confidence);
            } else {
                tagConfidenceByName[tag] = confidence;
            }
        }

        if (isSecurity) {
            AddTag("security", securityMatches, securityTokens, 0.58);
        }
        if (isBug) {
            AddTag("bugfix", bugMatches, bugTokens, 0.58);
        }
        if (isPerformance) {
            AddTag("performance", performanceMatches, performanceTokens, 0.58);
        }
        if (isDocs) {
            AddTag("docs", docsMatches, docsTokens, 0.58);
        }
        if (isTesting) {
            AddTag("testing", testingMatches, testingTokens, 0.58);
        }
        if (isCi) {
            AddTag("ci", ciMatches, ciTokens, 0.58);
        }
        if (isMaintenance) {
            AddTag("maintenance", maintenanceMatches, maintenanceTokens, 0.58);
        }
        if (apiMatches > 0) {
            AddTag("api", apiMatches, apiTokens, 0.56);
        }
        if (uxMatches > 0) {
            AddTag("ux", uxMatches, uxTokens, 0.56);
        }
        if (dependencyMatches > 0) {
            AddTag("dependencies", dependencyMatches, dependencyTokens, 0.56);
        }
        if (tags.Count == 0) {
            AddTag(category, ResolveCategoryMatches(), categoryCandidates, Math.Max(0.44, categoryConfidence - 0.08));
        }

        var normalizedTags = tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var normalizedTagConfidences = normalizedTags
            .ToDictionary(
                tag => tag,
                tag => tagConfidenceByName.TryGetValue(tag, out var confidence) ? confidence : 0.50,
                StringComparer.OrdinalIgnoreCase);

        return new CategoryTagInference(
            Category: category,
            CategoryConfidence: categoryConfidence,
            Tags: normalizedTags,
            TagConfidences: normalizedTagConfidences
        );
    }

}
