using System;
using System.Collections.Generic;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectFieldDefinition(
    string Name,
    string DataType,
    IReadOnlyList<string> SingleSelectOptions
);

internal static class ProjectFieldCatalog {
    public static readonly IReadOnlyList<ProjectFieldDefinition> DefaultFields = new[] {
        new ProjectFieldDefinition("Vision Fit", "SINGLE_SELECT", new[] {
            "aligned",
            "needs-human-review",
            "likely-out-of-scope"
        }),
        new ProjectFieldDefinition("Vision Confidence", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Category", "SINGLE_SELECT", new[] {
            "bug",
            "feature",
            "documentation",
            "maintenance",
            "security",
            "performance",
            "testing",
            "ci"
        }),
        new ProjectFieldDefinition("Category Confidence", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Signal Quality", "SINGLE_SELECT", new[] {
            "high",
            "medium",
            "low"
        }),
        new ProjectFieldDefinition("Signal Quality Score", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Signal Quality Notes", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("PR Size", "SINGLE_SELECT", new[] {
            "xsmall",
            "small",
            "medium",
            "large",
            "xlarge"
        }),
        new ProjectFieldDefinition("PR Churn Risk", "SINGLE_SELECT", new[] {
            "low",
            "medium",
            "high"
        }),
        new ProjectFieldDefinition("PR Merge Readiness", "SINGLE_SELECT", new[] {
            "ready",
            "needs-review",
            "blocked"
        }),
        new ProjectFieldDefinition("PR Freshness", "SINGLE_SELECT", new[] {
            "fresh",
            "recent",
            "aging",
            "stale"
        }),
        new ProjectFieldDefinition("PR Check Health", "SINGLE_SELECT", new[] {
            "healthy",
            "pending",
            "failing",
            "unknown"
        }),
        new ProjectFieldDefinition("PR Review Latency", "SINGLE_SELECT", new[] {
            "low",
            "medium",
            "high"
        }),
        new ProjectFieldDefinition("PR Merge Conflict Risk", "SINGLE_SELECT", new[] {
            "low",
            "medium",
            "high"
        }),
        new ProjectFieldDefinition("Tags", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Tag Confidence Summary", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Matched Issue", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Matched Issue Confidence", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Matched Issue Reason", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Related Issues", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Matched Pull Request", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Matched Pull Request Confidence", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Matched Pull Request Reason", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Related Pull Requests", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Triage Score", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Duplicate Cluster", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Canonical Item", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Triage Kind", "SINGLE_SELECT", new[] {
            "pull_request",
            "issue"
        }),
        new ProjectFieldDefinition("IX Suggested Decision", "SINGLE_SELECT", new[] {
            "accept",
            "defer",
            "reject",
            "merge-candidate"
        }),
        new ProjectFieldDefinition("Maintainer Decision", "SINGLE_SELECT", new[] {
            "accept",
            "defer",
            "reject",
            "merge-candidate"
        })
    };

    public static bool TryGetField(string name, out ProjectFieldDefinition field) {
        foreach (var candidate in DefaultFields) {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                field = candidate;
                return true;
            }
        }
        field = null!;
        return false;
    }
}
