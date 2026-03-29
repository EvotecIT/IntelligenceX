using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestProjectViewCatalogDefaultsIncludeQueueAndMergeViews() {
        var names = IntelligenceX.Cli.Todo.ProjectViewCatalog.DefaultViews
            .Select(view => view.Name)
            .ToList();

        AssertEqual(true, names.Contains("IX Queue", StringComparer.OrdinalIgnoreCase), "default queue view present");
        AssertEqual(true, names.Contains("Merge Candidates", StringComparer.OrdinalIgnoreCase), "merge candidates view present");
        AssertEqual(true, names.Contains("Vision Review", StringComparer.OrdinalIgnoreCase), "vision review view present");
        AssertEqual(true, names.Contains("Issue Ops", StringComparer.OrdinalIgnoreCase), "issue ops view present");
        AssertEqual(true, names.Contains("Duplicate Clusters", StringComparer.OrdinalIgnoreCase), "duplicate clusters view present");
        AssertEqual(true,
            IntelligenceX.Cli.Todo.ProjectViewCatalog.DefaultViews.All(view => view.SuggestedColumns.Count > 0),
            "default views expose suggested columns guidance");
        AssertEqual(false, names.Contains("Governance Review", StringComparer.OrdinalIgnoreCase), "optional governance view excluded by default");
    }

    private static void TestProjectViewCatalogBuildRecommendedViewsIncludesOptionalGovernanceView() {
        var views = IntelligenceX.Cli.Todo.ProjectViewCatalog.BuildRecommendedViews(includePrWatchGovernanceViews: true);
        var governance = views.Single(view => view.Name.Equals("Governance Review", StringComparison.OrdinalIgnoreCase));

        AssertEqual("TABLE", governance.Layout, "governance view layout");
        AssertContainsText(governance.Filter, "\"PR Governance Signal\":\"policy-review-suggested\"", "governance filter uses governance signal field");
        AssertEqual(true, governance.SuggestedColumns.Contains("PR Governance Summary", StringComparer.OrdinalIgnoreCase), "governance summary column included");
    }

    private static void TestProjectViewCatalogFindMissingDefaultViewsReturnsMissingOnly() {
        var existing = new Dictionary<string, IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView>(StringComparer.OrdinalIgnoreCase) {
            ["IX Queue"] = new("view1", "IX Queue", "TABLE", "https://example.test/view1"),
            ["Vision Review"] = new("view2", "Vision Review", "BOARD", "https://example.test/view2")
        };

        var missing = IntelligenceX.Cli.Todo.ProjectViewCatalog.FindMissingDefaultViews(existing)
            .Select(view => view.Name)
            .ToList();

        AssertEqual(true, missing.Contains("Merge Candidates", StringComparer.OrdinalIgnoreCase), "missing merge candidates detected");
        AssertEqual(true, missing.Contains("Issue Ops", StringComparer.OrdinalIgnoreCase), "missing issue ops detected");
        AssertEqual(true, missing.Contains("Duplicate Clusters", StringComparer.OrdinalIgnoreCase), "missing duplicate clusters detected");
        AssertEqual(false, missing.Contains("IX Queue", StringComparer.OrdinalIgnoreCase), "existing queue view not flagged");
    }

    private static void TestProjectViewCatalogFindMissingDefaultViewsReturnsEmptyWhenComplete() {
        var existing = IntelligenceX.Cli.Todo.ProjectViewCatalog.DefaultViews
            .ToDictionary(
                view => view.Name,
                view => new IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView(
                    $"id-{view.Name}",
                    view.Name,
                    view.Layout,
                    $"https://example.test/{view.Name}"),
                StringComparer.OrdinalIgnoreCase);

        var missing = IntelligenceX.Cli.Todo.ProjectViewCatalog.FindMissingDefaultViews(existing);
        AssertEqual(0, missing.Count, "no missing views when all defaults exist");
    }

    private static void TestProjectViewCatalogFindMissingRecommendedViewsIncludesGovernanceViewWhenEnabled() {
        var existing = IntelligenceX.Cli.Todo.ProjectViewCatalog.DefaultViews
            .ToDictionary(
                view => view.Name,
                view => new IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView(
                    $"id-{view.Name}",
                    view.Name,
                    view.Layout,
                    $"https://example.test/{view.Name}"),
                StringComparer.OrdinalIgnoreCase);

        var missing = IntelligenceX.Cli.Todo.ProjectViewCatalog.FindMissingRecommendedViews(
                existing,
                includePrWatchGovernanceViews: true)
            .Select(view => view.Name)
            .ToList();

        AssertEqual(true, missing.Contains("Governance Review", StringComparer.OrdinalIgnoreCase), "governance view missing when optional profile enabled");
    }

    private static void TestProjectConfigReaderReadsPrWatchGovernanceFeatures() {
        const string json = """
{
  "schema": "intelligencex.project-config.v1",
  "owner": "EvotecIT",
  "repo": "EvotecIT/IntelligenceX",
  "project": {
    "number": 321
  },
  "features": {
    "prWatchGovernance": {
      "labels": true,
      "fields": true,
      "views": true
    }
  }
}
""";

        var ok = IntelligenceX.Cli.Todo.ProjectConfigReader.TryReadFromJson(json, out var config, out var error);

        AssertEqual(true, ok, $"project config parses ({error})");
        AssertEqual("EvotecIT", config.Owner, "owner parsed");
        AssertEqual("EvotecIT/IntelligenceX", config.Repo, "repo parsed");
        AssertEqual(321, config.ProjectNumber, "project number parsed");
        AssertEqual(true, config.Features.PrWatchGovernanceLabels, "labels feature parsed");
        AssertEqual(true, config.Features.PrWatchGovernanceFields, "fields feature parsed");
        AssertEqual(true, config.Features.PrWatchGovernanceViews, "views feature parsed");
    }

    private static void TestProjectConfigReaderFallsBackToLegacyGovernanceViewFlag() {
        const string json = """
{
  "schema": "intelligencex.project-config.v1",
  "owner": "EvotecIT",
  "repo": "EvotecIT/IntelligenceX",
  "project": {
    "number": 654
  },
  "views": {
    "includePrWatchGovernanceViews": true
  }
}
""";

        var ok = IntelligenceX.Cli.Todo.ProjectConfigReader.TryReadFromJson(json, out var config, out var error);

        AssertEqual(true, ok, $"legacy project config parses ({error})");
        AssertEqual(false, config.Features.PrWatchGovernanceLabels, "legacy labels feature stays false");
        AssertEqual(false, config.Features.PrWatchGovernanceFields, "legacy fields feature stays false");
        AssertEqual(true, config.Features.PrWatchGovernanceViews, "legacy view feature fallback parsed");
    }
#endif
}
