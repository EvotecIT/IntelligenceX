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
        AssertEqual(true, names.Contains("Duplicate Clusters", StringComparer.OrdinalIgnoreCase), "duplicate clusters view present");
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
#endif
}
