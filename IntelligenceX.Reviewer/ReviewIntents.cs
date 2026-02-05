using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal static class ReviewIntents {
    private static readonly IReadOnlyList<string> SecurityFocus = new[] {
        "security",
        "auth",
        "secrets"
    };

    private static readonly IReadOnlyList<string> PerformanceFocus = new[] {
        "performance",
        "latency",
        "allocations"
    };

    private static readonly IReadOnlyList<string> MaintainabilityFocus = new[] {
        "maintainability",
        "readability",
        "testing"
    };
    private const string SecurityNotes =
        "Prioritize auth, access control, secrets handling, input validation, and injection risks.";
    private const string PerformanceNotes =
        "Prioritize hot paths, allocations, algorithmic complexity, and scalability.";
    private const string MaintainabilityNotes =
        "Prioritize readability, API clarity, testability, and reducing complexity or duplication.";

    public static void Apply(string intent, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(intent)) {
            return;
        }

        var key = intent.Trim().ToLowerInvariant();
        switch (key) {
            case "security":
                SetFocusIfEmpty(settings, SecurityFocus);
                SetIfBlank(settings.Strictness, value => settings.Strictness = value, "strict");
                SetIfBlank(settings.Notes, value => settings.Notes = value, SecurityNotes);
                break;
            case "performance":
            case "perf":
                SetFocusIfEmpty(settings, PerformanceFocus);
                SetIfBlank(settings.Strictness, value => settings.Strictness = value, "balanced");
                SetIfBlank(settings.Notes, value => settings.Notes = value, PerformanceNotes);
                break;
            case "maintainability":
                SetFocusIfEmpty(settings, MaintainabilityFocus);
                SetIfBlank(settings.Strictness, value => settings.Strictness = value, "balanced");
                SetIfBlank(settings.Notes, value => settings.Notes = value, MaintainabilityNotes);
                break;
        }
    }

    private static void SetFocusIfEmpty(ReviewSettings settings, IReadOnlyList<string> focus) {
        if (settings.Focus.Count == 0) {
            settings.Focus = focus;
        }
    }

    private static void SetIfBlank(string? current, Action<string> set, string value) {
        if (string.IsNullOrWhiteSpace(current)) {
            set(value);
        }
    }
}
