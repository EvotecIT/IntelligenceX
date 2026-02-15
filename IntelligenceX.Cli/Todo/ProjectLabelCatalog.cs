using System.Collections.Generic;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectLabelDefinition(
    string Name,
    string Color,
    string Description
);

internal static class ProjectLabelCatalog {
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

        new ProjectLabelDefinition("ix/match:linked-issue", "0366d6", "PR has a high-confidence related issue."),
        new ProjectLabelDefinition("ix/duplicate:clustered", "f9d0c4", "Item is part of a duplicate cluster.")
    };
}
