namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestResolveThreadsOptionParsing() {
        var options = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ParseOptions(new[] {
            "--repo", "owner/name",
            "--pr", "42",
            "--timeout-seconds", "15",
            "--include-human",
            "--include-current",
            "--bot", "intelligencex-review,copilot-pull-request-reviewer"
        });

        AssertEqual("owner/name", options.Repo ?? string.Empty, "repo parse");
        AssertEqual(42, options.PrNumber, "pr parse");
        AssertEqual(15, options.TimeoutSeconds, "timeout parse");
        AssertEqual(false, options.BotOnly, "include human");
        AssertEqual(false, options.OnlyOutdated, "include current");
        AssertEqual(2, options.BotLogins.Count, "bot logins count");
        AssertEqual("intelligencex-review", options.BotLogins[0], "bot login 1");
        AssertEqual("copilot-pull-request-reviewer", options.BotLogins[1], "bot login 2");
    }

    private static void TestOpenAiAccountOrderRoundRobin() {
        var firstRun = CallOrderOpenAiAccounts(
            new[] { "acc-1", "acc-2", "acc-3" },
            rotation: "round-robin",
            stickyAccountId: null,
            rotationSeed: 1);
        AssertSequenceEqual(new[] { "acc-1", "acc-2", "acc-3" }, firstRun.ToArray(),
            "openai account order round-robin first run");

        var secondRun = CallOrderOpenAiAccounts(
            new[] { "acc-1", "acc-2", "acc-3" },
            rotation: "round-robin",
            stickyAccountId: null,
            rotationSeed: 2);
        AssertSequenceEqual(new[] { "acc-2", "acc-3", "acc-1" }, secondRun.ToArray(),
            "openai account order round-robin second run");
    }

    private static void TestOpenAiAccountOrderSticky() {
        var ordered = CallOrderOpenAiAccounts(
            new[] { "acc-1", "acc-2", "acc-3" },
            rotation: "sticky",
            stickyAccountId: "acc-3",
            rotationSeed: 0);
        AssertSequenceEqual(new[] { "acc-3", "acc-1", "acc-2" }, ordered.ToArray(), "openai account order sticky");
    }

    private static void TestNormalizeAccountIdListDedupesCaseInsensitive() {
        var normalized = ReviewSettings.NormalizeAccountIdList(new[] {
            "acc-1",
            "ACC-1",
            " acc-2 ",
            "Acc-2"
        });
        AssertSequenceEqual(new[] { "acc-1", "acc-2" }, normalized.ToArray(), "normalize account id list dedupe");
    }

    private static void TestTryResolveOpenAiAccountStoresRotatedOrder() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ix-openai-accounts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth-store.json");
        var previousAuthPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        var previousRunNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");

        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", authPath);
            Environment.SetEnvironmentVariable("GITHUB_RUN_NUMBER", "2");

            var store = new IntelligenceX.OpenAI.Auth.FileAuthBundleStore(authPath);
            store.SaveAsync(new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "token-1", "refresh-1",
                    DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = "acc-1"
                }).GetAwaiter().GetResult();
            store.SaveAsync(new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "token-2", "refresh-2",
                    DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = "acc-2"
                }).GetAwaiter().GetResult();
            store.SaveAsync(new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "token-3", "refresh-3",
                    DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = "acc-3"
                }).GetAwaiter().GetResult();

            var settings = new ReviewSettings {
                Provider = ReviewProvider.OpenAI,
                OpenAiAccountIds = new[] { "acc-1", "acc-2", "acc-3" },
                OpenAiAccountRotation = "round-robin",
                ReviewUsageBudgetGuard = false
            };

            var result = CallTryResolveOpenAiAccount(settings);
            AssertEqual(true, result.Success, "try resolve openai account success");
            AssertEqual("acc-2", settings.OpenAiAccountId, "try resolve openai account selected");
            AssertSequenceEqual(new[] { "acc-2", "acc-3", "acc-1" }, settings.OpenAiAccountIds.ToArray(),
                "try resolve openai account rotated order");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", previousAuthPath);
            Environment.SetEnvironmentVariable("GITHUB_RUN_NUMBER", previousRunNumber);
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    private static void TestTryResolveOpenAiAccountPrefersExplicitPrimaryOverIdsList() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ix-openai-primary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth-store.json");
        var previousAuthPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");

        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", authPath);
            var store = new IntelligenceX.OpenAI.Auth.FileAuthBundleStore(authPath);
            store.SaveAsync(new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "token-1", "refresh-1",
                    DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = "acc-1"
                }).GetAwaiter().GetResult();
            store.SaveAsync(new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "token-2", "refresh-2",
                    DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = "acc-2"
                }).GetAwaiter().GetResult();
            store.SaveAsync(new IntelligenceX.OpenAI.Auth.AuthBundle("openai-codex", "token-3", "refresh-3",
                    DateTimeOffset.UtcNow.AddHours(1)) {
                    AccountId = "acc-3"
                }).GetAwaiter().GetResult();

            var settings = new ReviewSettings {
                Provider = ReviewProvider.OpenAI,
                OpenAiAccountId = "acc-3",
                OpenAiAccountIds = new[] { "acc-1", "acc-2" },
                OpenAiAccountRotation = "first-available",
                ReviewUsageBudgetGuard = false
            };

            var result = CallTryResolveOpenAiAccount(settings);
            AssertEqual(true, result.Success, "try resolve openai account explicit primary success");
            AssertEqual("acc-3", settings.OpenAiAccountId, "try resolve openai account explicit primary selected");
            AssertSequenceEqual(new[] { "acc-3", "acc-1", "acc-2" }, settings.OpenAiAccountIds.ToArray(),
                "try resolve openai account explicit primary candidate order");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", previousAuthPath);
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    private static void TestResolveThreadsEndpointResolution() {
        var (baseUri, graphQlPath) = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ResolveGraphQlEndpoint("https://github.company.local/api/v3");
        AssertEqual("https://github.company.local/api/v3", baseUri.ToString(), "base uri");
        AssertEqual("/api/graphql", graphQlPath, "graphql path");

        var (apiGraphBase, apiGraphPath) = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ResolveGraphQlEndpoint("https://github.company.local/api/graphql");
        AssertEqual("/api/graphql", apiGraphPath, "graphql path api/graphql");
        AssertEqual("https://github.company.local", apiGraphBase.GetLeftPart(UriPartial.Authority), "base uri api/graphql");

        var (rootGraphBase, rootGraphPath) = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ResolveGraphQlEndpoint("https://github.company.local/graphql");
        AssertEqual("/graphql", rootGraphPath, "graphql path root");
        AssertEqual("https://github.company.local", rootGraphBase.GetLeftPart(UriPartial.Authority), "base uri /graphql");

        var (defaultBase, defaultPath) = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ResolveGraphQlEndpoint("https://github.company.local");
        AssertEqual("/graphql", defaultPath, "graphql path default");
        AssertEqual("https://github.company.local", defaultBase.GetLeftPart(UriPartial.Authority), "base uri default");
    }

    private static void TestFilterFilesIncludeOnly() {
        var files = BuildFiles("src/app.cs", "docs/readme.md", "tests/test.cs");
        var filtered = ReviewerApp.FilterFilesByPaths(files, new[] { "src/**", "tests/*.cs" }, Array.Empty<string>());
        AssertSequenceEqual(new[] { "src/app.cs", "tests/test.cs" }, GetFilenames(filtered), "include-only");
    }

    private static void TestFilterFilesExcludeOnly() {
        var files = BuildFiles("src/app.cs", "docs/readme.md", "tests/test.cs");
        var filtered = ReviewerApp.FilterFilesByPaths(files, Array.Empty<string>(), new[] { "**/*.md" });
        AssertSequenceEqual(new[] { "src/app.cs", "tests/test.cs" }, GetFilenames(filtered), "exclude-only");
    }

    private static void TestFilterFilesIncludeExclude() {
        var files = BuildFiles("src/app.cs", "src/appTest.cs", "tests/test.cs");
        var filtered = ReviewerApp.FilterFilesByPaths(files, new[] { "**/*.cs" }, new[] { "**/*Test.cs", "tests/**" });
        AssertSequenceEqual(new[] { "src/app.cs" }, GetFilenames(filtered), "include+exclude");
    }

    private static void TestFilterFilesGlobPatterns() {
        var files = BuildFiles("docs/readme.md", "docs/nested/guide.md", "docs/notes.txt");
        var filteredSingle = ReviewerApp.FilterFilesByPaths(files, new[] { "docs/*.md" }, Array.Empty<string>());
        AssertSequenceEqual(new[] { "docs/readme.md" }, GetFilenames(filteredSingle), "glob single");

        var filteredDeep = ReviewerApp.FilterFilesByPaths(files, new[] { "docs/**/*.md" }, Array.Empty<string>());
        AssertSequenceEqual(new[] { "docs/nested/guide.md" }, GetFilenames(filteredDeep), "glob deep");

        var filteredAll = ReviewerApp.FilterFilesByPaths(files, new[] { "docs/*.md", "docs/**/*.md" }, Array.Empty<string>());
        AssertSequenceEqual(new[] { "docs/readme.md", "docs/nested/guide.md" }, GetFilenames(filteredAll), "glob combined");
    }

    private static void TestFilterFilesEmptyFilters() {
        var files = BuildFiles("src/app.cs", "docs/readme.md");
        var filtered = ReviewerApp.FilterFilesByPaths(files, Array.Empty<string>(), Array.Empty<string>());
        AssertSequenceEqual(new[] { "src/app.cs", "docs/readme.md" }, GetFilenames(filtered), "empty filters");
    }

    private static void TestFilterFilesSkipBinary() {
        var files = BuildFiles("src/app.cs", "assets/logo.png", "docs/readme.md");
        var filtered = ReviewerApp.FilterFilesByPaths(files, Array.Empty<string>(), Array.Empty<string>(),
            skipBinaryFiles: true, skipGeneratedFiles: false);
        AssertSequenceEqual(new[] { "src/app.cs", "docs/readme.md" }, GetFilenames(filtered), "skip binary");
    }

    private static void TestFilterFilesSkipBinaryCaseInsensitive() {
        var files = BuildFiles("src/app.cs", "assets/Logo.PNG");
        var filtered = ReviewerApp.FilterFilesByPaths(files, Array.Empty<string>(), Array.Empty<string>(),
            skipBinaryFiles: true, skipGeneratedFiles: false);
        AssertSequenceEqual(new[] { "src/app.cs" }, GetFilenames(filtered), "skip binary case-insensitive");
    }

    private static void TestFilterFilesSkipGenerated() {
        var files = BuildFiles("src/app.cs", "src/obj/Debug/net8.0/app.g.cs", "dist/app.min.js",
            "node_modules/lib/index.js", "src/Generated/Auto.generated.cs");
        var filtered = ReviewerApp.FilterFilesByPaths(files, Array.Empty<string>(), Array.Empty<string>(),
            skipBinaryFiles: false, skipGeneratedFiles: true);
        AssertSequenceEqual(new[] { "src/app.cs" }, GetFilenames(filtered), "skip generated");
    }

    private static void TestFilterFilesSkipBeforeInclude() {
        var files = BuildFiles("assets/logo.png", "src/app.cs", "obj/Debug/net8.0/app.g.cs");
        var filtered = ReviewerApp.FilterFilesByPaths(files, new[] { "**/*.png", "**/*.cs" }, Array.Empty<string>(),
            skipBinaryFiles: true, skipGeneratedFiles: true);
        AssertSequenceEqual(new[] { "src/app.cs" }, GetFilenames(filtered), "skip before include");
    }

    private static void TestFilterFilesGeneratedGlobsExtend() {
        var files = BuildFiles("snapshots/ui.snap", "src/app.cs");
        var filtered = ReviewerApp.FilterFilesByPaths(files, Array.Empty<string>(), Array.Empty<string>(),
            skipBinaryFiles: false, skipGeneratedFiles: true, generatedFileGlobs: new[] { "**/*.snap" });
        AssertSequenceEqual(new[] { "src/app.cs" }, GetFilenames(filtered), "generated globs extend");
    }

    private static void TestWorkflowChangesDetection() {
        var withWorkflow = BuildFiles(".github/workflows/ci.yml", "src/app.cs");
        AssertEqual(true, ReviewerApp.HasWorkflowChanges(withWorkflow), "workflow changes detected");

        var withWorkflowYaml = BuildFiles(".github/workflows/ci.yaml", "src/app.cs");
        AssertEqual(true, ReviewerApp.HasWorkflowChanges(withWorkflowYaml), "workflow changes detected yaml");

        var withoutWorkflow = BuildFiles(".github/workflows/README.md", "src/app.cs");
        AssertEqual(false, ReviewerApp.HasWorkflowChanges(withoutWorkflow), "workflow changes ignored");
    }

    private static void TestWorkflowChangesFiltering() {
        var files = BuildFiles(".github/workflows/ci.yml", "src/app.cs", ".github/workflows/release.yaml", "docs/readme.md");
        var filtered = ReviewerApp.ExcludeWorkflowFiles(files);

        AssertEqual(2, ReviewerApp.CountWorkflowFiles(files), "workflow file count");
        AssertSequenceEqual(new[] { "src/app.cs", "docs/readme.md" }, GetFilenames(filtered), "exclude workflow files");
    }

    private static void TestWorkflowGuardNoteSkip() {
        var note = ReviewerApp.BuildWorkflowGuardNote("1234567890abcdef", 2, 0, skipped: true);
        AssertContainsText(note, "Workflow-only changes detected", "workflow guard skip prefix");
        AssertContainsText(note, "Head SHA: 1234567890abcdef", "workflow guard skip sha");
        AssertContainsText(note, "Review skipped", "workflow guard skip action");
    }

    private static void TestWorkflowGuardNoteFiltered() {
        var note = ReviewerApp.BuildWorkflowGuardNote("abc1234", 1, 3, skipped: false);
        AssertContainsText(note, "excluded 1 workflow file", "workflow guard filtered count");
        AssertContainsText(note, "reviewed 3 non-workflow file(s)", "workflow guard filtered reviewed");
    }

    private static void TestSecretsAuditRecords() {
        SecretsAudit.Record("pending secret source");

        var settings = new ReviewSettings { SecretsAudit = true };
        var session = SecretsAudit.TryStart(settings);
        if (session is null) {
            throw new InvalidOperationException("Secrets audit session did not start.");
        }

        SecretsAudit.Record("active secret source");

        var entries = session.Entries;
        var hasPending = false;
        var hasActive = false;
        foreach (var entry in entries) {
            if (entry == "pending secret source") {
                hasPending = true;
            }
            if (entry == "active secret source") {
                hasActive = true;
            }
        }
        if (!hasPending || !hasActive) {
            throw new InvalidOperationException("Secrets audit entries were not recorded.");
        }

        session.Dispose();
    }

    private static void TestPromptBuilderLanguageHints() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs", "web/app.tsx");
        var settings = new ReviewSettings { IncludeLanguageHints = true };
        var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);
        AssertContainsText(prompt, "Language hints:", "language hints header");
        AssertContainsText(prompt, "C#", "language hints csharp");
    }

    private static void TestPromptBuilderLanguageHintsDisabled() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs", "web/app.tsx");
        var settings = new ReviewSettings { IncludeLanguageHints = false };
        var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);
        if (prompt.Contains("Language hints:", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected language hints to be omitted.");
        }
    }

    private static void TestRedactionDefaults() {
        var settings = new ReviewSettings { RedactPii = true };
        var input = "Authorization: Bearer abc123";
        var output = Redaction.Apply(input, settings.RedactionPatterns, settings.RedactionReplacement);
        AssertEqual(settings.RedactionReplacement, output, "redaction default match");
    }

    private static void TestReviewBudgetNote() {
        var note = ReviewerApp.BuildBudgetNote(10, 5, 2, 4000);
        AssertContainsText(note, "first 5 of 10 files", "budget note files");
        AssertContainsText(note, "2 patches trimmed to 4000 chars", "budget note patches");
        AssertContainsText(note, "review covers only included diff context", "budget note impact");
        AssertContainsText(note, "Increase review.maxFiles/review.maxPatchChars", "budget note guidance");
    }

    private static void TestReviewBudgetNoteEmpty() {
        var note = ReviewerApp.BuildBudgetNote(5, 5, 0, 4000);
        AssertEqual(string.Empty, note, "budget note empty");
    }

    private static void TestReviewBudgetNoteComment() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "head",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings();
        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: false, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: "Review context truncated: showing first 1 of 2 files.",
            usageLine: string.Empty, findingsBlock: string.Empty);
        AssertContainsText(comment, "Review context truncated", "budget note comment");
    }

    private static void TestCombineNotes() {
        var combined = ReviewerApp.CombineNotes("first note", "second note");
        AssertEqual("first note\nsecond note", combined, "combine notes two values");

        var firstOnly = ReviewerApp.CombineNotes("first note", string.Empty);
        AssertEqual("first note", firstOnly, "combine notes first only");

        var secondOnly = ReviewerApp.CombineNotes(string.Empty, "second note");
        AssertEqual("second note", secondOnly, "combine notes second only");
    }

    private static void TestReviewRetryBackoffMultiplierConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"retryBackoffMultiplier\": 1e309 } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(2.0, settings.RetryBackoffMultiplier, "retry backoff multiplier config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewRetryBackoffMultiplierEnv() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_RETRY_BACKOFF_MULTIPLIER");
        try {
            Environment.SetEnvironmentVariable("REVIEW_RETRY_BACKOFF_MULTIPLIER", "NaN");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(2.0, settings.RetryBackoffMultiplier, "retry backoff multiplier env");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_RETRY_BACKOFF_MULTIPLIER", previous);
        }
    }

    private static void TestPrepareFilesMaxFilesZero() {
        var files = BuildFiles("src/A.cs", "src/B.cs");
        var (limited, budgetNote) = CallPrepareFiles(files, 0, 4000);
        AssertEqual(2, limited.Count, "prepare files max files zero count");
        AssertEqual(string.Empty, budgetNote, "prepare files max files zero note");
    }

    private static void TestPrepareFilesMaxFilesNegative() {
        var files = BuildFiles("src/A.cs", "src/B.cs");
        var (limited, budgetNote) = CallPrepareFiles(files, -1, 4000);
        AssertEqual(2, limited.Count, "prepare files max files negative count");
        AssertEqual(string.Empty, budgetNote, "prepare files max files negative note");
    }

    private static void TestAzureDevOpsChangesPagination() {
        var project = "project";
        var repo = "repo";
        var prId = 42;

        var page1 = "{\"changes\":[{\"item\":{\"path\":\"/src/A.cs\"},\"changeType\":\"edit\"},{\"item\":{\"path\":\"/src/B.cs\"},\"changeType\":\"add\"}]}";
        var page2 = "{\"changes\":[{\"item\":{\"path\":\"/src/C.cs\"},\"changeType\":\"delete\"}]}";

        using var server = new LocalHttpServer(request => {
            if (!request.Path.StartsWith($"/{project}/_apis/git/repositories/{repo}/pullRequests/{prId}/changes",
                    StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            if (request.Path.Contains("continuationToken=token1", StringComparison.OrdinalIgnoreCase)) {
                return new HttpResponse(page2);
            }

            return new HttpResponse(page1, new Dictionary<string, string> {
                ["x-ms-continuationtoken"] = "token1"
            });
        });

        using var client = new AzureDevOpsClient(server.BaseUri, "token", AzureDevOpsAuthScheme.Bearer);
        var files = client.GetPullRequestChangesAsync(project, repo, prId, CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEqual(3, files.Count, "ado page count");
        AssertSequenceEqual(new[] { "src/A.cs", "src/B.cs", "src/C.cs" }, GetFilenames(files), "ado page files");
    }

    private static void TestAzureDevOpsDiffNoteZeroIterations() {
        var note = AzureDevOpsReviewRunner.BuildDiffNote(Array.Empty<int>());
        AssertEqual("pull request changes", note, "ado diff note zero");
    }

    private static void TestAzureDevOpsInlinePatchLineMapParsesAddedLines() {
        var patch = string.Join("\n", new[] {
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -1,2 +10,4 @@",
            " line1",
            "+added1",
            "+added2",
            " line2",
            "@@ -20,1 +99,2 @@",
            "+added3",
            " line3"
        });

        var result = AzureDevOpsReviewRunner.ParsePatchLines(patch);
        AssertEqual(true, result.Contains(11), "ado patch contains line 11");
        AssertEqual(true, result.Contains(12), "ado patch contains line 12");
        AssertEqual(true, result.Contains(99), "ado patch contains line 99");
    }

    private static void TestAzureDevOpsInlinePatchLineMapHandlesCrlfAndDeletions() {
        var patch = string.Join("\r\n", new[] {
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -1,3 +1,3 @@",
            "-line1",
            "+line1changed",
            " line2",
            " line3",
            "\\ No newline at end of file"
        });

        var result = AzureDevOpsReviewRunner.ParsePatchLines(patch);
        AssertEqual(true, result.Contains(1), "ado patch contains line 1");
        AssertEqual(false, result.Contains(2), "ado patch does not include context line 2");
        AssertEqual(false, result.Contains(3), "ado patch does not include context line 3");
    }

    private static void TestAzureDevOpsInlinePatchLineMapHandlesPlusPlusAndDashDashContent() {
        var patch = string.Join("\n", new[] {
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -1,1 +1,1 @@",
            // Removed line whose content begins with "--" => patch line begins with "---" (should not be treated as a header).
            "---bye",
            // Added line whose content begins with "++" => patch line begins with "+++" (should not be treated as a header).
            "+++hello"
        });

        var result = AzureDevOpsReviewRunner.ParsePatchLines(patch);
        AssertEqual(true, result.Contains(1), "ado patch includes +++ content line as added");
    }

    private static void TestAzureDevOpsInlineThreadContextUsesOneBasedLineAndZeroBasedOffset() {
        var project = "proj";
        var repo = "repo";
        var prId = 42;
        string capturedBody = string.Empty;

        using var server = new LocalHttpServer(request => {
            if (!request.Path.StartsWith($"/{project}/_apis/git/repositories/{repo}/pullRequests/{prId}/threads",
                    StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            capturedBody = request.Body;
            return new HttpResponse("{}");
        });

        using var client = new AzureDevOpsClient(server.BaseUri, "token", AzureDevOpsAuthScheme.Bearer);
        client.CreatePullRequestInlineThreadAsync(project, repo, prId, "src/A.cs", 5, "Hello", CancellationToken.None)
            .GetAwaiter().GetResult();

        var json = JsonLite.Parse(capturedBody);
        var obj = json?.AsObject();
        AssertNotNull(obj, "ado inline thread payload");

        var context = obj!.GetObject("threadContext");
        AssertNotNull(context, "ado threadContext");
        AssertEqual("/src/A.cs", context!.GetString("filePath"), "ado threadContext filePath");
        AssertEqual(5L, context.GetObject("rightFileStart")!.GetInt64("line"), "ado rightFileStart line");
        AssertEqual(0L, context.GetObject("rightFileStart")!.GetInt64("offset"), "ado rightFileStart offset");
        AssertEqual(5L, context.GetObject("rightFileEnd")!.GetInt64("line"), "ado rightFileEnd line");
        AssertEqual(0L, context.GetObject("rightFileEnd")!.GetInt64("offset"), "ado rightFileEnd offset");
    }

    private static void TestAzureDevOpsErrorSanitization() {
        var errorJson = "{\"message\":\"Authorization: Bearer abc123\"}";
        var sanitized = CallAzureDevOpsSanitize(errorJson);
        AssertContainsText(sanitized, "***", "sanitized token");
        if (sanitized.Contains("abc123", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected token value to be redacted.");
        }
    }

    private static PullRequestFile[] BuildFiles(params string[] paths) {
        var files = new PullRequestFile[paths.Length];
        for (var i = 0; i < paths.Length; i++) {
            files[i] = new PullRequestFile(paths[i], "modified", null);
        }
        return files;
    }

    private static PullRequestContext BuildContext() {
        return new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
    }

    private static string CallAzureDevOpsSanitize(string content) {
        var method = typeof(AzureDevOpsClient).GetMethod("SanitizeErrorContent", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("SanitizeErrorContent method not found.");
        }
        var result = method.Invoke(null, new object?[] { content }) as string;
        return result ?? string.Empty;
    }

    private static IReadOnlyList<string> GetFilenames(IReadOnlyList<PullRequestFile> files) {
        var names = new List<string>(files.Count);
        foreach (var file in files) {
            names.Add(file.Filename);
        }
        return names;
    }

    private static void TestContextDenyInvalidRegex() {
        var matched = ContextDenyMatcher.Matches("hello world", new[] { "[", "poem" });
        AssertEqual(false, matched, "invalid regex match");
        var matchedAllowed = ContextDenyMatcher.Matches("please write a poem", new[] { "poem" });
        AssertEqual(true, matchedAllowed, "valid regex match");
    }

    private static void TestContextDenyTimeout() {
        var input = new string('a', 20000) + "!";
        var matched = ContextDenyMatcher.Matches(input, new[] { "(a+)+$" });
        AssertEqual(false, matched, "timeout match");
    }

    private static void TestReviewSummaryParser() {
        var body = string.Join("\n", new[] {
            "## IntelligenceX Review",
            $"Reviewing PR #1: **Test**",
            $"{ReviewFormatter.ReviewedCommitMarker} `abc1234`",
            "",
            "Summary text"
        });

        var ok = ReviewSummaryParser.TryGetReviewedCommit(body, out var commit);
        AssertEqual(true, ok, "commit parse ok");
        AssertEqual("abc1234", commit, "commit value");

        var noBacktick = $"{ReviewFormatter.ReviewedCommitMarker} abc1234";
        ok = ReviewSummaryParser.TryGetReviewedCommit(noBacktick, out _);
        AssertEqual(false, ok, "no backtick");

        ok = ReviewSummaryParser.TryGetReviewedCommit("No marker here", out _);
        AssertEqual(false, ok, "missing marker");

        var malformedThenValid = string.Join("\n", new[] {
            $"{ReviewFormatter.ReviewedCommitMarker} abc1234",
            $"{ReviewFormatter.ReviewedCommitMarker} `deadbeef`"
        });
        ok = ReviewSummaryParser.TryGetReviewedCommit(malformedThenValid, out commit);
        AssertEqual(true, ok, "malformed then valid");
        AssertEqual("deadbeef", commit, "malformed then valid commit");
    }

    private static void TestReviewFormatterModelUsageSection() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "head",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Long,
            Mode = "inline"
        };

        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty,
            usageLine: "Usage: 5h limit: 90% remaining | credits: 4.52", findingsBlock: string.Empty);

        AssertContainsText(comment, "### Model & Usage 🤖", "model usage section header");
        AssertContainsText(comment, "- Model: `gpt-5-test`", "model usage model");
        AssertContainsText(comment, "- Usage: 5h limit: 90% remaining | credits: 4.52", "model usage line");
    }

    private static void TestReviewFormatterModelUsageUnavailable() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "head",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings();

        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);

        AssertContainsText(comment, "- Usage: unavailable", "model usage unavailable line");
    }

    private static void TestReviewFormatterGoldenSnapshot() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 42, "Formatter Golden Snapshot", "Body", false,
            "deadbeefcafebabe", "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var comment = ReviewFormatter.BuildComment(context, "Summary line.", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);
        MaybeUpdateReviewerFixture("review-summary-golden.md", comment);
        var expected = LoadReviewerFixture("review-summary-golden.md");
        AssertTextBlockEquals(expected, comment, "review formatter golden snapshot");
    }

    private static void TestReviewUsageIntegrationDisplay() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":25.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage integration json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var usageLine = CallFormatUsageSummary(snapshot);
        var context = BuildContext();
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Long,
            Mode = "inline"
        };
        var comment = ReviewFormatter.BuildComment(context, "Body", settings, inlineSupported: true, inlineSuppressed: false,
            autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: usageLine, findingsBlock: string.Empty);

        AssertContainsText(comment, "### Model & Usage 🤖", "usage integration section");
        AssertContainsText(comment, "- Usage: 5h limit: 87.5% remaining", "usage integration 5h");
        AssertContainsText(comment, "code review 5h limit: 75% remaining", "usage integration code review 5h");
    }

    private static void TestReviewUsageSummaryLine() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":25.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        AssertContainsText(line, "Usage:", "usage summary prefix");
        AssertContainsText(line, "5h limit", "usage window label");
        AssertEqual(false, line.IndexOf("\n", StringComparison.Ordinal) >= 0, "usage summary is single line");
    }

    private static void TestReviewUsageSummaryDisambiguatesCodeReviewWeekly() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":20.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120},"
            + "\"secondary_window\":{\"used_percent\":61.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":26.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary disambiguation json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(4, parts.Count, "usage part count weekly");
        AssertContains(parts, "weekly limit: 39% remaining", "weekly label");
        AssertContains(parts, "code review weekly limit: 74% remaining", "code review weekly label");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit: 74% remaining"), "plain duplicate weekly label removed");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit (secondary): 74% remaining"), "plain secondary weekly label removed");
    }

    private static void TestReviewUsageSummaryDisambiguatesCodeReviewWeeklySecondary() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"secondary_window\":{\"used_percent\":61.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"secondary_window\":{\"used_percent\":26.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":120}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary secondary disambiguation json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(3, parts.Count, "usage part count weekly secondary");
        AssertContains(parts, "weekly limit: 39% remaining", "weekly label secondary");
        AssertContains(parts, "code review weekly limit (secondary): 74% remaining", "code review weekly secondary label");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit (secondary): 74% remaining"), "plain weekly secondary label removed");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "weekly limit: 74% remaining"), "plain weekly label removed secondary");
    }

    private static void TestReviewUsageSummaryPrefixesNonWeeklyCodeReview() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":10.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false,"
            + "\"primary_window\":{\"used_percent\":25.0,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage summary non-weekly disambiguation json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var line = CallFormatUsageSummary(snapshot);
        var parts = ParseUsageSummaryParts(line);
        AssertEqual(2, parts.Count, "usage part count non-weekly");
        AssertContains(parts, "5h limit: 90% remaining", "general non-weekly label");
        AssertContains(parts, "code review 5h limit: 75% remaining", "code review non-weekly label");
        AssertEqual(false, ContainsUsageSummaryPart(parts, "5h limit: 75% remaining"), "plain non-weekly code review label removed");
    }

    private static void TestReviewUsageBudgetGuardBlocksWhenCreditsAndWeeklyExhausted() {
        const string json = "{"
            + "\"rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
            + "\"secondary_window\":{\"used_percent\":100.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":600}},"
            + "\"code_review_rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
            + "\"secondary_window\":{\"used_percent\":100.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":600}},"
            + "\"credits\":{\"has_credits\":false,\"unlimited\":false,\"balance\":0}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage budget block json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var settings = new ReviewSettings {
            ReviewUsageBudgetAllowCredits = true,
            ReviewUsageBudgetAllowWeeklyLimit = true
        };
        var failure = CallEvaluateUsageBudgetGuardFailure(settings, snapshot);
        AssertNotNull(failure, "usage budget block message");
        AssertContainsText(failure!, "credits exhausted", "usage budget block credits detail");
        AssertContainsText(failure!, "weekly limit", "usage budget block weekly detail");
    }

    private static void TestReviewUsageBudgetGuardAllowsCreditsFallback() {
        const string json = "{"
            + "\"rate_limit\":{\"allowed\":false,\"limit_reached\":true,"
            + "\"secondary_window\":{\"used_percent\":100.0,\"limit_window_seconds\":604800,\"reset_after_seconds\":600}},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":1.5}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage budget credits json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        var settings = new ReviewSettings {
            ReviewUsageBudgetAllowCredits = true,
            ReviewUsageBudgetAllowWeeklyLimit = true
        };
        var failure = CallEvaluateUsageBudgetGuardFailure(settings, snapshot);
        AssertEqual(null, failure, "usage budget allows credits fallback");
    }

    private static void TestReviewUsageBudgetGuardBlocksWhenNoBudgetSourcesAllowed() {
        var settings = new ReviewSettings {
            ReviewUsageBudgetGuard = true,
            ReviewUsageBudgetAllowCredits = false,
            ReviewUsageBudgetAllowWeeklyLimit = false
        };
        var failure = CallTryBuildUsageBudgetGuardFailure(settings, ReviewProvider.OpenAI);
        AssertNotNull(failure, "usage budget strict mode block message");
        AssertContainsText(failure!, "both credits and weekly budget allowances are disabled",
            "usage budget strict mode block detail");
    }
}
#endif
