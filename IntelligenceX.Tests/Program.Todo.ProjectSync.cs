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
        AssertEqual(true, names.Contains("Triage Score", StringComparer.OrdinalIgnoreCase), "has Triage Score");
        AssertEqual(true, names.Contains("Maintainer Decision", StringComparer.OrdinalIgnoreCase), "has Maintainer Decision");
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
      "matchedIssueConfidence": 0.93
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

        var issue = entries.First(item => item.Url.EndsWith("/issues/11", StringComparison.OrdinalIgnoreCase));
        AssertEqual("issue", issue.Kind, "issue kind preserved");
        AssertEqual("cluster-1", issue.DuplicateCluster, "duplicate cluster preserved");
        AssertEqual("https://github.com/EvotecIT/IntelligenceX/pull/10", issue.CanonicalItem, "issue canonical resolved");
    }
#endif
}
