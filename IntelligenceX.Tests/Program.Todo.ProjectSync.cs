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
        AssertEqual(true, names.Contains("Related Issues", StringComparer.OrdinalIgnoreCase), "has Related Issues");
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
        AssertEqual(false, labels.Any(label => label.Contains("unknown-tag", StringComparison.OrdinalIgnoreCase)), "unsupported tag skipped");
        AssertEqual(true, labels.Contains("ix/match:linked-issue", StringComparer.OrdinalIgnoreCase), "high confidence match label");
        AssertEqual(false, labels.Contains("ix/match:needs-review", StringComparer.OrdinalIgnoreCase), "no review label for high confidence");
        AssertEqual(true, labels.Contains("ix/decision:merge-candidate", StringComparer.OrdinalIgnoreCase), "decision label");
        AssertEqual(true, labels.Contains("ix/duplicate:clustered", StringComparer.OrdinalIgnoreCase), "duplicate label");
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
#endif
}
