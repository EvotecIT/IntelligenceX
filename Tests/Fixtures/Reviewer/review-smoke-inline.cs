using System;

namespace IntelligenceX.ReviewSmoke;

public static class InlineSmokeSample {
    /// <summary>Converts text to a lowercase dash-separated slug. Returns empty for null/whitespace.</summary>
    public static string Slugify(string? input) {
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
