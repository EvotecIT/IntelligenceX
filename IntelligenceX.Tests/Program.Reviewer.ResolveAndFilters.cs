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

        var comments = new[] {
            new InlineReviewComment(".github/workflows/ci.yml", 12, "Do not post this."),
            new InlineReviewComment("src/app.cs", 20, "Post this.")
        };
        var filteredComments = ReviewerApp.ExcludeWorkflowInlineComments(comments);
        AssertEqual(1, filteredComments.Count, "workflow inline comments excluded");
        AssertEqual("src/app.cs", filteredComments[0].Path, "non-workflow inline comments remain");

        var threads = new[] {
            new PullRequestReviewThread("workflow-thread", false, false, 1, new[] {
                new PullRequestReviewThreadComment(1, null, "Fix this workflow.", "intelligencex-review",
                    ".github/workflows/ci.yml", 12)
            }),
            new PullRequestReviewThread("source-thread", false, false, 1, new[] {
                new PullRequestReviewThreadComment(2, null, "Fix this source file.", "intelligencex-review",
                    "src/app.cs", 20)
            })
        };
        var filteredThreads = ReviewerApp.ExcludeWorkflowReviewThreads(threads);
        AssertEqual(1, filteredThreads.Count, "workflow review threads excluded");
        AssertEqual("source-thread", filteredThreads[0].Id, "non-workflow review thread remains");
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
        AssertContainsText(note, "Do not report Todo, Critical, or inline findings for excluded workflow files",
            "workflow guard tells reviewer not to report excluded workflow findings");
    }

    private static void TestWorkflowGuardSanitizerRemovesExcludedWorkflowTodo() {
        var settings = new ReviewSettings {
            MergeBlockerSections = new[] { "Todo List" },
            MergeBlockerRequireAllSections = false
        };
        var body = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks close.",
            "## Todo List ✅",
            "- [ ] Fix `.github/workflows/review-intelligencex-core.yml`.",
            "- [ ] Document the `.github/workflows/` guardrail.",
            "- [ ] Fix `src/app.cs` before merge.",
            "- Plain blocker mentioning `.github/workflows/build.yml` should remain visible.",
            "## Other Issues 🧯",
            "- `src/app.cs` can be tidier."
        });

        var sanitized = WorkflowGuardSanitizer.RemoveExcludedWorkflowBlockers(body, settings, workflowGuardActive: true);

        AssertContainsText(sanitized, "## Todo List", "workflow sanitizer keeps blocker section");
        AssertDoesNotContainText(sanitized, "None.", "workflow sanitizer does not mark mixed blocker section clean");
        AssertEqual(false, sanitized.Contains("- [ ] Fix `.github/workflows/review-intelligencex-core.yml`.", StringComparison.Ordinal),
            "workflow sanitizer removes excluded workflow todo");
        AssertContainsText(sanitized, "- [ ] Document the `.github/workflows/` guardrail.",
            "workflow sanitizer preserves checklist items that mention the workflow directory without a file path");
        AssertContainsText(sanitized, "- [ ] Fix `src/app.cs` before merge.",
            "workflow sanitizer preserves non-workflow todo in same blocker section");
        AssertContainsText(sanitized, "- Plain blocker mentioning `.github/workflows/build.yml` should remain visible.",
            "workflow sanitizer preserves plain workflow blocker bullets");
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(sanitized, settings),
            "workflow sanitizer keeps remaining blocker section active");
        AssertContainsText(sanitized, "src/app.cs", "workflow sanitizer preserves other sections");

        var promptHistory = WorkflowGuardSanitizer.RemoveExcludedWorkflowReferences(body, workflowGuardActive: true);
        AssertEqual(false,
            promptHistory.Contains("- [ ] Fix `.github/workflows/review-intelligencex-core.yml`.", StringComparison.Ordinal),
            "workflow sanitizer removes excluded workflow finding references from prompt history");
        AssertContainsText(promptHistory, "- Plain blocker mentioning `.github/workflows/build.yml` should remain visible.",
            "workflow sanitizer preserves non-finding workflow context in prompt history");
        AssertContainsText(promptHistory, "- [ ] Document the `.github/workflows/` guardrail.",
            "workflow sanitizer preserves workflow directory mentions in prompt history");
        AssertContainsText(promptHistory, "src/app.cs", "workflow sanitizer preserves non-workflow prompt history");

        var workflowOnly = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks close.",
            "## Todo List ✅",
            "- [ ] Fix `.github/workflows/review-intelligencex-core.yml`."
        });
        var workflowOnlySanitized = WorkflowGuardSanitizer.RemoveExcludedWorkflowBlockers(workflowOnly, settings,
            workflowGuardActive: true);
        AssertContainsText(workflowOnlySanitized, "None.",
            "workflow sanitizer marks workflow-only blocker section clean");
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

    private static void TestPromptBuilderIncludesCiContextSection() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings();
        var extras = new ReviewContextExtras {
            CiContextSection = string.Join("\n", new[] {
                "",
                "CI / checks context:",
                "- Head SHA abc1234 check-runs: passed 3, failed 1, pending 0.",
                "- Failing check-runs: unit-tests (failure).",
                ""
            })
        };

        var prompt = PromptBuilder.Build(context, files, settings, null, extras, inlineSupported: false);

        AssertContainsText(prompt, "CI / checks context:", "prompt ci context header");
        AssertContainsText(prompt, "unit-tests (failure)", "prompt ci context failing check");
    }

    private static void TestPromptBuilderIncludesReviewHistorySection() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings();
        var extras = new ReviewContextExtras {
            ReviewHistorySection = string.Join("\n", new[] {
                "",
                "Review history snapshot:",
                "- IX sticky summary reviewed `abc1234` (same SHA as current head (`abc1234`)).",
                ""
            })
        };

        var prompt = PromptBuilder.Build(context, files, settings, null, extras, inlineSupported: false);

        AssertContainsText(prompt, "Review history snapshot:", "prompt review history header");
        AssertContainsText(prompt, "IX sticky summary reviewed `abc1234`", "prompt review history body");
        AssertContainsText(prompt, "Treat review history as candidate context only.",
            "prompt review history safety contract");
    }

    private static void TestPromptBuilderCompactHistoryGuardIncludesCriticalIssues() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs");
        var settings = new ReviewSettings {
            OutputStyle = "compact"
        };
        var extras = new ReviewContextExtras {
            ReviewHistorySection = string.Join("\n", new[] {
                "",
                "Review history snapshot:",
                "- IX sticky summary reviewed `abc1234` (same SHA as current head (`abc1234`)).",
                ""
            })
        };

        var prompt = PromptBuilder.Build(context, files, settings, null, extras, inlineSupported: false);

        AssertContainsText(prompt,
            "Never put a prior finding in Todo List or Critical Issues unless the current diff, active thread state, or CI evidence independently confirms it still applies.",
            "compact prompt review history safety contract covers critical issues");
    }

    private static void TestPromptBuilderIncludesConventionPacks() {
        var context = BuildContext();
        var files = BuildFiles("src/app.cs", "assets/logo.png");
        var guidanceDir = Path.Combine(Directory.GetCurrentDirectory(), ".intelligencex");
        Directory.CreateDirectory(guidanceDir);
        var guidancePath = Path.Combine(guidanceDir, $"reviewer-guidance-test-{Guid.NewGuid():N}.md");
        var settings = new ReviewSettings {
            Conventions = new[] {
                new ReviewConventionPack {
                    Id = "png-assets",
                    Title = "PNG asset rules",
                    AppliesTo = new[] { "**/*.png" },
                    Rules = new[] { "Keep binary assets compressed and intentional." },
                    GoodSignals = new[] { "Asset change is isolated." },
                    RiskSignals = new[] { "Asset churn without visual context." },
                    FollowUps = new[] { "Ask for before/after screenshots." }
                },
                new ReviewConventionPack {
                    Id = "docs-only",
                    Title = "Docs only",
                    AppliesTo = new[] { "docs/**/*.md" },
                    Rules = new[] { "This should not apply to src/app.cs." }
                }
            }
        };
        settings.RepositoryGuidance.Paths = new[] { $".intelligencex/{Path.GetFileName(guidancePath)}" };
        try {
            File.WriteAllText(guidancePath, "Prefer repository-owned design guidance over global assumptions.");
            var prompt = PromptBuilder.Build(context, files, settings, null, null, inlineSupported: false);

            AssertContainsText(prompt, "Repository guidance:", "prompt repository guidance header");
            AssertContainsText(prompt, "Prefer repository-owned design guidance over global assumptions.",
                "prompt repository guidance content");
            AssertContainsText(prompt, "Configured review conventions:", "prompt custom convention header");
            AssertContainsText(prompt, "PNG asset rules", "prompt custom convention title");
            AssertContainsText(prompt, "Ask for before/after screenshots.", "prompt custom convention follow-up");
            AssertDoesNotContainText(prompt, "This should not apply to src/app.cs.",
                "prompt skips convention pack with non-matching paths");
        } finally {
            if (File.Exists(guidancePath)) {
                File.Delete(guidancePath);
            }
        }
    }

    private static void TestRepositoryGuidanceResolvesAgainstConfigRoot() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var previousDirectory = Directory.GetCurrentDirectory();
        var root = Path.Combine(Path.GetTempPath(), $"ix-guidance-{Guid.NewGuid():N}");
        var configDir = Path.Combine(root, ".intelligencex");
        var subdir = Path.Combine(root, "src");
        try {
            Directory.CreateDirectory(configDir);
            Directory.CreateDirectory(subdir);
            var configPath = Path.Combine(configDir, "reviewer.json");
            var guidancePath = Path.Combine(configDir, "reviewer-guidance.md");
            File.WriteAllText(configPath, """
{
  "review": {
    "repositoryGuidancePaths": [".intelligencex/reviewer-guidance.md"]
  }
}
""");
            File.WriteAllText(guidancePath, "Review from the repository root, even when launched below it.");

            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
            Directory.SetCurrentDirectory(subdir);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);

            AssertEqual(root, settings.RepositoryRoot, "review repository root resolves from config path");
            var prompt = PromptBuilder.Build(BuildContext(), BuildFiles("src/app.cs"), settings, null, null,
                inlineSupported: false);
            AssertContainsText(prompt, "Review from the repository root",
                "repository guidance loads from config repository root");
        } finally {
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestRepositoryGuidanceTruncatesOnLineBoundary() {
        var root = Path.Combine(Path.GetTempPath(), $"ix-guidance-trim-{Guid.NewGuid():N}");
        try {
            var guidanceDir = Path.Combine(root, ".intelligencex");
            Directory.CreateDirectory(guidanceDir);
            var guidancePath = Path.Combine(guidanceDir, "reviewer-guidance.md");
            const string content = "# Review posture\nKeep complete guidance lines.\nSecond line should not appear.";
            File.WriteAllText(guidancePath, content);

            var settings = new ReviewSettings {
                RepositoryRoot = root
            };
            settings.RepositoryGuidance.Paths = new[] { ".intelligencex/reviewer-guidance.md" };
            settings.RepositoryGuidance.MaxChars = "# Review posture\nKeep complete guidance lines.\nSec".Length;

            var prompt = PromptBuilder.Build(BuildContext(), BuildFiles("src/app.cs"), settings, null, null,
                inlineSupported: false);

            AssertContainsText(prompt, "# Review posture", "repository guidance keeps first heading");
            AssertContainsText(prompt, "Keep complete guidance lines.",
                "repository guidance keeps complete line before truncation");
            AssertDoesNotContainText(prompt, "\nSec", "repository guidance avoids partial next line");
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestReviewAutoApprovalReadinessGates() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Bump package", null, false,
            "abc1234", "base", new[] { "ix-auto-approve" }, "owner/repo", false, null,
            authorLogin: "dependabot[bot]");
        var settings = new ReviewSettings();
        settings.AutoApprove.Enabled = true;
        settings.AutoApprove.AllowedAuthors = new[] { "dependabot[bot]" };
        var history = new ReviewHistorySnapshot {
            ThreadSnapshot = new ReviewHistoryThreadSnapshot {
                ActiveCount = 0,
                ResolvedCount = 1
            }
        };
        var checks = new ReviewCheckSnapshot(new[] {
            new ReviewCheckRun("Build", "completed", "success", null),
            new ReviewCheckRun("IntelligenceX Review", "in_progress", null, null)
        });

        var decision = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: checks);
        AssertEqual(true, decision.ShouldApprove, "auto approval eligible after ignored pending reviewer check");
        AssertEqual(true, decision.DisplayReadiness, "auto approval readiness displays after opt-in gates pass");
        AssertEqual(true, decision.DryRun, "auto approval dry run default");
        AssertEqual(0, decision.EffectiveCheckSnapshot!.PendingCount, "auto approval ignored check removes pending count");
        var block = ReviewAutoApproval.BuildCommentBlock(decision);
        AssertContainsText(block, "Auto-Approval Readiness", "auto approval comment block heading");
        AssertContainsText(block, "Eligible (dry run)", "auto approval comment block dry-run status");
        AssertContainsText(block, "1 passed, 0 failed, 0 pending", "auto approval comment block filtered checks");
        AssertContainsText(block, "dry run is enabled", "auto approval comment block dry-run note");

        settings.AutoApprove.DryRun = false;
        var writeDecision = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: checks);
        var writeBlock = ReviewAutoApproval.BuildCommentBlock(writeDecision);
        AssertContainsText(writeBlock, "final submitted/skipped/failed status is recorded in workflow logs",
            "auto approval comment block submission outcome note");

        var blocked = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: true,
            history, allowWrites: true, checks: checks);
        AssertEqual(false, blocked.ShouldApprove, "auto approval blocked by merge blockers");
        AssertContainsText(string.Join("\n", blocked.Blockers), "merge blockers detected",
            "auto approval merge blocker reason");

        var noLabel = new PullRequestContext(context.RepoFullName, context.Owner, context.Repo, context.Number,
            context.Title, context.Body, context.Draft, context.HeadSha, context.BaseSha, Array.Empty<string>(),
            context.HeadRepoFullName, context.IsFork, context.AuthorAssociation, context.HeadRepositoryKnown,
            context.BaseRefName, context.AuthorLogin);
        blocked = ReviewAutoApproval.Evaluate(noLabel, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: checks);
        AssertEqual(false, blocked.ShouldApprove, "auto approval blocked without required label");
        AssertEqual(false, blocked.DisplayReadiness, "auto approval readiness hides without required opt-in label");
        AssertContainsText(string.Join("\n", blocked.Blockers), "missing required label",
            "auto approval required label reason");
        AssertEqual(string.Empty, ReviewAutoApproval.BuildCommentBlock(blocked),
            "auto approval comment block hides when policy opt-in is missing");

        var onlyIgnoredChecks = new ReviewCheckSnapshot(new[] {
            new ReviewCheckRun("IntelligenceX Review", "completed", "success", null)
        });
        blocked = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: onlyIgnoredChecks);
        AssertEqual(false, blocked.ShouldApprove, "auto approval blocks zero effective checks");
        AssertEqual(false, blocked.HasEffectiveCheckData, "auto approval effective check-data flag reflects effective checks");
        AssertContainsText(string.Join("\n", blocked.Blockers), "no effective checks",
            "auto approval zero effective checks reason");

        settings.AutoApprove.RequireChecksPass = false;
        settings.AutoApprove.RequireNoPendingChecks = true;
        var pendingChecks = new ReviewCheckSnapshot(new[] {
            new ReviewCheckRun("Build", "in_progress", null, null)
        });
        blocked = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: pendingChecks);
        AssertEqual(false, blocked.ShouldApprove, "auto approval pending-only mode blocks pending checks");
        AssertContainsText(string.Join("\n", blocked.Blockers), "pending check",
            "auto approval pending-only blocker reason");

        var failedChecks = new ReviewCheckSnapshot(new[] {
            new ReviewCheckRun("Experimental", "completed", "failure", null)
        });
        var pendingOnlyDecision = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false,
            hasMergeBlockers: false, history, allowWrites: true, checks: failedChecks);
        AssertEqual(true, pendingOnlyDecision.ShouldApprove,
            "auto approval pending-only mode ignores completed failing checks when pass gate is disabled");
        AssertContainsText(string.Join("\n", pendingOnlyDecision.PassedGates), "no pending checks",
            "auto approval pending-only pass reason");

        settings.AutoApprove.RequireChecksPass = true;
        settings.AutoApprove.RequireNoPendingChecks = true;
        blocked = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history: null, allowWrites: true, checks: checks,
            reviewThreadsUnavailable: true);
        AssertEqual(false, blocked.ShouldApprove, "auto approval blocks unavailable review thread state");
        AssertContainsText(string.Join("\n", blocked.Blockers), "review thread state unavailable",
            "auto approval unavailable review thread reason");

        blocked = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history: null, allowWrites: true, checks: checks,
            reviewThreadsUnavailable: false);
        AssertEqual(false, blocked.ShouldApprove,
            "auto approval blocks missing review thread snapshot without branch protection requirement");
        AssertContainsText(string.Join("\n", blocked.Blockers), "review thread state unavailable",
            "auto approval missing review thread snapshot reason");
    }

    private static void TestReviewAutoApprovalPendingOnlyGateIsIndependent() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Bump package", null, false,
            "abc1234", "base", new[] { "ix-auto-approve" }, "owner/repo", false, null,
            authorLogin: "dependabot[bot]");
        var settings = new ReviewSettings();
        settings.AutoApprove.Enabled = true;
        settings.AutoApprove.AllowedAuthors = new[] { "dependabot[bot]" };
        settings.AutoApprove.RequireChecksPass = false;
        settings.AutoApprove.RequireNoPendingChecks = true;
        var history = new ReviewHistorySnapshot {
            ThreadSnapshot = new ReviewHistoryThreadSnapshot {
                ActiveCount = 0
            }
        };
        var pendingChecks = new ReviewCheckSnapshot(new[] {
            new ReviewCheckRun("Build", "in_progress", null, null)
        });

        var blocked = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: pendingChecks);
        AssertEqual(false, blocked.ShouldApprove,
            "auto approval pending-only mode blocks when checks are still running");
        AssertContainsText(string.Join("\n", blocked.Blockers), "1 pending check(s)",
            "auto approval pending-only mode reports pending blocker");

        var failedChecks = new ReviewCheckSnapshot(new[] {
            new ReviewCheckRun("Experimental", "completed", "failure", null)
        });
        var eligible = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: failedChecks);
        AssertEqual(true, eligible.ShouldApprove,
            "auto approval pending-only mode ignores completed failing checks when pass gate is disabled");
        AssertContainsText(string.Join("\n", eligible.PassedGates), "no pending checks",
            "auto approval pending-only mode records the independent pass gate");

        var unavailable = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false, hasMergeBlockers: false,
            history, allowWrites: true, checks: null);
        AssertEqual(false, unavailable.ShouldApprove,
            "auto approval pending-only mode fails closed when check data is unavailable");
        AssertContainsText(string.Join("\n", unavailable.Blockers), "check status unavailable",
            "auto approval pending-only mode reports unavailable check status");

        settings.AutoApprove.RequireNoPendingChecks = false;
        var disabledCheckGate = ReviewAutoApproval.Evaluate(context, settings, reviewFailed: false,
            hasMergeBlockers: false, history, allowWrites: true, checks: null);
        AssertEqual(true, disabledCheckGate.ShouldApprove,
            "auto approval check data is bypassed only when both check gates are disabled");
        AssertContainsText(string.Join("\n", disabledCheckGate.PassedGates), "check gate disabled",
            "auto approval disabled check gate records explicit bypass");
    }

    private static void TestReviewThreadBlockerSanitizerRemovesStaleThreadTodo() {
        var settings = new ReviewSettings {
            MergeBlockerSections = new[] { "Todo List" },
            MergeBlockerRequireAllSections = false
        };
        var history = new ReviewHistorySnapshot {
            ThreadSnapshot = new ReviewHistoryThreadSnapshot {
                ActiveCount = 0,
                ResolvedCount = 3
            }
        };
        var body = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good.",
            "## Todo List ✅",
            "- [ ] Resolve the still-active review threads on `ReviewAutoApproval.cs` and `GitHubClient.cs`.",
            "## Other Issues 🧯",
            "- Consider simplifying a helper later."
        });

        var sanitized = ReviewThreadBlockerSanitizer.RemoveResolvedThreadBlockers(body, settings, history,
            reviewThreadsUnavailable: false);

        AssertDoesNotContainText(sanitized, "still-active review threads",
            "review thread sanitizer removes stale merge-blocker todo");
        AssertContainsText(sanitized, "None.", "review thread sanitizer leaves explicit clean section marker");
        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(sanitized, settings),
            "review thread sanitizer clears stale thread blocker from merge state");
        AssertContainsText(sanitized, "Consider simplifying a helper later.",
            "review thread sanitizer preserves non-blocking sections");
    }

    private static void TestReviewThreadBlockerSanitizerPreservesPlainThreadBullets() {
        var settings = new ReviewSettings {
            MergeBlockerSections = new[] { "Todo List" },
            MergeBlockerRequireAllSections = false
        };
        var history = new ReviewHistorySnapshot {
            ThreadSnapshot = new ReviewHistoryThreadSnapshot {
                ActiveCount = 0,
                ResolvedCount = 1
            }
        };
        var body = string.Join("\n", new[] {
            "## Todo List ✅",
            "- Resolve the stale review thread wording in a follow-up note."
        });

        var sanitized = ReviewThreadBlockerSanitizer.RemoveResolvedThreadBlockers(body, settings, history,
            reviewThreadsUnavailable: false);

        AssertContainsText(sanitized, "- Resolve the stale review thread wording in a follow-up note.",
            "review thread sanitizer preserves plain bullets");
        AssertDoesNotContainText(sanitized, "None.",
            "review thread sanitizer does not mark plain-bullet section clean");
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(sanitized, settings),
            "review thread sanitizer keeps plain merge-blocker bullet active");
    }

    private static void TestReviewThreadBlockerSanitizerKeepsTodoWhenThreadsUnavailable() {
        var settings = new ReviewSettings();
        var history = new ReviewHistorySnapshot {
            ThreadSnapshot = new ReviewHistoryThreadSnapshot {
                ActiveCount = 0
            }
        };
        var body = string.Join("\n", new[] {
            "## Todo List ✅",
            "- [ ] Resolve the still-active review threads before merging."
        });

        var sanitized = ReviewThreadBlockerSanitizer.RemoveResolvedThreadBlockers(body, settings, history,
            reviewThreadsUnavailable: true);

        AssertContainsText(sanitized, "still-active review threads",
            "review thread sanitizer keeps blocker when thread state is unavailable");
        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(sanitized, settings),
            "review thread sanitizer preserves blocker when structured thread state is unavailable");
    }

    private static void TestGitHubCommitStatusesContributeToCheckSnapshot() {
        var root = IntelligenceX.Json.JsonLite.Parse("""
{
  "statuses": [
    { "context": "legacy-build", "state": "success", "target_url": "https://example.test/build" },
    { "context": "legacy-quality", "state": "failure", "target_url": "https://example.test/quality" },
    { "context": "legacy-deploy", "state": "pending", "target_url": "https://example.test/deploy" }
  ]
}
""")?.AsObject();

        var runs = GitHubClient.ParseCommitStatusRunsForTests(root);
        var snapshot = new ReviewCheckSnapshot(runs);

        AssertEqual(3, runs.Count, "legacy status check run count");
        AssertEqual(1, snapshot.PassedCount, "legacy status passed count");
        AssertEqual(1, snapshot.FailedCount, "legacy status failed count");
        AssertEqual(1, snapshot.PendingCount, "legacy status pending count");
        AssertContainsText(runs[1].Name, "status: legacy-quality", "legacy status run name");
    }

    private static void TestGitHubCommitStatusesKeepLatestStatusPerContext() {
        var root = IntelligenceX.Json.JsonLite.Parse("""
{
  "statuses": [
    {
      "context": "legacy-build",
      "state": "failure",
      "target_url": "https://example.test/build-old",
      "updated_at": "2026-05-04T10:00:00Z"
    },
    {
      "context": "legacy-build",
      "state": "success",
      "target_url": "https://example.test/build-new",
      "updated_at": "2026-05-04T10:05:00Z"
    },
    {
      "context": "legacy-quality",
      "state": "pending",
      "target_url": "https://example.test/quality",
      "updated_at": "2026-05-04T10:06:00Z"
    }
  ]
}
""")?.AsObject();

        var runs = GitHubClient.ParseCommitStatusRunsForTests(root);
        var snapshot = new ReviewCheckSnapshot(runs);

        AssertEqual(2, runs.Count, "legacy status duplicate context run count");
        AssertEqual(1, snapshot.PassedCount, "legacy status duplicate context passed count");
        AssertEqual(0, snapshot.FailedCount, "legacy status duplicate context failed count");
        AssertEqual(1, snapshot.PendingCount, "legacy status duplicate context pending count");
        AssertContainsText(runs[0].DetailsUrl ?? string.Empty, "build-new",
            "legacy status duplicate context keeps newest target url");
    }

    private static void TestGitHubAutoApprovalReviewMatchRequiresExactHeadSha() {
        const string marker = "IntelligenceX auto-approval";
        const string headSha = "abcdef1234567890";
        const string prefix = "abcdef1";
        var body = $"Approved by {marker}.";

        AssertEqual(true,
            GitHubClient.IsMatchingAutoApprovalReviewForTests("APPROVED", headSha, body, headSha, marker),
            "auto approval review matches exact head sha");
        AssertEqual(false,
            GitHubClient.IsMatchingAutoApprovalReviewForTests("APPROVED", prefix, body, headSha, marker),
            "auto approval review rejects commit id prefixes");
        AssertEqual(false,
            GitHubClient.IsMatchingAutoApprovalReviewForTests("COMMENTED", headSha, body, headSha, marker),
            "auto approval review rejects non-approval states");
        AssertEqual(false,
            GitHubClient.IsMatchingAutoApprovalReviewForTests("APPROVED", headSha, "Looks good", headSha, marker),
            "auto approval review requires approval marker");
    }

    private static void TestReviewHistoryBuilderIncludesStickySummaryAndThreadSnapshot() {
        var settings = new ReviewSettings {
            MaxCommentChars = 120
        };
        settings.History.Enabled = true;
        settings.History.MaxRounds = 4;
        var summaryBody = string.Join("\n", new[] {
            "<!-- intelligencex:summary -->",
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Summary 📝",
            "Looks good overall.",
            "",
            "## Todo List ✅",
            "- [ ] Add null guard in parser.",
            "",
            "## Critical Issues ⚠️",
            "- [x] Cover the stale thread path.",
            "",
            "## Other Issues 🧯",
            "None."
        });
        var olderSummaryBody = string.Join("\n", new[] {
            "<!-- intelligencex:summary -->",
            "## IntelligenceX Review",
            "Reviewed commit: `9999999`",
            "",
            "## Todo List ✅",
            "- [ ] Add null guard in parser.",
            "",
            "## Critical Issues ⚠️",
            "- [ ] Cover the stale thread path."
        });
        var progressSummaryBody = string.Join("\n", new[] {
            "<!-- intelligencex:summary -->",
            "## IntelligenceX Review (in progress)",
            "Reviewing PR #42: **Parser update**",
            "",
            "- [x] Gather context",
            "- [ ] Post findings",
            "",
            "### Review Checklist",
            "- [ ] Review changes"
        });
        var issueComments = new[] {
            new IssueComment(1, summaryBody, "intelligencex-review"),
            new IssueComment(2, "human follow-up note", "reviewer-user"),
            new IssueComment(4, "Claude summary: prior low-priority notes now look resolved.", "claude"),
            new IssueComment(5, progressSummaryBody, "intelligencex-review"),
            new IssueComment(3, olderSummaryBody, "github-actions")
        };
        var threads = new[] {
            new PullRequestReviewThread(
                "thread-1",
                isResolved: false,
                isOutdated: false,
                totalComments: 1,
                comments: new[] {
                    new PullRequestReviewThreadComment(11, DateTimeOffset.UtcNow, "Please add regression coverage.", "reviewer-user", "src/app.cs", 42)
                }),
            new PullRequestReviewThread(
                "thread-2",
                isResolved: true,
                isOutdated: false,
                totalComments: 1,
                comments: new[] {
                    new PullRequestReviewThreadComment(12, DateTimeOffset.UtcNow, "Resolved already.", "reviewer-user", "src/app.cs", 10)
                })
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "def5678", threads, settings);
        var section = ReviewHistoryBuilder.Render(snapshot);

        AssertEqual(2, snapshot.Rounds.Count, "review history snapshot rounds");
        AssertEqual(0, snapshot.OpenFindings.Count, "review history snapshot prior-head findings are not current");
        AssertEqual(1, snapshot.ResolvedSinceLastRound.Count, "review history snapshot resolved since last round");
        AssertEqual("9999999", snapshot.Rounds[0].ReviewedSha, "review history snapshot oldest round first");
        AssertEqual("abc1234", snapshot.Rounds[1].ReviewedSha, "review history snapshot newest round second");
        AssertEqual(2, snapshot.Rounds[1].Findings.Count, "review history snapshot findings");
        AssertEqual("open", snapshot.Rounds[1].Findings[0].Status, "review history snapshot first finding status");
        AssertEqual("resolved", snapshot.Rounds[1].Findings[1].Status, "review history snapshot second finding status");
        AssertEqual(true, snapshot.Rounds[1].Findings[0].Fingerprint.Length > 0, "review history snapshot fingerprint");
        AssertEqual("Cover the stale thread path.", snapshot.ResolvedSinceLastRound[0].Text,
            "review history snapshot resolved finding text");
        AssertNotNull(snapshot.ThreadSnapshot, "review history snapshot threads");
        AssertEqual(1, snapshot.ThreadSnapshot!.ActiveCount, "review history snapshot active thread count");
        AssertContainsText(section, "Review history snapshot:", "review history header");
        AssertContainsText(section, "Open findings confirmed on the current head: none.",
            "review history open findings summary");
        AssertContainsText(section, "Prior-round findings below are candidates only",
            "review history prior findings safety label");
        AssertContainsText(section, "Resolved since the latest prior round:", "review history resolved summary");
        AssertContainsText(section, "Round 1: IX sticky summary reviewed `9999999`", "review history older round");
        AssertContainsText(section, "Round 2: IX sticky summary reviewed `abc1234`", "review history newer round");
        AssertContainsText(section, "[todo] Add null guard in parser.", "review history normalized todo item");
        AssertContainsText(section, "[critical] Cover the stale thread path.", "review history normalized resolved item");
        AssertDoesNotContainText(section, "in progress", "review history ignores progress summaries");
        AssertDoesNotContainText(section, "Review Checklist", "review history ignores progress checklist summaries");
        AssertContainsText(section, "Review threads snapshot: active 1, resolved 1, stale 0.", "review history thread counts");
        AssertContainsText(section, "reviewer-user (src/app.cs:42): Please add regression coverage.", "review history active thread excerpt");

        var sameHeadSnapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", threads, settings);
        AssertEqual(1, sameHeadSnapshot.OpenFindings.Count,
            "review history carries open findings only when summary reviewed current head");

        settings.History.MaxRounds = 0;
        snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "def5678", threads, settings);
        AssertEqual(0, snapshot.Rounds.Count, "review history max rounds zero disables sticky summary rounds");
        AssertEqual(0, snapshot.OpenFindings.Count, "review history max rounds zero disables open sticky findings");
        settings.History.MaxRounds = 4;

        settings.History.IncludeExternalBotSummaries = true;
        snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "def5678", threads, settings);
        section = ReviewHistoryBuilder.Render(snapshot);
        AssertEqual(1, snapshot.ExternalSummaries.Count, "review history external summary count");
        AssertEqual("claude", snapshot.ExternalSummaries[0].Author, "review history external summary author");
        AssertContainsText(section, "External bot summaries included as supporting context only:",
            "review history external summary safety label");
        AssertContainsText(section, "[claude] Claude summary: prior low-priority notes now look resolved.",
            "review history external summary excerpt");
    }

    private static void TestReviewHistoryBuilderBuildsCommentBlock() {
        var snapshot = new ReviewHistorySnapshot {
            Rounds = new[] {
                new ReviewHistoryRound {
                    Sequence = 1,
                    ReviewedSha = "abc1234"
                }
            },
            OpenFindings = new[] {
                new ReviewHistoryFinding {
                    Fingerprint = "open-1",
                    Section = "todo list",
                    Text = "Add null guard in parser.",
                    Status = "open"
                }
            },
            ResolvedSinceLastRound = new[] {
                new ReviewHistoryFinding {
                    Fingerprint = "resolved-1",
                    Section = "critical issues",
                    Text = "Cover the stale thread path.",
                    Status = "resolved"
                }
            }
        };

        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertContainsText(block, "## History Progress 🔁", "review history comment block heading");
        AssertContainsText(block, "- **Open on current head:** 1 finding.", "review history comment block open count");
        AssertContainsText(block, "- **Resolved since last round:** 1 finding.", "review history comment block resolved count");
        AssertContainsText(block, "Finding lifecycle:", "review history comment block lifecycle label");
        AssertContainsText(block, "| New | todo | Add null guard in parser. |",
            "review history comment block lifecycle new item");
        AssertContainsText(block, "| Resolved | critical | Cover the stale thread path. |",
            "review history comment block lifecycle resolved item");
    }

    private static void TestReviewHistoryBuilderTreatsMissingOptionalBlockerSectionAsCleanPosture() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        var body = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Summary 📝",
            "Looks good.",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "### Static Analysis Policy 🧭",
            "- Status: fail",
            "",
            "### Static Analysis 🔎",
            "- [warning] `src/app.cs:1` (IXLOC001) File has 701 lines."
        });
        var comments = new[] {
            new IssueComment(10, body, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(comments, "abc1234",
            Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(1, snapshot.Rounds.Count, "review history missing optional section round count");
        AssertEqual(false, snapshot.Rounds[0].HasMergeBlockers,
            "review history missing optional blocker section does not record needs-work posture");
        AssertEqual("approve", snapshot.Rounds[0].Recommendation,
            "review history missing optional blocker section records clean recommendation");
        AssertContainsText(block, "## History Progress 🔁",
            "review history missing optional blocker section still renders tracking block");
        AssertContainsText(block, "- **Tracked rounds:** 1 IX round; 1 round on current head.",
            "review history missing optional blocker section shows tracked round count");
        AssertContainsText(block, "- **Open on current head:** none.",
            "review history missing optional blocker section shows clean open state");
    }

    private static void TestReviewHistoryBuilderRequiresExactSameHeadSha() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        var body = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });

        var round = ReviewHistoryBuilder.BuildSummaryRound(body, 10, "abc1234567890", settings);

        AssertEqual(false, round?.SameHeadAsCurrent ?? true,
            "review history builder does not treat abbreviated reviewed SHA as current head");
    }

    private static void TestReviewHistoryBuilderUsesLatestSameHeadRound() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var firstSameHeadBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var latestSameHeadBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, latestSameHeadBody, "intelligencex-review"),
            new IssueComment(10, firstSameHeadBody, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(2, snapshot.Rounds.Count, "review history latest same-head round count");
        AssertEqual(0, snapshot.OpenFindings.Count, "review history latest same-head round supersedes prior stale finding");
        AssertEqual(1, snapshot.ResolvedSinceLastRound.Count,
            "review history latest same-head round marks disappeared stale finding resolved");
        AssertEqual("Retry the alternate transport.", snapshot.ResolvedSinceLastRound[0].Text,
            "review history resolved stale same-head finding text");
        AssertContainsText(block, "Resolved since last round:", "review history latest same-head block resolved label");
        AssertContainsText(block, "| Resolved | todo | Retry the alternate transport. |",
            "review history latest same-head block resolved stale finding");
    }

    private static void TestReviewHistoryBuilderDoesNotResolveAcrossDifferentHeads() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var olderHeadBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerHeadBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `def5678`",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, newerHeadBody, "intelligencex-review"),
            new IssueComment(10, olderHeadBody, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "def5678", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(0, snapshot.OpenFindings.Count, "review history cross-head snapshot has no current same-head open findings");
        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history cross-head snapshot does not mark disappeared finding resolved");
        AssertContainsText(block, "## History Progress 🔁",
            "review history cross-head block renders tracking state");
        AssertContainsText(block, "1 round on current head",
            "review history cross-head block shows current-head round count");
        AssertDoesNotContainText(block, "Retry the alternate transport.",
            "review history cross-head block does not surface prior-head finding as resolved");
    }

    private static void TestReviewHistoryBuilderDedupesLatestSameHeadOpenFindings() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var body = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(10, body, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(1, snapshot.OpenFindings.Count, "review history dedupes same-head open findings");
        AssertEqual("Retry the alternate transport.", snapshot.OpenFindings[0].Text,
            "review history deduped same-head finding text");
        AssertEqual(1, CountOccurrences(block, "Retry the alternate transport."),
            "review history deduped same-head finding appears once in comment block");
    }

    private static void TestReviewHistoryBuilderDoesNotResolveMissingFindingWhenLatestSameHeadHitsLimit() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        settings.History.MaxItems = 1;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            $"Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Add a new blocker ahead of the older one.",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(1, snapshot.OpenFindings.Count, "review history capped same-head snapshot keeps extracted open finding");
        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history capped same-head snapshot does not infer missing finding resolved");
        AssertContainsText(block, "| New | todo | Add a new blocker ahead of the older one. |",
            "review history capped same-head block keeps current extracted open finding");
        AssertContainsText(block, "- **Resolved since last round:** none.",
            "review history capped same-head block shows no resolved findings");
        AssertDoesNotContainText(block, "| Resolved | todo | Retry the alternate transport. |",
            "review history capped same-head block does not falsely report hidden finding resolved");
    }

    private static void TestReviewHistoryBuilderLatestSameHeadUsesResolvedStatusPrecedence() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "- [x] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(0, snapshot.OpenFindings.Count,
            "review history latest same-head snapshot suppresses finding resolved later in same round");
        AssertEqual(1, snapshot.ResolvedSinceLastRound.Count,
            "review history latest same-head snapshot keeps resolved finding when latest round checks it off");
        AssertContainsText(block, "| Resolved | todo | Retry the alternate transport. |",
            "review history latest same-head block reports the resolved finding once");
    }

    private static void TestReviewHistoryBuilderResolvesExactCapWhenLatestRoundIsComplete() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        settings.History.MaxItems = 1;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [x] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(0, snapshot.OpenFindings.Count,
            "review history exact-cap complete snapshot has no current open findings");
        AssertEqual(1, snapshot.ResolvedSinceLastRound.Count,
            "review history exact-cap complete snapshot still reports legitimate same-head resolution");
        AssertContainsText(block, "Resolved since last round:",
            "review history exact-cap complete block includes resolved section");
        AssertContainsText(block, "| Resolved | todo | Retry the alternate transport. |",
            "review history exact-cap complete block includes resolved finding text");
    }

    private static void TestReviewHistoryBuilderDoesNotResolveWhenLatestSameHeadBlockersAreUnparseable() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        settings.MergeBlockerRequireSectionMatch = true;
        settings.MergeBlockerRequireAllSections = true;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo-ish Notes",
            "- [ ] Blockers remain but the expected merge-blocker section header is malformed."
        });
        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };
        var extracted = ReviewSummaryParser.ExtractMergeBlockerFindings(newerBody, settings, settings.History.MaxItems);

        AssertEqual(true, ReviewSummaryParser.HasMergeBlockers(newerBody, settings),
            "review history unparseable same-head latest summary still reports merge blockers");
        AssertEqual(0, extracted.Count,
            "review history unparseable same-head latest summary yields no normalized findings");

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(0, snapshot.OpenFindings.Count,
            "review history unparseable same-head snapshot has no normalized open findings");
        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history unparseable same-head snapshot does not infer missing finding resolved");
        AssertContainsText(block, "- **Open on current head:** unknown; merge-blocker lines could not be fully normalized.",
            "review history unparseable same-head block reports unknown normalized open findings");
        AssertContainsText(block, "- **Resolved since last round:** none.",
            "review history unparseable same-head block shows no resolved findings");
        AssertDoesNotContainText(block, "[todo] Retry the alternate transport.",
            "review history unparseable same-head block does not surface prior finding as resolved");
    }

    private static void TestReviewHistoryBuilderDoesNotResolveWhenLatestSameHeadBlockersArePartiallyUnparseable() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Add a new blocker ahead of the older one.",
            "* [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };
        var extracted = ReviewSummaryParser.ExtractMergeBlockerFindings(newerBody, settings, settings.History.MaxItems,
            out var hitLimit, out var parseIncomplete);

        AssertEqual(false, hitLimit, "review history partial-parse latest summary does not hit findings limit");
        AssertEqual(true, parseIncomplete,
            "review history partial-parse latest summary reports incomplete merge-blocker parsing");
        AssertEqual(1, extracted.Count,
            "review history partial-parse latest summary still keeps normalized findings");

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);
        var rendered = ReviewHistoryBuilder.Render(snapshot);

        AssertEqual(1, snapshot.OpenFindings.Count,
            "review history partial-parse same-head snapshot keeps normalized current finding");
        AssertEqual("Add a new blocker ahead of the older one.", snapshot.OpenFindings[0].Text,
            "review history partial-parse same-head snapshot keeps normalized open finding text");
        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history partial-parse same-head snapshot does not infer malformed missing finding resolved");
        AssertEqual(true, snapshot.Rounds[1].FindingsParseIncomplete,
            "review history partial-parse same-head round records incomplete parsing");
        AssertContainsText(block, "| New | todo | Add a new blocker ahead of the older one. |",
            "review history partial-parse same-head block includes normalized current finding");
        AssertContainsText(rendered,
            "Additional merge-blocker lines were present but could not be normalized",
            "review history partial-parse same-head snapshot warns about incomplete normalization");
        AssertContainsText(block, "- **Resolved since last round:** none.",
            "review history partial-parse same-head block shows no resolved findings");
        AssertDoesNotContainText(block, "| Resolved | todo | Retry the alternate transport. |",
            "review history partial-parse same-head block does not falsely report malformed missing finding resolved");
    }

    private static void TestReviewHistoryBuilderDoesNotResolveWhenLatestSameHeadParseIncompleteWithoutDetectedBlockers() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "* [ ] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        _ = ReviewSummaryParser.ExtractMergeBlockerFindings(newerBody, settings, settings.History.MaxItems,
            out _, out var parseIncomplete);

        AssertEqual(false, ReviewSummaryParser.HasMergeBlockers(newerBody, settings),
            "review history parse-incomplete latest summary can evade merge-blocker detection");
        AssertEqual(true, parseIncomplete,
            "review history parse-incomplete latest summary still records incomplete parsing");

        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };
        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);
        var rendered = ReviewHistoryBuilder.Render(snapshot);

        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history parse-incomplete same-head snapshot does not infer missing finding resolved");
        AssertEqual("unknown; merge-blocker lines were present but could not be normalized.",
            snapshot.Rounds[1].MergeBlockerStatus,
            "review history parse-incomplete same-head round surfaces unknown merge-blocker state");
        AssertContainsText(block, "- **Open on current head:** unknown; merge-blocker lines could not be fully normalized.",
            "review history parse-incomplete same-head block surfaces unknown current-head status");
        AssertDoesNotContainText(block, "[todo] Retry the alternate transport.",
            "review history parse-incomplete same-head block does not surface malformed missing finding as resolved");
        AssertContainsText(rendered, "- IX merge blockers in sticky summary: unknown; merge-blocker lines were present but could not be normalized.",
            "review history parse-incomplete same-head render preserves unknown merge-blocker status");
    }

    private static void TestReviewHistoryBuilderDoesNotInferResolutionFromPreviouslyResolvedDuplicate() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;

        var olderBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "- [ ] Retry the alternate transport.",
            "- [x] Retry the alternate transport.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var newerBody = string.Join("\n", new[] {
            ReviewFormatter.SummaryMarker,
            "## IntelligenceX Review",
            "Reviewed commit: `abc1234`",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });
        var issueComments = new[] {
            new IssueComment(20, newerBody, "intelligencex-review"),
            new IssueComment(10, olderBody, "intelligencex-review")
        };

        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc1234", Array.Empty<PullRequestReviewThread>(), settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history duplicate previous statuses do not emit a fresh resolved finding");
        AssertContainsText(block, "- **Tracked rounds:** 2 IX rounds; 2 rounds on current head.",
            "review history duplicate previous statuses block keeps tracking visible");
        AssertContainsText(block, "- **Resolved since last round:** none.",
            "review history duplicate previous statuses block shows no new resolution");
        AssertDoesNotContainText(block, "[todo] Retry the alternate transport.",
            "review history duplicate previous statuses block does not surface already-resolved duplicate as newly resolved");
    }

    private static void TestReviewHistoryBuilderAppendsCurrentRoundForVisibleTracking() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        var priorSnapshot = new ReviewHistorySnapshot {
            CurrentHeadSha = "def5678",
            Rounds = new[] {
                new ReviewHistoryRound {
                    Sequence = 1,
                    Source = "intelligencex",
                    ReviewedSha = "abc1234",
                    SameHeadAsCurrent = false,
                    Recommendation = "approve",
                    MergeBlockerStatus = "none."
                }
            }
        };
        var currentBody = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good overall.",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });

        var snapshot = ReviewHistoryBuilder.AppendCurrentRound(priorSnapshot, currentBody, "def5678", settings);
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);

        AssertEqual(2, snapshot.Rounds.Count, "review history visible tracking appends current round");
        AssertEqual(true, snapshot.Rounds[1].SameHeadAsCurrent,
            "review history visible tracking marks appended round current");
        AssertContainsText(block, "- **Tracked rounds:** 2 IX rounds; 1 round on current head.",
            "review history visible tracking shows current round");
        AssertContainsText(block, "- **Latest reviewed head:** def5678 (current).",
            "review history visible tracking latest head is current");
        AssertContainsText(block, "- **Open on current head:** none.",
            "review history visible tracking shows clean current state");
    }

    private static void TestReviewSummaryStabilityDropsHistoryProgressBlock() {
        var context = BuildContext();
        var settings = new ReviewSettings {
            Model = "gpt-5-test",
            Length = ReviewLength.Medium,
            Mode = "summary"
        };
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good overall.",
            "",
            "## Todo List ✅",
            "None."
        });
        var historyBlock = string.Join("\n", new[] {
            "## Review State 🧭",
            "",
            "- **Recommendation:** Approve",
            "- **Merge blockers:** none detected",
            "- **Evidence:** configured merge-blocker sections parsed with no open items",
            "",
            "## History Progress 🔁",
            "",
            "Open on current head:",
            "- [todo] Previous issue."
        });
        var autoApprovalBlock = string.Join("\n", new[] {
            "## Auto-Approval Readiness 🤝",
            "",
            "| Status | Checks | Passed gates | Blockers |",
            "| --- | --- | --- | --- |",
            "| Eligible | 1 passed, 0 failed, 0 pending | checks passed | none |"
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true,
            inlineSuppressed: false, autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty,
            findingsBlock: string.Empty, historyBlock: string.Join("\n\n", historyBlock, autoApprovalBlock));
        var extracted = ReviewerApp.ExtractSummaryBodyForTests(comment, 10000) ?? string.Empty;

        AssertDoesNotContainText(extracted, "History Progress 🔁", "summary stability strips history progress heading");
        AssertDoesNotContainText(extracted, "Review State 🧭", "summary stability strips review state heading");
        AssertDoesNotContainText(extracted, "configured merge-blocker sections parsed",
            "summary stability strips review state body");
        AssertDoesNotContainText(extracted, "Previous issue", "summary stability strips history progress body");
        AssertDoesNotContainText(extracted, "Auto-Approval Readiness", "summary stability strips auto approval heading");
        AssertDoesNotContainText(extracted, "checks passed", "summary stability strips auto approval body");
        AssertContainsText(extracted, "## Summary 📝", "summary stability keeps review summary heading");
        AssertContainsText(extracted, "Looks good overall.", "summary stability keeps review summary body");
    }

    private static void TestReviewHistoryArtifactsRenderJsonAndMarkdown() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Test title", "Body", false,
            "abc1234", "base", Array.Empty<string>(), "owner/repo", false, null);
        var snapshot = new ReviewHistorySnapshot {
            CurrentHeadSha = "abc1234",
            Rounds = new[] {
                new ReviewHistoryRound {
                    Sequence = 1,
                    Source = "intelligencex",
                    SummaryCommentId = 123,
                    ReviewedSha = "9999999",
                    HasMergeBlockers = true,
                    MergeBlockerStatus = "1 normalized item(s).",
                    Recommendation = "needs-work",
                    PositiveHighlights = new[] { "Clear parser split." },
                    RiskNotes = new[] { "Compatibility surface changed." },
                    FollowUps = new[] { "Add regression coverage." },
                    Findings = new[] {
                        new ReviewHistoryFinding {
                            Fingerprint = "todo-add-null-guard",
                            Section = "Todo List",
                            Text = "Add null guard in parser.",
                            Status = "open"
                        }
                    }
                }
            },
            OpenFindings = new[] {
                new ReviewHistoryFinding {
                    Fingerprint = "todo-add-null-guard",
                    Section = "Todo List",
                    Text = "Add null guard in parser.",
                    Status = "open"
                }
            },
            ExternalSummaries = new[] {
                new ReviewHistoryExternalSummary {
                    CommentId = 456,
                    Author = "claude",
                    Source = "external-bot",
                    Excerpt = "Claude summary: no additional blockers."
                }
            },
            ThreadSnapshot = new ReviewHistoryThreadSnapshot {
                ActiveCount = 1,
                ResolvedCount = 2,
                StaleCount = 1,
                Excerpts = new[] {
                    new ReviewHistoryThreadExcerpt {
                        ThreadId = "thread-1",
                        Status = "active",
                        Author = "reviewer",
                        Body = "Please add regression coverage.",
                        Path = "src/app.cs",
                        Line = 42
                    }
                }
            }
        };

        var json = ReviewRunner.BuildReviewHistoryArtifactJsonForTests(context, snapshot,
            "Review history snapshot:\n- Open findings confirmed on the current head.");
        var markdown = ReviewRunner.BuildReviewHistoryArtifactMarkdownForTests(context, snapshot,
            "Review history snapshot:\n- Open findings confirmed on the current head.");

        AssertContainsText(json, "\"schema\": \"intelligencex.review.history.v1\"",
            "review history artifact json schema");
        AssertContainsText(json, "\"repository\": \"owner/repo\"", "review history artifact json repository");
        AssertContainsText(json, "\"openFindings\": 1", "review history artifact json open count");
        AssertContainsText(json, "\"fingerprint\": \"todo-add-null-guard\"",
            "review history artifact json finding fingerprint");
        AssertContainsText(json, "\"externalSummaries\": 1",
            "review history artifact json external summary count");
        AssertContainsText(json, "\"author\": \"claude\"", "review history artifact json external author");
        AssertContainsText(json, "\"recommendation\": \"needs-work\"",
            "review history artifact json recommendation");
        AssertContainsText(json, "\"positiveHighlights\"", "review history artifact json positive highlights");
        AssertContainsText(markdown, "# IntelligenceX Review History Artifact",
            "review history artifact markdown title");
        AssertContainsText(markdown, "- Open findings: `1`", "review history artifact markdown open count");
        AssertContainsText(markdown, "- External bot summaries: `1`",
            "review history artifact markdown external count");
        AssertContainsText(markdown, "Supporting context only; these summaries are not treated as IX-owned blocker state.",
            "review history artifact markdown external safety label");
        AssertContainsText(markdown, "recommendation `needs-work`",
            "review history artifact markdown recommendation");
        AssertContainsText(markdown, "Good: Clear parser split.",
            "review history artifact markdown positive signal");
        AssertContainsText(markdown, "reviewer (src/app.cs:42): Please add regression coverage.",
            "review history artifact markdown thread excerpt");
        AssertContainsText(markdown, "## Prompt Section", "review history artifact markdown prompt section");
    }

    private static void TestReviewHistoryMarkerRoundTripsStickyLedger() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        settings.History.MaxRounds = 6;
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Test title", "Body", false,
            "abc2222", "base", Array.Empty<string>(), "owner/repo", false, null);
        var priorSnapshot = new ReviewHistorySnapshot {
            CurrentHeadSha = "abc2222",
            Rounds = new[] {
                new ReviewHistoryRound {
                    Sequence = 1,
                    Source = "intelligencex",
                    ReviewedSha = "abc1111",
                    HasMergeBlockers = true,
                    MergeBlockerStatus = "1 normalized item(s).",
                    Findings = new[] {
                        new ReviewHistoryFinding {
                            Fingerprint = "todo-add-null-guard",
                            Section = "Todo List",
                            Text = "Add null guard in parser.",
                            Status = "open"
                        }
                    }
                }
            }
        };
        var currentComment = ReviewFormatter.BuildComment(context, string.Join("\n", new[] {
                "## Summary 📝",
                "Looks good overall.",
                "",
                "## Todo List ✅",
                "None.",
                "",
                "## Critical Issues ⚠️",
                "None.",
                "",
                "## Excellent Aspects ✨",
                "- Keeps the parser path simple.",
                "",
                "## Other Issues 🧯",
                "- Watch the compatibility surface.",
                "",
                "## Recommendations 💡",
                "- Add a focused regression test."
            }), settings, inlineSupported: true, inlineSuppressed: false, autoResolveNote: string.Empty,
            budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);

        var withMarker = ReviewHistoryMarker.AppendOrReplace(currentComment, priorSnapshot, context, settings);

        AssertContainsText(withMarker, "<!-- intelligencex:history:v1 ", "review history marker appended");
        AssertEqual(true, ReviewHistoryMarker.TryReadRounds(withMarker, "abc2222", settings, out var rounds),
            "review history marker parses");
        AssertEqual(2, rounds.Count, "review history marker preserves prior and current rounds");
        AssertEqual("abc1111", rounds[0].ReviewedSha, "review history marker prior round sha");
        AssertEqual("abc2222", rounds[1].ReviewedSha, "review history marker current round sha");
        AssertEqual(true, rounds[1].SameHeadAsCurrent, "review history marker current round same-head");
        AssertEqual("approve", rounds[1].Recommendation, "review history marker stores current recommendation");
        AssertEqual("Keeps the parser path simple.", rounds[1].PositiveHighlights[0],
            "review history marker stores positive highlight");
        AssertEqual("Watch the compatibility surface.", rounds[1].RiskNotes[0],
            "review history marker stores risk note");
        AssertEqual("Add a focused regression test.", rounds[1].FollowUps[0],
            "review history marker stores follow-up");

        var issueComments = new[] { new IssueComment(20, withMarker, "intelligencex-review") };
        var snapshot = ReviewHistoryBuilder.BuildSnapshot(issueComments, "abc2222",
            Array.Empty<PullRequestReviewThread>(), settings);
        AssertEqual(2, snapshot.Rounds.Count, "review history builder loads marker rounds");
        AssertEqual(0, snapshot.ResolvedSinceLastRound.Count,
            "review history marker ledger does not infer cross-head resolution");
        AssertDoesNotContainText(ReviewHistoryMarker.Remove(withMarker), "<!-- intelligencex:history:v1 ",
            "review history marker can be stripped before parsing visible summary");
        var promptSection = ReviewHistoryBuilder.Render(snapshot);
        AssertContainsText(promptSection, "Prior-head IX merge blockers from sticky summary",
            "review history marker ledger preserves prior-head context for prompt");
        AssertContainsText(promptSection, "IX recommendation: Approve.",
            "review history marker ledger renders recommendation in prompt context");
        AssertContainsText(promptSection, "IX Good: Keeps the parser path simple.",
            "review history marker ledger renders positive highlights in prompt context");
        AssertContainsText(promptSection, "[todo] Add null guard in parser.",
            "review history marker ledger renders prior finding in prompt context");
        var block = ReviewHistoryBuilder.BuildCommentBlock(snapshot);
        AssertContainsText(block, "- **Tracked rounds:** 2 IX rounds; 1 round on current head.",
            "review history comment block renders tracked round count");
        AssertDoesNotContainText(block, "Keeps the parser path simple.",
            "review history comment block does not duplicate positive highlight text");
    }

    private static void TestReviewStateBlockRendersDeterministicRecommendation() {
        var settings = new ReviewSettings();
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good.",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None."
        });

        var block = ReviewStateBuilder.BuildCommentBlock(reviewBody, settings, reviewFailed: false);
        var state = ReviewStateBuilder.Build(reviewBody, settings, reviewFailed: false);

        AssertEqual("approve", state.Recommendation, "review state approve recommendation");
        AssertContainsText(block, "## Review State 🧭", "review state heading");
        AssertContainsText(block, "- **Recommendation:** Approve",
            "review state approve recommendation line");
        AssertContainsText(block, "- **Merge blockers:** none detected",
            "review state approve blocker line");
        AssertContainsText(block, "- **Evidence:** configured merge-blocker sections parsed with no open items",
            "review state approve evidence line");
    }

    private static void TestReviewStateBlockFailsClosedWithoutMergeBlockerSections() {
        var settings = new ReviewSettings();
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good."
        });

        var state = ReviewStateBuilder.Build(reviewBody, settings, reviewFailed: false);

        AssertEqual("manual-review", state.Recommendation,
            "review state missing sections requires manual review");
        AssertEqual("unknown", state.MergeBlockerLabel,
            "review state missing sections has unknown blockers");
    }

    private static void TestReviewStateBlockFailsClosedWhenRequiredBlockerSectionIsMissing() {
        var settings = new ReviewSettings();
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good.",
            "",
            "## Todo List ✅",
            "None."
        });

        var block = ReviewStateBuilder.BuildCommentBlock(reviewBody, settings, reviewFailed: false);
        var state = ReviewStateBuilder.Build(reviewBody, settings, reviewFailed: false);

        AssertEqual("manual-review", state.Recommendation,
            "review state missing required blocker section requires manual review");
        AssertEqual("unknown", state.MergeBlockerLabel,
            "review state missing required blocker section has unknown blockers");
        AssertContainsText(block, "- **Recommendation:** Manual review",
            "review state missing required blocker section recommendation line");
        AssertContainsText(block, "- **Merge blockers:** unknown",
            "review state missing required blocker section blocker line");
        AssertContainsText(block, "- **Evidence:** configured merge-blocker sections were missing or could not be normalized",
            "review state missing required blocker section evidence line");
    }

    private static void TestReviewHighlightsBlockSummarizesCurrentReviewSections() {
        var settings = new ReviewSettings();
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝",
            "This PR improves reviewer output and keeps merge gates deterministic.",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None.",
            "",
            "## Other Issues 🧯",
            "- Watch history marker size as more fields are added.",
            "",
            "## Tests / Coverage 🧪",
            "- Added coverage for output highlights.",
            "",
            "## Next Steps 🚀",
            "Looks ready after CI passes."
        });

        var block = ReviewHighlightsBuilder.BuildCommentBlock(reviewBody, settings, reviewFailed: false);

        AssertContainsText(block, "## Review Highlights ✨", "review highlights heading");
        AssertContainsText(block, "- **Good:** 1 positive signal captured in Summary / Excellent Aspects / Code Quality Assessment.",
            "review highlights good signal count");
        AssertContainsText(block, "- **Risks / Watch:** 1 watch item captured in Other Issues / Security & Performance / Backward Compatibility.",
            "review highlights risk signal count");
        AssertContainsText(block, "- **Tests:** 1 test or coverage note captured in Tests / Coverage / Test Quality.",
            "review highlights tests signal count");
        AssertContainsText(block, "- **Next:** 1 follow-up note captured in Next Steps / Recommendations.",
            "review highlights next signal count");
        AssertDoesNotContainText(block, "This PR improves reviewer output and keeps merge gates deterministic.",
            "review highlights does not duplicate summary text");
    }

    private static void TestReviewHighlightsBlockStopsAtNestedHeadings() {
        var settings = new ReviewSettings();
        var reviewBody = string.Join("\n", new[] {
            "## Summary 📝",
            "Looks good.",
            "",
            "## Todo List ✅",
            "None.",
            "",
            "## Critical Issues ⚠️",
            "None.",
            "",
            "## Other Issues 🧯",
            "- Watch the intended risk.",
            "",
            "### Extra Risk Detail",
            "- Do not count this nested risk.",
            "",
            "## Tests / Coverage 🧪",
            "Coverage is described.",
            "",
            "### Raw Test Notes",
            "- Do not count this nested test note.",
            "",
            "## Next Steps 🚀",
            "Looks merge-ready.",
            "",
            "### Static Analysis Policy 🧭",
            "- Config mode: respect",
            "- Packs: All Essentials"
        });

        var block = ReviewHighlightsBuilder.BuildCommentBlock(reviewBody, settings, reviewFailed: false);

        AssertContainsText(block, "- **Good:** 1 positive signal captured in Summary / Excellent Aspects / Code Quality Assessment.",
            "review highlights stops next steps good fallback count");
        AssertContainsText(block, "- **Risks / Watch:** 1 watch item captured in Other Issues / Security & Performance / Backward Compatibility.",
            "review highlights stops risk capture at nested headings");
        AssertContainsText(block, "- **Tests:** 1 test or coverage note captured in Tests / Coverage / Test Quality.",
            "review highlights stops test capture at nested headings");
        AssertContainsText(block, "- **Next:** 1 follow-up note captured in Next Steps / Recommendations.",
            "review highlights stops next steps at nested headings");
        AssertDoesNotContainText(block, "2 watch items",
            "review highlights does not count nested risk bullets");
        AssertDoesNotContainText(block, "2 test or coverage notes",
            "review highlights does not count nested test bullets");
        AssertDoesNotContainText(block, "Looks merge-ready.",
            "review highlights does not duplicate next steps prose");
        AssertDoesNotContainText(block, "Config mode: respect",
            "review highlights excludes nested static analysis bullets");
    }

    private static void TestReviewHistoryMarkerKeepsLatestRoundsAndRecomputesHead() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        settings.History.MaxRounds = 2;
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Test title", "Body", false,
            "abc4444", "base", Array.Empty<string>(), "owner/repo", false, null);
        var priorSnapshot = new ReviewHistorySnapshot {
            Rounds = new[] {
                new ReviewHistoryRound { Sequence = 1, Source = "intelligencex", ReviewedSha = "abc1111" },
                new ReviewHistoryRound { Sequence = 2, Source = "intelligencex", ReviewedSha = "abc2222" },
                new ReviewHistoryRound {
                    Sequence = 3,
                    Source = "intelligencex",
                    ReviewedSha = "abc3333",
                    SameHeadAsCurrent = true
                }
            }
        };
        var currentComment = ReviewFormatter.BuildComment(context, string.Join("\n", new[] {
                "## Summary 📝",
                "Looks good overall.",
                "",
                "## Todo List ✅",
                "None."
            }), settings, inlineSupported: true, inlineSuppressed: false, autoResolveNote: string.Empty,
            budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);

        var withMarker = ReviewHistoryMarker.AppendOrReplace(currentComment, priorSnapshot, context, settings);

        AssertEqual(true, ReviewHistoryMarker.TryReadRounds(withMarker, "abc4444", settings, out var rounds),
            "review history marker parses trimmed rounds");
        AssertEqual(2, rounds.Count, "review history marker keeps max latest rounds");
        AssertEqual("abc3333", rounds[0].ReviewedSha, "review history marker keeps newest prior round");
        AssertEqual(false, rounds[0].SameHeadAsCurrent,
            "review history marker recomputes stale same-head value for prior round");
        AssertEqual("abc4444", rounds[1].ReviewedSha, "review history marker keeps current round");
        AssertEqual(true, rounds[1].SameHeadAsCurrent,
            "review history marker recomputes same-head value for current round");
    }

    private static void TestReviewHistoryMarkerTryReadRoundsKeepsNewestParsedRounds() {
        var writeSettings = new ReviewSettings();
        writeSettings.History.Enabled = true;
        writeSettings.History.MaxRounds = 6;
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Test title", "Body", false,
            "abc4444", "base", Array.Empty<string>(), "owner/repo", false, null);
        var priorSnapshot = new ReviewHistorySnapshot {
            Rounds = new[] {
                new ReviewHistoryRound { Sequence = 1, Source = "intelligencex", ReviewedSha = "abc1111" },
                new ReviewHistoryRound { Sequence = 2, Source = "intelligencex", ReviewedSha = "abc2222" },
                new ReviewHistoryRound { Sequence = 3, Source = "intelligencex", ReviewedSha = "abc3333" }
            }
        };
        var currentComment = ReviewFormatter.BuildComment(context, string.Join("\n", new[] {
                "## Summary 📝",
                "Looks good overall.",
                "",
                "## Todo List ✅",
                "None.",
                "",
                "## Critical Issues ⚠️",
                "None."
            }), writeSettings, inlineSupported: true, inlineSuppressed: false, autoResolveNote: string.Empty,
            budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);

        var withMarker = ReviewHistoryMarker.AppendOrReplace(currentComment, priorSnapshot, context, writeSettings);
        var readSettings = new ReviewSettings();
        readSettings.History.Enabled = true;
        readSettings.History.MaxRounds = 2;

        AssertEqual(true, ReviewHistoryMarker.TryReadRounds(withMarker, "abc4444", readSettings, out var rounds),
            "review history marker parses oversized marker payload");
        AssertEqual(2, rounds.Count, "review history marker read trim keeps configured max rounds");
        AssertEqual("abc3333", rounds[0].ReviewedSha,
            "review history marker read trim keeps newest prior round");
        AssertEqual("abc4444", rounds[1].ReviewedSha,
            "review history marker read trim keeps current round");
        AssertEqual(1, rounds[0].Sequence, "review history marker read trim resequences first kept round");
        AssertEqual(2, rounds[1].Sequence, "review history marker read trim resequences current round");
    }

    private static void TestReviewHistoryMarkerRequiresExactSameHeadSha() {
        var settings = new ReviewSettings();
        settings.History.Enabled = true;
        settings.History.MaxRounds = 6;
        var priorSnapshot = new ReviewHistorySnapshot {
            Rounds = new[] {
                new ReviewHistoryRound {
                    Sequence = 1,
                    Source = "intelligencex",
                    ReviewedSha = "abc1234",
                    SameHeadAsCurrent = true
                }
            }
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 12, "Test title", "Body", false,
            "abc1234567890", "base", Array.Empty<string>(), "owner/repo", false, null);
        var currentComment = ReviewFormatter.BuildComment(context, string.Join("\n", new[] {
                "## Summary 📝",
                "Looks good overall.",
                "",
                "## Todo List ✅",
                "None.",
                "",
                "## Critical Issues ⚠️",
                "None."
            }), settings, inlineSupported: true, inlineSuppressed: false, autoResolveNote: string.Empty,
            budgetNote: string.Empty, usageLine: string.Empty, findingsBlock: string.Empty);

        var withMarker = ReviewHistoryMarker.AppendOrReplace(currentComment, priorSnapshot, context, settings);

        AssertEqual(true, ReviewHistoryMarker.TryReadRounds(withMarker, "abc1234567890", settings, out var rounds),
            "review history marker parses exact-sha fixture");
        AssertEqual(false, rounds[0].SameHeadAsCurrent,
            "review history marker does not treat abbreviated prior reviewed SHA as current head");
        AssertEqual("abc1234567890", rounds[^1].ReviewedSha,
            "review history marker stores full current head from formatter");
        AssertEqual(true, rounds[^1].SameHeadAsCurrent,
            "review history marker treats exact full current head as same-head");
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

    private static void TestReviewCommentIncludesHistoryBlock() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Test title", "Test body", false, "abc1234",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings();
        var comment = ReviewFormatter.BuildComment(context, "## Summary 📝\nLooks good overall.", settings,
            inlineSupported: true, inlineSuppressed: false, autoResolveNote: string.Empty, budgetNote: string.Empty,
            usageLine: string.Empty, findingsBlock: string.Empty, historyBlock: string.Join("\n", new[] {
                "## History Progress 🔁",
                "",
                "Open on current head:",
                "- [todo] Add null guard in parser."
            }));

        AssertContainsText(comment, "## History Progress 🔁", "review comment history heading");
        AssertContainsText(comment, "Open on current head:", "review comment history body");
        AssertContainsText(comment, "## Summary 📝", "review comment still includes summary");
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
