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
    private static IReadOnlyList<BestPullRequest> BuildBestPullRequests(
        IReadOnlyList<ItemWithScore> scoredItems,
        IReadOnlyList<DuplicateCluster> clusters,
        int limit) {
        var canonicalByCluster = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clusterMap = clusters.ToDictionary(cluster => cluster.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters) {
            var candidate = scoredItems
                .Where(item => item.DuplicateClusterId != null &&
                               item.DuplicateClusterId.Equals(cluster.Id, StringComparison.OrdinalIgnoreCase) &&
                               item.Item.Kind == "pull_request")
                .OrderByDescending(item => item.Score ?? 0)
                .ThenByDescending(item => item.Item.UpdatedAtUtc)
                .Select(item => item.Item.Id)
                .FirstOrDefault();
            canonicalByCluster[cluster.Id] = string.IsNullOrWhiteSpace(candidate) ? cluster.CanonicalItemId : candidate;
        }

        return scoredItems
            .Where(item => item.Item.Kind == "pull_request")
            .Where(item => item.DuplicateClusterId is null ||
                           (canonicalByCluster.TryGetValue(item.DuplicateClusterId, out var canonical) &&
                            canonical.Equals(item.Item.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Score ?? 0)
            .ThenByDescending(item => item.Item.UpdatedAtUtc)
            .Take(Math.Max(1, limit))
            .Select(item => {
                var reasons = item.ScoreReasons.ToList();
                if (item.DuplicateClusterId is not null && clusterMap.TryGetValue(item.DuplicateClusterId, out var cluster)) {
                    reasons.Add($"Cluster representative for {cluster.Id} ({cluster.ItemIds.Count} related items).");
                }
                return new BestPullRequest(
                    Id: item.Item.Id,
                    Number: item.Item.Number,
                    Title: item.Item.Title,
                    Url: item.Item.Url,
                    Score: item.Score ?? 0,
                    Reasons: reasons,
                    DuplicateClusterId: item.DuplicateClusterId
                );
            })
            .ToList();
    }

    private static object BuildReport(
        Options options,
        DateTimeOffset nowUtc,
        IReadOnlyList<ItemWithScore> scoredItems,
        IReadOnlyList<DuplicateCluster> clusters,
        IReadOnlyList<BestPullRequest> bestPullRequests,
        IReadOnlyDictionary<string, ItemEnrichment> enrichments) {
        var duplicateIds = new HashSet<string>(
            clusters.SelectMany(cluster => cluster.ItemIds),
            StringComparer.OrdinalIgnoreCase
        );
        var signalAssessmentsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessSignalQuality(item.Item, enrichments.TryGetValue(item.Item.Id, out var enrichment) ? enrichment : null),
                StringComparer.OrdinalIgnoreCase);
        var pullRequestOperationalSignalsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessPullRequestOperationalSignals(item.Item, nowUtc),
                StringComparer.OrdinalIgnoreCase);
        var highSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "high");
        var mediumSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "medium");
        var lowSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "low");
        var pullRequestSignalValues = pullRequestOperationalSignalsById.Values
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        var items = scoredItems
            .OrderByDescending(item => item.Item.UpdatedAtUtc)
            .ThenBy(item => item.Item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.Number)
            .Select(item => {
                enrichments.TryGetValue(item.Item.Id, out var enrichment);
                signalAssessmentsById.TryGetValue(item.Item.Id, out var signalQuality);
                pullRequestOperationalSignalsById.TryGetValue(item.Item.Id, out var operationalSignals);
                return new {
                    id = item.Item.Id,
                    kind = item.Item.Kind,
                    number = item.Item.Number,
                    title = item.Item.Title,
                    url = item.Item.Url,
                    updatedAtUtc = item.Item.UpdatedAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                    labels = item.Item.Labels,
                    dedupeKey = string.Join("-", item.Item.TitleTokens.Take(8)),
                    duplicateClusterId = item.DuplicateClusterId,
                    category = enrichment?.Category,
                    categoryConfidence = enrichment?.CategoryConfidence,
                    tags = enrichment?.Tags ?? Array.Empty<string>(),
                    tagConfidences = enrichment?.TagConfidences ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    matchedIssueUrl = enrichment?.MatchedIssueUrl,
                    matchedIssueConfidence = enrichment?.MatchedIssueConfidence,
                    matchedPullRequestUrl = enrichment?.MatchedPullRequestUrl,
                    matchedPullRequestConfidence = enrichment?.MatchedPullRequestConfidence,
                    relatedIssues = enrichment?.RelatedIssues
                        .Select(related => new {
                            number = related.Number,
                            url = related.Url,
                            confidence = related.Confidence,
                            reason = related.Reason
                        })
                        .Cast<object>()
                        .ToList() ?? new List<object>(),
                    relatedPullRequests = enrichment?.RelatedPullRequests
                        .Select(related => new {
                            number = related.Number,
                            url = related.Url,
                            confidence = related.Confidence,
                            reason = related.Reason
                        })
                        .Cast<object>()
                        .ToList() ?? new List<object>(),
                    signalQuality = signalQuality?.Level ?? "low",
                    signalQualityScore = signalQuality?.Score ?? 0,
                    signalQualityReasons = signalQuality?.Reasons ?? Array.Empty<string>(),
                    prSizeBand = operationalSignals?.SizeBand,
                    prChurnRisk = operationalSignals?.ChurnRisk,
                    prMergeReadiness = operationalSignals?.MergeReadiness,
                    prFreshness = operationalSignals?.Freshness,
                    prCheckHealth = operationalSignals?.CheckHealth,
                    prReviewLatency = operationalSignals?.ReviewLatency,
                    prMergeConflictRisk = operationalSignals?.MergeConflictRisk,
                    score = item.Score,
                    scoreReasons = item.ScoreReasons,
                    signals = new {
                        pullRequest = item.Item.PullRequest,
                        issue = item.Item.Issue
                    }
                };
            })
            .ToList();

        var duplicates = clusters.Select(cluster => new {
            id = cluster.Id,
            confidence = cluster.Confidence,
            canonicalItemId = cluster.CanonicalItemId,
            itemIds = cluster.ItemIds,
            reason = cluster.Reason
        }).ToList();

        var best = bestPullRequests.Select(candidate => new {
            id = candidate.Id,
            number = candidate.Number,
            title = candidate.Title,
            url = candidate.Url,
            score = candidate.Score,
            duplicateClusterId = candidate.DuplicateClusterId,
            reasons = candidate.Reasons
        }).ToList();

        return new {
            schema = "intelligencex.triage-index.v1",
            generatedAtUtc = nowUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            repo = options.Repo,
            settings = new {
                maxPrs = options.MaxPrs,
                maxIssues = options.MaxIssues,
                duplicateThreshold = options.DuplicateThreshold,
                bestLimit = options.BestLimit
            },
            summary = new {
                totalItems = scoredItems.Count,
                pullRequests = scoredItems.Count(item => item.Item.Kind == "pull_request"),
                issues = scoredItems.Count(item => item.Item.Kind == "issue"),
                duplicateClusters = clusters.Count,
                duplicateItems = duplicateIds.Count,
                bestPullRequestCandidates = bestPullRequests.Count,
                pullRequestsWithMatchedIssue = enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedIssueUrl)),
                issuesWithMatchedPullRequest = enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedPullRequestUrl)),
                signalQuality = new {
                    high = highSignalCount,
                    medium = mediumSignalCount,
                    low = lowSignalCount
                },
                pullRequestSignals = new {
                    size = new {
                        xsmall = pullRequestSignalValues.Count(value => value.SizeBand.Equals("xsmall", StringComparison.OrdinalIgnoreCase)),
                        small = pullRequestSignalValues.Count(value => value.SizeBand.Equals("small", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.SizeBand.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        large = pullRequestSignalValues.Count(value => value.SizeBand.Equals("large", StringComparison.OrdinalIgnoreCase)),
                        xlarge = pullRequestSignalValues.Count(value => value.SizeBand.Equals("xlarge", StringComparison.OrdinalIgnoreCase))
                    },
                    churnRisk = new {
                        low = pullRequestSignalValues.Count(value => value.ChurnRisk.Equals("low", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.ChurnRisk.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        high = pullRequestSignalValues.Count(value => value.ChurnRisk.Equals("high", StringComparison.OrdinalIgnoreCase))
                    },
                    mergeReadiness = new {
                        ready = pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                        needsReview = pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("needs-review", StringComparison.OrdinalIgnoreCase)),
                        blocked = pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("blocked", StringComparison.OrdinalIgnoreCase))
                    },
                    freshness = new {
                        fresh = pullRequestSignalValues.Count(value => value.Freshness.Equals("fresh", StringComparison.OrdinalIgnoreCase)),
                        recent = pullRequestSignalValues.Count(value => value.Freshness.Equals("recent", StringComparison.OrdinalIgnoreCase)),
                        aging = pullRequestSignalValues.Count(value => value.Freshness.Equals("aging", StringComparison.OrdinalIgnoreCase)),
                        stale = pullRequestSignalValues.Count(value => value.Freshness.Equals("stale", StringComparison.OrdinalIgnoreCase))
                    },
                    checkHealth = new {
                        healthy = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("healthy", StringComparison.OrdinalIgnoreCase)),
                        pending = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("pending", StringComparison.OrdinalIgnoreCase)),
                        failing = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("failing", StringComparison.OrdinalIgnoreCase)),
                        unknown = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    },
                    reviewLatency = new {
                        low = pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        high = pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("high", StringComparison.OrdinalIgnoreCase))
                    },
                    mergeConflictRisk = new {
                        low = pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("low", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        high = pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("high", StringComparison.OrdinalIgnoreCase))
                    }
                }
            },
            bestPullRequests = best,
            duplicateClusters = duplicates,
            items
        };
    }

    private static string BuildMarkdownSummary(
        Options options,
        DateTimeOffset nowUtc,
        IReadOnlyList<ItemWithScore> scoredItems,
        IReadOnlyList<DuplicateCluster> clusters,
        IReadOnlyList<BestPullRequest> bestPullRequests,
        IReadOnlyDictionary<string, ItemEnrichment> enrichments) {
        var duplicateIds = new HashSet<string>(
            clusters.SelectMany(cluster => cluster.ItemIds),
            StringComparer.OrdinalIgnoreCase
        );
        var signalAssessmentsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessSignalQuality(item.Item, enrichments.TryGetValue(item.Item.Id, out var enrichment) ? enrichment : null),
                StringComparer.OrdinalIgnoreCase);
        var pullRequestOperationalSignalsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessPullRequestOperationalSignals(item.Item, nowUtc),
                StringComparer.OrdinalIgnoreCase);
        var highSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "high");
        var mediumSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "medium");
        var lowSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "low");
        var pullRequestSignalValues = pullRequestOperationalSignalsById.Values
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Triage Index");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {nowUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- Repo: `{options.Repo}`");
        sb.AppendLine($"- Scope: {options.MaxPrs} PRs + {options.MaxIssues} issues");
        sb.AppendLine($"- Duplicate threshold: {options.DuplicateThreshold.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Total items: {scoredItems.Count}");
        sb.AppendLine($"- PRs: {scoredItems.Count(item => item.Item.Kind == "pull_request")}");
        sb.AppendLine($"- Issues: {scoredItems.Count(item => item.Item.Kind == "issue")}");
        sb.AppendLine($"- Duplicate clusters: {clusters.Count}");
        sb.AppendLine($"- Duplicate items: {duplicateIds.Count}");
        sb.AppendLine($"- PRs with matched issue: {enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedIssueUrl))}");
        sb.AppendLine($"- Issues with matched PR: {enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedPullRequestUrl))}");
        sb.AppendLine($"- Signal quality (high/medium/low): {highSignalCount}/{mediumSignalCount}/{lowSignalCount}");
        sb.AppendLine(
            $"- PR size (xsmall/small/medium/large/xlarge): " +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("xsmall", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("small", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("medium", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("large", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("xlarge", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR merge readiness (ready/needs-review/blocked): " +
            $"{pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("ready", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("needs-review", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("blocked", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR check health (healthy/pending/failing/unknown): " +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("healthy", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("pending", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("failing", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("unknown", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR review latency (low/medium/high): " +
            $"{pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("medium", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("high", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR merge conflict risk (low/medium/high): " +
            $"{pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("low", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("medium", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("high", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine();
        sb.AppendLine("## Best PR Candidates");
        sb.AppendLine();

        if (bestPullRequests.Count == 0) {
            sb.AppendLine("None.");
        } else {
            var rank = 0;
            foreach (var candidate in bestPullRequests) {
                rank++;
                sb.AppendLine($"{rank}. #{candidate.Number} ({candidate.Score.ToString("0.00", CultureInfo.InvariantCulture)}) - [{candidate.Title}]({candidate.Url})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Duplicate Clusters");
        sb.AppendLine();
        if (clusters.Count == 0) {
            sb.AppendLine("None.");
        } else {
            foreach (var cluster in clusters.Take(30)) {
                sb.AppendLine($"- {cluster.Id} (confidence: {cluster.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}; items: {cluster.ItemIds.Count}; canonical: `{cluster.CanonicalItemId}`)");
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

}
