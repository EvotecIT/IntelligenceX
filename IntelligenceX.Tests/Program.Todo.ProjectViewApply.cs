using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestProjectViewApplyMarkdownIncludesMissingViewsAndPlatformNote() {
        var existingViews = new Dictionary<string, IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView>(StringComparer.OrdinalIgnoreCase) {
            ["IX Queue"] = new(
                "view1",
                "IX Queue",
                "TABLE",
                "https://github.com/orgs/EvotecIT/projects/123/views/1")
        };

        var markdown = IntelligenceX.Cli.Todo.ProjectViewApplyRunner.BuildApplyMarkdown(
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            123,
            "https://github.com/orgs/EvotecIT/projects/123",
            existingViews,
            directCreateSupported: false,
            new DateTimeOffset(2026, 02, 15, 23, 10, 00, TimeSpan.Zero));

        AssertContainsText(markdown, "intelligencex:project-view-apply", "apply marker present");
        AssertContainsText(markdown, "Default view coverage: 1/4", "coverage reflects missing defaults");
        AssertContainsText(markdown, "- [ ] **Merge Candidates** (`TABLE`)", "missing default view listed");
        AssertContainsText(markdown, "Suggested columns:", "suggested columns guidance included");
        AssertContainsText(markdown, "Select `+ New view` in GitHub Projects.", "manual apply step present");
        AssertContainsText(markdown, "public API surface does not expose direct project view creation", "platform note present");
    }

    private static void TestProjectViewApplyMarkdownAllViewsPresentShowsCompletedChecklist() {
        var existingViews = IntelligenceX.Cli.Todo.ProjectViewCatalog.DefaultViews
            .ToDictionary(
                view => view.Name,
                view => new IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView(
                    $"id-{view.Name}",
                    view.Name,
                    view.Layout,
                    $"https://example.local/{view.Name.Replace(' ', '-')}"),
                StringComparer.OrdinalIgnoreCase);

        var markdown = IntelligenceX.Cli.Todo.ProjectViewApplyRunner.BuildApplyMarkdown(
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            123,
            "https://github.com/orgs/EvotecIT/projects/123",
            existingViews,
            directCreateSupported: true,
            new DateTimeOffset(2026, 02, 15, 23, 15, 00, TimeSpan.Zero));

        AssertContainsText(markdown, "Default view coverage: 4/4", "coverage reflects all defaults present");
        AssertContainsText(markdown, "Missing default views: 0", "missing count is zero");
        AssertContainsText(markdown, "- [x] All recommended IX default views are present.", "completed checklist message");
        AssertContainsText(markdown, "Direct API view-create support: available", "capability line included");
    }
#endif
}
