using System;

namespace IntelligenceX.Docs;

internal static class ReviewSuggestionSmoke {
    public static string NormalizeSlug(string? input) {
        if (string.IsNullOrWhiteSpace(input)) {
            return string.Empty;
        }

        var parts = input
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("-", parts);
    }
}
