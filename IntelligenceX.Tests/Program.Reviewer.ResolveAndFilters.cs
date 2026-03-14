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
        AssertEqual(20, options.MaxComments, "default max comments");
    }

    private static void TestResolveThreadsDefaultBotLoginsIncludeManagedBots() {
        var options = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ParseOptions(Array.Empty<string>());
        var bots = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.ResolveBotLogins(options);
        AssertEqual(true, ContainsCaseInsensitive(bots, "intelligencex-review"), "default bot includes intelligencex-review");
        AssertEqual(true, ContainsCaseInsensitive(bots, "chatgpt-codex-connector"), "default bot includes codex connector");
        AssertEqual(true, ContainsCaseInsensitive(bots, "copilot-pull-request-reviewer"), "default bot includes copilot reviewer");
        AssertEqual(true, ContainsCaseInsensitive(bots, "github-actions"), "default bot includes github-actions");
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

    private static void TestOpenAiAccountOrderRoundRobinSupportsManyAccounts() {
        var ordered = CallOrderOpenAiAccounts(
            new[] { "acc-1", "acc-2", "acc-3", "acc-4", "acc-5", "acc-6" },
            rotation: "round-robin",
            stickyAccountId: null,
            rotationSeed: 5);
        AssertSequenceEqual(new[] { "acc-5", "acc-6", "acc-1", "acc-2", "acc-3", "acc-4" }, ordered.ToArray(),
            "openai account order round-robin many accounts");
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
                DeleteDirectoryIfExistsWithRetries(tempDir);
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
                DeleteDirectoryIfExistsWithRetries(tempDir);
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

    private static void TestResolveThreadsRunnerTreatsAlreadyResolvedAsSuccess() {
        var listed = 0;
        var resolveAttempts = 0;
        var stateLookups = 0;
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (request.Body.Contains("reviewThreads(first:50", StringComparison.Ordinal)) {
                listed++;
                return new HttpResponse(BuildGraphQlThreadsResponse("Fix", "src/Foo.cs", 10, "intelligencex-review",
                    "thread1", isResolved: false, isOutdated: true, totalComments: 1));
            }
            if (TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                resolveAttempts++;
                return new HttpResponse(
                    "{\"errors\":[{\"type\":\"UNPROCESSABLE\",\"message\":\"Pull request review thread is already resolved.\"}],\"data\":{\"resolveReviewThread\":null}}");
            }
            if (request.Body.Contains("node(id:$id)", StringComparison.Ordinal)) {
                stateLookups++;
                return new HttpResponse("{\"data\":{\"node\":{\"isResolved\":true}}}");
            }
            return null;
        });

        var args = new[] {
            "--repo", "owner/repo",
            "--pr", "1",
            "--github-token", "token",
            "--api-base-url", server.BaseUri.ToString().TrimEnd('/')
        };

        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        int exitCode;
        try {
            exitCode = IntelligenceX.Cli.ReviewThreads.ReviewThreadResolveRunner.RunAsync(args).GetAwaiter().GetResult();
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        var output = outWriter.ToString() + errWriter.ToString();
        AssertEqual(0, exitCode, "resolve threads runner exit");
        AssertEqual(1, listed, "resolve threads runner list count");
        AssertEqual(1, resolveAttempts, "resolve threads runner resolve attempts");
        AssertEqual(1, stateLookups, "resolve threads runner state lookups");
        AssertContainsText(output, "Resolved 1 thread(s).", "resolve threads runner resolved output");
        if (output.Contains("Failed to resolve thread", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected already-resolved fallback state check to avoid failure output.");
        }
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

    private static void TestPromptBuilderNarrativeModeStructuredDefault() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings();
        var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);
        AssertContainsText(prompt, "one-sentence rationale (why it matters)", "narrative mode structured rationale");
        if (prompt.Contains("Use a natural reviewer voice.", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Expected structured narrative mode prompt contract by default.");
        }
    }

    private static void TestPromptBuilderNarrativeModeFreedom() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings {
            NarrativeMode = ReviewNarrativeMode.Freedom,
            OutputStyle = "compact"
        };
        var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);
        AssertContainsText(prompt, "Use a natural reviewer voice.", "narrative mode freedom contract");
        if (prompt.Contains("one-sentence rationale (why it matters)", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected freedom narrative mode to remove structured rationale contract.");
        }
    }

    private static void TestPromptBuilderMergeBlockerSectionsDefault() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings();
        var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);
        AssertContainsText(prompt, "Merge-blocker sections: todo list, critical issues.",
            "prompt merge blocker default sections");
    }

    private static void TestPromptBuilderMergeBlockerSectionsCompactDefault() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings {
            OutputStyle = "compact"
        };
        var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);
        AssertContainsText(prompt, "Merge-blocker sections: todo list.", "prompt merge blocker compact sections");
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

    private static IReadOnlyList<string> GetFilenames(IReadOnlyList<PullRequestFile> files) {
        var names = new List<string>(files.Count);
        foreach (var file in files) {
            names.Add(file.Filename);
        }
        return names;
    }

    private static bool ContainsCaseInsensitive(IReadOnlyList<string> values, string expected) {
        foreach (var value in values) {
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

}
#endif
