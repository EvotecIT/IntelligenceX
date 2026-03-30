using System;
using System.Collections.Generic;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestProjectViewChecklistMarkdownIncludesMarkerAndCoverage() {
        var existingViews = new Dictionary<string, IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView>(StringComparer.OrdinalIgnoreCase) {
            ["IX Queue"] = new(
                "view1",
                "IX Queue",
                "TABLE",
                "https://github.com/orgs/EvotecIT/projects/123/views/1")
        };

        var markdown = IntelligenceX.Cli.Todo.ProjectViewChecklistRunner.BuildChecklistMarkdown(
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            123,
            "https://github.com/orgs/EvotecIT/projects/123",
            existingViews,
            includePrWatchGovernanceViews: false,
            new DateTimeOffset(2026, 02, 15, 22, 00, 00, TimeSpan.Zero));

        AssertContainsText(markdown, "intelligencex:project-view-checklist", "checklist marker present");
        AssertContainsText(markdown, "Default view coverage: 1/5", "coverage reflects missing defaults");
        AssertContainsText(markdown, "- [x] **IX Queue** (`TABLE`) - present", "existing default view checked");
        AssertContainsText(markdown, "- [ ] **Merge Candidates** (`TABLE`) - missing", "missing default view unchecked");
        AssertContainsText(markdown, "- [ ] **Issue Ops** (`TABLE`) - missing", "issue ops default view unchecked");
        AssertContainsText(markdown, "Suggested columns:", "suggested columns guidance included");
        AssertContainsText(markdown, "https://github.com/orgs/EvotecIT/projects/123/views/1", "existing view link included");
    }

    private static void TestProjectViewChecklistMarkdownIncludesApplyInstructions() {
        var markdown = IntelligenceX.Cli.Todo.ProjectViewChecklistRunner.BuildChecklistMarkdown(
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            123,
            "https://github.com/orgs/EvotecIT/projects/123",
            new Dictionary<string, IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView>(StringComparer.OrdinalIgnoreCase),
            includePrWatchGovernanceViews: false,
            new DateTimeOffset(2026, 02, 15, 22, 05, 00, TimeSpan.Zero));

        AssertContainsText(markdown, "How To Complete Missing Views", "instruction section present");
        AssertContainsText(markdown, "Select `+ New view` in GitHub Projects.", "manual apply step present");
    }

    private static void TestProjectViewChecklistMarkdownIncludesOptionalGovernanceViewWhenEnabled() {
        var markdown = IntelligenceX.Cli.Todo.ProjectViewChecklistRunner.BuildChecklistMarkdown(
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            123,
            "https://github.com/orgs/EvotecIT/projects/123",
            new Dictionary<string, IntelligenceX.Cli.Todo.ProjectV2Client.ProjectView>(StringComparer.OrdinalIgnoreCase),
            includePrWatchGovernanceViews: true,
            new DateTimeOffset(2026, 02, 15, 22, 10, 00, TimeSpan.Zero));

        AssertContainsText(markdown, "Default view coverage: 0/6", "coverage includes optional governance view");
        AssertContainsText(markdown, "**Governance Review** (`TABLE`) - missing", "optional governance view listed");
        AssertContainsText(markdown, "\"PR Governance Signal\":\"policy-review-suggested\"", "governance view filter guidance included");
    }
#endif
}
