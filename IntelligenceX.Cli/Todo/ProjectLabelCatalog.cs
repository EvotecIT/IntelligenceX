using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Cli.Todo;

internal sealed record ProjectLabelDefinition(
    string Name,
    string Color,
    string Description
);

internal static class ProjectLabelCatalog {
    private const int MaxNormalizedTokenLength = 38;
    private static readonly IReadOnlyDictionary<string, string> CategoryToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["bug"] = "ix/category:bug",
        ["feature"] = "ix/category:feature",
        ["documentation"] = "ix/category:documentation",
        ["docs"] = "ix/category:documentation",
        ["maintenance"] = "ix/category:maintenance",
        ["security"] = "ix/category:security",
        ["performance"] = "ix/category:performance",
        ["testing"] = "ix/category:testing",
        ["ci"] = "ix/category:ci"
    };

    private static readonly IReadOnlyDictionary<string, string> TagToLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["security"] = "ix/tag:security",
        ["bugfix"] = "ix/tag:bugfix",
        ["bug-fix"] = "ix/tag:bugfix",
        ["performance"] = "ix/tag:performance",
        ["docs"] = "ix/tag:docs",
        ["doc"] = "ix/tag:docs",
        ["documentation"] = "ix/tag:docs",
        ["testing"] = "ix/tag:testing",
        ["test"] = "ix/tag:testing",
        ["tests"] = "ix/tag:testing",
        ["ci"] = "ix/tag:ci",
        ["maintenance"] = "ix/tag:maintenance",
        ["api"] = "ix/tag:api",
        ["ux"] = "ix/tag:ux",
        ["dependencies"] = "ix/tag:dependencies",
        ["dependency"] = "ix/tag:dependencies"
    };

    private static readonly string[] DynamicLabelPalette = {
        "5319e7",
        "1d76db",
        "0052cc",
        "0e8a16",
        "fbca04",
        "d73a4a",
        "6f42c1",
        "b60205"
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
        new ProjectLabelDefinition("ix/match:linked-pr", "0366d6", "Issue has a high-confidence related pull request."),
        new ProjectLabelDefinition("ix/match:needs-review-pr", "fbca04", "Issue has a low-confidence related pull request that needs maintainer review."),
        new ProjectLabelDefinition("ix/decision:accept", "0e8a16", "IX suggested decision is accept."),
        new ProjectLabelDefinition("ix/decision:defer", "fbca04", "IX suggested decision is defer."),
        new ProjectLabelDefinition("ix/decision:reject", "d73a4a", "IX suggested decision is reject."),
        new ProjectLabelDefinition("ix/decision:merge-candidate", "1d76db", "IX suggested decision is merge-candidate."),
        new ProjectLabelDefinition("ix/signal:low", "fbca04", "Signal quality is low and needs maintainer context checks."),
        new ProjectLabelDefinition("ix/duplicate:clustered", "f9d0c4", "Item is part of a duplicate cluster.")
    };

    public static bool TryMapCategoryLabel(string category, out string label) {
        label = string.Empty;
        if (!TryNormalizeToken(category, out var normalized)) {
            return false;
        }

        if (CategoryToLabel.TryGetValue(normalized, out var knownLabel)) {
            label = knownLabel;
            return true;
        }

        label = $"ix/category:{normalized}";
        return true;
    }

    public static bool TryMapTagLabel(string tag, out string label) {
        label = string.Empty;
        if (!TryNormalizeToken(tag, out var normalized)) {
            return false;
        }

        if (TagToLabel.TryGetValue(normalized, out var knownLabel)) {
            label = knownLabel;
            return true;
        }

        label = $"ix/tag:{normalized}";
        return true;
    }

    public static IReadOnlyList<ProjectLabelDefinition> BuildEnsureLabelCatalog(
        IEnumerable<string?> categories,
        IEnumerable<string?> tags) {
        var byName = DefaultLabels.ToDictionary(label => label.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories) {
            if (!TryBuildDynamicCategoryLabelDefinition(category, out var definition)) {
                continue;
            }
            byName[definition.Name] = definition;
        }

        foreach (var tag in tags) {
            if (!TryBuildDynamicTagLabelDefinition(tag, out var definition)) {
                continue;
            }
            byName[definition.Name] = definition;
        }

        return byName.Values
            .OrderBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryBuildDynamicCategoryLabelDefinition(string? category, out ProjectLabelDefinition definition) {
        definition = default!;
        if (!TryNormalizeToken(category, out var normalized) || CategoryToLabel.ContainsKey(normalized)) {
            return false;
        }

        var labelName = $"ix/category:{normalized}";
        definition = new ProjectLabelDefinition(
            labelName,
            ResolveDynamicColor(labelName),
            $"Triage category: {normalized}");
        return true;
    }

    private static bool TryBuildDynamicTagLabelDefinition(string? tag, out ProjectLabelDefinition definition) {
        definition = default!;
        if (!TryNormalizeToken(tag, out var normalized) || TagToLabel.ContainsKey(normalized)) {
            return false;
        }

        var labelName = $"ix/tag:{normalized}";
        definition = new ProjectLabelDefinition(
            labelName,
            ResolveDynamicColor(labelName),
            $"Triage tag: {normalized}");
        return true;
    }

    private static bool TryNormalizeToken(string? value, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var buffer = new char[Math.Min(MaxNormalizedTokenLength, value.Length * 2)];
        var length = 0;
        var previousDash = false;
        foreach (var character in value.Trim().ToLowerInvariant()) {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')) {
                if (length < buffer.Length) {
                    buffer[length++] = character;
                }
                previousDash = false;
                continue;
            }

            if (!previousDash && length > 0) {
                if (length < buffer.Length) {
                    buffer[length++] = '-';
                }
                previousDash = true;
            }
        }

        while (length > 0 && buffer[length - 1] == '-') {
            length--;
        }
        if (length <= 0) {
            return false;
        }

        normalized = new string(buffer, 0, length);
        return true;
    }

    private static string ResolveDynamicColor(string labelName) {
        uint hash = 2166136261;
        foreach (var character in labelName) {
            hash ^= character;
            hash *= 16777619;
        }

        var index = (int)(hash % (uint)DynamicLabelPalette.Length);
        return DynamicLabelPalette[index];
    }
}
