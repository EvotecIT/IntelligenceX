namespace IntelligenceX.Docs;

internal static class ReviewSuggestionSmoke {
    public static string NormalizeSlug(string? input) {
        if (input == null) {
            return "";
        }

        return input.Trim().ToLowerInvariant().Replace(" ", "-");
    }
}
