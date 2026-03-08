namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestReviewerUntrustedPrSkipsAuthStoreWriteFromEnv() {
        var previousReviewerToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN");
        var previousGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var previousEventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        var previousAuthB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        var previousAuthPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        var tempDir = Path.Combine(Path.GetTempPath(), "intelligencex-reviewer-untrusted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var eventPath = Path.Combine(tempDir, "event.json");
        var authPath = Path.Combine(tempDir, "auth.json");
        var eventJson = """
{
  "repository": { "full_name": "owner/repo" },
  "pull_request": {
    "title": "Test",
    "number": 1,
    "draft": false,
    "author_association": "CONTRIBUTOR",
    "head": {
      "sha": "head",
      "repo": {
        "full_name": "fork/repo",
        "fork": true
      }
    },
    "base": {
      "sha": "base"
    }
  }
}
""";

        try {
            File.WriteAllText(eventPath, eventJson);
            var bundleJson = "{\"provider\":\"openai-codex\",\"access_token\":\"access\",\"refresh_token\":\"refresh\"}";
            var authB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bundleJson));
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN", "test-token");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "test-token");
            Environment.SetEnvironmentVariable("GITHUB_EVENT_PATH", eventPath);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_B64", authB64);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", authPath);

            var (exitCode, output) = RunReviewerAndCaptureOutput(Array.Empty<string>());
            AssertEqual(0, exitCode, "reviewer untrusted auth store skip exit");
            AssertContainsText(output, "Skipping review to avoid secret access",
                "reviewer untrusted auth store skip message");
            AssertEqual(false, File.Exists(authPath), "reviewer untrusted auth store file should not be written");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN", previousReviewerToken);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHubToken);
            Environment.SetEnvironmentVariable("GITHUB_EVENT_PATH", previousEventPath);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_B64", previousAuthB64);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", previousAuthPath);
            try {
                DeleteDirectoryIfExistsWithRetries(tempDir);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    private static void TestReviewCodeHostEnv() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CODE_HOST");
        try {
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", "azure");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(ReviewCodeHost.AzureDevOps, settings.CodeHost, "code host azure");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", previous);
        }
    }

    private static void TestReviewerGitHubTokenResolverUsesGhTokenFallback() {
        var previousGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var previousGhToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        try {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            Environment.SetEnvironmentVariable("GH_TOKEN", "gh-token-value");
            var token = ReviewerApp.ResolveFirstNonEmptyGitHubToken("GITHUB_TOKEN", "GH_TOKEN");
            var source = ReviewerApp.ResolveFirstNonEmptyGitHubTokenSource("GITHUB_TOKEN", "GH_TOKEN");
            AssertEqual("gh-token-value", token ?? string.Empty, "github token resolver gh token value");
            AssertEqual("GH_TOKEN", source ?? string.Empty, "github token resolver gh token source");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHubToken);
            Environment.SetEnvironmentVariable("GH_TOKEN", previousGhToken);
        }
    }

    private static void TestReviewerGitHubTokenResolverPrefersGithubTokenOverGhToken() {
        var previousGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var previousGhToken = Environment.GetEnvironmentVariable("GH_TOKEN");
        try {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "github-token-value");
            Environment.SetEnvironmentVariable("GH_TOKEN", "gh-token-value");
            var token = ReviewerApp.ResolveFirstNonEmptyGitHubToken("GITHUB_TOKEN", "GH_TOKEN");
            var source = ReviewerApp.ResolveFirstNonEmptyGitHubTokenSource("GITHUB_TOKEN", "GH_TOKEN");
            AssertEqual("github-token-value", token ?? string.Empty, "github token resolver prefers GITHUB_TOKEN");
            AssertEqual("GITHUB_TOKEN", source ?? string.Empty, "github token resolver prefers GITHUB_TOKEN source");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHubToken);
            Environment.SetEnvironmentVariable("GH_TOKEN", previousGhToken);
        }
    }

    private static void TestGitHubContextCache() {
        var prHits = 0;
        var filesHits = 0;
        var compareHits = 0;

        const string prJson = "{"
            + "\"title\":\"Test\",\"body\":\"Body\",\"draft\":false,\"number\":1,"
            + "\"head\":{\"sha\":\"headsha\"},"
            + "\"base\":{\"sha\":\"basesha\",\"repo\":{\"full_name\":\"owner/repo\"}},"
            + "\"labels\":[{\"name\":\"bug\"}]"
            + "}";
        const string filesJson = "[{\"filename\":\"src/A.cs\",\"status\":\"modified\",\"patch\":\"@@\"}]";
        const string compareJson = "{\"files\":[{\"filename\":\"src/A.cs\",\"status\":\"modified\",\"patch\":\"@@\"}]}";

        using var server = new LocalHttpServer(request => {
            if (request.Path.StartsWith("/repos/owner/repo/pulls/1/files", StringComparison.OrdinalIgnoreCase)) {
                filesHits++;
                return new HttpResponse(filesJson);
            }
            if (request.Path.StartsWith("/repos/owner/repo/pulls/1", StringComparison.OrdinalIgnoreCase)) {
                prHits++;
                return new HttpResponse(prJson);
            }
            if (request.Path.Contains("/repos/owner/repo/compare/", StringComparison.OrdinalIgnoreCase)) {
                compareHits++;
                return new HttpResponse(compareJson);
            }
            return null;
        });

        using var client = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var pr1 = client.GetPullRequestAsync("owner", "repo", 1, CancellationToken.None).GetAwaiter().GetResult();
        var pr2 = client.GetPullRequestAsync("owner", "repo", 1, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(1, prHits, "pr cache hits");

        var files1 = client.GetPullRequestFilesAsync("owner", "repo", 1, CancellationToken.None).GetAwaiter().GetResult();
        var files2 = client.GetPullRequestFilesAsync("owner", "repo", 1, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(1, filesHits, "files cache hits");

        var compare1 = client.GetCompareFilesAsync("owner", "repo", "base", "head", CancellationToken.None).GetAwaiter().GetResult();
        var compare2 = client.GetCompareFilesAsync("owner", "repo", "base", "head", CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(1, compareHits, "compare cache hits");

        AssertEqual(pr1.Title, pr2.Title, "pr cache data");
        AssertEqual(files1.Count, files2.Count, "files cache data");
        AssertEqual(compare1.Files.Count, compare2.Files.Count, "compare cache data");
    }

    private static void TestGitHubConcurrencyEnv() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_GITHUB_MAX_CONCURRENCY");
        try {
            Environment.SetEnvironmentVariable("REVIEW_GITHUB_MAX_CONCURRENCY", "2");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(2, settings.GitHubMaxConcurrency, "github concurrency env");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_GITHUB_MAX_CONCURRENCY", previous);
        }
    }

    private static void TestGitHubClientConcurrency() {
        using var client = new GitHubClient("token", "https://api.github.com", 2);
        AssertEqual(2, client.MaxConcurrency, "github client concurrency");
    }

    private static void TestGitHubCodeHostReaderSmoke() {
        const string prJson = "{"
            + "\"title\":\"Reader test\",\"body\":\"Body\",\"draft\":false,\"number\":1,"
            + "\"head\":{\"sha\":\"headsha\",\"repo\":{\"full_name\":\"owner/repo\",\"fork\":false}},"
            + "\"base\":{\"sha\":\"basesha\",\"repo\":{\"full_name\":\"owner/repo\"}},"
            + "\"labels\":[{\"name\":\"bug\"}]"
            + "}";
        const string filesJson = "[{\"filename\":\"src/A.cs\",\"status\":\"modified\",\"patch\":\"@@ -1 +1 @@\\n-a\\n+b\"}]";
        const string compareJson = "{\"files\":[{\"filename\":\"src/A.cs\",\"status\":\"modified\",\"patch\":\"@@\"}]}";
        const string issueCommentsJson = "[{\"id\":1,\"body\":\"Issue comment\",\"user\":{\"login\":\"author\"}}]";
        const string reviewCommentsJson = "[{\"body\":\"Review comment\",\"path\":\"src/A.cs\",\"line\":1,\"user\":{\"login\":\"reviewer\"}}]";

        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/pulls/1/files?per_page=100&page=1") {
                return new HttpResponse(filesJson);
            }
            if (request.Path == "/repos/owner/repo/pulls/1/files?per_page=100&page=2") {
                return new HttpResponse("[]");
            }
            if (request.Path == "/repos/owner/repo/pulls/1") {
                return new HttpResponse(prJson);
            }
            if (request.Path.StartsWith("/repos/owner/repo/compare/", StringComparison.OrdinalIgnoreCase)) {
                return new HttpResponse(compareJson);
            }
            if (request.Path == "/repos/owner/repo/issues/1/comments?per_page=100&page=1&sort=created&direction=desc") {
                return new HttpResponse(issueCommentsJson);
            }
            if (request.Path == "/repos/owner/repo/pulls/1/comments?per_page=100&page=1&sort=created&direction=desc") {
                return new HttpResponse(reviewCommentsJson);
            }
            if (request.Path == "/graphql") {
                return new HttpResponse(BuildGraphQlThreadsResponse("reader"));
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        IReviewCodeHostReader reader = new GitHubCodeHostReader(github);
        var context = reader.GetPullRequestAsync("owner/repo", 1, CancellationToken.None).GetAwaiter().GetResult();
        var files = reader.GetPullRequestFilesAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        var compare = reader.GetCompareFilesAsync(context, "base", "head", CancellationToken.None).GetAwaiter().GetResult();
        var issueComments = reader.ListIssueCommentsAsync(context, 10, CancellationToken.None).GetAwaiter().GetResult();
        var reviewComments = reader.ListPullRequestReviewCommentsAsync(context, 10, CancellationToken.None).GetAwaiter().GetResult();
        var threads = reader.ListPullRequestReviewThreadsAsync(context, 10, 10, CancellationToken.None).GetAwaiter().GetResult();

        AssertEqual("owner", context.Owner, "reader context owner");
        AssertEqual(1, files.Count, "reader files count");
        AssertEqual(1, compare.Files.Count, "reader compare files count");
        AssertEqual(false, compare.IsTruncated, "reader compare truncated");
        AssertEqual(1, issueComments.Count, "reader issue comments");
        AssertEqual(1, reviewComments.Count, "reader review comments");
        AssertEqual(1, threads.Count, "reader review threads");
    }

    private static void TestGitHubCompareTruncation() {
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Contains("/repos/owner/repo/compare/", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            var page = GetQueryInt(request.Path, "page", 1);
            if (page > 20) {
                return new HttpResponse("{\"files\":[]}");
            }
            var startIndex = (page - 1) * 100;
            return new HttpResponse(BuildCompareFilesPage(startIndex, 100));
        });

        using var client = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var result = client.GetCompareFilesAsync("owner", "repo", "base", "head", CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(true, result.IsTruncated, "compare truncated flag");
        AssertEqual(2000, result.Files.Count, "compare truncated count");
    }

    private static void TestDiffRangeCompareTruncation() {
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Contains("/repos/owner/repo/compare/", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            var page = GetQueryInt(request.Path, "page", 1);
            if (page > 20) {
                return new HttpResponse("{\"files\":[]}");
            }
            var startIndex = (page - 1) * 100;
            return new HttpResponse(BuildCompareFilesPage(startIndex, 100));
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var currentFiles = BuildFiles("src/A.cs");
        var settings = new ReviewSettings();
        var (files, note) = CallResolveDiffRangeFiles(github, context, "pr-base", currentFiles, settings);
        AssertEqual(currentFiles.Length, files.Count, "diff range compare truncated files");
        AssertContainsText(note, "current PR files", "diff range compare truncated note");
        AssertContainsText(note, "truncated", "diff range compare truncated marker");
    }

    private static void TestAzureAuthSchemeEnv() {
        var previous = Environment.GetEnvironmentVariable("AZURE_DEVOPS_AUTH_SCHEME");
        try {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_AUTH_SCHEME", "pat");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(AzureDevOpsAuthScheme.Basic, settings.AzureAuthScheme, "azure auth scheme");
            AssertEqual(true, settings.AzureAuthSchemeSpecified, "azure auth scheme specified");
        } finally {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_AUTH_SCHEME", previous);
        }
    }

    private static void TestAzureAuthSchemeInvalidEnv() {
        var previous = Environment.GetEnvironmentVariable("AZURE_DEVOPS_AUTH_SCHEME");
        try {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_AUTH_SCHEME", "nope");
            AssertThrows<InvalidOperationException>(() => ReviewSettings.FromEnvironment(), "azure auth scheme invalid");
        } finally {
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_AUTH_SCHEME", previous);
        }
    }

    private static void TestReviewSettingsDefaultsAndEnvMerge() {
        var previousProvider = Environment.GetEnvironmentVariable("REVIEW_PROVIDER");
        var previousCodeHost = Environment.GetEnvironmentVariable("REVIEW_CODE_HOST");
        var previousMaxFiles = Environment.GetEnvironmentVariable("OPENAI_MAX_FILES");
        var previousSkipGenerated = Environment.GetEnvironmentVariable("SKIP_GENERATED_FILES");
        try {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", "copilot");
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", "azure");
            Environment.SetEnvironmentVariable("OPENAI_MAX_FILES", "42");
            Environment.SetEnvironmentVariable("SKIP_GENERATED_FILES", "false");

            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(ReviewProvider.Copilot, settings.Provider, "review settings env provider");
            AssertEqual(ReviewCodeHost.AzureDevOps, settings.CodeHost, "review settings env code host");
            AssertEqual(42, settings.MaxFiles, "review settings env max files");
            AssertEqual(false, settings.SkipGeneratedFiles, "review settings env skip generated");
            AssertEqual("current", settings.ReviewDiffRange, "review settings default diff range");
            AssertEqual(AnalysisConfigMode.Respect, settings.Analysis.ConfigMode,
                "review settings analysis default config mode");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", previousCodeHost);
            Environment.SetEnvironmentVariable("OPENAI_MAX_FILES", previousMaxFiles);
            Environment.SetEnvironmentVariable("SKIP_GENERATED_FILES", previousSkipGenerated);
        }
    }

    private static void TestAzureDevOpsCodeHostReaderSmoke() {
        const string pullRequestJson = "{"
            + "\"pullRequestId\":7,"
            + "\"title\":\"ADO Reader\","
            + "\"description\":\"Body\","
            + "\"isDraft\":false,"
            + "\"repository\":{"
            + "\"id\":\"repo-id\","
            + "\"name\":\"repo-name\","
            + "\"project\":{\"name\":\"project-name\"}"
            + "},"
            + "\"lastMergeSourceCommit\":{\"commitId\":\"source-sha\"},"
            + "\"lastMergeTargetCommit\":{\"commitId\":\"target-sha\"}"
            + "}";
        const string changesJson = "{\"changes\":[{\"item\":{\"path\":\"/src/A.cs\"},\"changeType\":\"edit\"}]}";

        using var server = new LocalHttpServer(request => {
            if (request.Path == "/project-name/_apis/git/pullrequests/7?api-version=7.1") {
                return new HttpResponse(pullRequestJson);
            }
            if (request.Path == "/project-name/_apis/git/repositories/repo-id/pullRequests/7/changes?api-version=7.1") {
                return new HttpResponse(changesJson);
            }
            return null;
        });

        using var client = new AzureDevOpsClient(server.BaseUri, "token", AzureDevOpsAuthScheme.Bearer);
        IReviewCodeHostReader reader = new AzureDevOpsCodeHostReader(client, "project-name", "repo-id");
        var context = reader.GetPullRequestAsync("project-name", 7, CancellationToken.None).GetAwaiter().GetResult();
        var files = reader.GetPullRequestFilesAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        var compare = reader.GetCompareFilesAsync(context, "a", "b", CancellationToken.None).GetAwaiter().GetResult();
        var issueComments = reader.ListIssueCommentsAsync(context, 10, CancellationToken.None).GetAwaiter().GetResult();
        var reviewComments = reader.ListPullRequestReviewCommentsAsync(context, 10, CancellationToken.None).GetAwaiter().GetResult();
        var threads = reader.ListPullRequestReviewThreadsAsync(context, 10, 10, CancellationToken.None).GetAwaiter().GetResult();

        AssertEqual("project-name", context.Owner, "ado reader owner");
        AssertEqual("repo-name", context.Repo, "ado reader repo");
        AssertEqual(1, files.Count, "ado reader files");
        AssertEqual("src/A.cs", files[0].Filename, "ado reader filename");
        AssertEqual(0, compare.Files.Count, "ado reader compare");
        AssertEqual(false, compare.IsTruncated, "ado reader compare truncated");
        AssertEqual(0, issueComments.Count, "ado reader issue comments");
        AssertEqual(0, reviewComments.Count, "ado reader review comments");
        AssertEqual(0, threads.Count, "ado reader review threads");
    }

    private static void TestTriageOnlyLoadsThreads() {
        var response = BuildGraphQlThreadsResponse();
        using var server = new LocalHttpServer(request => request.Path == "/graphql" ? response : null);
        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false,
            ReviewThreadsMax = 5,
            ReviewThreadsMaxComments = 2
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var extras = CallBuildExtrasAsync(github, context, settings, true);
        AssertEqual(1, extras.ReviewThreads.Count, "triage-only forces thread load");
    }

    private static void TestTriageThreadHydrationUsesFallbackClientWhenProvided() {
        var primaryHydrationAttempts = 0;
        var fallbackHydrationAttempts = 0;

        using var primaryServer = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (request.Body.Contains("node(id:$id)", StringComparison.Ordinal)) {
                primaryHydrationAttempts++;
                return new HttpResponse("{\"errors\":[{\"message\":\"forbidden\"}]}", StatusCode: 403, StatusText: "Forbidden");
            }
            return new HttpResponse(BuildGraphQlThreadsResponse(
                $"{ReviewFormatter.InlineMarker}\nInitial",
                "src/Foo.cs",
                10,
                "intelligencex-review",
                "thread1",
                totalComments: 2));
        });
        using var fallbackServer = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (request.Body.Contains("node(id:$id)", StringComparison.Ordinal)) {
                fallbackHydrationAttempts++;
                return new HttpResponse(BuildGraphQlHydratedThreadResponse(
                    "thread1",
                    ($"{ReviewFormatter.InlineMarker}\nInitial", "src/Foo.cs", 10, "intelligencex-review"),
                    ("Follow-up", "src/Foo.cs", 10, "intelligencex-review")));
            }
            return new HttpResponse("{\"data\":{}}");
        });

        using var github = new GitHubClient("token", primaryServer.BaseUri.ToString().TrimEnd('/'));
        using var fallbackGithub = new GitHubClient("token", fallbackServer.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = true,
            ReviewThreadsAutoResolveBotsOnly = true,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false,
            ReviewThreadsMax = 5,
            ReviewThreadsMaxComments = 1
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, fallbackGithub, context, settings, true);

        AssertEqual(0, primaryHydrationAttempts, "primary hydration skipped when fallback client is preferred");
        AssertEqual(1, fallbackHydrationAttempts, "fallback hydration attempted");
        AssertEqual(2, extras.ReviewThreads[0].Comments.Count, "fallback hydration expands thread");
    }

    private static void TestReviewThreadsDiffRangeNormalize() {
        AssertEqual("current", ReviewSettings.NormalizeDiffRange("current", "pr-base"), "diff current");
        AssertEqual("pr-base", ReviewSettings.NormalizeDiffRange("pr_base", "current"), "diff pr-base");
        AssertEqual("first-review", ReviewSettings.NormalizeDiffRange("first_review", "current"), "diff first-review");
        AssertEqual("current", ReviewSettings.NormalizeDiffRange("unknown", "current"), "diff fallback");
    }

    private static void TestReviewThreadsAutoResolveSweepNoBlockersConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"reviewThreadsAutoResolveSweepNoBlockers\": true } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);

            AssertEqual(true, settings.ReviewThreadsAutoResolveSweepNoBlockers, "sweep-no-blockers config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewThreadsAutoResolveSweepNoBlockersEnv() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_THREADS_AUTO_RESOLVE_SWEEP_NO_BLOCKERS");
        try {
            Environment.SetEnvironmentVariable("REVIEW_THREADS_AUTO_RESOLVE_SWEEP_NO_BLOCKERS", "true");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(true, settings.ReviewThreadsAutoResolveSweepNoBlockers, "sweep-no-blockers env");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_THREADS_AUTO_RESOLVE_SWEEP_NO_BLOCKERS", previous);
        }
    }

    private static void TestReviewMergeBlockerPolicyConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path,
                "{ \"review\": { \"mergeBlockerSections\": [\"Todo List\", \"Release Risk\"], " +
                "\"mergeBlockerRequireAllSections\": false, \"mergeBlockerRequireSectionMatch\": false } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);

            AssertSequenceEqual(new[] { "Todo List", "Release Risk" }, settings.MergeBlockerSections,
                "merge blocker sections config");
            AssertEqual(false, settings.MergeBlockerRequireAllSections, "merge blocker require all config");
            AssertEqual(false, settings.MergeBlockerRequireSectionMatch, "merge blocker require section match config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewMergeBlockerPolicyEnv() {
        var previousSections = Environment.GetEnvironmentVariable("REVIEW_MERGE_BLOCKER_SECTIONS");
        var previousRequireAll = Environment.GetEnvironmentVariable("REVIEW_MERGE_BLOCKER_REQUIRE_ALL_SECTIONS");
        var previousRequireMatch = Environment.GetEnvironmentVariable("REVIEW_MERGE_BLOCKER_REQUIRE_SECTION_MATCH");
        try {
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_SECTIONS", "Todo List,Release Risk");
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_REQUIRE_ALL_SECTIONS", "false");
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_REQUIRE_SECTION_MATCH", "false");
            var settings = ReviewSettings.FromEnvironment();
            AssertSequenceEqual(new[] { "Todo List", "Release Risk" }, settings.MergeBlockerSections,
                "merge blocker sections env");
            AssertEqual(false, settings.MergeBlockerRequireAllSections, "merge blocker require all env");
            AssertEqual(false, settings.MergeBlockerRequireSectionMatch, "merge blocker require section match env");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_SECTIONS", previousSections);
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_REQUIRE_ALL_SECTIONS", previousRequireAll);
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_REQUIRE_SECTION_MATCH", previousRequireMatch);
        }
    }

    private static void TestReviewMergeBlockerPolicyEnvNormalizesWhitespace() {
        var previousSections = Environment.GetEnvironmentVariable("REVIEW_MERGE_BLOCKER_SECTIONS");
        try {
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_SECTIONS", "Todo   List,Release\tRisk, Todo List ");
            var settings = ReviewSettings.FromEnvironment();
            AssertSequenceEqual(new[] { "Todo List", "Release Risk" }, settings.MergeBlockerSections,
                "merge blocker sections env whitespace normalization");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_MERGE_BLOCKER_SECTIONS", previousSections);
        }
    }

    private static void TestCopilotEnvAllowlistConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path,
                "{ \"copilot\": { \"envAllowlist\": [\"GH_TOKEN\"], \"inheritEnvironment\": false, " +
                "\"env\": { \"COPILOT_DEBUG\": \"1\" }, " +
                "\"transport\": \"direct\", \"directUrl\": \"https://example.local/api\", " +
                "\"directTokenEnv\": \"COPILOT_DIRECT_TOKEN\", \"directTimeoutSeconds\": 12, " +
                "\"directHeaders\": { \"X-Test\": \"ok\" } } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);

            AssertSequenceEqual(new[] { "GH_TOKEN" }, settings.CopilotEnvAllowlist, "copilot env allowlist");
            AssertEqual(false, settings.CopilotInheritEnvironment, "copilot inherit environment");
            AssertEqual("1", settings.CopilotEnv["COPILOT_DEBUG"], "copilot env map");
            AssertEqual(CopilotTransportKind.Direct, settings.CopilotTransport, "copilot transport");
            AssertEqual("https://example.local/api", settings.CopilotDirectUrl, "copilot direct url");
            AssertEqual("COPILOT_DIRECT_TOKEN", settings.CopilotDirectTokenEnv ?? string.Empty, "copilot direct token env");
            AssertEqual(12, settings.CopilotDirectTimeoutSeconds, "copilot direct timeout");
            AssertEqual("ok", settings.CopilotDirectHeaders["X-Test"], "copilot direct header");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestCopilotInheritEnvironmentDefault() {
        var settings = new ReviewSettings();
        AssertEqual(true, settings.CopilotInheritEnvironment, "copilot inherit environment default");
    }

    private static void TestCopilotDirectTimeoutValidation() {
        var options = new IntelligenceX.Copilot.Direct.CopilotDirectOptions {
            Url = "https://example.local/api",
            Timeout = TimeSpan.Zero
        };
        AssertThrows<ArgumentOutOfRangeException>(() => options.Validate(), "copilot direct timeout");
    }

    private static void TestCopilotChatTimeoutValidation() {
        var options = new IntelligenceX.Copilot.CopilotChatClientOptions {
            Timeout = TimeSpan.Zero
        };
        AssertThrows<ArgumentOutOfRangeException>(() => options.Validate(), "copilot chat timeout");
    }

    private static void TestCopilotDirectAuthorizationConflict() {
        var options = new IntelligenceX.Copilot.Direct.CopilotDirectOptions {
            Url = "https://example.local/api",
            Token = "token"
        };
        options.Headers["authorization"] = "Bearer override";
        AssertThrows<ArgumentException>(() => options.Validate(), "copilot direct auth conflict");
    }

    private static void TestCopilotCliPathRequiresEnvironment() {
        var options = new IntelligenceX.Copilot.CopilotClientOptions {
            InheritEnvironment = false,
            CliPath = "copilot"
        };
        AssertThrows<InvalidOperationException>(() => options.Validate(), "copilot cli path");
    }

    private static void TestCopilotCliPathOptionalWithUrl() {
        var options = new IntelligenceX.Copilot.CopilotClientOptions {
            InheritEnvironment = false,
            AutoStart = false,
            CliPath = "copilot",
            CliUrl = "http://localhost:1234"
        };
        options.Validate();
    }

    private static void TestCopilotCliUrlValidation() {
        var options = new IntelligenceX.Copilot.CopilotClientOptions {
            CliUrl = "bad url"
        };
        AssertThrows<ArgumentException>(() => options.Validate(), "copilot cli url");
    }


}
#endif
