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
                "PR Size",
                "PR Churn Risk",
                "PR Merge Readiness",
                "PR Freshness",
                "PR Check Health",
                "PR Review Latency",
                "PR Merge Conflict Risk",
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
                "PR Size",
                "PR Merge Readiness",
                "PR Freshness",
                "PR Check Health",
                "PR Review Latency",
                "PR Merge Conflict Risk",
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
                "PR Size",
                "PR Merge Readiness",
                "PR Check Health",
                "PR Review Latency",
                "Signal Quality",
                "IX Suggested Decision",
                "Category"
            }),
        new ProjectViewDefinition(
            "Issue Ops",
            "TABLE",
            "Issue-first operations queue for stale infra blockers and applicability review signals.",
            "is:open \"Triage Kind\":\"issue\"",
            new[] {
                "Title",
                "Status",
                "Triage Kind",
                "Issue Review Action",
                "Issue Review Action Confidence",
                "Matched Pull Request",
                "Matched Pull Request Confidence",
                "Related Pull Requests",
                "Signal Quality",
                "Signal Quality Score",
                "Triage Score",
                "Category",
                "Duplicate Cluster"
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
                "PR Size",
                "PR Churn Risk",
                "PR Merge Conflict Risk",
                "Signal Quality",
                "Triage Score",
                "Category"
            })
    };

    public static readonly IReadOnlyList<ProjectViewDefinition> OptionalPrWatchGovernanceViews = new[] {
        new ProjectViewDefinition(
            "Governance Review",
            "TABLE",
            "Pull requests carrying a live pr-watch governance recommendation that need maintainer policy review.",
            "is:open \"Triage Kind\":\"pull_request\" \"PR Governance Signal\":\"policy-review-suggested\"",
            new[] {
                "Title",
                "Status",
                "PR Governance Signal",
                "PR Governance Summary",
                "PR Merge Readiness",
                "PR Check Health",
                "Signal Quality",
                "IX Suggested Decision",
                "Triage Score"
            })
    };

    public static IReadOnlyList<ProjectViewDefinition> BuildRecommendedViews(bool includePrWatchGovernanceViews) {
        if (!includePrWatchGovernanceViews) {
            return DefaultViews;
        }

        var combined = new List<ProjectViewDefinition>(DefaultViews.Count + OptionalPrWatchGovernanceViews.Count);
        combined.AddRange(DefaultViews);
        combined.AddRange(OptionalPrWatchGovernanceViews);
        return combined;
    }

    public static IReadOnlyList<ProjectViewDefinition> FindMissingDefaultViews(
        IReadOnlyDictionary<string, ProjectV2Client.ProjectView> existingViews) {
        return FindMissingRecommendedViews(existingViews, includePrWatchGovernanceViews: false);
    }

    public static IReadOnlyList<ProjectViewDefinition> FindMissingRecommendedViews(
        IReadOnlyDictionary<string, ProjectV2Client.ProjectView> existingViews,
        bool includePrWatchGovernanceViews) {
        var recommended = BuildRecommendedViews(includePrWatchGovernanceViews);
        if (existingViews.Count == 0) {
            return recommended.ToList();
        }

        return recommended
            .Where(view => !existingViews.ContainsKey(view.Name))
            .ToList();
    }
}
