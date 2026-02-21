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
    internal static IReadOnlyList<RelatedIssueCandidate> MatchPullRequestToIssues(
        string repo,
        string prTitle,
        string prBody,
        IReadOnlyList<TriageIndexItem> issueItems) {
        if (issueItems.Count == 0) {
            return Array.Empty<RelatedIssueCandidate>();
        }

        var (owner, name) = SplitRepo(repo);
        var issueByNumber = issueItems.ToDictionary(item => item.Number);
        var matchesByNumber = new Dictionary<int, RelatedIssueCandidate>();

        foreach (var issueHint in ParseExplicitIssueReferences(prTitle, prBody, owner, name)) {
            if (!issueByNumber.TryGetValue(issueHint.Number, out var issue)) {
                continue;
            }

            matchesByNumber[issue.Number] = new RelatedIssueCandidate(
                Number: issue.Number,
                Url: issue.Url,
                Confidence: issueHint.Confidence,
                Reason: issueHint.Reason
            );
        }

        var prTitleTokens = Tokenize(prTitle);
        var prContextTokens = Tokenize($"{prTitle}\n{prBody}");

        foreach (var issue in issueItems) {
            if (matchesByNumber.ContainsKey(issue.Number)) {
                continue;
            }

            var titleScore = Jaccard(prTitleTokens, issue.TitleTokens);
            var contextScore = Jaccard(prContextTokens, issue.ContextTokens);
            var blended = Math.Round((titleScore * 0.55) + (contextScore * 0.45), 4, MidpointRounding.AwayFromZero);
            if (blended < 0.34) {
                continue;
            }

            var reason = $"token similarity title={titleScore.ToString("0.00", CultureInfo.InvariantCulture)}, context={contextScore.ToString("0.00", CultureInfo.InvariantCulture)}";
            matchesByNumber[issue.Number] = new RelatedIssueCandidate(
                Number: issue.Number,
                Url: issue.Url,
                Confidence: blended,
                Reason: reason
            );
        }

        return matchesByNumber.Values
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Number)
            .Take(3)
            .ToList();
    }

    internal static IReadOnlyList<RelatedPullRequestCandidate> MatchIssueToPullRequests(
        string repo,
        string issueTitle,
        string issueBody,
        IReadOnlyList<TriageIndexItem> pullRequestItems) {
        if (pullRequestItems.Count == 0) {
            return Array.Empty<RelatedPullRequestCandidate>();
        }

        var (owner, name) = SplitRepo(repo);
        var pullRequestByNumber = pullRequestItems.ToDictionary(item => item.Number);
        var matchesByNumber = new Dictionary<int, RelatedPullRequestCandidate>();

        foreach (var pullRequestHint in ParseExplicitPullRequestReferences(issueTitle, issueBody, owner, name)) {
            if (!pullRequestByNumber.TryGetValue(pullRequestHint.Number, out var pullRequest)) {
                continue;
            }

            matchesByNumber[pullRequest.Number] = new RelatedPullRequestCandidate(
                Number: pullRequest.Number,
                Url: pullRequest.Url,
                Confidence: pullRequestHint.Confidence,
                Reason: pullRequestHint.Reason
            );
        }

        var issueTitleTokens = Tokenize(issueTitle);
        var issueContextTokens = Tokenize($"{issueTitle}\n{issueBody}");

        foreach (var pullRequest in pullRequestItems) {
            if (matchesByNumber.ContainsKey(pullRequest.Number)) {
                continue;
            }

            var titleScore = Jaccard(issueTitleTokens, pullRequest.TitleTokens);
            var contextScore = Jaccard(issueContextTokens, pullRequest.ContextTokens);
            var blended = Math.Round((titleScore * 0.55) + (contextScore * 0.45), 4, MidpointRounding.AwayFromZero);
            if (blended < 0.34) {
                continue;
            }

            var reason = $"token similarity title={titleScore.ToString("0.00", CultureInfo.InvariantCulture)}, context={contextScore.ToString("0.00", CultureInfo.InvariantCulture)}";
            matchesByNumber[pullRequest.Number] = new RelatedPullRequestCandidate(
                Number: pullRequest.Number,
                Url: pullRequest.Url,
                Confidence: blended,
                Reason: reason
            );
        }

        return matchesByNumber.Values
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Number)
            .Take(3)
            .ToList();
    }

    private static IReadOnlyList<IssueReferenceHint> ParseExplicitIssueReferences(
        string prTitle,
        string prBody,
        string owner,
        string repoName) {
        var text = $"{prTitle}\n{prBody}";
        var results = new Dictionary<int, IssueReferenceHint>();

        static void AddHint(
            IDictionary<int, IssueReferenceHint> map,
            int number,
            double confidence,
            string reason) {
            if (!map.TryGetValue(number, out var existing) ||
                confidence > existing.Confidence) {
                map[number] = new IssueReferenceHint(number, confidence, reason);
            }
        }

        foreach (Match match in ExplicitIssueRef.Matches(text)) {
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit issue reference in PR title/body");
            }
        }

        foreach (Match match in ExplicitRepoIssueRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit issue reference in PR title/body");
            }
        }

        foreach (Match match in ExplicitIssueUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit issue reference in PR title/body");
            }
        }

        foreach (Match match in DirectIssueRef.Matches(text)) {
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.92, "direct issue reference in PR title/body");
            }
        }

        foreach (Match match in DirectRepoIssueRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.92, "direct issue reference in PR title/body");
            }
        }

        foreach (Match match in DirectIssueUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.92, "direct issue reference in PR title/body");
            }
        }

        foreach (Match match in BareIssueUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.90, "issue URL reference in PR title/body");
            }
        }

        return results.Values
            .OrderByDescending(value => value.Confidence)
            .ThenBy(value => value.Number)
            .ToList();
    }

    private static IReadOnlyList<PullRequestReferenceHint> ParseExplicitPullRequestReferences(
        string issueTitle,
        string issueBody,
        string owner,
        string repoName) {
        var text = $"{issueTitle}\n{issueBody}";
        var results = new Dictionary<int, PullRequestReferenceHint>();

        static void AddHint(
            IDictionary<int, PullRequestReferenceHint> map,
            int number,
            double confidence,
            string reason) {
            if (!map.TryGetValue(number, out var existing) ||
                confidence > existing.Confidence) {
                map[number] = new PullRequestReferenceHint(number, confidence, reason);
            }
        }

        foreach (Match match in ExplicitPullRequestRef.Matches(text)) {
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit pull request reference in issue title/body");
            }
        }

        foreach (Match match in DirectPullRequestRef.Matches(text)) {
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.93, "direct pull request reference in issue title/body");
            }
        }

        foreach (Match match in DirectRepoPullRequestRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.93, "direct pull request reference in issue title/body");
            }
        }

        foreach (Match match in DirectPullRequestUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.93, "direct pull request reference in issue title/body");
            }
        }

        foreach (Match match in BarePullRequestUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.90, "pull request URL reference in issue title/body");
            }
        }

        return results.Values
            .OrderByDescending(value => value.Confidence)
            .ThenBy(value => value.Number)
            .ToList();
    }

    private static bool TryReadIssueNumber(Match match, out int number) {
        number = 0;
        return match.Groups["num"].Success &&
               int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }

    private static bool TryReadPullRequestNumber(Match match, out int number) {
        number = 0;
        return match.Groups["num"].Success &&
               int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }

    internal static string NormalizeText(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }
        var lowered = value.ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        var inWhitespace = false;
        foreach (var c in lowered) {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') {
                sb.Append(c);
                inWhitespace = false;
                continue;
            }
            if (char.IsWhiteSpace(c)) {
                if (!inWhitespace) {
                    sb.Append(' ');
                    inWhitespace = true;
                }
                continue;
            }
            if (!inWhitespace) {
                sb.Append(' ');
                inWhitespace = true;
            }
        }
        return sb.ToString().Trim();
    }

    internal static IReadOnlyList<string> Tokenize(string? value) {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return Array.Empty<string>();
        }
        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tokens;
    }

    internal static double Jaccard(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count == 0 && right.Count == 0) {
            return 1.0;
        }
        if (left.Count == 0 || right.Count == 0) {
            return 0.0;
        }

        var leftSet = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(right, StringComparer.OrdinalIgnoreCase);
        var union = new HashSet<string>(leftSet, StringComparer.OrdinalIgnoreCase);
        union.UnionWith(rightSet);
        if (union.Count == 0) {
            return 0.0;
        }
        leftSet.IntersectWith(rightSet);
        return Math.Round(leftSet.Count / (double)union.Count, 4, MidpointRounding.AwayFromZero);
    }

    private static bool IsLikelyPrefixTitle(TriageIndexItem left, TriageIndexItem right) {
        if (string.IsNullOrWhiteSpace(left.NormalizedTitle) || string.IsNullOrWhiteSpace(right.NormalizedTitle)) {
            return false;
        }
        return left.NormalizedTitle.Contains(right.NormalizedTitle, StringComparison.Ordinal) ||
               right.NormalizedTitle.Contains(left.NormalizedTitle, StringComparison.Ordinal);
    }

    private static double ComputeDuplicateScore(TriageIndexItem left, TriageIndexItem right) {
        if (string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)) {
            return 1.0;
        }
        if (!string.IsNullOrWhiteSpace(left.NormalizedTitle) &&
            left.NormalizedTitle.Equals(right.NormalizedTitle, StringComparison.OrdinalIgnoreCase)) {
            return 1.0;
        }

        var titleScore = Jaccard(left.TitleTokens, right.TitleTokens);
        var contextScore = Jaccard(left.ContextTokens, right.ContextTokens);
        var blended = Math.Round((titleScore * 0.80) + (contextScore * 0.20), 4, MidpointRounding.AwayFromZero);

        if (IsLikelyPrefixTitle(left, right) && titleScore >= 0.55) {
            blended = Math.Max(blended, 0.86);
        }

        if (Math.Min(left.TitleTokens.Count, right.TitleTokens.Count) < 3 && titleScore < 0.95) {
            blended = Math.Min(blended, 0.79);
        }
        return blended;
    }

    internal static IReadOnlyList<DuplicateCluster> BuildDuplicateClusters(IReadOnlyList<TriageIndexItem> items, double threshold) {
        if (items.Count < 2) {
            return Array.Empty<DuplicateCluster>();
        }

        var normalizedThreshold = Math.Clamp(threshold, 0.50, 1.0);
        var parent = Enumerable.Range(0, items.Count).ToArray();
        var rank = new int[items.Count];
        var pairScores = new Dictionary<(int A, int B), double>();

        int Find(int i) {
            if (parent[i] != i) {
                parent[i] = Find(parent[i]);
            }
            return parent[i];
        }

        void Union(int a, int b) {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB) {
                return;
            }
            if (rank[rootA] < rank[rootB]) {
                parent[rootA] = rootB;
                return;
            }
            if (rank[rootA] > rank[rootB]) {
                parent[rootB] = rootA;
                return;
            }
            parent[rootB] = rootA;
            rank[rootA]++;
        }

        for (var i = 0; i < items.Count; i++) {
            for (var j = i + 1; j < items.Count; j++) {
                var score = ComputeDuplicateScore(items[i], items[j]);
                if (score >= normalizedThreshold) {
                    Union(i, j);
                    pairScores[(i, j)] = score;
                }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < items.Count; i++) {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list)) {
                list = new List<int>();
                groups[root] = list;
            }
            list.Add(i);
        }

        var clusterIndex = 0;
        var results = new List<DuplicateCluster>();
        foreach (var memberIndexes in groups.Values.Where(group => group.Count > 1)) {
            clusterIndex++;
            var confidence = 0.0;
            foreach (var first in memberIndexes) {
                foreach (var second in memberIndexes) {
                    if (first >= second) {
                        continue;
                    }
                    if (pairScores.TryGetValue((first, second), out var score) ||
                        pairScores.TryGetValue((second, first), out score)) {
                        confidence = Math.Max(confidence, score);
                    }
                }
            }

            var members = memberIndexes
                .Select(index => items[index])
                .OrderByDescending(item => item.Kind == "pull_request")
                .ThenByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var canonical = members[0];
            results.Add(new DuplicateCluster(
                Id: $"cluster-{clusterIndex:000}",
                Confidence: Math.Round(confidence, 4, MidpointRounding.AwayFromZero),
                CanonicalItemId: canonical.Id,
                ItemIds: members.Select(member => member.Id).ToList(),
                Reason: $"token similarity >= {normalizedThreshold.ToString("0.00", CultureInfo.InvariantCulture)}"
            ));
        }

        return results
            .OrderByDescending(cluster => cluster.Confidence)
            .ThenBy(cluster => cluster.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> BuildClusterLookup(IReadOnlyList<DuplicateCluster> clusters) {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters) {
            foreach (var itemId in cluster.ItemIds) {
                lookup[itemId] = cluster.Id;
            }
        }
        return lookup;
    }

    internal static double ScorePullRequest(TriageIndexItem item, DateTimeOffset nowUtc, out List<string> reasons) {
        reasons = new List<string>();
        if (item.PullRequest is null) {
            reasons.Add("Not a pull request.");
            return 0;
        }

        var signals = item.PullRequest;
        var score = 50.0;

        if (signals.IsDraft) {
            score -= 20;
            reasons.Add("Draft PR penalty.");
        } else {
            score += 4;
            reasons.Add("Ready-for-review bonus.");
        }

        if (signals.Mergeable.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase)) {
            score += 12;
            reasons.Add("Mergeable status bonus.");
        } else if (signals.Mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase)) {
            score -= 12;
            reasons.Add("Conflicting branch penalty.");
        } else {
            score -= 4;
            reasons.Add("Unknown mergeability penalty.");
        }

        if (signals.ReviewDecision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)) {
            score += 15;
            reasons.Add("Approved review decision bonus.");
        } else if (signals.ReviewDecision.Equals("CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)) {
            score -= 15;
            reasons.Add("Changes requested penalty.");
        } else if (signals.ReviewDecision.Equals("REVIEW_REQUIRED", StringComparison.OrdinalIgnoreCase)) {
            score -= 6;
            reasons.Add("Review required penalty.");
        }

        if (signals.StatusCheckState.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)) {
            score += 8;
            reasons.Add("Status checks success bonus.");
        } else if (signals.StatusCheckState.Equals("FAILURE", StringComparison.OrdinalIgnoreCase) ||
                   signals.StatusCheckState.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) {
            score -= 12;
            reasons.Add("Failing status checks penalty.");
        } else if (signals.StatusCheckState.Equals("PENDING", StringComparison.OrdinalIgnoreCase) ||
                   signals.StatusCheckState.Equals("EXPECTED", StringComparison.OrdinalIgnoreCase)) {
            score -= 4;
            reasons.Add("Pending status checks penalty.");
        } else if (!string.IsNullOrWhiteSpace(signals.StatusCheckState)) {
            score -= 2;
            reasons.Add($"Unknown status check state penalty ({signals.StatusCheckState}).");
        }

        var changeVolume = Math.Max(0, signals.Additions) + Math.Max(0, signals.Deletions);
        if (signals.ChangedFiles > 200) {
            score -= 16;
            reasons.Add("Large changed-file count penalty (>200).");
        } else if (signals.ChangedFiles > 80) {
            score -= 10;
            reasons.Add("Large changed-file count penalty (>80).");
        } else if (signals.ChangedFiles > 30) {
            score -= 5;
            reasons.Add("Medium changed-file count penalty (>30).");
        } else {
            score += 2;
            reasons.Add("Focused change-set bonus.");
        }

        if (changeVolume > 5000) {
            score -= 12;
            reasons.Add("Very high churn penalty (>5000 lines).");
        } else if (changeVolume > 2000) {
            score -= 7;
            reasons.Add("High churn penalty (>2000 lines).");
        } else if (changeVolume > 800) {
            score -= 3;
            reasons.Add("Moderate churn penalty (>800 lines).");
        } else {
            score += 3;
            reasons.Add("Low churn bonus.");
        }

        if (signals.Commits > 40) {
            score -= 5;
            reasons.Add("Many commits penalty (>40).");
        }

        if (signals.Comments > 25) {
            score -= 4;
            reasons.Add("High discussion load penalty (>25 comments).");
        } else if (signals.Comments == 0) {
            score += 1;
            reasons.Add("No outstanding discussion bonus.");
        }

        if (item.TitleTokens.Count < 3) {
            score -= 6;
            reasons.Add("Sparse-title confidence penalty.");
        }

        if (item.ContextTokens.Count < 10) {
            score -= 8;
            reasons.Add("Sparse-description confidence penalty.");
        }

        var ageDays = Math.Max(0, (nowUtc - item.UpdatedAtUtc).TotalDays);
        if (ageDays <= 1) {
            score += 8;
            reasons.Add("Fresh activity bonus (<=1 day).");
        } else if (ageDays <= 3) {
            score += 5;
            reasons.Add("Recent activity bonus (<=3 days).");
        } else if (ageDays <= 7) {
            score += 2;
            reasons.Add("Active this week bonus.");
        } else if (ageDays > 30) {
            score -= 6;
            reasons.Add("Stale PR penalty (>30 days).");
        }

        foreach (var label in item.Labels) {
            if (label.Equals("blocked", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("do-not-merge", StringComparison.OrdinalIgnoreCase)) {
                score -= 15;
                reasons.Add($"Blocking label penalty ({label}).");
            } else if (label.Equals("ready", StringComparison.OrdinalIgnoreCase) ||
                       label.Equals("ready-to-merge", StringComparison.OrdinalIgnoreCase)) {
                score += 8;
                reasons.Add($"Ready label bonus ({label}).");
            } else if (label.Equals("wip", StringComparison.OrdinalIgnoreCase)) {
                score -= 10;
                reasons.Add("WIP label penalty.");
            }
        }

        return Math.Round(Math.Clamp(score, 0, 100), 2, MidpointRounding.AwayFromZero);
    }

}
