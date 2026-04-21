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

    private static void TestReviewerValidateAuthRejectsExpiredBundleForNonNativeTransportWithRefreshGuidance() {
        var previousAuthPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        var previousGitHubRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var tempDir = Path.Combine(Path.GetTempPath(), "intelligencex-reviewer-auth-expired-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        try {
            var expired = new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "access", "refresh",
                DateTimeOffset.UtcNow.AddMinutes(-10));
            var store = new IntelligenceX.OpenAI.Auth.FileAuthBundleStore(authPath);
            store.SaveAsync(expired).GetAwaiter().GetResult();

            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", authPath);
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "owner/repo");

            var settings = new ReviewSettings {
                Provider = ReviewProvider.OpenAI,
                OpenAITransport = OpenAITransportKind.AppServer
            };

            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            try {
                var ok = ReviewerApp.ValidateAuthForTestsAsync(settings).GetAwaiter().GetResult();
                AssertEqual(false, ok, "expired auth validation result");
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }

            var error = errWriter.ToString();
            AssertContainsText(error, "OpenAI auth bundle expired at", "expired auth message");
            AssertContainsText(error, "intelligencex auth login --set-github-secret --repo owner/repo",
                "expired auth remediation command");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", previousAuthPath);
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", previousGitHubRepo);
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

    private static void TestGitHubRequiredConversationResolutionLookup() {
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/branches/master/protection") {
                return new HttpResponse("{\"required_conversation_resolution\":{\"enabled\":true}}");
            }
            return null;
        });

        using var client = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var enabled = client.GetRequiredConversationResolutionAsync("owner", "repo", "master", CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertEqual(true, enabled, "required conversation resolution lookup");
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

    private static void TestBuildExtrasIncludesCiContextSection() {
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/commits/head/check-runs?per_page=100&page=1") {
                return new HttpResponse("""
{"check_runs":[
  {"name":"unit-tests","status":"completed","conclusion":"failure","details_url":"https://example.local/checks/1"},
  {"name":"lint","status":"completed","conclusion":"success","details_url":"https://example.local/checks/2"},
  {"name":"integration","status":"in_progress","conclusion":null,"details_url":"https://example.local/checks/3"}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs?head_sha=head&per_page=100") {
                return new HttpResponse("""
{"workflow_runs":[
  {"id":100,"name":"CI","status":"completed","conclusion":"failure","head_sha":"head","html_url":"https://example.local/runs/1"},
  {"id":200,"name":"Infra","status":"completed","conclusion":"timed_out","head_sha":"head","html_url":"https://example.local/runs/2"},
  {"id":300,"name":"Other Sha","status":"completed","conclusion":"failure","head_sha":"other","html_url":"https://example.local/runs/3"}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs/100/jobs?per_page=100&page=1") {
                return new HttpResponse("""
{"jobs":[
  {"name":"build-and-test","status":"completed","conclusion":"failure","steps":[
    {"name":"Checkout","status":"completed","conclusion":"success"},
    {"name":"dotnet test","status":"completed","conclusion":"failure"}
  ]}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs/200/jobs?per_page=100&page=1") {
                return new HttpResponse("""
{"jobs":[
  {"name":"bootstrap","status":"completed","conclusion":"timed_out","steps":[
    {"name":"Set up job","status":"completed","conclusion":"timed_out"}
  ]}
]}
""");
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false
        };
        settings.CiContext.Enabled = true;
        settings.CiContext.IncludeCheckSummary = true;
        settings.CiContext.IncludeFailedRuns = true;
        settings.CiContext.IncludeFailureSnippets = "always";
        settings.CiContext.MaxFailedRuns = 5;
        settings.CiContext.MaxSnippetCharsPerRun = 200;
        settings.CiContext.ClassifyInfraFailures = true;

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, context, settings, false);

        AssertContainsText(extras.CiContextSection, "CI / checks context:", "build extras ci context header");
        AssertContainsText(extras.CiContextSection, "Head SHA head check-runs: passed 1, failed 1, pending 1.",
            "build extras ci context counts");
        AssertContainsText(extras.CiContextSection, "Failing check-runs: unit-tests (failure).",
            "build extras ci context failed checks");
        AssertContainsText(extras.CiContextSection, "CI (failure) https://example.local/runs/1",
            "build extras ci context failed workflow");
        AssertContainsText(extras.CiContextSection, "Infra (timed_out) https://example.local/runs/2",
            "build extras ci context timed out workflow");
        AssertContainsText(extras.CiContextSection, "[likely code/test]: job build-and-test: failed step dotnet test (failure)",
            "build extras ci context actionable snippet");
        AssertContainsText(extras.CiContextSection, "[likely operational/infra]: job bootstrap: failed step Set up job (timed_out)",
            "build extras ci context operational snippet");
        AssertContainsText(extras.CiContextSection,
            "Failure evidence is summarized from failed GitHub Actions jobs/steps only",
            "build extras ci context failure evidence note");
        AssertContainsText(extras.CiContextSection,
            "cancelled or timed-out workflow runs may be operational rather than code failures",
            "build extras ci context operational note");
    }

    private static void TestBuildExtrasCiContextAutoSkipsOperationalOnlySnippets() {
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/commits/head/check-runs?per_page=100&page=1") {
                return new HttpResponse("""
{"check_runs":[
  {"name":"infra","status":"completed","conclusion":"timed_out","details_url":"https://example.local/checks/9"}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs?head_sha=head&per_page=100") {
                return new HttpResponse("""
{"workflow_runs":[
  {"id":200,"name":"Infra","status":"completed","conclusion":"timed_out","head_sha":"head","html_url":"https://example.local/runs/2"}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs/200/jobs?per_page=100&page=1") {
                return new HttpResponse("""
{"jobs":[
  {"name":"bootstrap","status":"completed","conclusion":"timed_out","steps":[
    {"name":"Set up job","status":"completed","conclusion":"timed_out"}
  ]}
]}
""");
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false
        };
        settings.CiContext.Enabled = true;
        settings.CiContext.IncludeFailedRuns = true;
        settings.CiContext.IncludeFailureSnippets = "auto";
        settings.CiContext.MaxSnippetCharsPerRun = 200;
        settings.CiContext.ClassifyInfraFailures = true;

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, context, settings, false);

        AssertContainsText(extras.CiContextSection, "Infra (timed_out) https://example.local/runs/2 [likely operational/infra]",
            "build extras ci context auto classification");
        AssertEqual(false, extras.CiContextSection.Contains("Set up job (timed_out)", StringComparison.Ordinal),
            "build extras ci context auto skips operational snippet");
        AssertEqual(false, extras.CiContextSection.Contains(
                "Failure evidence is summarized from failed GitHub Actions jobs/steps only", StringComparison.Ordinal),
            "build extras ci context auto skips note without included snippets");
    }

    private static void TestBuildExtrasCiContextFailureIsSupplemental() {
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/commits/head/check-runs?per_page=100&page=1") {
                return new HttpResponse("{\"message\":\"forbidden\"}", null, 403, "Forbidden");
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false
        };
        settings.CiContext.Enabled = true;

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, context, settings, false);

        AssertEqual(string.Empty, extras.CiContextSection, "build extras ci context failure remains supplemental");
    }

    private static void TestBuildExtrasLoadsIssueCommentsForExternalHistory() {
        var issueCommentHits = 0;
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/issues/1/comments?per_page=100&page=1&sort=created&direction=desc") {
                issueCommentHits++;
                return new HttpResponse("""
[
  {
    "id": 10,
    "body": "Claude summary: prior finding appears resolved.",
    "user": { "login": "claude" },
    "html_url": "https://example.local/comment/10"
  }
]
""");
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false
        };
        settings.History.Enabled = true;
        settings.History.IncludeIxSummaryHistory = false;
        settings.History.IncludeExternalBotSummaries = true;
        settings.History.ExternalBotLogins = new[] { "claude" };

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, context, settings, false);

        AssertEqual(1, issueCommentHits, "external history forces issue comment load");
        AssertEqual(1, extras.IssueComments.Count, "external history issue comments available");
    }

    private static void TestBuildExtrasKeepsIssueCommentPromptCapWithHistory() {
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/issues/1/comments?per_page=100&page=1&sort=created&direction=desc") {
                return new HttpResponse("""
[
  {
    "id": 9,
    "body": "<!-- intelligencex:summary -->\n## IntelligenceX Review\nPrevious sticky summary.",
    "user": { "login": "intelligencex-review" },
    "html_url": "https://example.local/comment/9"
  },
  {
    "id": 10,
    "body": "First prompt comment.",
    "user": { "login": "alice" },
    "html_url": "https://example.local/comment/10"
  },
  {
    "id": 11,
    "body": "Second history-only comment.",
    "user": { "login": "bob" },
    "html_url": "https://example.local/comment/11"
  }
]
""");
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = true,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            MaxComments = 1,
            CommentSearchLimit = 2,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false
        };
        settings.History.Enabled = true;
        settings.History.IncludeIxSummaryHistory = true;

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, context, settings, false);

        AssertEqual(2, extras.IssueComments.Count, "history can retain deeper issue comments");
        AssertContainsText(extras.IssueCommentsSection, "First prompt comment.",
            "issue comments prompt caps after filtering sticky summary comments");
        AssertEqual(false, extras.IssueCommentsSection.Contains("Previous sticky summary.", StringComparison.Ordinal),
            "issue comments prompt excludes sticky summary before cap");
        AssertEqual(false, extras.IssueCommentsSection.Contains("Second history-only comment.", StringComparison.Ordinal),
            "issue comments prompt keeps maxComments cap");
    }

    private static void TestBuildExtrasCiFailureEvidenceFailureIsSupplemental() {
        using var server = new LocalHttpServer(request => {
            if (request.Path == "/repos/owner/repo/commits/head/check-runs?per_page=100&page=1") {
                return new HttpResponse("""
{"check_runs":[
  {"name":"unit-tests","status":"completed","conclusion":"failure","details_url":"https://example.local/checks/1"}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs?head_sha=head&per_page=100") {
                return new HttpResponse("""
{"workflow_runs":[
  {"id":100,"name":"CI","status":"completed","conclusion":"failure","head_sha":"head","html_url":"https://example.local/runs/1"}
]}
""");
            }
            if (request.Path == "/repos/owner/repo/actions/runs/100/jobs?per_page=100&page=1") {
                return new HttpResponse("{\"message\":\"forbidden\"}", null, 403, "Forbidden");
            }
            return null;
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = false
        };
        settings.CiContext.Enabled = true;
        settings.CiContext.IncludeCheckSummary = true;
        settings.CiContext.IncludeFailedRuns = true;
        settings.CiContext.IncludeFailureSnippets = "always";
        settings.CiContext.MaxSnippetCharsPerRun = 200;

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);

        var extras = CallBuildExtrasAsync(github, context, settings, false);

        AssertContainsText(extras.CiContextSection, "CI (failure) https://example.local/runs/1",
            "build extras ci evidence failure still keeps workflow");
        AssertEqual(false, extras.CiContextSection.Contains("failed step", StringComparison.Ordinal),
            "build extras ci evidence failure does not inject snippet");
    }

    private static void TestBuildExtrasCapturesStaleAutoResolvePermissionFailures() {
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (request.Body.Contains("resolveReviewThread", StringComparison.Ordinal)) {
                return new HttpResponse(
                    "{\"data\":{\"resolveReviewThread\":null},\"errors\":[{\"type\":\"FORBIDDEN\",\"message\":\"Resource not accessible by integration\"}]}");
            }
            return new HttpResponse(BuildGraphQlThreadsResponse(
                $"{ReviewFormatter.InlineMarker}\nInitial",
                "src/Foo.cs",
                10,
                "intelligencex-review",
                "thread1",
                isResolved: false,
                isOutdated: true));
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'), credentialLabel: "GITHUB_TOKEN");
        var settings = new ReviewSettings {
            IncludeIssueComments = false,
            IncludeReviewComments = false,
            IncludeReviewThreads = false,
            ReviewThreadsAutoResolveAI = false,
            ReviewThreadsAutoResolveStale = true,
            ReviewThreadsAutoResolveBotsOnly = true,
            ReviewThreadsMax = 5,
            ReviewThreadsMaxComments = 2
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null, baseRefName: "master");

        var extras = CallBuildExtrasAsync(github, context, settings, true);

        AssertEqual(1, extras.StaleThreadAutoResolvePermissions.DeniedThreadCount,
            "stale auto-resolve permission denied count");
        AssertEqual(true, extras.StaleThreadAutoResolvePermissions.DeniedCredentialLabels.Contains("GITHUB_TOKEN",
            StringComparer.OrdinalIgnoreCase), "stale auto-resolve permission denied token");
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
                "\"launcher\": \"gh\", " +
                "\"model\": \"claude-sonnet-4.6\", " +
                "\"env\": { \"COPILOT_DEBUG\": \"1\" }, " +
                "\"transport\": \"direct\", \"directUrl\": \"https://example.local/api\", " +
                "\"directTokenEnv\": \"COPILOT_DIRECT_TOKEN\", \"directTimeoutSeconds\": 12, " +
                "\"directHeaders\": { \"X-Test\": \"ok\" } } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);

            AssertSequenceEqual(new[] { "GH_TOKEN" }, settings.CopilotEnvAllowlist, "copilot env allowlist");
            AssertEqual(false, settings.CopilotInheritEnvironment, "copilot inherit environment");
            AssertEqual("gh", settings.CopilotLauncher, "copilot launcher");
            AssertEqual("claude-sonnet-4.6", settings.CopilotModel ?? string.Empty, "copilot model");
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

    private static void TestCopilotAgentProfileConfigPreservesExplicitEmptyMaps() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-profile-clear-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path,
                "{ \"review\": { \"agentProfiles\": { \"clearer\": { " +
                "\"provider\": \"copilot\", \"model\": \"gpt-5.4\", " +
                "\"envAllowlist\": [], \"copilotEnv\": {}, \"directHeaders\": {} } } } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);

            AssertEqual(true, settings.AgentProfiles.TryGetValue("clearer", out var profile),
                "copilot agent profile clear profile exists");
            AssertNotNull(profile, "copilot agent profile clear profile value");
            AssertNotNull(profile!.CopilotEnvAllowlist, "copilot agent profile clear allowlist preserved");
            AssertEqual(0, profile.CopilotEnvAllowlist!.Count, "copilot agent profile clear allowlist count");
            AssertNotNull(profile.CopilotEnv, "copilot agent profile clear env map preserved");
            AssertEqual(0, profile.CopilotEnv!.Count, "copilot agent profile clear env map count");
            AssertNotNull(profile.CopilotDirectHeaders, "copilot agent profile clear header map preserved");
            AssertEqual(0, profile.CopilotDirectHeaders!.Count, "copilot agent profile clear header map count");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestNonCopilotAgentProfileIgnoresRootCopilotAliases() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-profile-noncopilot-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, """
{
  "review": {
    "provider": "copilot",
    "copilot": {
      "transport": "cli"
    },
    "agentProfile": "openai-main",
    "agentProfiles": {
      "openai-main": {
        "provider": "openai",
        "model": "gpt-5.4",
        "transport": "direct",
        "launcher": "gh"
      }
    }
  }
}
""");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();

            ReviewConfigLoader.Apply(settings);

            AssertEqual(true, settings.AgentProfiles.TryGetValue("openai-main", out var profile),
                "non-copilot root alias profile exists");
            AssertNotNull(profile, "non-copilot root alias profile value");
            AssertEqual(ReviewProvider.OpenAI, profile!.Provider, "non-copilot root alias provider");
            AssertEqual(null, profile.CopilotTransport, "non-copilot root alias should not set copilot transport");
            AssertEqual(null, profile.CopilotLauncher, "non-copilot root alias should not set copilot launcher");
            AssertEqual(CopilotTransportKind.Cli, settings.CopilotTransport,
                "non-copilot root alias should preserve ambient copilot transport");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestCopilotAgentProfileAllowsRootCopilotAliases() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-profile-copilot-root-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, """
{
  "review": {
    "agentProfiles": {
      "copilot-root": {
        "provider": "copilot",
        "model": "claude-sonnet-4.6",
        "transport": "direct",
        "launcher": "gh",
        "directUrl": "https://example.local/copilot",
        "directTokenEnv": "COPILOT_DIRECT_TOKEN"
      }
    }
  }
}
""");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();

            ReviewConfigLoader.Apply(settings);

            AssertEqual(true, settings.AgentProfiles.TryGetValue("copilot-root", out var profile),
                "copilot root alias profile exists");
            AssertNotNull(profile, "copilot root alias profile value");
            AssertEqual(ReviewProvider.Copilot, profile!.Provider, "copilot root alias provider");
            AssertEqual(CopilotTransportKind.Direct, profile.CopilotTransport, "copilot root alias transport");
            AssertEqual("gh", profile.CopilotLauncher, "copilot root alias launcher");
            AssertEqual("https://example.local/copilot", profile.CopilotDirectUrl ?? string.Empty,
                "copilot root alias direct url");
            AssertEqual("COPILOT_DIRECT_TOKEN", profile.CopilotDirectTokenEnv ?? string.Empty,
                "copilot root alias direct token env");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewConfigLoaderApplyMaterializesSelectedAgentProfile() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-profile-apply-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, """
{
  "review": {
    "provider": "openai",
    "model": "gpt-5.4-mini",
    "agentProfile": "copilot-claude",
    "agentProfiles": {
      "copilot-claude": {
        "provider": "copilot",
        "model": "claude-sonnet-4.5",
        "copilot": {
          "launcher": "auto",
          "autoInstall": true
        }
      }
    }
  }
}
""");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();

            ReviewConfigLoader.Apply(settings);

            AssertEqual("copilot-claude", settings.AgentProfile ?? string.Empty,
                "review config apply selected agent profile id");
            AssertEqual(ReviewProvider.Copilot, settings.Provider,
                "review config apply selected agent profile provider");
            AssertEqual("claude-sonnet-4.5", settings.Model,
                "review config apply selected agent profile model");
            AssertEqual("auto", settings.CopilotLauncher,
                "review config apply selected agent profile launcher");
            AssertEqual(true, settings.CopilotAutoInstall,
                "review config apply selected agent profile auto install");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestCopilotLauncherEnv() {
        var previousInput = Environment.GetEnvironmentVariable("INPUT_COPILOT_LAUNCHER");
        var previousEnv = Environment.GetEnvironmentVariable("COPILOT_LAUNCHER");
        try {
            Environment.SetEnvironmentVariable("INPUT_COPILOT_LAUNCHER", null);
            Environment.SetEnvironmentVariable("COPILOT_LAUNCHER", "github-cli");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual("gh", settings.CopilotLauncher, "copilot launcher env");
        } finally {
            Environment.SetEnvironmentVariable("INPUT_COPILOT_LAUNCHER", previousInput);
            Environment.SetEnvironmentVariable("COPILOT_LAUNCHER", previousEnv);
        }
    }

    private static void TestCopilotModelEnvOverridesGenericModel() {
        var previousProvider = Environment.GetEnvironmentVariable("INPUT_PROVIDER");
        var previousModel = Environment.GetEnvironmentVariable("INPUT_MODEL");
        var previousCopilotModel = Environment.GetEnvironmentVariable("INPUT_COPILOT_MODEL");
        try {
            Environment.SetEnvironmentVariable("INPUT_PROVIDER", "copilot");
            Environment.SetEnvironmentVariable("INPUT_MODEL", "gpt-5.4");
            Environment.SetEnvironmentVariable("INPUT_COPILOT_MODEL", "claude-sonnet-4.6");
            var settings = ReviewSettings.FromEnvironment();

            AssertEqual(ReviewProvider.Copilot, settings.Provider, "copilot provider env");
            AssertEqual("gpt-5.4", settings.Model, "generic model env remains recorded");
            AssertEqual("claude-sonnet-4.6", settings.CopilotModel ?? string.Empty, "copilot model env");
            AssertEqual("claude-sonnet-4.6", ReviewRunner.ResolveCopilotModel(settings) ?? string.Empty,
                "copilot resolved model prefers copilot_model");
        } finally {
            Environment.SetEnvironmentVariable("INPUT_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("INPUT_MODEL", previousModel);
            Environment.SetEnvironmentVariable("INPUT_COPILOT_MODEL", previousCopilotModel);
        }
    }

    private static void TestCopilotDefaultOpenAiModelUsesCliDefault() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.Copilot,
            Model = OpenAIModelCatalog.DefaultModel
        };

        AssertEqual(null, ReviewRunner.ResolveCopilotModel(settings), "copilot default model");
    }

    private static void TestCopilotPromptTimeoutUsesRunnerSafeMinimum() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.Copilot,
            CopilotTransport = CopilotTransportKind.Cli,
            WaitSeconds = 17
        };

        AssertEqual(TimeSpan.FromSeconds(17), ReviewRunner.ResolveCopilotReviewTimeout(settings),
            "copilot prompt timeout should honor configured wait");
    }

    private static void TestCopilotPromptTimeoutHonorsHigherExplicitWait() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.Copilot,
            CopilotTransport = CopilotTransportKind.Cli,
            WaitSeconds = 600
        };

        AssertEqual(TimeSpan.FromSeconds(600), ReviewRunner.ResolveCopilotReviewTimeout(settings),
            "copilot prompt timeout should preserve larger explicit wait");
    }

    private static void TestCopilotCliSessionTimeoutUsesRunnerSafeMinimum() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.Copilot,
            CopilotTransport = CopilotTransportKind.Cli,
            WaitSeconds = 23
        };

        AssertEqual(TimeSpan.FromSeconds(23), ReviewRunner.ResolveCopilotReviewTimeout(settings),
            "copilot cli session timeout should honor configured wait");
    }

    private static void TestCopilotCliSessionTimeoutHonorsHigherExplicitWait() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.Copilot,
            CopilotTransport = CopilotTransportKind.Cli,
            WaitSeconds = 900
        };

        AssertEqual(TimeSpan.FromSeconds(900), ReviewRunner.ResolveCopilotReviewTimeout(settings),
            "copilot cli session timeout should preserve larger explicit wait");
    }

    private static void TestCopilotPromptFailureFallsBackForTimeoutAndPromptErrors() {
        AssertEqual(true,
            ReviewRunner.ShouldFallbackFromCopilotPromptFailure(
                new InvalidOperationException("Copilot CLI not found or failed to start in prompt mode.")),
            "copilot prompt start failure should trigger session fallback");

        AssertEqual(false,
            ReviewRunner.ShouldFallbackFromCopilotPromptFailure(
                new TimeoutException("Copilot CLI prompt mode timed out after 420 seconds.")),
            "copilot prompt timeout should not trigger session fallback");

        AssertEqual(false,
            ReviewRunner.ShouldFallbackFromCopilotPromptFailure(
                new InvalidOperationException("Copilot authentication failed.")),
            "copilot auth failure should not trigger prompt fallback");

        AssertEqual(true,
            ReviewRunner.ShouldFallbackFromCopilotPromptFailure(
                new InvalidOperationException(
                    "Copilot CLI prompt mode exited with code 1.\nRecent Copilot CLI stderr:\n  error: unknown option '--available-tools=none'")),
            "copilot prompt compatibility exit failures should trigger session fallback");

        AssertEqual(false,
            ReviewRunner.ShouldFallbackFromCopilotPromptFailure(
                new InvalidOperationException("Copilot CLI prompt mode exited with code 1.")),
            "copilot prompt generic exit failures should not trigger session fallback");

        AssertEqual(true,
            ReviewRunner.ShouldFallbackFromCopilotPromptFailure(
                new InvalidOperationException("Copilot CLI prompt mode failed while writing prompt input.")),
            "copilot prompt write failures should trigger session fallback");
    }

    private static void TestCopilotPromptModeSelectionUsesCliUrlOnly() {
        var settings = new ReviewSettings();
        AssertEqual(true, ReviewRunner.ShouldUseCopilotPromptModeForTests(settings),
            "copilot prompt mode should stay enabled when cliUrl is absent");
        AssertEqual(true, ReviewRunner.ShouldUseCopilotPromptModeForTests(settings),
            "copilot prompt mode should not inspect prompt size");

        settings.CopilotCliUrl = "http://localhost:4141";
        AssertEqual(false, ReviewRunner.ShouldUseCopilotPromptModeForTests(settings),
            "copilot prompt mode should be disabled when cliUrl is configured");
    }

    private static void TestCopilotCliSessionRequiresIdleSignalForCompletion() {
        var inactivityWindow = ReviewRunner.ResolveCopilotCliSessionCompletionInactivity(new ReviewSettings {
            IdleSeconds = 15
        });

        AssertEqual(false,
            ReviewRunner.ShouldCompleteCopilotSessionWithoutIdle(
                "## Summary",
                string.Empty,
                inactivityWindow,
                inactivityWindow),
            "copilot cli session should not complete after silence without idle");

        AssertEqual(false,
            ReviewRunner.ShouldCompleteCopilotSessionWithoutIdle(
                null,
                "partial delta",
                inactivityWindow,
                inactivityWindow),
            "copilot cli session should not complete from silence with delta-only content");

        AssertEqual(false,
            ReviewRunner.ShouldCompleteCopilotSessionWithoutIdle(
                null,
                string.Empty,
                inactivityWindow,
                inactivityWindow),
            "copilot cli session should not complete from silence without any content");

        AssertEqual(false,
            ReviewRunner.ShouldCompleteCopilotSessionWithoutIdle(
                "## Summary",
                string.Empty,
                TimeSpan.FromSeconds(2),
                inactivityWindow),
            "copilot cli session should wait for the inactivity window");
    }

    private static void TestCopilotInstallResolverFindsPlatformInstall() {
        var previousHome = Environment.GetEnvironmentVariable("HOME");
        var previousUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-copilot-install-" + Guid.NewGuid().ToString("N"));
        var isWindows = OperatingSystem.IsWindows();
        var installDir = isWindows
            ? Path.Combine(tempDir, "AppData", "Local", "Programs", "GitHub Copilot")
            : Path.Combine(tempDir, ".local", "bin");
        Directory.CreateDirectory(installDir);
        var cliPath = Path.Combine(installDir, isWindows ? "copilot.exe" : "copilot");

        try {
            File.WriteAllText(cliPath, "#!/bin/sh\nexit 0\n");
            Environment.SetEnvironmentVariable("HOME", tempDir);
            Environment.SetEnvironmentVariable("USERPROFILE", tempDir);

            AssertEqual(cliPath, CopilotCliInstall.TryResolveInstalledCliPath("copilot") ?? string.Empty,
                "copilot install resolver should find platform install");
        } finally {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Environment.SetEnvironmentVariable("USERPROFILE", previousUserProfile);
            DeleteDirectoryIfExistsWithRetries(tempDir);
        }
    }

    private static void TestCopilotPromptRunnerRejectsMissingConfiguredCliPath() {
        var previousHome = Environment.GetEnvironmentVariable("HOME");
        var previousUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-copilot-missing-path-" + Guid.NewGuid().ToString("N"));
        var isWindows = OperatingSystem.IsWindows();
        var installDir = isWindows
            ? Path.Combine(tempDir, "AppData", "Local", "Programs", "GitHub Copilot")
            : Path.Combine(tempDir, ".local", "bin");
        Directory.CreateDirectory(installDir);
        var installedCliPath = Path.Combine(installDir, isWindows ? "copilot.exe" : "copilot");
        var missingCliPath = Path.Combine(tempDir, "missing", isWindows ? "copilot.exe" : "copilot");

        try {
            File.WriteAllText(installedCliPath, "#!/bin/sh\nexit 0\n");
            Environment.SetEnvironmentVariable("HOME", tempDir);
            Environment.SetEnvironmentVariable("USERPROFILE", tempDir);

            InvalidOperationException? ex = null;
            try {
                ReviewerCopilotPromptRunner.ResolveCliPathForTests(missingCliPath);
            } catch (InvalidOperationException caught) {
                ex = caught;
            }
            if (ex is null) {
                throw new InvalidOperationException(
                    "Expected copilot prompt missing configured cli path to throw InvalidOperationException.");
            }

            AssertContainsText(ex.Message, missingCliPath,
                "copilot prompt missing configured cli path should mention configured value");
            AssertEqual(false, ex.Message.Contains(installedCliPath, StringComparison.OrdinalIgnoreCase),
                "copilot prompt missing configured cli path should not fall back to installed binary");
        } finally {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Environment.SetEnvironmentVariable("USERPROFILE", previousUserProfile);
            DeleteDirectoryIfExistsWithRetries(tempDir);
        }
    }

    private static void TestCopilotPromptRunnerParsesJsonOutput() {
        var output = string.Join("\n", new[] {
            """{"type":"assistant.message_delta","data":{"deltaContent":"Hel"}}""",
            """{"type":"assistant.message_delta","data":{"deltaContent":"lo"}}""",
            """{"type":"assistant.message","data":{"content":"Final review"}}""",
            """{"type":"result","usage":{"premiumRequests":1,"totalApiDurationMs":2500,"sessionDurationMs":3000}}"""
        });

        var result = ReviewerCopilotPromptRunner.ParseJsonLinesForTests(output);

        AssertEqual("Final review", result.Response, "copilot prompt final message");
        AssertContainsText(result.UsageSummary ?? string.Empty, "premium requests: 1", "copilot prompt usage premium");
        AssertContainsText(result.UsageSummary ?? string.Empty, "API: 2500 ms", "copilot prompt usage api");
    }

    private static void TestCopilotPromptRunnerParsesConcatenatedJsonOutput() {
        var output =
            """{"type":"assistant.message_delta","data":{"deltaContent":"Hel"}}{"type":"assistant.message","data":{"content":"## Summary\n\nFinal review"}}{"type":"result","usage":{"premiumRequests":1}}""";

        var result = ReviewerCopilotPromptRunner.ParseJsonLinesForTests(output);

        AssertEqual("## Summary\n\nFinal review", result.Response, "copilot prompt concatenated final message");
        AssertContainsText(result.UsageSummary ?? string.Empty, "premium requests: 1",
            "copilot prompt concatenated usage");
    }

    private static void TestCopilotPromptRunnerFallsBackToStdoutWhenJsonSharesLineWithNoise() {
        var output =
            """prefix noise {"type":"assistant.message","data":{"content":"Should stay embedded"}} trailing noise""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts mixed stdout line as plain text");
        AssertEqual(output, result?.Response ?? string.Empty,
            "copilot prompt should preserve mixed stdout instead of extracting embedded JSON");
    }

    private static void TestCopilotPromptRunnerFallsBackToStdoutWhenJsonHasTrailingNoise() {
        var output =
            """{"type":"assistant.message","data":{"content":"Should stay embedded"}} trailing noise""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts JSON with trailing noise as plain text");
        AssertEqual(output, result?.Response ?? string.Empty,
            "copilot prompt should preserve JSON-plus-noise stdout instead of extracting embedded JSON");
    }

    private static void TestCopilotPromptRunnerFallsBackToStdoutWhenJsonIsValidButNoAssistantMessageWasParsed() {
        var output = """
Review completed successfully.
Embedded JSON for reference:
{"config":{"provider":"copilot","model":"gpt-5.4"}}
No structured assistant event was emitted.
""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts plain-text stdout when JSON is valid");
        AssertContainsText(result?.Response ?? string.Empty, "Review completed successfully.",
            "copilot prompt preserves plain-text stdout");
        AssertContainsText(result?.Response ?? string.Empty, "\"model\":\"gpt-5.4\"",
            "copilot prompt keeps embedded JSON snippet in stdout fallback");
    }

    private static void TestCopilotPromptRunnerRejectsMalformedJsonWithoutAssistantMessage() {
        var output = """
{"type":"assistant.message_delta","data":{"deltaContent":"partial"
""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(false, accepted, "copilot prompt rejects malformed stdout fallback");
        AssertEqual(null, result, "copilot prompt malformed stdout fallback result");
    }

    private static void TestCopilotPromptRunnerFallsBackToStdoutWhenBraceNoisePreventsJsonParsing() {
        var output = """
Review completed successfully.
Compatibility note: {not actually json
Please keep the review text as plain stdout.
""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts stdout with brace-heavy noise");
        AssertContainsText(result?.Response ?? string.Empty, "Review completed successfully.",
            "copilot prompt keeps prose when brace noise appears");
        AssertContainsText(result?.Response ?? string.Empty, "Compatibility note:",
            "copilot prompt preserves malformed brace snippet in stdout fallback");
    }

    private static void TestCopilotPromptRunnerIgnoresBraceNoiseBeforeValidJsonLine() {
        var output = """
Compatibility note: {not actually json
{"type":"assistant.message","data":{"content":"Final review"}}
{"type":"result","usage":{"premiumRequests":1}}
""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts valid JSON after prose noise");
        AssertEqual("Final review", result?.Response ?? string.Empty,
            "copilot prompt should prefer assistant message from valid JSON line");
        AssertContainsText(result?.UsageSummary ?? string.Empty, "premium requests: 1",
            "copilot prompt keeps usage from valid JSON line");
    }

    private static void TestCopilotPromptRunnerFallsBackToStdoutWhenBraceLineStartsWithNonJsonToken() {
        var output = """
{warning} cached tool output follows
Review completed successfully.
""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts brace-prefixed prose as plain stdout");
        AssertContainsText(result?.Response ?? string.Empty, "{warning}",
            "copilot prompt should preserve brace-prefixed prose");
        AssertContainsText(result?.Response ?? string.Empty, "Review completed successfully.",
            "copilot prompt keeps plain stdout fallback");
    }

    private static void TestCopilotPromptRunnerDoesNotTreatJsonWarningsAsReviewContent() {
        var output = """{"type":"session.info","data":{"infoType":"configuration","message":"Unknown tool name in the tool allowlist: \"none\""}}""";

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            output,
            string.Empty,
            out var result);

        AssertEqual(false, accepted, "copilot prompt should not treat warning-only JSON as review content");
        AssertEqual(null, result, "copilot prompt warning-only JSON result");
    }

    private static void TestCopilotPromptRunnerBuildsMcpDisabledArgs() {
        var cliPath = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory) ?? Directory.GetCurrentDirectory(),
            "copilot");
        var binaryOptions = new IntelligenceX.Copilot.CopilotClientOptions();
        var binaryArgs = ReviewerCopilotPromptRunner.BuildArgumentsForTests(binaryOptions, cliPath, "review prompt");

        AssertContainsText(string.Join("\n", binaryArgs), "--disable-builtin-mcps",
            "copilot binary prompt disables built-in MCPs");
        AssertContainsText(string.Join("\n", binaryArgs), "--available-tools=none",
            "copilot binary prompt disables tool surface");
        AssertContainsText(string.Join("\n", binaryArgs), "--log-dir",
            "copilot binary prompt captures CLI logs");

        var fallbackArgs = ReviewerCopilotPromptRunner.BuildArgumentsForTests(binaryOptions, cliPath, "review prompt",
            disableBuiltinMcps: false);
        AssertEqual(false, fallbackArgs.Contains("--disable-builtin-mcps"),
            "copilot prompt fallback omits built-in MCP flag");

        var legacyFallbackArgs = ReviewerCopilotPromptRunner.BuildArgumentsForTests(binaryOptions, cliPath, "review prompt",
            disableBuiltinMcps: false, disableToolSurface: false);
        AssertEqual(false, legacyFallbackArgs.Contains("--disable-builtin-mcps"),
            "copilot prompt legacy fallback omits built-in MCP flag");
        AssertEqual(false, legacyFallbackArgs.Contains("--available-tools=none"),
            "copilot prompt legacy fallback omits available-tools flag");
        var noLogArgs = ReviewerCopilotPromptRunner.BuildArgumentsForTests(binaryOptions, cliPath, "review prompt",
            captureLogs: false);
        AssertEqual(false, noLogArgs.Contains("--log-dir"), "copilot prompt log fallback omits log dir flag");
        AssertEqual(false, noLogArgs.Contains("--log-level"), "copilot prompt log fallback omits log level flag");
        var promptArgArgs = ReviewerCopilotPromptRunner.BuildArgumentsForTests(binaryOptions, cliPath, "review prompt",
            usePromptArgument: true);
        AssertContainsText(string.Join("\n", promptArgArgs), "-p",
            "copilot prompt argument fallback uses -p");
        AssertContainsText(string.Join("\n", promptArgArgs), "review prompt",
            "copilot prompt argument fallback includes prompt text");

        var ghOptions = new IntelligenceX.Copilot.CopilotClientOptions();
        ghOptions.CliArgs.Add("copilot");
        ghOptions.CliArgs.Add("--");
        var ghArgs = ReviewerCopilotPromptRunner.BuildArgumentsForTests(ghOptions, cliPath, "review prompt");

        AssertSequenceEqual(new[] {
            "copilot",
            "--",
            "--silent",
            "--no-ask-user",
            "--no-custom-instructions",
            "--no-auto-update",
            "--log-dir",
            Path.Combine(Environment.CurrentDirectory, "artifacts", "copilot-logs"),
            "--log-level",
            "info",
            "--available-tools=none",
            "--disable-builtin-mcps",
            "--stream",
            "on",
            "--output-format",
            "json"
        }, ghArgs, "copilot gh prompt args");
        AssertEqual(false, ghArgs.Contains("review prompt"),
            "copilot prompt args should not inline prompt content");
    }

    private static void TestCopilotPromptRunnerRetriesPromptArgumentWhenStdinProducesNoReview() {
        AssertEqual(true, ReviewerCopilotPromptRunner.ShouldRetryWithAlternatePromptTransportForTests(
                0,
                string.Empty,
                string.Empty),
            "copilot prompt should retry alternate transport when the first run produces no output");
        AssertEqual(false, ReviewerCopilotPromptRunner.ShouldRetryWithAlternatePromptTransportForTests(
                0,
                "Compatibility note: review completed with no structured output.",
                string.Empty),
            "copilot prompt should keep successful prose fallback instead of retrying transports");
        AssertEqual(false, ReviewerCopilotPromptRunner.ShouldRetryWithAlternatePromptTransportForTests(
                0,
                """
{"type":"assistant.message","data":{"content":"Final review"}}
""",
                string.Empty),
            "copilot prompt should not retry transports when the first run already succeeded");
        AssertEqual(false, ReviewerCopilotPromptRunner.ShouldRetryWithAlternatePromptTransportForTests(
                0,
                """
{"type":"assistant.message_delta","data":{"deltaContent":"partial"
""",
                string.Empty),
            "copilot prompt should not hide malformed JSON behind transport retry");
    }

    private static void TestCopilotPromptRunnerWrapsRootedWindowsCmdPaths() {
        var cliPath = OperatingSystem.IsWindows()
            ? @"C:\Tools\copilot.cmd"
            : "/tmp/copilot";

        var resolved = ReviewerCopilotPromptRunner.ResolveCliCommandForTests(cliPath, "-p", "review prompt");

        if (OperatingSystem.IsWindows()) {
            AssertEqual("cmd", resolved.FileName, "copilot prompt rooted cmd wrapper filename");
            AssertSequenceEqual(new[] { "/c", cliPath, "-p", "review prompt" }, resolved.Args,
                "copilot prompt rooted cmd wrapper args");
            return;
        }

        AssertEqual(cliPath, resolved.FileName, "copilot prompt rooted unix path filename");
        AssertSequenceEqual(new[] { "-p", "review prompt" }, resolved.Args,
            "copilot prompt rooted unix path args");
    }

    private static void TestCopilotPromptRunnerDetectsUnsupportedMcpFlag() {
        AssertEqual(true, ReviewerCopilotPromptRunner.IsUnsupportedDisableBuiltinMcpsFlagForTests(
            string.Empty, "error: unknown option '--disable-builtin-mcps'"),
            "copilot prompt detects unknown MCP flag");
        AssertEqual(false, ReviewerCopilotPromptRunner.IsUnsupportedDisableBuiltinMcpsFlagForTests(
            string.Empty, "error: unknown option '--other-flag'"),
            "copilot prompt ignores unrelated unknown flag");
        AssertEqual(true, ReviewerCopilotPromptRunner.IsUnsupportedAvailableToolsFlagForTests(
            string.Empty, "error: unknown option '--available-tools'"),
            "copilot prompt detects unknown available-tools flag");
        AssertEqual(true, ReviewerCopilotPromptRunner.IsUnsupportedAvailableToolsFlagForTests(
            string.Empty, "error: unrecognized option '--available-tools=none'"),
            "copilot prompt detects unknown available-tools value flag");
        AssertEqual(true, ReviewerCopilotPromptRunner.IsUnsupportedAvailableToolsFlagForTests(
            string.Empty, "Unknown tool name in the tool allowlist: \"none\""),
            "copilot prompt treats unknown none tool name as unsupported available-tools");
        AssertEqual(true, ReviewerCopilotPromptRunner.IsUnsupportedLogCaptureFlagForTests(
            string.Empty, "error: unknown option '--log-dir'"),
            "copilot prompt detects unknown log dir flag");
        AssertEqual(true, ReviewerCopilotPromptRunner.IsUnsupportedLogCaptureFlagForTests(
            string.Empty, "error: unrecognized option '--log-level'"),
            "copilot prompt detects unknown log level flag");
        AssertEqual(false, ReviewerCopilotPromptRunner.IsUnsupportedLogCaptureFlagForTests(
            string.Empty, "error: unknown option '--available-tools'"),
            "copilot prompt ignores unrelated unknown flag for log capture");
    }

    private static void TestCopilotPromptRunnerAcceptsSuccessfulWarningsWithoutRetry() {
        var stdout = string.Join("\n", new[] {
            """{"type":"session.info","data":{"infoType":"configuration","message":"Unknown tool name in the tool allowlist: \"none\""}}""",
            """{"type":"assistant.message","data":{"content":"## Summary\n\nReview completed"}}""",
            """{"type":"result","usage":{"premiumRequests":1}}"""
        });

        var accepted = ReviewerCopilotPromptRunner.TryBuildSuccessfulResultForTests(
            0,
            stdout,
            string.Empty,
            out var result);

        AssertEqual(true, accepted, "copilot prompt accepts successful response even with compatibility warning");
        AssertEqual("## Summary\n\nReview completed", result?.Response ?? string.Empty,
            "copilot prompt preserves successful response when warning is present");

        var fallback = ReviewerCopilotPromptRunner.ApplyCompatibilityFallbacksForTests(
            1,
            stdout,
            string.Empty,
            disableBuiltinMcps: true,
            disableToolSurface: true,
            captureLogs: true);

        AssertEqual(true, fallback.Retry, "copilot prompt retries only when warning appears on unsuccessful run");
        AssertEqual(true, fallback.DisableBuiltinMcps, "copilot prompt keeps MCP flag when unrelated");
        AssertEqual(false, fallback.DisableToolSurface, "copilot prompt drops unsupported tool surface flag");
        AssertEqual(true, fallback.CaptureLogs, "copilot prompt keeps log capture when unrelated");
    }

    private static void TestCopilotPromptRunnerRecoversExitedProcessResultAfterPromptWriteFailure() {
        var recovered = ReviewerCopilotPromptRunner.TryCreateExitedProcessResultForTests(
            hasExited: true,
            exitCode: 1,
            stdout: string.Empty,
            stderr: "error: unknown option '--available-tools=none'",
            out var result);
        var recoveredResult = result ?? default;

        AssertEqual(true, recovered, "copilot prompt should recover exited process result after write failure");
        AssertEqual(1, recoveredResult.ExitCode, "copilot prompt recovered exit code");
        AssertContainsText(recoveredResult.Stderr, "--available-tools=none",
            "copilot prompt recovered stderr preserves compatibility detail");

        var fallback = ReviewerCopilotPromptRunner.ApplyCompatibilityFallbacksForTests(
            recoveredResult.ExitCode,
            recoveredResult.Stdout,
            recoveredResult.Stderr,
            disableBuiltinMcps: true,
            disableToolSurface: true,
            captureLogs: true);

        AssertEqual(true, fallback.Retry,
            "copilot prompt recovered result should still trigger compatibility retry");
        AssertEqual(false, fallback.DisableToolSurface,
            "copilot prompt recovered result should drop unsupported tool surface flag");
    }

    private static void TestCopilotPromptRunnerRequiresActionsCopilotToken() {
        var options = new IntelligenceX.Copilot.CopilotClientOptions();
        var actionsEnvironment = new Dictionary<string, string?> {
            ["GITHUB_ACTIONS"] = "true",
            ["COPILOT_GITHUB_TOKEN"] = null,
            ["GH_TOKEN"] = null,
            ["COPILOT_PROVIDER_BASE_URL"] = null,
            ["COPILOT_PROVIDER_API_KEY"] = null,
            ["GITHUB_TOKEN"] = "ghs_installation_token"
        };

        var missing = ReviewerCopilotPromptRunner.ValidateGitHubActionsAuthForTests(options, actionsEnvironment);

        AssertContainsText(missing ?? string.Empty, "COPILOT_GITHUB_TOKEN",
            "copilot prompt requires a Copilot-specific token in Actions");

        actionsEnvironment["COPILOT_GITHUB_TOKEN"] = "github_pat_supported";
        var supported = ReviewerCopilotPromptRunner.ValidateGitHubActionsAuthForTests(options, actionsEnvironment);

        AssertEqual(null, supported, "copilot prompt accepts fine-grained PAT token in Actions");

        actionsEnvironment["COPILOT_GITHUB_TOKEN"] = null;
        actionsEnvironment["COPILOT_PROVIDER_BASE_URL"] = "https://models.example.test";
        var byok = ReviewerCopilotPromptRunner.ValidateGitHubActionsAuthForTests(options, actionsEnvironment);

        AssertEqual(null, byok, "copilot prompt allows BYOK provider override without GitHub Copilot token");

        var isolatedOptions = new IntelligenceX.Copilot.CopilotClientOptions {
            InheritEnvironment = false
        };
        var isolatedMissing = ReviewerCopilotPromptRunner.ValidateGitHubActionsAuthForTests(isolatedOptions,
            new Dictionary<string, string?> {
                ["GITHUB_ACTIONS"] = "true",
                ["COPILOT_GITHUB_TOKEN"] = "github_pat_not_forwarded"
            });

        AssertContainsText(isolatedMissing ?? string.Empty, "COPILOT_GITHUB_TOKEN",
            "copilot prompt ignores host token when child env inheritance is disabled");

        isolatedOptions.Environment["COPILOT_GITHUB_TOKEN"] = "github_pat_forwarded";
        isolatedOptions.Environment["GITHUB_ACTIONS"] = "true";
        var isolatedForwarded = ReviewerCopilotPromptRunner.ValidateGitHubActionsAuthForTests(isolatedOptions,
            actionsEnvironment);

        AssertEqual(null, isolatedForwarded, "copilot prompt accepts explicitly forwarded token when env is isolated");
    }

    private static void TestCopilotPromptRunnerWriteHonorsTimeout() {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows()) {
            startInfo = new ProcessStartInfo {
                FileName = "powershell",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Start-Sleep -Seconds 60");
        } else {
            startInfo = new ProcessStartInfo {
                FileName = "bash",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add("sleep 60");
        }

        var prompt = new string('x', 1_000_000);
        TimeoutException? timeout = null;
        try {
            ReviewerCopilotPromptRunner.RunProcessForTests(startInfo, prompt, TimeSpan.FromMilliseconds(250))
                .GetAwaiter()
                .GetResult();
        } catch (TimeoutException ex) {
            timeout = ex;
        }

        if (timeout is null) {
            throw new InvalidOperationException("Expected copilot prompt write timeout to throw TimeoutException.");
        }

        AssertContainsText(timeout.Message, "timed out", "copilot prompt write timeout should mention timeout");
    }

    private static void TestCopilotPromptStartFailureKeepsCauseDetails() {
        var exception = new InvalidOperationException(
            "Copilot CLI not found or failed to start in prompt mode. File: /home/runner/.local/bin/copilot. Working directory: /repo. Cause: Argument list too long.");

        AssertEqual(true, ReviewRunner.ShouldFallbackFromCopilotPromptFailure(exception),
            "copilot prompt start failure with extra cause detail should still trigger fallback");
        AssertContainsText(exception.Message, "Argument list too long",
            "copilot prompt start failure should preserve the underlying cause");
    }

    private static void TestCopilotGhLauncherBuildsWrapperCommand() {
        var settings = new ReviewSettings {
            CopilotLauncher = "gh",
            CopilotCliPath = "custom-copilot"
        };
        var options = new ReviewRunner(settings).BuildCopilotClientOptionsForTests();

        AssertEqual("gh", options.CliPath ?? string.Empty, "copilot gh launcher path");
        AssertSequenceEqual(new[] { "copilot", "--" }, options.CliArgs.ToArray(), "copilot gh launcher args");
    }

    private static void TestCopilotAutoLauncherUsesBinary() {
        var settings = new ReviewSettings {
            CopilotLauncher = "auto"
        };

        var resolved = ReviewRunner.ResolveCopilotLauncherForTests(settings);

        AssertEqual("binary", resolved, "copilot auto launcher uses standalone binary path");
    }

    private static void TestCopilotAutoLauncherKeepsGhWrapperExplicit() {
        var settings = new ReviewSettings {
            CopilotLauncher = "auto"
        };

        var resolved = ReviewRunner.ResolveCopilotLauncherForTests(settings);

        AssertEqual("binary", resolved, "copilot auto launcher keeps gh wrapper as explicit opt-in");
    }

    private static void TestCopilotAutoLauncherUsesBinaryForAutoInstall() {
        var settings = new ReviewSettings {
            CopilotLauncher = "auto",
            CopilotAutoInstall = true
        };

        var resolved = ReviewRunner.ResolveCopilotLauncherForTests(settings);

        AssertEqual("binary", resolved, "copilot auto launcher lets missing binary be resolved by auto-install");
    }

    private static void TestCopilotCliAutoInstallDefaultsPreferLinuxScript() {
        var command = IntelligenceX.Copilot.CopilotCliInstall.GetDefaultCommandForPlatform(
            isWindows: false,
            isMac: false,
            isLinux: true);

        AssertEqual(IntelligenceX.Copilot.CopilotCliInstallMethod.Script, command.Method,
            "copilot linux auto install default");
        AssertEqual("bash", command.FileName, "copilot linux auto install file");
    }

    private static void TestCopilotCliAutoInstallDefaultsHonorLinuxPrerelease() {
        var command = IntelligenceX.Copilot.CopilotCliInstall.GetDefaultCommandForPlatform(
            isWindows: false,
            isMac: false,
            isLinux: true,
            prerelease: true);

        AssertEqual(IntelligenceX.Copilot.CopilotCliInstallMethod.Npm, command.Method,
            "copilot linux prerelease auto install default");
        AssertContainsText(command.Arguments, "@github/copilot@prerelease",
            "copilot linux prerelease auto install package");
    }

    private static void TestCopilotCliAutoInstallDefaultsKeepMacHomebrew() {
        var command = IntelligenceX.Copilot.CopilotCliInstall.GetDefaultCommandForPlatform(
            isWindows: false,
            isMac: true,
            isLinux: false);

        AssertEqual(IntelligenceX.Copilot.CopilotCliInstallMethod.Homebrew, command.Method,
            "copilot mac auto install default");
    }

    private static void TestCopilotLauncherDiagnosticsDescribeResolvedCommand() {
        var originalError = Console.Error;
        using var errorWriter = new StringWriter();
        try {
            Console.SetError(errorWriter);
            var settings = new ReviewSettings {
                CopilotLauncher = "gh",
                Diagnostics = true
            };

            _ = new ReviewRunner(settings).BuildCopilotClientOptionsForTests();
        } finally {
            Console.SetError(originalError);
        }

        var output = errorWriter.ToString();
        AssertContainsText(output, "Copilot launcher resolved: gh", "copilot launcher diagnostic mode");
        AssertContainsText(output, "cliPath=gh", "copilot launcher diagnostic path");
        AssertContainsText(output, "prefixArgs=copilot --", "copilot launcher diagnostic wrapper args");
    }

    private static void TestCopilotBinaryLauncherKeepsDirectCliPath() {
        var settings = new ReviewSettings {
            CopilotLauncher = "binary",
            CopilotCliPath = "custom-copilot"
        };
        var options = new ReviewRunner(settings).BuildCopilotClientOptionsForTests();

        AssertEqual("custom-copilot", options.CliPath ?? string.Empty, "copilot binary launcher path");
        AssertEqual(0, options.CliArgs.Count, "copilot binary launcher args");
    }

    private static void TestCopilotClientWrapsRootedWindowsCmdPaths() {
        var cliPath = OperatingSystem.IsWindows()
            ? @"C:\Tools\copilot.cmd"
            : "/tmp/copilot";

        var resolved = CopilotClient.ResolveCliCommandForTests(cliPath, "--server", "--log-level", "info");

        if (OperatingSystem.IsWindows()) {
            AssertEqual("cmd", resolved.FileName, "copilot client rooted cmd wrapper filename");
            AssertSequenceEqual(new[] { "/c", cliPath, "--server", "--log-level", "info" }, resolved.Args,
                "copilot client rooted cmd wrapper args");
            return;
        }

        AssertEqual(cliPath, resolved.FileName, "copilot client rooted unix path filename");
        AssertSequenceEqual(new[] { "--server", "--log-level", "info" }, resolved.Args,
            "copilot client rooted unix path args");
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
