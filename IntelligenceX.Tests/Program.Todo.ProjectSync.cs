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
        AssertEqual(true, names.Contains("Category Confidence", StringComparer.OrdinalIgnoreCase), "has Category Confidence");
        AssertEqual(true, names.Contains("Signal Quality", StringComparer.OrdinalIgnoreCase), "has Signal Quality");
        AssertEqual(true, names.Contains("Signal Quality Score", StringComparer.OrdinalIgnoreCase), "has Signal Quality Score");
        AssertEqual(true, names.Contains("Signal Quality Notes", StringComparer.OrdinalIgnoreCase), "has Signal Quality Notes");
        AssertEqual(true, names.Contains("PR Size", StringComparer.OrdinalIgnoreCase), "has PR Size");
        AssertEqual(true, names.Contains("PR Churn Risk", StringComparer.OrdinalIgnoreCase), "has PR Churn Risk");
        AssertEqual(true, names.Contains("PR Merge Readiness", StringComparer.OrdinalIgnoreCase), "has PR Merge Readiness");
        AssertEqual(true, names.Contains("PR Freshness", StringComparer.OrdinalIgnoreCase), "has PR Freshness");
        AssertEqual(true, names.Contains("PR Check Health", StringComparer.OrdinalIgnoreCase), "has PR Check Health");
        AssertEqual(true, names.Contains("PR Review Latency", StringComparer.OrdinalIgnoreCase), "has PR Review Latency");
        AssertEqual(true, names.Contains("PR Merge Conflict Risk", StringComparer.OrdinalIgnoreCase), "has PR Merge Conflict Risk");
        AssertEqual(true, names.Contains("Tags", StringComparer.OrdinalIgnoreCase), "has Tags");
        AssertEqual(true, names.Contains("Tag Confidence Summary", StringComparer.OrdinalIgnoreCase), "has Tag Confidence Summary");
        AssertEqual(true, names.Contains("Matched Issue", StringComparer.OrdinalIgnoreCase), "has Matched Issue");
        AssertEqual(true, names.Contains("Matched Issue Confidence", StringComparer.OrdinalIgnoreCase), "has Matched Issue Confidence");
        AssertEqual(true, names.Contains("Matched Issue Reason", StringComparer.OrdinalIgnoreCase), "has Matched Issue Reason");
        AssertEqual(true, names.Contains("Related Issues", StringComparer.OrdinalIgnoreCase), "has Related Issues");
        AssertEqual(true, names.Contains("Matched Pull Request", StringComparer.OrdinalIgnoreCase), "has Matched Pull Request");
        AssertEqual(true, names.Contains("Matched Pull Request Confidence", StringComparer.OrdinalIgnoreCase), "has Matched Pull Request Confidence");
        AssertEqual(true, names.Contains("Matched Pull Request Reason", StringComparer.OrdinalIgnoreCase), "has Matched Pull Request Reason");
        AssertEqual(true, names.Contains("Related Pull Requests", StringComparer.OrdinalIgnoreCase), "has Related Pull Requests");
        AssertEqual(true, names.Contains("Issue Review Action", StringComparer.OrdinalIgnoreCase), "has Issue Review Action");
        AssertEqual(true, names.Contains("Issue Review Action Confidence", StringComparer.OrdinalIgnoreCase), "has Issue Review Action Confidence");
        AssertEqual(true, names.Contains("Triage Score", StringComparer.OrdinalIgnoreCase), "has Triage Score");
        AssertEqual(true, names.Contains("IX Suggested Decision", StringComparer.OrdinalIgnoreCase), "has IX Suggested Decision");
        AssertEqual(true, names.Contains("Maintainer Decision", StringComparer.OrdinalIgnoreCase), "has Maintainer Decision");
        AssertEqual(false, names.Contains("PR Governance Signal", StringComparer.OrdinalIgnoreCase), "optional governance signal field excluded by default");
        AssertEqual(false, names.Contains("PR Governance Summary", StringComparer.OrdinalIgnoreCase), "optional governance summary field excluded by default");
    }

    private static void TestProjectFieldCatalogBuildEnsureFieldCatalogIncludesOptionalPrWatchGovernanceFields() {
        var names = IntelligenceX.Cli.Todo.ProjectFieldCatalog.BuildEnsureFieldCatalog(includePrWatchGovernance: true)
            .Select(field => field.Name)
            .ToList();

        AssertEqual(true, names.Contains("PR Governance Signal", StringComparer.OrdinalIgnoreCase), "optional governance signal field included when enabled");
        AssertEqual(true, names.Contains("PR Governance Summary", StringComparer.OrdinalIgnoreCase), "optional governance summary field included when enabled");
    }

    private static void TestProjectLabelCatalogDefaultsIncludeDecisionLabels() {
        var labels = IntelligenceX.Cli.Todo.ProjectLabelCatalog.DefaultLabels
            .Select(label => label.Name)
            .ToList();
        AssertEqual(true, labels.Contains("ix/decision:accept", StringComparer.OrdinalIgnoreCase), "decision accept label");
        AssertEqual(true, labels.Contains("ix/decision:defer", StringComparer.OrdinalIgnoreCase), "decision defer label");
        AssertEqual(true, labels.Contains("ix/decision:reject", StringComparer.OrdinalIgnoreCase), "decision reject label");
        AssertEqual(true, labels.Contains("ix/decision:merge-candidate", StringComparer.OrdinalIgnoreCase), "decision merge-candidate label");
        AssertEqual(true, labels.Contains("ix/signal:low", StringComparer.OrdinalIgnoreCase), "signal low label");
        AssertEqual(true, labels.Contains("ix/match:linked-pr", StringComparer.OrdinalIgnoreCase), "issue linked-pr label");
        AssertEqual(true, labels.Contains("ix/match:needs-review-pr", StringComparer.OrdinalIgnoreCase), "issue needs-review-pr label");
        AssertEqual(true, labels.Contains("ix/pr-watch:policy-review-suggested", StringComparer.OrdinalIgnoreCase), "pr-watch governance label");
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

    private static void TestProjectSyncParseOptionsSupportsGovernanceNoFlags() {
        var options = IntelligenceX.Cli.Todo.ProjectSyncRunner.ParseOptions(new[] {
            "--repo", "EvotecIT/IntelligenceX",
            "--no-apply-pr-watch-governance-labels",
            "--no-apply-pr-watch-governance-fields"
        });

        AssertEqual(false, options.ApplyPrWatchGovernanceLabels, "governance labels disabled");
        AssertEqual(true, options.ApplyPrWatchGovernanceLabelsSpecified, "governance labels explicit disable recorded");
        AssertEqual(false, options.ApplyPrWatchGovernanceFields, "governance fields disabled");
        AssertEqual(true, options.ApplyPrWatchGovernanceFieldsSpecified, "governance fields explicit disable recorded");
    }

    private static void TestProjectSyncApplyProjectConfigFeatureDefaultsUsesConfigWhenUnspecified() {
        var options = IntelligenceX.Cli.Todo.ProjectSyncRunner.ParseOptions(new[] {
            "--repo", "EvotecIT/IntelligenceX"
        });

        IntelligenceX.Cli.Todo.ProjectSyncRunner.ApplyProjectConfigFeatureDefaults(
            options,
            new IntelligenceX.Cli.Todo.ProjectConfigFeatures(
                PrWatchGovernanceLabels: true,
                PrWatchGovernanceFields: true,
                PrWatchGovernanceViews: true));

        AssertEqual(true, options.ApplyPrWatchGovernanceLabels, "config enables governance labels");
        AssertEqual(true, options.ApplyPrWatchGovernanceFields, "config enables governance fields");
    }

    private static void TestProjectSyncApplyProjectConfigFeatureDefaultsRespectsExplicitOverrides() {
        var options = IntelligenceX.Cli.Todo.ProjectSyncRunner.ParseOptions(new[] {
            "--repo", "EvotecIT/IntelligenceX",
            "--no-apply-pr-watch-governance-labels",
            "--apply-pr-watch-governance-fields"
        });

        IntelligenceX.Cli.Todo.ProjectSyncRunner.ApplyProjectConfigFeatureDefaults(
            options,
            new IntelligenceX.Cli.Todo.ProjectConfigFeatures(
                PrWatchGovernanceLabels: true,
                PrWatchGovernanceFields: false,
                PrWatchGovernanceViews: true));

        AssertEqual(false, options.ApplyPrWatchGovernanceLabels, "explicit no-labels wins over config");
        AssertEqual(true, options.ApplyPrWatchGovernanceFields, "explicit apply-fields wins over config");
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
      "signalQuality": "low",
      "signalQualityScore": 42.5,
      "signalQualityReasons": [
        "Description/context is sparse.",
        "No labels present."
      ],
      "prSizeBand": "xsmall",
      "prChurnRisk": "low",
      "prMergeReadiness": "ready",
      "prFreshness": "fresh",
      "prCheckHealth": "healthy",
      "prReviewLatency": "low",
      "prMergeConflictRisk": "low",
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
        AssertEqual("low", entry.SignalQuality, "signal quality parsed");
        AssertEqual(true, entry.SignalQualityScore.HasValue, "signal quality score parsed");
        AssertEqual(42.5, entry.SignalQualityScore!.Value, "signal quality score value");
        AssertEqual(true, (entry.SignalQualityReasons?.Count ?? 0) == 2, "signal quality reasons parsed");
        AssertEqual("xsmall", entry.PullRequestSize, "pull request size parsed");
        AssertEqual("low", entry.PullRequestChurnRisk, "pull request churn risk parsed");
        AssertEqual("ready", entry.PullRequestMergeReadiness, "pull request merge readiness parsed");
        AssertEqual("fresh", entry.PullRequestFreshness, "pull request freshness parsed");
        AssertEqual("healthy", entry.PullRequestCheckHealth, "pull request check health parsed");
        AssertEqual("low", entry.PullRequestReviewLatency, "pull request review latency parsed");
        AssertEqual("low", entry.PullRequestMergeConflictRisk, "pull request merge conflict risk parsed");
    }

    private static void TestProjectSyncBuildEntriesMergesIssueReviewSignals() {
        const string triageJson = """
{
  "items": [
    {
      "id": "issue#362",
      "kind": "issue",
      "number": 362,
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/362",
      "score": 17.0
    }
  ]
}
""";

        const string issueReviewJson = """
{
  "items": [
    {
      "number": 362,
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/362",
      "proposedAction": "close",
      "actionConfidence": 92
    },
    {
      "number": 440,
      "url": "https://github.com/EvotecIT/IntelligenceX/issues/440",
      "proposedAction": "needs-human-review",
      "actionConfidence": 64
    }
  ]
}
""";

        using var triageDoc = System.Text.Json.JsonDocument.Parse(triageJson);
        using var issueReviewDoc = System.Text.Json.JsonDocument.Parse(issueReviewJson);
        var entries = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildEntriesFromDocuments(
            triageDoc.RootElement,
            null,
            100,
            issueReviewDoc.RootElement);

        var issue362 = entries.Single(item => item.Url.EndsWith("/issues/362", StringComparison.OrdinalIgnoreCase));
        AssertEqual("issue", issue362.Kind, "issue kind preserved");
        AssertEqual("close", issue362.IssueReviewAction, "issue action merged");
        AssertEqual(true, issue362.IssueReviewActionConfidence.HasValue, "issue confidence merged");
        AssertEqual(92.0, issue362.IssueReviewActionConfidence!.Value, "issue confidence value merged");

        var issue440 = entries.Single(item => item.Url.EndsWith("/issues/440", StringComparison.OrdinalIgnoreCase));
        AssertEqual("issue", issue440.Kind, "issue-only review entry created");
        AssertEqual("needs-human-review", issue440.IssueReviewAction, "issue-only action merged");
        AssertEqual(true, issue440.IssueReviewActionConfidence.HasValue, "issue-only confidence merged");
        AssertEqual(64.0, issue440.IssueReviewActionConfidence!.Value, "issue-only confidence value merged");
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

    private static void TestProjectSyncBuildEntriesSuggestsDeferForLowSignalPr() {
        const string triageJson = """
{
  "items": [
    {
      "id": "pr#37",
      "kind": "pull_request",
      "number": 37,
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/37",
      "score": 94.6,
      "signalQuality": "low",
      "signalQualityScore": 41.0,
      "signals": {
        "pullRequest": {
          "isDraft": false,
          "mergeable": "MERGEABLE",
          "reviewDecision": "APPROVED",
          "statusCheckState": "SUCCESS"
        }
      }
    }
  ],
  "bestPullRequests": [
    {
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/37"
    }
  ]
}
""";

        const string visionJson = """
{
  "assessments": [
    {
      "url": "https://github.com/EvotecIT/IntelligenceX/pull/37",
      "classification": "aligned",
      "confidence": 0.92
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

        var pr = entries.Single(item => item.Url.EndsWith("/pull/37", StringComparison.OrdinalIgnoreCase));
        AssertEqual("defer", pr.SuggestedDecision, "low signal PR is deferred");
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
            SuggestedDecision: "merge-candidate",
            PrWatchGovernanceSuggested: true
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
        AssertEqual(true, labels.Contains("ix/pr-watch:policy-review-suggested", StringComparer.OrdinalIgnoreCase), "pr-watch governance label");
    }

    private static void TestProjectSyncGovernanceContextPrefersWeeklyTrackerAndParsesSignal() {
        const string issuesJson = """
[
  {
    "html_url": "https://github.com/EvotecIT/IntelligenceX/issues/201",
    "body": "<!-- intelligencex:pr-watch-rollup-tracker:schedule -->\n- Governance: no active policy-review suggestions"
  },
  {
    "html_url": "https://github.com/EvotecIT/IntelligenceX/issues/202",
    "body": "<!-- intelligencex:pr-watch-rollup-tracker:weekly-governance -->\n- Governance: retry-policy-review-suggested=yes; suggested policy `non-actionable-only`; confidence high; streak 3; profile operational_or_unknown"
  }
]
""";

        using var issuesDoc = System.Text.Json.JsonDocument.Parse(issuesJson);
        var context = IntelligenceX.Cli.Todo.ProjectSyncRunner.SelectPrWatchGovernanceContextFromIssueList(issuesDoc.RootElement);

        AssertEqual(true, context is not null, "weekly governance context found");
        AssertEqual("weekly-governance", context!.Source, "weekly tracker preferred");
        AssertEqual(true, context.RetryPolicyReviewSuggested, "signal parsed from governance line");
        AssertContainsText(context.SummaryLine, "retry-policy-review-suggested=yes", "summary line preserved");
        AssertContainsText(context.TrackerIssueUrl, "/issues/202", "weekly tracker url preserved");
    }

    private static void TestProjectSyncGovernanceContextFallsBackToScheduleTracker() {
        const string issuesJson = """
[
  {
    "html_url": "https://github.com/EvotecIT/IntelligenceX/issues/301",
    "body": "<!-- intelligencex:pr-watch-rollup-tracker:schedule -->\n- Governance: no active policy-review suggestions"
  }
]
""";

        using var issuesDoc = System.Text.Json.JsonDocument.Parse(issuesJson);
        var context = IntelligenceX.Cli.Todo.ProjectSyncRunner.SelectPrWatchGovernanceContextFromIssueList(issuesDoc.RootElement);

        AssertEqual(true, context is not null, "schedule governance context found");
        AssertEqual("schedule", context!.Source, "schedule tracker used as fallback");
        AssertEqual(false, context.RetryPolicyReviewSuggested, "inactive governance signal stays false");
        AssertEqual("no active policy-review suggestions", context.SummaryLine, "summary line normalized");
    }

    private static void TestProjectSyncBuildPrWatchGovernanceFieldValuesUseSuggestedSignal() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 55,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/55",
            Kind: "pull_request",
            TriageScore: 84.1,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "maintenance",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            PrWatchGovernanceSuggested: true,
            PrWatchGovernanceSummary: "retry-policy-review-suggested=yes; suggested policy `non-actionable-only`; confidence high",
            PrWatchGovernanceSource: "weekly-governance"
        );

        var signal = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildPrWatchGovernanceSignalFieldValue(entry);
        var summary = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildPrWatchGovernanceSummaryFieldValue(entry);

        AssertEqual("policy-review-suggested", signal, "governance signal field value");
        AssertContainsText(summary, "[weekly-governance]", "summary includes source prefix");
        AssertContainsText(summary, "retry-policy-review-suggested=yes", "summary includes governance payload");
    }

    private static void TestProjectSyncBuildPrWatchGovernanceFieldValuesClearWhenInactive() {
        var entry = new IntelligenceX.Cli.Todo.ProjectSyncRunner.ProjectSyncEntry(
            Number: 56,
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/56",
            Kind: "issue",
            TriageScore: 61.0,
            DuplicateCluster: null,
            CanonicalItem: null,
            Category: "maintenance",
            Tags: Array.Empty<string>(),
            MatchedIssueUrl: null,
            MatchedIssueConfidence: null,
            VisionFit: null,
            VisionConfidence: null,
            PrWatchGovernanceSuggested: false,
            PrWatchGovernanceSummary: "retry-policy-review-suggested=yes",
            PrWatchGovernanceSource: "weekly-governance"
        );

        AssertEqual(string.Empty, IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildPrWatchGovernanceSignalFieldValue(entry), "inactive governance signal clears field");
        AssertEqual(string.Empty, IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildPrWatchGovernanceSummaryFieldValue(entry), "inactive governance summary clears field");
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
            SuggestedDecision: "defer",
            SignalQuality: "low",
            SignalQualityScore: 41.2
        );

        var labels = IntelligenceX.Cli.Todo.ProjectSyncRunner.BuildLabelsForEntry(entry);
        AssertEqual(true, labels.Contains("ix/match:needs-review", StringComparer.OrdinalIgnoreCase), "low confidence match review label");
        AssertEqual(false, labels.Contains("ix/match:linked-issue", StringComparer.OrdinalIgnoreCase), "low confidence should not be linked-issue");
        AssertEqual(true, labels.Contains("ix/decision:defer", StringComparer.OrdinalIgnoreCase), "decision defer label");
        AssertEqual(true, labels.Contains("ix/signal:low", StringComparer.OrdinalIgnoreCase), "low signal label");
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

#endif
}
