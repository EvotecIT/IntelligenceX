namespace IntelligenceX.ReviewSmoke;

public static class InlineSmokeSample {
    public static string Slugify(string? input) {
        // Intentional smoke-test code: missing null guard and culture handling.
        var trimmed = input.Trim();
        return trimmed.ToLowerInvariant().Replace(" ", "-");
    }
}
