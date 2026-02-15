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
        new ProjectFieldDefinition("Triage Score", "NUMBER", Array.Empty<string>()),
        new ProjectFieldDefinition("Duplicate Cluster", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Canonical Item", "TEXT", Array.Empty<string>()),
        new ProjectFieldDefinition("Triage Kind", "SINGLE_SELECT", new[] {
            "pull_request",
            "issue"
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
