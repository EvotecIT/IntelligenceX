using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectViewDefinition(
    string Name,
    string Layout,
    string Description,
    string Filter
);

internal static class ProjectViewCatalog {
    public static readonly IReadOnlyList<ProjectViewDefinition> DefaultViews = new[] {
        new ProjectViewDefinition(
            "IX Queue",
            "TABLE",
            "Primary triage queue for open backlog items.",
            "is:open"),
        new ProjectViewDefinition(
            "Merge Candidates",
            "TABLE",
            "Items with high merge readiness and aligned signals.",
            "is:open \"IX Suggested Decision\":\"merge-candidate\""),
        new ProjectViewDefinition(
            "Vision Review",
            "BOARD",
            "Items grouped by vision-fit outcomes for maintainer review.",
            "is:open"),
        new ProjectViewDefinition(
            "Duplicate Clusters",
            "TABLE",
            "Items that belong to duplicate clusters and need consolidation.",
            "is:open \"Duplicate Cluster\":*")
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
