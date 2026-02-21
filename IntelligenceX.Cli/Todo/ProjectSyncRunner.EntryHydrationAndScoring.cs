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
    private static List<ProjectSyncEntry> LoadEntries(string triagePath, string visionPath, string issueReviewPath, int maxItems) {
        using var triageDoc = JsonDocument.Parse(File.ReadAllText(triagePath));
        JsonDocument? visionDoc = null;
        JsonDocument? issueReviewDoc = null;
        if (File.Exists(visionPath)) {
            visionDoc = JsonDocument.Parse(File.ReadAllText(visionPath));
        }
        if (File.Exists(issueReviewPath)) {
            issueReviewDoc = JsonDocument.Parse(File.ReadAllText(issueReviewPath));
        }

        var entries = BuildEntriesFromDocuments(
            triageDoc.RootElement,
            visionDoc?.RootElement,
            maxItems,
            issueReviewDoc?.RootElement);
        visionDoc?.Dispose();
        issueReviewDoc?.Dispose();
        return entries;
    }

    internal static List<ProjectSyncEntry> BuildEntriesFromDocuments(
        JsonElement triageRoot,
        JsonElement? visionRoot,
        int maxItems,
        JsonElement? issueReviewRoot = null) {
        var entriesByUrl = new Dictionary<string, ProjectSyncEntry>(StringComparer.OrdinalIgnoreCase);
        var idToUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clusterToCanonicalId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var decisionSignalsByUrl = new Dictionary<string, PullRequestDecisionSignals>(StringComparer.OrdinalIgnoreCase);
        var bestPullRequestUrls = ParseBestPullRequestUrls(triageRoot);

        if (TryGetProperty(triageRoot, "items", out var items) && items.ValueKind == JsonValueKind.Array) {
            foreach (var item in items.EnumerateArray()) {
                var id = ReadString(item, "id");
                var url = ReadString(item, "url");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(url)) {
                    idToUrl[id] = url;
                }
            }

            if (TryGetProperty(triageRoot, "duplicateClusters", out var clusters) && clusters.ValueKind == JsonValueKind.Array) {
                foreach (var cluster in clusters.EnumerateArray()) {
                    var clusterId = ReadString(cluster, "id");
                    var canonicalId = ReadString(cluster, "canonicalItemId");
                    if (!string.IsNullOrWhiteSpace(clusterId) && !string.IsNullOrWhiteSpace(canonicalId)) {
                        clusterToCanonicalId[clusterId] = canonicalId;
                    }
                }
            }

            foreach (var item in items.EnumerateArray()) {
                var url = ReadString(item, "url");
                if (string.IsNullOrWhiteSpace(url)) {
                    continue;
                }
                var number = ReadInt(item, "number");
                var kind = ReadString(item, "kind");
                if (string.IsNullOrWhiteSpace(kind)) {
                    kind = "pull_request";
                }
                var triageScore = ReadNullableDouble(item, "score");
                var duplicateClusterId = ReadNullableString(item, "duplicateClusterId");
                var category = ReadNullableString(item, "category");
                var categoryConfidence = ReadNullableDouble(item, "categoryConfidence");
                var tags = ReadStringArray(item, "tags");
                var tagConfidences = ReadStringDoubleMap(item, "tagConfidences");
                var signalQuality = NormalizeSignalQuality(ReadNullableString(item, "signalQuality"));
                var signalQualityScore = ReadNullableDouble(item, "signalQualityScore");
                var signalQualityReasons = ReadStringArray(item, "signalQualityReasons");
                var pullRequestSize = NormalizePullRequestSize(ReadNullableString(item, "prSizeBand"));
                var pullRequestChurnRisk = NormalizePullRequestChurnRisk(ReadNullableString(item, "prChurnRisk"));
                var pullRequestMergeReadiness = NormalizePullRequestMergeReadiness(ReadNullableString(item, "prMergeReadiness"));
                var pullRequestFreshness = NormalizePullRequestFreshness(ReadNullableString(item, "prFreshness"));
                var pullRequestCheckHealth = NormalizePullRequestCheckHealth(ReadNullableString(item, "prCheckHealth"));
                var pullRequestReviewLatency = NormalizePullRequestReviewLatency(ReadNullableString(item, "prReviewLatency"));
                var pullRequestMergeConflictRisk = NormalizePullRequestMergeConflictRisk(ReadNullableString(item, "prMergeConflictRisk"));
                var existingLabels = ReadStringArray(item, "labels");
                var matchedIssueUrl = ReadNullableString(item, "matchedIssueUrl");
                var matchedIssueConfidence = ReadNullableDouble(item, "matchedIssueConfidence");
                var matchedIssueReason = ReadNullableString(item, "matchedIssueReason");
                var relatedIssues = ParseRelatedIssueCandidates(item);
                if (string.IsNullOrWhiteSpace(matchedIssueUrl) && relatedIssues.Count > 0) {
                    matchedIssueUrl = relatedIssues[0].Url;
                    matchedIssueConfidence = relatedIssues[0].Confidence;
                    matchedIssueReason = relatedIssues[0].Reason;
                } else if (!string.IsNullOrWhiteSpace(matchedIssueUrl) && !matchedIssueConfidence.HasValue) {
                    var confidenceFromRelated = relatedIssues
                        .Where(candidate => candidate.Number > 0 &&
                                            !string.IsNullOrWhiteSpace(candidate.Url) &&
                                            candidate.Url.Equals(matchedIssueUrl, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(candidate => candidate.Confidence)
                        .FirstOrDefault();
                    if (confidenceFromRelated is not null) {
                        matchedIssueConfidence = confidenceFromRelated.Confidence;
                        if (string.IsNullOrWhiteSpace(matchedIssueReason)) {
                            matchedIssueReason = confidenceFromRelated.Reason;
                        }
                    }
                }
                var matchedPullRequestUrl = ReadNullableString(item, "matchedPullRequestUrl");
                var matchedPullRequestConfidence = ReadNullableDouble(item, "matchedPullRequestConfidence");
                var matchedPullRequestReason = ReadNullableString(item, "matchedPullRequestReason");
                var relatedPullRequests = ParseRelatedPullRequestCandidates(item);
                if (string.IsNullOrWhiteSpace(matchedPullRequestUrl) && relatedPullRequests.Count > 0) {
                    matchedPullRequestUrl = relatedPullRequests[0].Url;
                    matchedPullRequestConfidence = relatedPullRequests[0].Confidence;
                    matchedPullRequestReason = relatedPullRequests[0].Reason;
                } else if (!string.IsNullOrWhiteSpace(matchedPullRequestUrl) &&
                           (string.IsNullOrWhiteSpace(matchedPullRequestReason) || !matchedPullRequestConfidence.HasValue)) {
                    var candidate = relatedPullRequests
                        .Where(itemCandidate => itemCandidate.Number > 0 &&
                                                !string.IsNullOrWhiteSpace(itemCandidate.Url) &&
                                                itemCandidate.Url.Equals(matchedPullRequestUrl, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(itemCandidate => itemCandidate.Confidence)
                        .ThenBy(itemCandidate => itemCandidate.Number)
                        .FirstOrDefault();
                    if (candidate is not null) {
                        if (!matchedPullRequestConfidence.HasValue) {
                            matchedPullRequestConfidence = candidate.Confidence;
                        }
                        if (string.IsNullOrWhiteSpace(matchedPullRequestReason)) {
                            matchedPullRequestReason = candidate.Reason;
                        }
                    }
                }
                var decisionSignals = ParsePullRequestSignals(item);
                if (decisionSignals is not null) {
                    decisionSignalsByUrl[url] = decisionSignals;
                }

                string? canonicalUrl = null;
                if (!string.IsNullOrWhiteSpace(duplicateClusterId) &&
                    clusterToCanonicalId.TryGetValue(duplicateClusterId, out var canonicalId) &&
                    idToUrl.TryGetValue(canonicalId, out var canonicalFromId)) {
                    canonicalUrl = canonicalFromId;
                }

                entriesByUrl[url] = new ProjectSyncEntry(
                    Number: number,
                    Url: url,
                    Kind: kind,
                    TriageScore: triageScore,
                    DuplicateCluster: duplicateClusterId,
                    CanonicalItem: canonicalUrl,
                    Category: category,
                    Tags: tags,
                    MatchedIssueUrl: matchedIssueUrl,
                    MatchedIssueConfidence: matchedIssueConfidence,
                    MatchedIssueReason: matchedIssueReason,
                    VisionFit: null,
                    VisionConfidence: null,
                    RelatedIssues: relatedIssues,
                    SuggestedDecision: null,
                    MatchedPullRequestUrl: matchedPullRequestUrl,
                    MatchedPullRequestConfidence: matchedPullRequestConfidence,
                    MatchedPullRequestReason: matchedPullRequestReason,
                    RelatedPullRequests: relatedPullRequests,
                    ExistingLabels: existingLabels,
                    CategoryConfidence: categoryConfidence,
                    TagConfidences: tagConfidences,
                    SignalQuality: signalQuality,
                    SignalQualityScore: signalQualityScore,
                    SignalQualityReasons: signalQualityReasons,
                    PullRequestSize: pullRequestSize,
                    PullRequestChurnRisk: pullRequestChurnRisk,
                    PullRequestMergeReadiness: pullRequestMergeReadiness,
                    PullRequestFreshness: pullRequestFreshness,
                    PullRequestCheckHealth: pullRequestCheckHealth,
                    PullRequestReviewLatency: pullRequestReviewLatency,
                    PullRequestMergeConflictRisk: pullRequestMergeConflictRisk
                );
            }
        }

        if (visionRoot.HasValue &&
            TryGetProperty(visionRoot.Value, "assessments", out var assessments) &&
            assessments.ValueKind == JsonValueKind.Array) {
            foreach (var assessment in assessments.EnumerateArray()) {
                var url = ReadString(assessment, "url");
                if (string.IsNullOrWhiteSpace(url)) {
                    continue;
                }
                var classification = ReadString(assessment, "classification");
                var confidence = ReadNullableDouble(assessment, "confidence");
                var score = ReadNullableDouble(assessment, "score");

                if (entriesByUrl.TryGetValue(url, out var existing)) {
                    entriesByUrl[url] = existing with {
                        VisionFit = string.IsNullOrWhiteSpace(classification) ? existing.VisionFit : classification,
                        VisionConfidence = confidence ?? existing.VisionConfidence,
                        TriageScore = existing.TriageScore ?? score
                    };
                } else {
                    var (kind, number) = ParseKindAndNumberFromUrl(url);
                    entriesByUrl[url] = new ProjectSyncEntry(
                        Number: number,
                        Url: url,
                        Kind: kind,
                        TriageScore: score,
                        DuplicateCluster: null,
                        CanonicalItem: null,
                        Category: null,
                        Tags: Array.Empty<string>(),
                        MatchedIssueUrl: null,
                        MatchedIssueConfidence: null,
                        VisionFit: classification,
                        VisionConfidence: confidence,
                        RelatedIssues: Array.Empty<RelatedIssueCandidate>(),
                        SuggestedDecision: null,
                        RelatedPullRequests: Array.Empty<RelatedPullRequestCandidate>(),
                        ExistingLabels: Array.Empty<string>(),
                        SignalQualityReasons: Array.Empty<string>()
                    );
                }
            }
        }

        if (issueReviewRoot.HasValue) {
            MergeIssueReviewAssessments(entriesByUrl, issueReviewRoot.Value);
        }

        foreach (var pair in entriesByUrl.ToList()) {
            decisionSignalsByUrl.TryGetValue(pair.Key, out var decisionSignals);
            var suggestion = SuggestMaintainerDecision(
                pair.Value,
                bestPullRequestUrls.Contains(pair.Key),
                decisionSignals);
            if (!string.IsNullOrWhiteSpace(suggestion)) {
                entriesByUrl[pair.Key] = pair.Value with { SuggestedDecision = suggestion };
            }
        }

        var issueMatchByUrl = BuildIssueToPullRequestMatchByUrl(entriesByUrl.Values);
        var issueMatchByNumber = BuildIssueToPullRequestMatchByNumber(entriesByUrl.Values);
        foreach (var pair in entriesByUrl.ToList()) {
            if (!pair.Value.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (issueMatchByUrl.TryGetValue(pair.Key, out var byUrlMatch) ||
                (pair.Value.Number > 0 && issueMatchByNumber.TryGetValue(pair.Value.Number, out byUrlMatch))) {
                if (!ShouldReplaceIssuePullRequestMatch(
                        pair.Value.MatchedPullRequestUrl,
                        pair.Value.MatchedPullRequestConfidence,
                        byUrlMatch)) {
                    continue;
                }

                entriesByUrl[pair.Key] = pair.Value with {
                    MatchedPullRequestUrl = byUrlMatch.Url,
                    MatchedPullRequestConfidence = byUrlMatch.Confidence,
                    MatchedPullRequestReason = byUrlMatch.Reason
                };
            }
        }

        return entriesByUrl.Values
            .OrderBy(entry => VisionPriority(entry.VisionFit))
            .ThenByDescending(entry => entry.TriageScore ?? double.MinValue)
            .ThenBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxItems))
            .ToList();
    }

    private static int VisionPriority(string? visionFit) {
        return visionFit?.ToLowerInvariant() switch {
            "likely-out-of-scope" => 0,
            "needs-human-review" => 1,
            "aligned" => 2,
            _ => 3
        };
    }

    private static string? NormalizeSignalQuality(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => null
        };
    }

    private static string? NormalizePullRequestSize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "xsmall" => "xsmall",
            "small" => "small",
            "medium" => "medium",
            "large" => "large",
            "xlarge" => "xlarge",
            _ => null
        };
    }

    private static string? NormalizePullRequestChurnRisk(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string? NormalizePullRequestMergeReadiness(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "ready" => "ready",
            "needs-review" => "needs-review",
            "blocked" => "blocked",
            _ => null
        };
    }

    private static string? NormalizePullRequestFreshness(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "fresh" => "fresh",
            "recent" => "recent",
            "aging" => "aging",
            "stale" => "stale",
            _ => null
        };
    }

    private static string? NormalizePullRequestCheckHealth(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "healthy" => "healthy",
            "pending" => "pending",
            "failing" => "failing",
            "unknown" => "unknown",
            _ => null
        };
    }

    private static string? NormalizePullRequestReviewLatency(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string? NormalizePullRequestMergeConflictRisk(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string? SuggestMaintainerDecision(
        ProjectSyncEntry entry,
        bool isBestPullRequest,
        PullRequestDecisionSignals? prSignals) {
        if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var visionFit = entry.VisionFit?.Trim().ToLowerInvariant();
        var visionConfidence = entry.VisionConfidence ?? 0;
        var triageScore = entry.TriageScore ?? 0;

        if (visionFit == "likely-out-of-scope" && visionConfidence >= RejectVisionConfidenceThreshold) {
            return "reject";
        }

        if (IsLowSignalQuality(entry)) {
            return "defer";
        }

        var blockedBySignals = prSignals is not null && IsBlockedByReviewOrChecks(prSignals);
        if (blockedBySignals && visionFit != "likely-out-of-scope") {
            return "defer";
        }

        if (prSignals is not null &&
            IsStronglyReadyForMerge(prSignals) &&
            visionFit != "likely-out-of-scope" &&
            (isBestPullRequest || triageScore >= MergeCandidateScoreThreshold)) {
            return "merge-candidate";
        }

        if (visionFit == "aligned" &&
            visionConfidence >= AcceptVisionConfidenceThreshold &&
            triageScore >= 60 &&
            !blockedBySignals) {
            return "accept";
        }

        return "defer";
    }

    private static bool IsLowSignalQuality(ProjectSyncEntry entry) {
        if (!string.IsNullOrWhiteSpace(entry.SignalQuality) &&
            entry.SignalQuality.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return entry.SignalQualityScore.HasValue &&
               entry.SignalQualityScore.Value < LowSignalQualityScoreThreshold;
    }

    private static bool IsStronglyReadyForMerge(PullRequestDecisionSignals signals) {
        return signals.IsDraft == false &&
               string.Equals(signals.Mergeable, "MERGEABLE", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(signals.ReviewDecision, "APPROVED", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(signals.StatusCheckState, "SUCCESS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedByReviewOrChecks(PullRequestDecisionSignals signals) {
        if (signals.IsDraft == true) {
            return true;
        }

        if (string.Equals(signals.Mergeable, "CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(signals.Mergeable, "UNKNOWN", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(signals.ReviewDecision, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return string.Equals(signals.StatusCheckState, "FAILURE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(signals.StatusCheckState, "ERROR", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(signals.StatusCheckState, "PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ParseBestPullRequestUrls(JsonElement triageRoot) {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(triageRoot, "bestPullRequests", out var best) || best.ValueKind != JsonValueKind.Array) {
            return urls;
        }

        foreach (var candidate in best.EnumerateArray()) {
            var url = ReadString(candidate, "url");
            if (!string.IsNullOrWhiteSpace(url)) {
                urls.Add(url);
            }
        }
        return urls;
    }

    private static IReadOnlyDictionary<string, RelatedPullRequestCandidate> BuildIssueToPullRequestMatchByUrl(
        IEnumerable<ProjectSyncEntry> entries) {
        var map = new Dictionary<string, RelatedPullRequestCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            foreach (var candidate in entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>()) {
                if (string.IsNullOrWhiteSpace(candidate.Url) || candidate.Number <= 0) {
                    continue;
                }

                if (map.TryGetValue(candidate.Url, out var existing) &&
                    ComparePullRequestMatch(existing, entry.Number, candidate.Confidence) >= 0) {
                    continue;
                }

                map[candidate.Url] = new RelatedPullRequestCandidate(
                    entry.Number,
                    entry.Url,
                    candidate.Confidence,
                    candidate.Reason
                );
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<int, RelatedPullRequestCandidate> BuildIssueToPullRequestMatchByNumber(
        IEnumerable<ProjectSyncEntry> entries) {
        var map = new Dictionary<int, RelatedPullRequestCandidate>();
        foreach (var entry in entries) {
            if (!entry.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || entry.Number <= 0) {
                continue;
            }

            foreach (var candidate in entry.RelatedIssues ?? Array.Empty<RelatedIssueCandidate>()) {
                if (candidate.Number <= 0) {
                    continue;
                }

                if (map.TryGetValue(candidate.Number, out var existing) &&
                    ComparePullRequestMatch(existing, entry.Number, candidate.Confidence) >= 0) {
                    continue;
                }

                map[candidate.Number] = new RelatedPullRequestCandidate(
                    entry.Number,
                    entry.Url,
                    candidate.Confidence,
                    candidate.Reason
                );
            }
        }

        return map;
    }

    private static int ComparePullRequestMatch(RelatedPullRequestCandidate existing, int number, double confidence) {
        var confidenceCompare = existing.Confidence.CompareTo(confidence);
        if (confidenceCompare != 0) {
            return confidenceCompare;
        }
        return number.CompareTo(existing.Number);
    }

    private static bool ShouldReplaceIssuePullRequestMatch(
        string? existingPullRequestUrl,
        double? existingConfidence,
        RelatedPullRequestCandidate candidate) {
        if (string.IsNullOrWhiteSpace(existingPullRequestUrl) || !existingConfidence.HasValue) {
            return true;
        }

        if (candidate.Confidence > existingConfidence.Value) {
            return true;
        }

        if (candidate.Confidence < existingConfidence.Value) {
            return false;
        }

        var (existingKind, existingNumber) = ParseKindAndNumberFromUrl(existingPullRequestUrl);
        if (!existingKind.Equals("pull_request", StringComparison.OrdinalIgnoreCase) || existingNumber <= 0) {
            return true;
        }

        return candidate.Number > 0 && candidate.Number < existingNumber;
    }

    private static PullRequestDecisionSignals? ParsePullRequestSignals(JsonElement item) {
        var kind = ReadString(item, "kind");
        if (!kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (!TryGetPropertyCaseInsensitive(item, "signals", out var signalsObj) ||
            signalsObj.ValueKind != JsonValueKind.Object ||
            !TryGetPropertyCaseInsensitive(signalsObj, "pullRequest", out var prSignalsObj) ||
            prSignalsObj.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var isDraft = ReadNullableBoolCaseInsensitive(prSignalsObj, "isDraft");
        var mergeable = ReadNullableStringCaseInsensitive(prSignalsObj, "mergeable");
        var reviewDecision = ReadNullableStringCaseInsensitive(prSignalsObj, "reviewDecision");
        var statusCheckState = ReadNullableStringCaseInsensitive(prSignalsObj, "statusCheckState");
        if (!isDraft.HasValue &&
            string.IsNullOrWhiteSpace(mergeable) &&
            string.IsNullOrWhiteSpace(reviewDecision) &&
            string.IsNullOrWhiteSpace(statusCheckState)) {
            return null;
        }

        return new PullRequestDecisionSignals(
            isDraft,
            mergeable,
            reviewDecision,
            statusCheckState
        );
    }

    private static void MergeIssueReviewAssessments(
        IDictionary<string, ProjectSyncEntry> entriesByUrl,
        JsonElement issueReviewRoot) {
        if (!TryGetProperty(issueReviewRoot, "items", out var items) || items.ValueKind != JsonValueKind.Array) {
            return;
        }

        var issueEntriesByNumber = entriesByUrl.Values
            .Where(entry => entry.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase) && entry.Number > 0)
            .GroupBy(entry => entry.Number)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var item in items.EnumerateArray()) {
            var proposedAction = NormalizeIssueReviewAction(ReadNullableStringCaseInsensitive(item, "proposedAction"));
            var actionConfidenceRaw = ReadNullableDoubleCaseInsensitive(item, "actionConfidence");
            var actionConfidence = actionConfidenceRaw.HasValue
                ? Math.Round(Math.Clamp(actionConfidenceRaw.Value, 0.0, 100.0), 2, MidpointRounding.AwayFromZero)
                : (double?)null;
            if (string.IsNullOrWhiteSpace(proposedAction) && !actionConfidence.HasValue) {
                continue;
            }

            var url = ReadNullableStringCaseInsensitive(item, "url") ?? string.Empty;
            var number = ReadInt(item, "number");
            if (string.IsNullOrWhiteSpace(url) &&
                number > 0 &&
                issueEntriesByNumber.TryGetValue(number, out var byNumberMatch)) {
                url = byNumberMatch.Url;
            }

            ProjectSyncEntry existing;
            if (!string.IsNullOrWhiteSpace(url) && entriesByUrl.TryGetValue(url, out var byUrlExisting)) {
                existing = byUrlExisting;
            } else if (number > 0 && issueEntriesByNumber.TryGetValue(number, out var byNumberExisting)) {
                existing = byNumberExisting;
                url = existing.Url;
            } else {
                if (string.IsNullOrWhiteSpace(url)) {
                    continue;
                }

                var (_, parsedNumber) = ParseKindAndNumberFromUrl(url);
                existing = new ProjectSyncEntry(
                    Number: number > 0 ? number : parsedNumber,
                    Url: url,
                    Kind: "issue",
                    TriageScore: null,
                    DuplicateCluster: null,
                    CanonicalItem: null,
                    Category: null,
                    Tags: Array.Empty<string>(),
                    MatchedIssueUrl: null,
                    MatchedIssueConfidence: null,
                    VisionFit: null,
                    VisionConfidence: null,
                    RelatedIssues: Array.Empty<RelatedIssueCandidate>(),
                    SuggestedDecision: null,
                    RelatedPullRequests: Array.Empty<RelatedPullRequestCandidate>(),
                    ExistingLabels: Array.Empty<string>(),
                    SignalQualityReasons: Array.Empty<string>()
                );
            }

            if (!existing.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var updated = existing with {
                IssueReviewAction = proposedAction ?? existing.IssueReviewAction,
                IssueReviewActionConfidence = actionConfidence ?? existing.IssueReviewActionConfidence
            };
            entriesByUrl[updated.Url] = updated;
            if (!string.IsNullOrWhiteSpace(url) &&
                !updated.Url.Equals(url, StringComparison.OrdinalIgnoreCase)) {
                entriesByUrl[url] = updated;
            }
            if (updated.Number > 0) {
                issueEntriesByNumber[updated.Number] = updated;
            }
        }
    }

}
