using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectViewDefinition(
    string Name,
    string Layout,
    string Description,
    string Filter,
    IReadOnlyList<string> SuggestedColumns
);

internal static class ProjectViewCatalog {
    public static readonly IReadOnlyList<ProjectViewDefinition> DefaultViews = new[] {
        new ProjectViewDefinition(
            "IX Queue",
            "TABLE",
            "Primary pull-request triage queue with decision and confidence signals visible.",
            "is:open \"Triage Kind\":\"pull_request\"",
            new[] {
                "Title",
                "Status",
                "Triage Kind",
                "Signal Quality",
                "Signal Quality Score",
                "Vision Fit",
                "IX Suggested Decision",
                "Triage Score",
                "Category",
                "Category Confidence",
                "Matched Issue",
                "Duplicate Cluster"
            }),
        new ProjectViewDefinition(
            "Merge Candidates",
            "TABLE",
            "High-readiness pull requests that are candidates for maintainer merge review.",
            "is:open \"IX Suggested Decision\":\"merge-candidate\"",
            new[] {
                "Title",
                "Status",
                "Signal Quality",
                "Vision Fit",
                "IX Suggested Decision",
                "Triage Score",
                "Category",
                "Matched Issue"
            }),
        new ProjectViewDefinition(
            "Vision Review",
            "BOARD",
            "Maintainer board grouped by vision-fit outcomes with low-signal items visible.",
            "is:open",
            new[] {
                "Title",
                "Status",
                "Vision Fit",
                "Signal Quality",
                "IX Suggested Decision",
                "Category"
            }),
        new ProjectViewDefinition(
            "Duplicate Clusters",
            "TABLE",
            "Items that belong to duplicate clusters and need consolidation.",
            "is:open \"Duplicate Cluster\":*",
            new[] {
                "Title",
                "Status",
                "Duplicate Cluster",
                "Canonical Item",
                "Signal Quality",
                "Triage Score",
                "Category"
            })
    };

    public static IReadOnlyList<ProjectViewDefinition> FindMissingDefaultViews(
        IReadOnlyDictionary<string, ProjectV2Client.ProjectView> existingViews) {
        if (existingViews.Count == 0) {
            return DefaultViews.ToList();
        }

        return DefaultViews
            .Where(view => !existingViews.ContainsKey(view.Name))
            .ToList();
    }
}
