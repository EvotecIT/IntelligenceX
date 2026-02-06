namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestSetupArgsRejectSkipUpdate() {
        var plan = new SetupPlan("owner/repo") {
            SkipSecret = true,
            UpdateSecret = true
        };
        AssertThrows<InvalidOperationException>(() => SetupArgsBuilder.FromPlan(plan), "skip+update");
    }

    private static void TestGitHubSecretsRejectEmptyValue() {
        using var client = new GitHubSecretsClient("token");
        AssertThrows<InvalidOperationException>(() =>
            client.SetRepoSecretAsync("owner", "repo", "SECRET_NAME", "").GetAwaiter().GetResult(),
            "repo secret empty");
        AssertThrows<InvalidOperationException>(() =>
            client.SetOrgSecretAsync("org", "SECRET_NAME", " ").GetAwaiter().GetResult(),
            "org secret empty");
    }

    private static void TestReleaseReviewerEnvToken() {
        var previous = Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN", "token-value");
            var options = new ReleaseReviewerOptions();
            ReleaseReviewerOptions.ApplyEnvDefaults(options);
            AssertEqual("token-value", options.Token, "reviewer token");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN", previous);
        }
    }
#endif
}
