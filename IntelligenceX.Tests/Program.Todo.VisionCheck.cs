namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestVisionCheckClassifiesOutOfScopeWhenOutTokensDominate() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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

    private static void TestVisionCheckExplicitRejectPolicyTakesPrecedence() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "marketing" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "migration" }, StringComparer.OrdinalIgnoreCase)
        );
        var candidate = new IntelligenceX.Cli.Todo.VisionCheckRunner.PullRequestCandidate(
            "pr#79",
            79,
            "Marketing redesign PR cleanup",
            "https://example/pr/79",
            Array.Empty<string>(),
            22.4
        );
        var assessment = IntelligenceX.Cli.Todo.VisionCheckRunner.EvaluateAlignment(candidate, signals);
        AssertEqual(true, assessment.ExplicitRejectMatches.Count >= 1, "explicit reject matches");
        AssertEqual("likely-out-of-scope", assessment.Classification, "explicit reject policy classification");
    }

    private static void TestVisionCheckParseSignalsSupportsExplicitPolicyPrefixes() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Website redesign and marketing",
                string.Empty,
                "## Maintainer Guidance",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var signals = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionSignals(visionPath);
            AssertEqual(true, signals.ExplicitAcceptTokens.Contains("security"), "accept policy token");
            AssertEqual(true, signals.ExplicitRejectTokens.Contains("marketing"), "reject policy token");
            AssertEqual(true, signals.ExplicitReviewTokens.Contains("migration"), "review policy token");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }
#endif
}
