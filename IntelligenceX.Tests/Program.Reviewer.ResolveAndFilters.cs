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
        AssertContainsText(block, "Open on current head:", "review history comment block open label");
        AssertContainsText(block, "[todo] Add null guard in parser.", "review history comment block open item");
        AssertContainsText(block, "Resolved since last round:", "review history comment block resolved label");
        AssertContainsText(block, "[critical] Cover the stale thread path.", "review history comment block resolved item");
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
        AssertContainsText(block, "Open on current head: none.", "review history latest same-head block no open findings");
        AssertContainsText(block, "Resolved since last round:", "review history latest same-head block resolved label");
        AssertContainsText(block, "[todo] Retry the alternate transport.",
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
        AssertContainsText(block, "Open on current head: none.", "review history cross-head block no open findings");
        AssertContainsText(block, "Resolved since last round: none newly resolved.",
            "review history cross-head block no resolved findings");
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
        AssertContainsText(block, "[todo] Add a new blocker ahead of the older one.",
            "review history capped same-head block keeps current extracted open finding");
        AssertDoesNotContainText(block, "Resolved since last round:",
            "review history capped same-head block omits resolved section when nothing was resolved");
        AssertDoesNotContainText(block, "[todo] Retry the alternate transport.",
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
        AssertContainsText(block, "Open on current head: none.",
            "review history latest same-head block has no current open finding after later resolution");
        AssertContainsText(block, "[todo] Retry the alternate transport.",
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
        AssertContainsText(block, "[todo] Retry the alternate transport.",
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
        AssertContainsText(block, "Open on current head: none.",
            "review history unparseable same-head block reports no normalized open findings");
        AssertContainsText(block, "Resolved since last round: none newly resolved.",
            "review history unparseable same-head block does not claim the missing finding resolved");
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
        AssertContainsText(block, "[todo] Add a new blocker ahead of the older one.",
            "review history partial-parse same-head block includes normalized current finding");
        AssertContainsText(rendered,
            "Additional merge-blocker lines were present but could not be normalized",
            "review history partial-parse same-head snapshot warns about incomplete normalization");
        AssertDoesNotContainText(block, "Resolved since last round:",
            "review history partial-parse same-head block omits resolved section when nothing was resolved");
        AssertDoesNotContainText(block, "[todo] Retry the alternate transport.",
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
        AssertContainsText(block, "Open on current head: unknown; merge-blocker lines could not be fully normalized.",
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
        AssertContainsText(block, "Resolved since last round: none newly resolved.",
            "review history duplicate previous statuses block does not claim a new resolution");
        AssertDoesNotContainText(block, "[todo] Retry the alternate transport.",
            "review history duplicate previous statuses block does not surface already-resolved duplicate as newly resolved");
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
            "## History Progress 🔁",
            "",
            "Open on current head:",
            "- [todo] Previous issue."
        });

        var comment = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: true,
            inlineSuppressed: false, autoResolveNote: string.Empty, budgetNote: string.Empty, usageLine: string.Empty,
            findingsBlock: string.Empty, historyBlock: historyBlock);
        var extracted = ReviewerApp.ExtractSummaryBodyForTests(comment, 10000) ?? string.Empty;

        AssertDoesNotContainText(extracted, "History Progress 🔁", "summary stability strips history progress heading");
        AssertDoesNotContainText(extracted, "Previous issue", "summary stability strips history progress body");
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
        AssertContainsText(markdown, "# IntelligenceX Review History Artifact",
            "review history artifact markdown title");
        AssertContainsText(markdown, "- Open findings: `1`", "review history artifact markdown open count");
        AssertContainsText(markdown, "- External bot summaries: `1`",
            "review history artifact markdown external count");
        AssertContainsText(markdown, "Supporting context only; these summaries are not treated as IX-owned blocker state.",
            "review history artifact markdown external safety label");
        AssertContainsText(markdown, "reviewer (src/app.cs:42): Please add regression coverage.",
            "review history artifact markdown thread excerpt");
        AssertContainsText(markdown, "## Prompt Section", "review history artifact markdown prompt section");
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
