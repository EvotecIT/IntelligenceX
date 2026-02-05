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

    public static void Apply(string intent, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(intent)) {
            return;
        }

        var key = intent.Trim().ToLowerInvariant();
        switch (key) {
            case "security":
                SetFocusIfEmpty(settings, SecurityFocus);
                break;
            case "performance":
            case "perf":
                SetFocusIfEmpty(settings, PerformanceFocus);
                break;
            case "maintainability":
                SetFocusIfEmpty(settings, MaintainabilityFocus);
                break;
        }
    }

    private static void SetFocusIfEmpty(ReviewSettings settings, IReadOnlyList<string> focus) {
        if (settings.Focus.Count == 0) {
            settings.Focus = focus;
        }
    }
}
