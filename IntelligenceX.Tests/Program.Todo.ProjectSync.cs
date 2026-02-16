using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestProjectFieldCatalogDefaultsIncludeVisionAndDecisionFields() {
        var names = IntelligenceX.Cli.Todo.ProjectFieldCatalog.DefaultFields
            .Select(field => field.Name)
            .ToList();

        AssertEqual(true, names.Contains("Vision Fit", StringComparer.OrdinalIgnoreCase), "has Vision Fit");
        AssertEqual(true, names.Contains("Vision Confidence", StringComparer.OrdinalIgnoreCase), "has Vision Confidence");
        AssertEqual(true, names.Contains("Category", StringComparer.OrdinalIgnoreCase), "has Category");
        AssertEqual(true, names.Contains("Tags", StringComparer.OrdinalIgnoreCase), "has Tags");
        AssertEqual(true, names.Contains("Matched Issue", StringComparer.OrdinalIgnoreCase), "has Matched Issue");
        AssertEqual(true, names.Contains("Matched Issue Confidence", StringComparer.OrdinalIgnoreCase), "has Matched Issue Confidence");
        AssertEqual(true, names.Contains("Matched Issue Reason", StringComparer.OrdinalIgnoreCase), "has Matched Issue Reason");
        AssertEqual(true, names.Contains("Related Issues", StringComparer.OrdinalIgnoreCase), "has Related Issues");
        AssertEqual(true, names.Contains("Matched Pull Request", StringComparer.OrdinalIgnoreCase), "has Matched Pull Request");
        AssertEqual(true, names.Contains("Matched Pull Request Confidence", StringComparer.OrdinalIgnoreCase), "has Matched Pull Request Confidence");
        AssertEqual(true, names.Contains("Matched Pull Request Reason", StringComparer.OrdinalIgnoreCase), "has Matched Pull Request Reason");
        AssertEqual(true, names.Contains("Related Pull Requests", StringComparer.OrdinalIgnoreCase), "has Related Pull Requests");
        AssertEqual(true, names.Contains("Triage Score", StringComparer.OrdinalIgnoreCase), "has Triage Score");
        AssertEqual(true, names.Contains("IX Suggested Decision", StringComparer.OrdinalIgnoreCase), "has IX Suggested Decision");
        AssertEqual(true, names.Contains("Maintainer Decision", StringComparer.OrdinalIgnoreCase), "has Maintainer Decision");
    }

    private static void TestProjectLabelCatalogDefaultsIncludeDecisionLabels() {
        var labels = IntelligenceX.Cli.Todo.ProjectLabelCatalog.DefaultLabels
            .Select(label => label.Name)
            .ToList();
        AssertEqual(true, labels.Contains("ix/decision:accept", StringComparer.OrdinalIgnoreCase), "decision accept label");
        AssertEqual(true, labels.Contains("ix/decision:defer", StringComparer.OrdinalIgnoreCase), "decision defer label");
        AssertEqual(true, labels.Contains("ix/decision:reject", StringComparer.OrdinalIgnoreCase), "decision reject label");
        AssertEqual(true, labels.Contains("ix/decision:merge-candidate", StringComparer.OrdinalIgnoreCase), "decision merge-candidate label");
        AssertEqual(true, labels.Contains("ix/match:linked-pr", StringComparer.OrdinalIgnoreCase), "issue linked-pr label");
        AssertEqual(true, labels.Contains("ix/match:needs-review-pr", StringComparer.OrdinalIgnoreCase), "issue needs-review-pr label");
    }

    private static void TestProjectLabelCatalogBuildEnsureCatalogIncludesDynamicCategoryAndTags() {
        var labels = IntelligenceX.Cli.Todo.ProjectLabelCatalog.BuildEnsureLabelCatalog(
            categories: new[] { "security", "ML Ops" },
            tags: new[] { "docs", "Release Candidate", "unknown-tag" });

        AssertEqual(true, labels.Any(label => label.Name.Equals("ix/category:security", StringComparison.OrdinalIgnoreCase)), "known category label present");
        AssertEqual(true, labels.Any(label => label.Name.Equals("ix/category:ml-ops", StringComparison.OrdinalIgnoreCase)), "dynamic category label present");
        AssertEqual(true, labels.Any(label => label.Name.Equals("ix/tag:docs", StringComparison.OrdinalIgnoreCase)), "known tag label present");
        AssertEqual(true, labels.Any(label => label.Name.Equals("ix/tag:release-candidate", StringComparison.OrdinalIgnoreCase)), "dynamic normalized tag label present");
        AssertEqual(true, labels.Any(label => label.Name.Equals("ix/tag:unknown-tag", StringComparison.OrdinalIgnoreCase)), "dynamic passthrough tag label present");
        AssertEqual(1, labels.Count(label => label.Name.Equals("ix/tag:docs", StringComparison.OrdinalIgnoreCase)), "known tag label deduped");
    }

    private static void TestProjectSyncBuildEntriesMergesVisionAndCanonical() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#10",
      "kind": "pull_request",
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/10",
      "score": 91.2,
      "duplicateClusterId": "cluster-1",
      "category": "feature",
      "tags": [ "api", "automation" ],
      "matchedIssueUrl": "https://github.com/EvotecIT/IntelligenceX/issues/11",
      "matchedIssueConfidence": 0.93,
      "relatedIssues": [
        {
          "number": 11,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/11",
          "confidence": 0.93,
          "reason": "explicit issue reference in PR title/body"
        },
        {
          "number": 19,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/19",
          "confidence": 0.61,
          "reason": "token similarity title=0.72, context=0.51"
        }
      ]
    },
    {
      "id": "issue#11",
      "kind": "issue",
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/11",
      "score": null,
      "duplicateClusterId": "cluster-1"
    }
  ],
  "duplicateClusters": [
    {
      "id": "cluster-1",
      "canonicalItemId": "pr#10"
    }
  ]
}
""";

        const string visionJson = """
{
  "assessments": [
    {
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/10",
      "classification": "likely-out-of-scope",
      "confidence": 0.87
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        using var visionDoc = System.Text.Json.JsonDocument.Parse(visionJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            visionDoc.RootElement,
            100);

        var pr = entries.First(item => item.Url.EndsWith("/pull/10", StringComparison.OrdinalIgnoreCase));
        AssertEqual("likely-out-of-scope", pr.VisionFit, "vision class merged");
        AssertEqual(true, pr.VisionConfidence.HasValue && pr.VisionConfidence.Value > 0.8, "vision confidence merged");
        AssertEqual(true, pr.TriageScore.HasValue && pr.TriageScore.Value > 90, "triage score preserved");
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/pull/10", pr.CanonicalItem, "canonical url resolved");
        AssertEqual("feature", pr.Category, "category merged");
        AssertEqual(true, pr.Tags.Contains("api", StringComparer.OrdinalIgnoreCase), "tags merged");
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/issues/11", pr.MatchedIssueUrl, "matched issue url merged");
        AssertEqual(true, pr.MatchedIssueConfidence.HasValue && pr.MatchedIssueConfidence.Value > 0.9, "matched issue confidence merged");
        AssertEqual(true, (pr.RelatedIssues?.Count ?? 0) == 2, "related issues merged");
        AssertEqual(11, pr.RelatedIssues![0].Number, "related issue ordering");
        AssertEqual("reject", pr.SuggestedDecision, "vision reject suggestion");

        var issue = entries.First(item => item.Url.EndsWith("/issues/11", StringComparison.OrdinalIgnoreCase));
        AssertEqual("issue", issue.Kind, "issue kind preserved");
        AssertEqual("cluster-1", issue.DuplicateCluster, "duplicate cluster preserved");
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/pull/10", issue.CanonicalItem, "issue canonical resolved");
        AssertEqual(true, string.IsNullOrWhiteSpace(issue.SuggestedDecision), "issues do not get automated decision");
    }

    private static void TestProjectSyncBuildEntriesParsesCategoryAndTagConfidences() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#410",
      "kind": "pull_request",
      "number": 410,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/410",
      "category": "security",
      "categoryConfidence": 0.83,
      "tags": [ "security", "api" ],
      "tagConfidences": {
        "security": 0.91,
        "api": 0.57
      }
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100);

        AssertEqual(1, entries.Count, "entries count");
        var entry = entries[0];
        AssertEqual(true, entry.CategoryConfidence.HasValue, "category confidence parsed");
        AssertEqual(0.83, entry.CategoryConfidence!.Value, "category confidence value");
        AssertEqual(true, entry.TagConfidences is not null, "tag confidence map parsed");
        AssertEqual(true, entry.TagConfidences!.ContainsKey("security"), "security tag confidence key");
        AssertEqual(0.91, entry.TagConfidences["security"], "security tag confidence value");
        AssertEqual(0.57, entry.TagConfidences["api"], "api tag confidence value");
    }

    private static void TestProjectSyncBuildEntriesSuggestsMergeCandidateForBestReadyPr() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#21",
      "kind": "pull_request",
      "number": 21,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/21",
      "score": 92.3,
      "signals": {
        "pullRequest": {
          "IsDraft": false,
          "Mergeable": "MERGEABLE",
          "ReviewDecision": "APPROVED",
          "StatusCheckState": "SUCCESS"
        }
      }
    }
  ],
  "bestPullRequests": [
    {
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/21"
    }
  ]
}
""";

        const string visionJson = """
{
  "assessments": [
    {
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/21",
      "classification": "aligned",
      "confidence": 0.84
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        using var visionDoc = System.Text.Json.JsonDocument.Parse(visionJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            visionDoc.RootElement,
            100);

        var pr = entries.Single(item => item.Url.EndsWith("/pull/21", StringComparison.OrdinalIgnoreCase));
        AssertEqual("merge-candidate", pr.SuggestedDecision, "best ready PR suggestion");
    }

    private static void TestProjectSyncBuildEntriesSuggestsDeferForBlockedPr() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#33",
      "kind": "pull_request",
      "number": 33,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/33",
      "score": 88.1,
      "signals": {
        "pullRequest": {
          "isDraft": false,
          "mergeable": "MERGEABLE",
          "reviewDecision": "CHANGES_REQUESTED",
          "statusCheckState": "SUCCESS"
        }
      }
    }
  ]
}
""";

        const string visionJson = """
{
  "assessments": [
    {
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/33",
      "classification": "aligned",
      "confidence": 0.91
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        using var visionDoc = System.Text.Json.JsonDocument.Parse(visionJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            visionDoc.RootElement,
            100);

        var pr = entries.Single(item => item.Url.EndsWith("/pull/33", StringComparison.OrdinalIgnoreCase));
        AssertEqual("defer", pr.SuggestedDecision, "blocked PR suggestion");
    }

    private static void TestProjectSyncBuildLabelsIncludesTagsAndHighConfidenceIssueMatch() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 42,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/42",
            Kind: "pull_request",
            TriageScore: 88.5,
            DuplicateCluster: "cluster-9",
            CanonicalItem: "https://github.com/EvotecIT/IntelligenceX/pull/42",
            Category: "security",
            Tags: new[] { "security", "api", "unknown-tag" },
            MatchedIssueUrl: "https://github.com/EvotecIT/IntelligenceX/issues/10",
            MatchedIssueConfidence: 0.95,
            VisionFit: "aligned",
            VisionConfidence: 0.9,
            SuggestedDecision: "merge-candidate"
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(entry);
        AssertEqual(true, labels.Contains("ix/category:security", StringComparer.OrdinalIgnoreCase), "category label");
        AssertEqual(true, labels.Contains("ix/tag:security", StringComparer.OrdinalIgnoreCase), "security tag label");
        AssertEqual(true, labels.Contains("ix/tag:api", StringComparer.OrdinalIgnoreCase), "api tag label");
        AssertEqual(true, labels.Contains("ix/tag:unknown-tag", StringComparer.OrdinalIgnoreCase), "dynamic tag label");
        AssertEqual(true, labels.Contains("ix/match:linked-issue", StringComparer.OrdinalIgnoreCase), "high confidence match label");
        AssertEqual(false, labels.Contains("ix/match:needs-review", StringComparer.OrdinalIgnoreCase), "no review label for high confidence");
        AssertEqual(true, labels.Contains("ix/decision:merge-candidate", StringComparer.OrdinalIgnoreCase), "decision label");
        AssertEqual(true, labels.Contains("ix/duplicate:clustered", StringComparer.OrdinalIgnoreCase), "duplicate label");
    }

    private static void TestProjectSyncBuildLabelsNormalizesDynamicCategoryAndTags() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 88,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/88",
            Kind: "pull_request",
            TriageScore: 79.4,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "ML Ops",
            Tags: new[] { "Release Candidate", "Docs" },
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(entry);
        AssertEqual(true, labels.Contains("ix/category:ml-ops", StringComparer.OrdinalIgnoreCase), "dynamic category label");
        AssertEqual(true, labels.Contains("ix/tag:release-candidate", StringComparer.OrdinalIgnoreCase), "dynamic tag label");
        AssertEqual(true, labels.Contains("ix/tag:docs", StringComparer.OrdinalIgnoreCase), "known normalized alias tag label");
    }

    private static void TestProjectSyncBuildLabelsSkipsLowConfidenceCategoryAndTags() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 89,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/89",
            Kind: "pull_request",
            TriageScore: 72.0,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "security",
            Tags: new[] { "security", "api" },
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            CategoryConfidence: 0.58,
            TagConfidences: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) {
                ["security"] = 0.55,
                ["api"] = 0.71
            }
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(entry);
        AssertEqual(false, labels.Contains("ix/category:security", StringComparer.OrdinalIgnoreCase), "low confidence category label skipped");
        AssertEqual(false, labels.Contains("ix/tag:security", StringComparer.OrdinalIgnoreCase), "low confidence tag label skipped");
        AssertEqual(true, labels.Contains("ix/tag:api", StringComparer.OrdinalIgnoreCase), "high confidence tag label kept");
    }

    private static void TestProjectSyncBuildLabelsUsesNeedsReviewForLowConfidenceIssueMatch() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 77,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/77",
            Kind: "pull_request",
            TriageScore: 60.0,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: "https://github.com/EvotecIT/IntelligenceX/issues/99",
            MatchedIssueConfidence: 0.62,
            VisionFit: null,
            VisionConfidence: null,
            SuggestedDecision: "defer"
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(entry);
        AssertEqual(true, labels.Contains("ix/match:needs-review", StringComparer.OrdinalIgnoreCase), "low confidence match review label");
        AssertEqual(false, labels.Contains("ix/match:linked-issue", StringComparer.OrdinalIgnoreCase), "low confidence should not be linked-issue");
        AssertEqual(true, labels.Contains("ix/decision:defer", StringComparer.OrdinalIgnoreCase), "decision defer label");
    }

    private static void TestProjectSyncBuildLabelsUsesRelatedIssueFallbackWhenMatchedIssueMissing() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 79,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/79",
            Kind: "pull_request",
            TriageScore: 73.0,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            RelatedIssues: new[] {
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(20, "https://github.com/EvotecIT/IntelligenceX/issues/20", 0.87, "explicit issue reference")
            }
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(entry);
        AssertEqual(true, labels.Contains("ix/match:linked-issue", StringComparer.OrdinalIgnoreCase), "fallback related issue adds linked-issue label");
    }

    private static void TestProjectSyncBuildLabelsUsesIssuePullRequestMatchSignals() {
        var highConfidenceIssue = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 120,
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/120",
            Kind: "issue",
            TriageScore: null,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "bug",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            MatchedPullRequestUrl: "https://github.com/EvotecIT/IntelligenceX/pull/77",
            MatchedPullRequestConfidence: 0.89
        );
        var lowConfidenceIssue = highConfidenceIssue with {
            Number = 121,
            Url = "https://github.com/EvotecIT/IntelligenceX/issues/121",
            MatchedPullRequestConfidence = 0.61
        };

        var highLabels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(highConfidenceIssue);
        var lowLabels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(lowConfidenceIssue);

        AssertEqual(true, highLabels.Contains("ix/match:linked-pr", StringComparer.OrdinalIgnoreCase), "high confidence issue gets linked-pr label");
        AssertEqual(false, highLabels.Contains("ix/match:needs-review-pr", StringComparer.OrdinalIgnoreCase), "high confidence issue does not get needs-review-pr label");
        AssertEqual(true, lowLabels.Contains("ix/match:needs-review-pr", StringComparer.OrdinalIgnoreCase), "low confidence issue gets needs-review-pr label");
        AssertEqual(false, lowLabels.Contains("ix/match:linked-pr", StringComparer.OrdinalIgnoreCase), "low confidence issue does not get linked-pr label");
    }

    private static void TestProjectSyncBuildLabelsUsesIssueRelatedPullRequestFallbackWhenMatchedPullRequestMissing() {
        var issue = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 122,
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/122",
            Kind: "issue",
            TriageScore: null,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            MatchedPullRequestUrl: null,
            MatchedPullRequestConfidence: null,
            RelatedPullRequests: new[] {
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(83, "https://github.com/EvotecIT/IntelligenceX/pull/83", 0.86, "explicit pull request reference in issue title/body")
            }
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(issue);
        AssertEqual(true, labels.Contains("ix/match:linked-pr", StringComparer.OrdinalIgnoreCase), "fallback related pull request adds linked-pr label");
        AssertEqual(false, labels.Contains("ix/match:needs-review-pr", StringComparer.OrdinalIgnoreCase), "high confidence fallback should not need review label");
    }

    private static void TestProjectSyncBuildEntriesDerivesIssuePullRequestMatches() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#77",
      "kind": "pull_request",
      "number": 77,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/77",
      "relatedIssues": [
        {
          "number": 120,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/120",
          "confidence": 0.89,
          "reason": "explicit issue reference in PR title/body"
        }
      ]
    },
    {
      "id": "issue#120",
      "kind": "issue",
      "number": 120,
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/120"
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100);

        var issue = entries.Single(item => item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase));
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/pull/77", issue.MatchedPullRequestUrl, "issue matched pull request url derived");
        AssertEqual(true, issue.MatchedPullRequestConfidence.HasValue && issue.MatchedPullRequestConfidence.Value >= 0.89, "issue matched pull request confidence derived");
        AssertEqual(true, issue.MatchedPullRequestReason?.Contains("explicit issue reference", StringComparison.OrdinalIgnoreCase) == true, "issue matched pull request reason derived");
    }

    private static void TestProjectSyncBuildEntriesPreservesHigherConfidenceIssueSidePullRequestMatch() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#77",
      "kind": "pull_request",
      "number": 77,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/77",
      "relatedIssues": [
        {
          "number": 120,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/120",
          "confidence": 0.61,
          "reason": "token overlap"
        }
      ]
    },
    {
      "id": "issue#120",
      "kind": "issue",
      "number": 120,
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/120",
      "matchedPullRequestUrl": "https://github.com/EvotecIT/IntelligenceX/pull/88",
      "matchedPullRequestConfidence": 0.97,
      "matchedPullRequestReason": "manual maintainer link"
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100);

        var issue = entries.Single(item => item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase));
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/pull/88", issue.MatchedPullRequestUrl, "issue-side pull request match preserved");
        AssertEqual(true, issue.MatchedPullRequestConfidence.HasValue && issue.MatchedPullRequestConfidence.Value >= 0.97, "issue-side confidence preserved");
        AssertEqual("manual maintainer link", issue.MatchedPullRequestReason, "issue-side reason preserved");
    }

    private static void TestProjectSyncBuildEntriesUsesIssueRelatedPullRequestFallback() {
        const string triageJson = """
{
  "items": [
    {
      "id": "issue#130",
      "kind": "issue",
      "number": 130,
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/130",
      "relatedPullRequests": [
        {
          "number": 91,
          "url": "https://github.com/EvotecIT/IntelligenceX/pull/91",
          "confidence": 0.72,
          "reason": "token overlap"
        },
        {
          "number": 90,
          "url": "https://github.com/EvotecIT/IntelligenceX/pull/90",
          "confidence": 0.88,
          "reason": "explicit pull request reference in issue title/body"
        }
      ]
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100);

        var issue = entries.Single(item => item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase));
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/pull/90", issue.MatchedPullRequestUrl, "fallback uses top related pull request");
        AssertEqual(true, issue.MatchedPullRequestConfidence.HasValue && issue.MatchedPullRequestConfidence.Value >= 0.88, "fallback confidence derived from top related pull request");
        AssertEqual(true, issue.MatchedPullRequestReason?.Contains("explicit pull request reference", StringComparison.OrdinalIgnoreCase) == true, "fallback reason derived from top related pull request");
    }

    private static void TestProjectSyncBuildEntriesUsesPullRequestRelatedIssueFallback() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#141",
      "kind": "pull_request",
      "number": 141,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/141",
      "relatedIssues": [
        {
          "number": 201,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/201",
          "confidence": 0.88,
          "reason": "explicit issue reference in PR title/body"
        },
        {
          "number": 202,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/202",
          "confidence": 0.71,
          "reason": "token overlap"
        }
      ]
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100);

        var pullRequest = entries.Single(item => item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase));
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/issues/201", pullRequest.MatchedIssueUrl, "fallback uses top related issue");
        AssertEqual(true, pullRequest.MatchedIssueConfidence.HasValue && pullRequest.MatchedIssueConfidence.Value >= 0.88, "fallback confidence derived from top related issue");
        AssertEqual(true, pullRequest.MatchedIssueReason?.Contains("explicit issue reference", StringComparison.OrdinalIgnoreCase) == true, "fallback reason derived from top related issue");
    }

    private static void TestProjectSyncBuildEntriesDerivesMissingMatchedIssueConfidenceFromRelatedIssues() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#142",
      "kind": "pull_request",
      "number": 142,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/142",
      "matchedIssueUrl": "https://github.com/EvotecIT/IntelligenceX/issues/220",
      "relatedIssues": [
        {
          "number": 220,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/220",
          "confidence": 0.64,
          "reason": "token overlap"
        },
        {
          "number": 221,
          "url": "https://github.com/EvotecIT/IntelligenceX/issues/221",
          "confidence": 0.90,
          "reason": "explicit issue reference in PR title/body"
        }
      ]
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100);

        var pullRequest = entries.Single(item => item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase));
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/issues/220", pullRequest.MatchedIssueUrl, "explicit matched issue url preserved");
        AssertEqual(true, pullRequest.MatchedIssueConfidence.HasValue && pullRequest.MatchedIssueConfidence.Value >= 0.64, "matched issue confidence derived from same related issue");
        AssertEqual(true, pullRequest.MatchedIssueReason?.Contains("token overlap", StringComparison.OrdinalIgnoreCase) == true, "matched issue reason derived from same related issue");
    }

    private static void TestProjectSyncBuildIssueMatchSuggestionCommentFiltersByConfidence() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 55,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/55",
            Kind: "pull_request",
            TriageScore: 71.1,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: "https://github.com/EvotecIT/IntelligenceX/issues/10",
            MatchedIssueConfidence: 0.88,
            VisionFit: "aligned",
            VisionConfidence: 0.73,
            RelatedIssues: new[] {
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(10, "https://github.com/EvotecIT/IntelligenceX/issues/10", 0.88, "explicit issue reference in PR title/body"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(12, "https://github.com/EvotecIT/IntelligenceX/issues/12", 0.49, "token similarity title=0.62, context=0.31")
            }
        );

        var comment = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildIssueMatchSuggestionComment(entry, minConfidence: 0.50, maxIssues: 3);
        AssertEqual(true, !string.IsNullOrWhiteSpace(comment), "comment generated");
        AssertContainsText(comment ?? string.Empty, "<!-- intelligencex:pr-issue-suggestions -->", "marker present");
        AssertContainsText(comment ?? string.Empty, "https://github.com/EvotecIT/IntelligenceX/issues/10", "high confidence issue included");
        AssertEqual(false, (comment ?? string.Empty).Contains("issues/12", StringComparison.OrdinalIgnoreCase), "low confidence issue excluded");
    }

    private static void TestProjectSyncBuildPullRequestIssueSuggestionCommentsIncludesIssueSideCandidates() {
        var entries = new[] {
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 210,
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/210",
                Kind: "pull_request",
                TriageScore: 71.0,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: "https://github.com/EvotecIT/IntelligenceX/issues/310",
                MatchedIssueConfidence: 0.62,
                VisionFit: "aligned",
                VisionConfidence: 0.74,
                RelatedIssues: new[] {
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(310, "https://github.com/EvotecIT/IntelligenceX/issues/310", 0.60, "token overlap")
                }
            ),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 310,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/310",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null,
                RelatedPullRequests: new[] {
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(210, "https://github.com/EvotecIT/IntelligenceX/pull/210", 0.87, "explicit pull request reference")
                }
            ),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 311,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/311",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null,
                MatchedPullRequestUrl: "https://github.com/EvotecIT/IntelligenceX/pull/210",
                MatchedPullRequestConfidence: 0.79
            ),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 312,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/312",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null,
                RelatedPullRequests: new[] {
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(210, "https://github.com/EvotecIT/IntelligenceX/pull/210", 0.41, "weak overlap")
                }
            )
        };

        var comments = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildPullRequestIssueSuggestionComments(
            entries,
            minConfidence: 0.55,
            maxIssuesPerPullRequest: 3);

        AssertEqual(true, comments.ContainsKey(210), "pull request comment generated");
        var comment = comments[210];
        AssertContainsText(comment, "<!-- intelligencex:pr-issue-suggestions -->", "pull request marker present");
        AssertContainsText(comment, "#310 (https://github.com/EvotecIT/IntelligenceX/issues/310) - confidence 0.87", "issue-side confidence supersedes weaker PR-side candidate");
        AssertContainsText(comment, "#311 (https://github.com/EvotecIT/IntelligenceX/issues/311)", "matched pull request fallback contributes issue");
        AssertEqual(false, comment.Contains("issues/312", StringComparison.OrdinalIgnoreCase), "below-threshold issue-side candidate excluded");
    }

    private static void TestProjectSyncBuildStaleSuggestionCommentTargetsForPullRequests() {
        var entries = new[] {
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 501,
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/501",
                Kind: "pull_request",
                TriageScore: 80.0,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 502,
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/502",
                Kind: "pull_request",
                TriageScore: 82.0,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 601,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/601",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null)
        };

        var activeComments = new Dictionary<int, string> {
            [501] = "active"
        };

        var staleTargets = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildStaleSuggestionCommentTargets(
            entries,
            kind: "pull_request",
            activeComments: activeComments);

        AssertEqual(1, staleTargets.Count, "single stale pull request comment target");
        AssertEqual(502, staleTargets[0], "pull request without active suggestion is stale target");
    }

    private static void TestProjectSyncBuildStaleSuggestionCommentTargetsForIssuesDedupesAndSorts() {
        var entries = new[] {
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 702,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/702",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "bug",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 700,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/700",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "bug",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 701,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/701",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "bug",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 700,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/700",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "bug",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null)
        };

        var activeComments = new Dictionary<int, string> {
            [701] = "active"
        };

        var staleTargets = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildStaleSuggestionCommentTargets(
            entries,
            kind: "issue",
            activeComments: activeComments);

        AssertEqual(2, staleTargets.Count, "two stale issue comment targets");
        AssertEqual(700, staleTargets[0], "sorted stale issue target #1");
        AssertEqual(702, staleTargets[1], "sorted stale issue target #2");
    }

    private static void TestProjectSyncBuildIssueMatchSuggestionCommentReturnsNullWithoutQualifiedCandidates() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 56,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/56",
            Kind: "pull_request",
            TriageScore: 48.2,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            RelatedIssues: new[] {
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(15, "https://github.com/EvotecIT/IntelligenceX/issues/15", 0.22, "weak token overlap")
            }
        );

        var comment = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildIssueMatchSuggestionComment(entry, minConfidence: 0.55, maxIssues: 3);
        AssertEqual(true, string.IsNullOrWhiteSpace(comment), "no comment for weak matches");
    }

    private static void TestProjectSyncBuildIssueBacklinkSuggestionCommentsAggregatesPullRequests() {
        var entries = new[] {
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 70,
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/70",
                Kind: "pull_request",
                TriageScore: 71.5,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: "https://github.com/EvotecIT/IntelligenceX/issues/15",
                MatchedIssueConfidence: 0.93,
                VisionFit: "aligned",
                VisionConfidence: 0.78,
                RelatedIssues: new[] {
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(15, "https://github.com/EvotecIT/IntelligenceX/issues/15", 0.93, "explicit issue reference"),
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(22, "https://github.com/EvotecIT/IntelligenceX/issues/22", 0.57, "token overlap")
                }
            ),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 71,
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/71",
                Kind: "pull_request",
                TriageScore: 66.0,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: "https://github.com/EvotecIT/IntelligenceX/issues/15",
                MatchedIssueConfidence: 0.62,
                VisionFit: "needs-human-review",
                VisionConfidence: 0.55,
                RelatedIssues: new[] {
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(15, "https://github.com/EvotecIT/IntelligenceX/issues/15", 0.62, "token overlap")
                }
            )
        };

        var comments = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildIssueBacklinkSuggestionComments(
            entries,
            minConfidence: 0.55,
            maxPullRequestsPerIssue: 3);

        AssertEqual(true, comments.ContainsKey(15), "issue 15 has backlink comment");
        var issue15 = comments[15];
        AssertContainsText(issue15, "<!-- intelligencex:issue-pr-suggestions -->", "issue marker present");
        AssertContainsText(issue15, "PR #70", "top PR included");
        AssertContainsText(issue15, "PR #71", "second PR included");
    }

    private static void TestProjectSyncBuildIssueBacklinkSuggestionCommentsIncludesIssueSideCandidates() {
        var entries = new[] {
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
                Number: 160,
                Url: "https://github.com/EvotecIT/IntelligenceX/issues/160",
                Kind: "issue",
                TriageScore: null,
                DuplicateCluster: null,
                CanonicalItem: null,
                Category: "feature",
                Tags: Array.Empty<string>(),
                MatchedIssueUrl: null,
                MatchedIssueConfidence: null,
                VisionFit: null,
                VisionConfidence: null,
                MatchedPullRequestUrl: "https://github.com/EvotecIT/IntelligenceX/pull/92",
                MatchedPullRequestConfidence: 0.77,
                RelatedPullRequests: new[] {
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(91, "https://github.com/EvotecIT/IntelligenceX/pull/91", 0.88, "explicit pull request reference"),
                    new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(90, "https://github.com/EvotecIT/IntelligenceX/pull/90", 0.43, "weak overlap")
                }
            )
        };

        var comments = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildIssueBacklinkSuggestionComments(
            entries,
            minConfidence: 0.55,
            maxPullRequestsPerIssue: 3);

        AssertEqual(true, comments.ContainsKey(160), "issue-side suggestions produce comment");
        var issue160 = comments[160];
        AssertContainsText(issue160, "PR #91", "high-confidence issue-side candidate included");
        AssertContainsText(issue160, "PR #92", "matched pull request fallback included");
        AssertEqual(false, issue160.Contains("PR #90", StringComparison.OrdinalIgnoreCase), "below-threshold candidate excluded");
    }

    private static void TestProjectSyncBuildIssueBacklinkSuggestionCommentRespectsThresholdAndLimit() {
        var candidates = new[] {
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(81, "https://github.com/EvotecIT/IntelligenceX/pull/81", 0.91, "explicit"),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(82, "https://github.com/EvotecIT/IntelligenceX/pull/82", 0.74, "token overlap"),
            new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(83, "https://github.com/EvotecIT/IntelligenceX/pull/83", 0.44, "weak overlap")
        };

        var comment = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildIssueBacklinkSuggestionComment(
            issueNumber: 30,
            candidates: candidates,
            minConfidence: 0.55,
            maxPullRequests: 1);

        AssertEqual(true, !string.IsNullOrWhiteSpace(comment), "issue backlink comment generated");
        AssertContainsText(comment ?? string.Empty, "PR #81", "top confidence PR kept");
        AssertEqual(false, (comment ?? string.Empty).Contains("PR #82", StringComparison.OrdinalIgnoreCase), "limited to maxPullRequests");
        AssertEqual(false, (comment ?? string.Empty).Contains("PR #83", StringComparison.OrdinalIgnoreCase), "below threshold excluded");
    }

    private static void TestProjectSyncBuildRelatedIssuesFieldValueOrdersAndLimitsCandidates() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 57,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/57",
            Kind: "pull_request",
            TriageScore: 70.0,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            RelatedIssues: new[] {
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(20, "https://github.com/EvotecIT/IntelligenceX/issues/20", 0.52, "token overlap"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(11, "https://github.com/EvotecIT/IntelligenceX/issues/11", 0.93, "explicit"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(19, "https://github.com/EvotecIT/IntelligenceX/issues/19", 0.61, "token overlap"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedIssueCandidate(18, "https://github.com/EvotecIT/IntelligenceX/issues/18", 0.58, "token overlap")
            }
        );

        var value = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildRelatedIssuesFieldValue(entry, maxIssues: 3);
        AssertContainsText(value, "#11 | 0.93", "top candidate first");
        AssertContainsText(value, "#19 | 0.61", "second candidate");
        AssertContainsText(value, "#18 | 0.58", "third candidate");
        AssertEqual(false, value.Contains("#20", StringComparison.OrdinalIgnoreCase), "limited to top three");
    }

    private static void TestProjectSyncBuildRelatedPullRequestsFieldValueOrdersAndLimitsCandidates() {
        var issue = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 161,
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/161",
            Kind: "issue",
            TriageScore: null,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "feature",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            RelatedPullRequests: new[] {
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(20, "https://github.com/EvotecIT/IntelligenceX/pull/20", 0.52, "token overlap"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(11, "https://github.com/EvotecIT/IntelligenceX/pull/11", 0.93, "explicit"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(19, "https://github.com/EvotecIT/IntelligenceX/pull/19", 0.61, "token overlap"),
                new IntelligenceX.Cli.Todo.ProjectSyncRunner.RelatedPullRequestCandidate(18, "https://github.com/EvotecIT/IntelligenceX/pull/18", 0.58, "token overlap")
            }
        );

        var value = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildRelatedPullRequestsFieldValue(issue, maxPullRequests: 3);
        AssertContainsText(value, "PR #11 | 0.93", "top pull request candidate first");
        AssertContainsText(value, "PR #19 | 0.61", "second pull request candidate");
        AssertContainsText(value, "PR #18 | 0.58", "third pull request candidate");
        AssertEqual(false, value.Contains("PR #20", StringComparison.OrdinalIgnoreCase), "limited to top three pull request candidates");
    }

    private static void TestProjectSyncBuildMatchReasonFieldValueNormalizesReasonText() {
        var empty = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildMatchReasonFieldValue("   ");
        AssertEqual(string.Empty, empty, "empty reason values omitted");

        var normalized = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildMatchReasonFieldValue(" explicit issue reference \r\n in PR body ");
        AssertEqual("explicit issue reference in PR body", normalized, "reason whitespace normalized");
    }
#endif
}
