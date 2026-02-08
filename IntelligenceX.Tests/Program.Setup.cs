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

    private static void TestGitHubRepoDetectorParsesRemoteUrls() {
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("https://github.com/owner/repo.git"), "https git");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("https://github.com/owner/repo"), "https no git");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("git@github.com:owner/repo.git"), "ssh scp");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("ssh://git@github.com/owner/repo.git"), "ssh url");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("ssh://git@github.mycorp.local/owner/repo.git"), "ssh ghe");
        AssertEqual(null, GitHubRepoDetector.ParseRepoFromRemoteUrl("not a url"), "invalid url");
    }

    private static void TestGitHubRepoDetectorParsesGitConfigRemoteSection() {
        var config = """
[core]
    repositoryformatversion = 0
    url = SHOULD_NOT_MATCH
[remote "origin"]
    fetch = +refs/heads/*:refs/remotes/origin/*
    url = git@github.com:EvotecIT/IntelligenceX.git
[branch "main"]
    remote = origin
    merge = refs/heads/main
    url = ALSO_SHOULD_NOT_MATCH
[remote "upstream"]
    url = https://github.com/other/repo.git
""";

        AssertEqual("git@github.com:EvotecIT/IntelligenceX.git",
            GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "origin"),
            "origin url");
        AssertEqual("https://github.com/other/repo.git",
            GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "upstream"),
            "upstream url");
        AssertEqual(null, GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "missing"), "missing remote");
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
