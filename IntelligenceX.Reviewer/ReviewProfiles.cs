using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal static class ReviewProfiles {
    public static void Apply(string profile, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(profile)) {
            return;
        }

        var key = profile.Trim().ToLowerInvariant();
        switch (key) {
            case "picky":
                settings.Length = ReviewLength.Long;
                settings.Strictness = "picky";
                settings.Focus = new[] { "bugs", "security", "edge cases", "tests" };
                settings.MaxInlineComments = Math.Max(settings.MaxInlineComments, 15);
                settings.IncludeNextSteps = true;
                break;
            case "highlevel":
            case "high-level":
                settings.Length = ReviewLength.Short;
                settings.Mode = "summary";
                settings.Strictness = "high-level";
                settings.Focus = new[] { "architecture", "design", "risks" };
                settings.MaxInlineComments = 0;
                settings.IncludeNextSteps = true;
                break;
            case "security":
                settings.Length = ReviewLength.Medium;
                settings.Strictness = "strict";
                settings.Focus = new[] { "security", "privacy", "auth" };
                settings.IncludeNextSteps = true;
                break;
            case "performance":
                settings.Length = ReviewLength.Medium;
                settings.Strictness = "balanced";
                settings.Focus = new[] { "performance", "scalability" };
                break;
            case "tests":
                settings.Length = ReviewLength.Medium;
                settings.Strictness = "balanced";
                settings.Focus = new[] { "tests", "coverage", "edge cases" };
                settings.IncludeNextSteps = true;
                break;
            case "balanced":
            default:
                settings.Length = ReviewLength.Medium;
                settings.Strictness = "balanced";
                settings.Focus = new[] { "correctness", "maintainability" };
                settings.MaxInlineComments = 10;
                settings.IncludeNextSteps = true;
                break;
        }
    }
}
