using System;
using System.Collections.Generic;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectLabelDefinition(
    string Name,
    string Color,
    string Description
);

internal static class ProjectLabelCatalog {
    private static readonly IReadOnlyDictionary<string, string> TagToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["security"] = "ix/tag:security",
        ["bugfix"] = "ix/tag:bugfix",
        ["performance"] = "ix/tag:performance",
        ["docs"] = "ix/tag:docs",
        ["testing"] = "ix/tag:testing",
        ["ci"] = "ix/tag:ci",
        ["maintenance"] = "ix/tag:maintenance",
        ["api"] = "ix/tag:api",
        ["ux"] = "ix/tag:ux",
        ["dependencies"] = "ix/tag:dependencies"
    };

    public static readonly IReadOnlyList<ProjectLabelDefinition> DefaultLabels = new[] {
        new ProjectLabelDefinition("ix/vision:aligned", "0e8a16", "Vision fit is aligned."),
        new ProjectLabelDefinition("ix/vision:needs-review", "fbca04", "Vision fit needs human maintainer review."),
        new ProjectLabelDefinition("ix/vision:out-of-scope", "d73a4a", "Vision fit appears out of scope."),

        new ProjectLabelDefinition("ix/category:bug", "d73a4a", "Likely bug-fix work."),
        new ProjectLabelDefinition("ix/category:feature", "1d76db", "Likely feature/enhancement work."),
        new ProjectLabelDefinition("ix/category:documentation", "5319e7", "Likely documentation work."),
        new ProjectLabelDefinition("ix/category:maintenance", "6f42c1", "Likely maintenance/chore/refactor work."),
        new ProjectLabelDefinition("ix/category:security", "b60205", "Likely security work."),
        new ProjectLabelDefinition("ix/category:performance", "0052cc", "Likely performance work."),
        new ProjectLabelDefinition("ix/category:testing", "0e8a16", "Likely testing work."),
        new ProjectLabelDefinition("ix/category:ci", "c5def5", "Likely CI/workflow automation work."),

        new ProjectLabelDefinition("ix/tag:security", "b60205", "Triage tag: security"),
        new ProjectLabelDefinition("ix/tag:bugfix", "d73a4a", "Triage tag: bugfix"),
        new ProjectLabelDefinition("ix/tag:performance", "0052cc", "Triage tag: performance"),
        new ProjectLabelDefinition("ix/tag:docs", "5319e7", "Triage tag: docs"),
        new ProjectLabelDefinition("ix/tag:testing", "0e8a16", "Triage tag: testing"),
        new ProjectLabelDefinition("ix/tag:ci", "c5def5", "Triage tag: ci"),
        new ProjectLabelDefinition("ix/tag:maintenance", "6f42c1", "Triage tag: maintenance"),
        new ProjectLabelDefinition("ix/tag:api", "1d76db", "Triage tag: api"),
        new ProjectLabelDefinition("ix/tag:ux", "fbca04", "Triage tag: ux"),
        new ProjectLabelDefinition("ix/tag:dependencies", "6f42c1", "Triage tag: dependencies"),

        new ProjectLabelDefinition("ix/match:linked-issue", "0366d6", "PR has a high-confidence related issue."),
        new ProjectLabelDefinition("ix/match:needs-review", "fbca04", "PR has a low-confidence issue match that needs maintainer review."),
        new ProjectLabelDefinition("ix/duplicate:clustered", "f9d0c4", "Item is part of a duplicate cluster.")
    };

    public static bool TryMapTagLabel(string tag, out string label) {
        if (string.IsNullOrWhiteSpace(tag)) {
            label = string.Empty;
            return false;
        }

        return TagToLabel.TryGetValue(tag.Trim(), out label!);
    }
}
