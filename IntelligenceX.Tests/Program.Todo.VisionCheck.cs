namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestVisionCheckClassifiesOutOfScopeWhenOutTokensDominate() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase)
        );
        var candidate = new IntelligenceX.Cli.Todo.VisionCheckRunner.PullRequestCandidate(
            "pr#77",
            77,
            "Website rebrand and marketing refresh",
            "https://example/pr/77",
            new[] { "website" },
            44.2
        );
        var assessment = IntelligenceX.Cli.Todo.VisionCheckRunner.EvaluateAlignment(candidate, signals);
        AssertEqual("likely-out-of-scope", assessment.Classification, "vision classification");
        AssertEqual(true, assessment.OutOfScopeMatches.Count >= 2, "out-of-scope token matches");
    }

    private static void TestVisionCheckClassifiesAlignedWhenInTokensMatch() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase)
        );
        var candidate = new IntelligenceX.Cli.Todo.VisionCheckRunner.PullRequestCandidate(
            "pr#78",
            78,
            "API security hardening for stability",
            "https://example/pr/78",
            new[] { "security" },
            88.9
        );
        var assessment = IntelligenceX.Cli.Todo.VisionCheckRunner.EvaluateAlignment(candidate, signals);
        AssertEqual("aligned", assessment.Classification, "vision classification");
        AssertEqual(true, assessment.InScopeMatches.Count >= 2, "in-scope token matches");
    }
#endif
}
