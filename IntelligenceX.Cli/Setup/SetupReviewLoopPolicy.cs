using System;

namespace IntelligenceX.Cli.Setup;

internal static class SetupReviewLoopPolicy {
    internal const string Strict = "strict";
    internal const string Balanced = "balanced";
    internal const string Lenient = "lenient";
    internal const string TodoOnly = "todo-only";
    internal const string Vision = "vision";

    internal static bool TryNormalize(string? value, out string normalized) {
        normalized = Strict;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var key = value.Trim().ToLowerInvariant();
        switch (key) {
            case "strict":
            case "default":
                normalized = Strict;
                return true;
            case "balanced":
                normalized = Balanced;
                return true;
            case "lenient":
                normalized = Lenient;
                return true;
            case "todo-only":
            case "todo_only":
            case "todo":
            case "single-section":
            case "single_section":
                normalized = TodoOnly;
                return true;
            case "vision":
                normalized = Vision;
                return true;
            default:
                return false;
        }
    }

    internal static string AllowedValuesMessage() {
        return "strict, balanced, lenient, todo-only, or vision";
    }
}
