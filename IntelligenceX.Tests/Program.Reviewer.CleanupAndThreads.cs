namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestCleanupNormalizeAllowedEdits() {
        var normalized = CleanupSettings.NormalizeAllowedEdits(new[] { "Grammar", "unknown", "TITLE", " " });
        AssertSequenceEqual(new[] { "grammar", "title" }, normalized, "normalized");

        var defaults = CleanupSettings.NormalizeAllowedEdits(Array.Empty<string>());
        AssertContains(defaults, "formatting", "defaults formatting");
        AssertContains(defaults, "grammar", "defaults grammar");
        AssertContains(defaults, "title", "defaults title");
        AssertContains(defaults, "sections", "defaults sections");
    }

    private static void TestCleanupClampConfidence() {
        AssertEqual(0d, CleanupSettings.ClampConfidence(-1), "clamp below");
        AssertEqual(1d, CleanupSettings.ClampConfidence(2), "clamp above");
        AssertEqual(0.42d, CleanupSettings.ClampConfidence(0.42d), "clamp mid");
    }

    private static void TestCleanupResultParseFenced() {
        var input = "```json\n{ \"needs_cleanup\": true, \"confidence\": 0.9, \"title\": \"Fix\", \"body\": \"Body\" }\n```";
        var result = CleanupResult.TryParse(input);
        AssertNotNull(result, "result");
        AssertEqual(true, result!.NeedsCleanup, "needs cleanup");
        AssertEqual(0.9d, result.Confidence, "confidence");
        AssertEqual("Fix", result.Title, "title");
        AssertEqual("Body", result.Body, "body");
    }

    private static void TestCleanupResultParseEmbedded() {
        var input = "note {\"needsCleanup\":true,\"confidence\":2} trailing";
        var result = CleanupResult.TryParse(input);
        AssertNotNull(result, "result");
        AssertEqual(true, result!.NeedsCleanup, "needs cleanup");
        AssertEqual(1d, result.Confidence, "confidence");
    }

    private static void TestCleanupTemplatePathGuard() {
        var previous = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var root = Path.Combine(Path.GetTempPath(), "ix-tests-" + Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "ix-tests-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);

        var insidePath = Path.Combine(root, "template.md");
        var outsidePath = Path.Combine(outsideRoot, "template.md");
        File.WriteAllText(insidePath, "inside");
        File.WriteAllText(outsidePath, "outside");

        try {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", root);
            var settings = new CleanupSettings { TemplatePath = "template.md" };
            AssertEqual("inside", settings.ResolveTemplate(), "inside template");

            settings.TemplatePath = outsidePath;
            AssertEqual<string?>(null, settings.ResolveTemplate(), "outside template");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previous);
            DeleteDirectoryIfExistsWithRetries(root);
            DeleteDirectoryIfExistsWithRetries(outsideRoot);
        }
    }

    private static void TestInlineCommentsExtract() {
        var text = string.Join("\n", new[] {
            "Summary",
            "- ok",
            "",
            "Inline Comments (max 2)",
            "1) src/Foo.cs:42",
            "Use null-guard here.",
            "",
            "2) `src/Bar.cs:10`",
            "Nit: spacing.",
            "",
            "Tests / Coverage",
            "N/A"
        });

        var result = ReviewInlineParser.Extract(text, 5);
        AssertEqual(2, result.Comments.Count, "inline count");
        AssertEqual("src/Foo.cs", result.Comments[0].Path, "inline path 1");
        AssertEqual(42, result.Comments[0].Line, "inline line 1");
        AssertContains(result.Body.Split('\n'), "Summary", "inline strip summary");
        if (result.Body.Contains("Inline Comments", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Inline section was not stripped.");
        }
    }

    private static void TestInlineCommentsBackticks() {
        var text = string.Join("\n", new[] {
            "Inline Comments (max 2)",
            "1) src/Foo.cs:42",
            "Use `ConfigureAwait(false)` to avoid context capture.",
            "",
            "Tests / Coverage",
            "N/A"
        });

        var result = ReviewInlineParser.Extract(text, 5);
        AssertEqual(1, result.Comments.Count, "inline count backticks");
        AssertContains(result.Comments[0].Body.Split('\n'), "Use `ConfigureAwait(false)` to avoid context capture.", "inline body backticks");
    }

    private static void TestInlineCommentsSnippetHeader() {
        var text = string.Join("\n", new[] {
            "Inline Comments (max 1)",
            "1) `public string Slugify(string input)`",
            "Add a null guard to avoid exceptions.",
            "",
            "Tests / Coverage",
            "N/A"
        });

        var result = ReviewInlineParser.Extract(text, 5);
        AssertEqual(1, result.Comments.Count, "inline count snippet");
        AssertEqual(string.Empty, result.Comments[0].Path, "inline snippet path");
        AssertEqual(0, result.Comments[0].Line, "inline snippet line");
        AssertEqual("public string Slugify(string input)", result.Comments[0].Snippet, "inline snippet");
        AssertContains(result.Comments[0].Body.Split('\n'), "Add a null guard to avoid exceptions.", "inline snippet body");
    }

    private static void TestReviewThreadInlineKey() {
        var settings = new ReviewSettings { ReviewThreadsAutoResolveBotsOnly = true };
        var comment = new PullRequestReviewThreadComment(null, null, $"{ReviewFormatter.InlineMarker}\nFix it.", "intelligencex-review", "src/Foo.cs", 10);
        var thread = new PullRequestReviewThread("id", false, false, 1, new[] { comment });
        var ok = ReviewerApp.TryGetInlineThreadKey(thread, settings, out var key);
        AssertEqual(true, ok, "inline key ok");
        AssertEqual("src/Foo.cs:10", key, "inline key value");
    }

    private static void TestReviewThreadInlineKeyBotsOnly() {
        var settings = new ReviewSettings { ReviewThreadsAutoResolveBotsOnly = true };
        var comment = new PullRequestReviewThreadComment(null, null, $"{ReviewFormatter.InlineMarker}\nFix it.", "alice", "src/Foo.cs", 10);
        var thread = new PullRequestReviewThread("id", false, false, 1, new[] { comment });
        var ok = ReviewerApp.TryGetInlineThreadKey(thread, settings, out _);
        AssertEqual(false, ok, "inline key bots only");
    }

    private static void TestGitHubEventForkParsing() {
        var root = new JsonObject()
            .Add("repository", new JsonObject().Add("full_name", "base/repo"))
            .Add("pull_request", new JsonObject()
                .Add("title", "Test")
                .Add("number", 1)
                .Add("draft", false)
                .Add("author_association", "CONTRIBUTOR")
                .Add("head", new JsonObject()
                    .Add("sha", "head")
                    .Add("repo", new JsonObject()
                        .Add("full_name", "fork/repo")
                        .Add("fork", true)))
                .Add("base", new JsonObject()
                    .Add("sha", "base")));

        var context = GitHubEventParser.ParsePullRequest(root);
        AssertEqual(true, context.IsFork, "fork flag");
        AssertEqual(true, context.IsFromFork, "fork detection");
        AssertEqual("fork/repo", context.HeadRepoFullName, "head repo");
        AssertEqual("CONTRIBUTOR", context.AuthorAssociation, "author association");
    }

    private static void TestThreadAssessmentEvidenceParse() {
        const string json = "{\"threads\":[{\"id\":\"t1\",\"action\":\"resolve\",\"reason\":\"fixed\",\"evidence\":\"42: added guard\"}]}";
        var method = typeof(ReviewerApp).GetMethod("ParseThreadAssessments", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("ParseThreadAssessments not found.");
        }
        var result = method.Invoke(null, new object?[] { json }) as System.Collections.IEnumerable;
        if (result is null) {
            throw new InvalidOperationException("ParseThreadAssessments result invalid.");
        }
        object? first = null;
        foreach (var item in result) {
            first = item;
            break;
        }
        if (first is null) {
            throw new InvalidOperationException("No assessment parsed.");
        }
        var evidenceProp = first.GetType().GetProperty("Evidence");
        if (evidenceProp is null) {
            throw new InvalidOperationException("Evidence property not found.");
        }
        var evidence = evidenceProp.GetValue(first) as string;
        AssertEqual("42: added guard", evidence ?? string.Empty, "evidence");
    }

    private static void TestThreadTriageFallbackSummary() {
        var assessment = CreateThreadAssessment("1");
        var resolved = CreateThreadAssessmentArray(assessment, 1);
        var kept = CreateThreadAssessmentArray(null, 0);
        var summary = ReviewerApp.BuildFallbackTriageSummary(resolved, kept);
        AssertEqual("Auto-resolve: resolved 1 thread(s).", summary ?? string.Empty, "summary resolved");

        var keptOnly = CreateThreadAssessmentArray(CreateThreadAssessment("2"), 1);
        summary = ReviewerApp.BuildFallbackTriageSummary(CreateThreadAssessmentArray(null, 0), keptOnly);
        AssertEqual("Auto-resolve: kept 1 thread(s).", summary ?? string.Empty, "summary kept");

        summary = ReviewerApp.BuildFallbackTriageSummary(resolved, keptOnly);
        AssertEqual("Auto-resolve: resolved 1, kept 1 thread(s).", summary ?? string.Empty, "summary mixed");

        summary = ReviewerApp.BuildFallbackTriageSummary(CreateThreadAssessmentArray(null, 0),
            CreateThreadAssessmentArray(null, 0));
        AssertEqual(string.Empty, summary ?? string.Empty, "summary empty");
    }

    private static void TestThreadAutoResolveSummaryComment() {
        var method = typeof(ReviewerApp).GetMethod("BuildThreadAutoResolveSummaryComment",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("BuildThreadAutoResolveSummaryComment not found.");
        }
        var resolved = CreateThreadAssessmentArray(CreateThreadAssessment("1"), 1);
        var kept = CreateThreadAssessmentArray(null, 0);
        var summary = method.Invoke(null, new object?[] { resolved, kept, "abcdef1234567890", "current PR files" }) as string;
        AssertContainsText(summary ?? string.Empty, "auto-resolve summary", "summary header");
        AssertContainsText(summary ?? string.Empty, "Resolved:", "summary resolved label");
    }

    private static ReviewerApp.ThreadAssessment CreateThreadAssessment(string id) {
        return new ReviewerApp.ThreadAssessment(id, "resolve", "ok", string.Empty);
    }

    private static ReviewerApp.ThreadAssessment[] CreateThreadAssessmentArray(ReviewerApp.ThreadAssessment? item,
        int length) {
        var array = new ReviewerApp.ThreadAssessment[length];
        if (item is not null && length > 0) {
            array[0] = item;
        }
        return array;
    }

    private static void TestReviewThreadInlineKeyAllowlist() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveBotsOnly = true,
            ReviewThreadsAutoResolveBotLogins = new[] { "intelligencex-review" }
        };
        var comment = new PullRequestReviewThreadComment(null, null, $"{ReviewFormatter.InlineMarker}\nFix it.",
            "dependabot[bot]", "src/Foo.cs", 10);
        var thread = new PullRequestReviewThread("id", false, false, 1, new[] { comment });
        var ok = ReviewerApp.TryGetInlineThreadKey(thread, settings, out _);
        AssertEqual(false, ok, "inline key allowlist");
    }

    private static void TestReviewThreadInlineKeyCodexConnectorDefault() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveBotsOnly = true
        };
        var comment = new PullRequestReviewThreadComment(null, null, $"{ReviewFormatter.InlineMarker}\nFix it.",
            "chatgpt-codex-connector", "src/Foo.cs", 10);
        var thread = new PullRequestReviewThread("id", false, false, 1, new[] { comment });
        var ok = ReviewerApp.TryGetInlineThreadKey(thread, settings, out var key);
        AssertEqual(true, ok, "inline key codex connector");
        AssertEqual("src/Foo.cs:10", key, "inline key codex connector value");
    }

    private static void TestThreadResolveEvidenceCrossFileFallback() {
        var threadComment = new PullRequestReviewThreadComment(null, null, "Looks fixed.", "intelligencex-review",
            "src/Foo.cs", 80);
        var thread = new PullRequestReviewThread("thread-1", false, true, 1, new[] { threadComment });
        var files = new[] {
            new PullRequestFile("src/Bar.cs", "modified", "@@ -10,1 +10,2 @@\n- return oldValue;\n+ if (featureFlag) return newValue;\n+ return oldValue;")
        };

        var hasEvidence = CallHasValidResolveEvidence("10: if (featureFlag) return newValue;", thread, files, 4000);
        AssertEqual(true, hasEvidence, "cross-file evidence fallback");
    }

    private static void TestThreadResolveEvidenceUsesThreadContextWhenAvailable() {
        var threadComment = new PullRequestReviewThreadComment(null, null, "Looks fixed.", "intelligencex-review",
            "src/Foo.cs", 10);
        var thread = new PullRequestReviewThread("thread-2", false, false, 1, new[] { threadComment });
        var files = new[] {
            new PullRequestFile("src/Foo.cs", "modified", "@@ -10,1 +10,2 @@\n- return oldValue;\n+ return currentValue;\n+ return oldValue;"),
            new PullRequestFile("src/Bar.cs", "modified", "@@ -10,1 +10,2 @@\n- return oldValue;\n+ if (featureFlag) return newValue;\n+ return oldValue;")
        };

        var hasEvidence = CallHasValidResolveEvidence("10: if (featureFlag) return newValue;", thread, files, 4000);
        AssertEqual(false, hasEvidence, "thread-local evidence required when context is available");
    }

    private static void TestThreadResolveEvidenceCrossFileFallbackOnlyForStaleThreads() {
        var threadComment = new PullRequestReviewThreadComment(null, null, "Looks fixed.", "intelligencex-review",
            "src/Foo.cs", 80);
        var thread = new PullRequestReviewThread("thread-3", false, false, 1, new[] { threadComment });
        var files = new[] {
            new PullRequestFile("src/Bar.cs", "modified", "@@ -10,1 +10,2 @@\n- return oldValue;\n+ if (featureFlag) return newValue;\n+ return oldValue;")
        };

        var hasEvidence = CallHasValidResolveEvidence("10: if (featureFlag) return newValue;", thread, files, 4000);
        AssertEqual(false, hasEvidence, "cross-file fallback is limited to stale threads");
    }

    private static void TestThreadResolveEvidenceNormalizeSingleWrapperOnly() {
        var normalized = CallNormalizeResolveEvidence("\"`42: fixed null check`\"");
        AssertEqual("`42: fixed null check`", normalized, "single wrapper unwrap");
    }

    private static void TestThreadResolveEvidenceNormalizePreservesUnbalancedDelimiters() {
        var normalized = CallNormalizeResolveEvidence("\"42: fixed null check`");
        AssertEqual("\"42: fixed null check`", normalized, "unbalanced delimiters preserved");
    }

    private static void TestThreadResolveEvidenceDeduplicatesPatchPathScans() {
        var files = new[] {
            new PullRequestFile("src/Foo.cs", "modified", "@@ -1,1 +1,2 @@\n- return oldValue;\n+ return newValue;\n+ return oldValue;"),
            new PullRequestFile("src/Bar.cs", "modified", "@@ -5,1 +5,2 @@\n- old();\n+ updated();\n+ old();")
        };

        var scanned = CallCollectEvidenceScanPaths(files, "not-present-evidence", "src/Foo.cs");
        AssertEqual(2, scanned.Count, "scan count with preferred path");
        AssertEqual(2, scanned.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "scan paths are unique");
        AssertEqual(1, scanned.Count(path => path.Equals("src/Foo.cs", StringComparison.OrdinalIgnoreCase)),
            "preferred path scanned once");
    }

    private static void TestThreadTriageEmbedPlacement() {
        var method = typeof(ReviewerApp).GetMethod("ApplyEmbedPlacement", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("ApplyEmbedPlacement not found.");
        }
        var top = method.Invoke(null, new object?[] { "Body", "Triage", "top" }) as string;
        AssertEqual("Triage\n\nBody", top ?? string.Empty, "embed top");

        var bottom = method.Invoke(null, new object?[] { "Body", "Triage", "bottom" }) as string;
        AssertEqual("Body\n\nTriage", bottom ?? string.Empty, "embed bottom");

        var fallback = method.Invoke(null, new object?[] { "Body", "Triage", "unknown" }) as string;
        AssertEqual("Body\n\nTriage", fallback ?? string.Empty, "embed fallback");
    }

    private static void TestThreadAssessmentPromptSmoke() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 7, "Fix null reference", "Body", false, "head",
            "base", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            ReviewThreadsMaxComments = 5,
            MaxPatchChars = 4000
        };
        var comment = new PullRequestReviewThreadComment(null, null, "Please add a null check.", "intelligencex-review",
            "src/Foo.cs", 10);
        var thread = new PullRequestReviewThread("thread-1", false, false, 1, new[] { comment });
        var patch = "@@ -9,2 +9,3 @@\n- var x = y.Name;\n+ if (y == null) return;\n+ var x = y.Name;";
        var files = new[] { new PullRequestFile("src/Foo.cs", "modified", patch) };

        var prompt = CallBuildThreadAssessmentPrompt(context, new[] { thread }, files, settings, "current PR files");
        AssertContainsText(prompt, "Thread 1:", "thread prompt header");
        AssertContainsText(prompt, "location: src/Foo.cs:10", "thread prompt location");
        AssertContainsText(prompt, "diff context:", "thread prompt diff section");
        AssertContainsText(prompt, "PR: owner/repo #7", "thread prompt pr line");
    }

    private static void TestAutoResolveStaleThreadsSmoke() {
        var resolved = 0;
        var resolvePayloadObserved = false;
        var resolvedThreadIdObserved = false;
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!TryGetResolveThreadIdFromGraphQlPayload(request.Body, out var threadId)) {
                return null;
            }
            resolvePayloadObserved = true;
            resolvedThreadIdObserved = string.Equals(threadId, "thread-old", StringComparison.Ordinal);
            resolved++;
            return new HttpResponse("{\"data\":{\"resolveReviewThread\":{\"thread\":{\"id\":\"thread-old\",\"isResolved\":true}}}}");
        });
        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));

        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 10,
            ReviewThreadsAutoResolveBotsOnly = true
        };
        var staleBotComment = new PullRequestReviewThreadComment(null, null, "Fix this", "intelligencex-review", "src/Foo.cs", 1);
        var staleHumanComment = new PullRequestReviewThreadComment(null, null, "Please update", "alice", "src/Bar.cs", 2);
        var activeBotComment = new PullRequestReviewThreadComment(null, null, "Nit", "intelligencex-review", "src/Baz.cs", 3);
        var threads = new[] {
            new PullRequestReviewThread("thread-old", false, true, 1, new[] { staleBotComment }),
            new PullRequestReviewThread("thread-human", false, true, 1, new[] { staleHumanComment }),
            new PullRequestReviewThread("thread-active", false, false, 1, new[] { activeBotComment })
        };

        CallAutoResolveStaleThreads(github, threads, settings);
        AssertEqual(1, resolved, "stale auto-resolve smoke");
        AssertEqual(true, resolvePayloadObserved, "stale auto-resolve payload observed");
        AssertEqual(true, resolvedThreadIdObserved, "stale auto-resolve targets thread-old");
    }

    private static void TestAutoResolveStaleThreadsFallbackOnInsufficientScopes() {
        var primaryResolveAttempts = 0;
        var fallbackResolveAttempts = 0;
        using var primaryServer = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                return null;
            }
            primaryResolveAttempts++;
            return new HttpResponse(
                "{\"errors\":[{\"type\":\"INSUFFICIENT_SCOPES\",\"message\":\"missing pull_requests:write\"}],\"data\":{\"resolveReviewThread\":null}}");
        });
        using var fallbackServer = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                return null;
            }
            fallbackResolveAttempts++;
            return new HttpResponse("{\"data\":{\"resolveReviewThread\":{\"thread\":{\"id\":\"thread-old\",\"isResolved\":true}}}}");
        });
        using var github = new GitHubClient("token", primaryServer.BaseUri.ToString().TrimEnd('/'));
        using var fallbackGithub = new GitHubClient("token", fallbackServer.BaseUri.ToString().TrimEnd('/'));

        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 10,
            ReviewThreadsAutoResolveBotsOnly = true
        };
        var staleBotComment = new PullRequestReviewThreadComment(null, null, "Fix this", "intelligencex-review", "src/Foo.cs", 1);
        var threads = new[] {
            new PullRequestReviewThread("thread-old", false, true, 1, new[] { staleBotComment })
        };

        CallAutoResolveStaleThreads(github, fallbackGithub, threads, settings);
        AssertEqual(1, primaryResolveAttempts, "stale auto-resolve primary resolve attempts");
        AssertEqual(1, fallbackResolveAttempts, "stale auto-resolve fallback resolve attempts");
    }

    private static void TestAutoResolveStaleThreadsTreatsAlreadyResolvedAsSuccess() {
        var resolveAttempts = 0;
        var stateLookupAttempts = 0;
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                resolveAttempts++;
                return new HttpResponse(
                    "{\"errors\":[{\"type\":\"UNPROCESSABLE\",\"message\":\"Pull request review thread is already resolved.\"}],\"data\":{\"resolveReviewThread\":null}}");
            }
            if (request.Body.Contains("node(id:$id)", StringComparison.Ordinal)) {
                stateLookupAttempts++;
                return new HttpResponse(BuildGraphQlHydratedThreadResponse(
                    "thread-old",
                    true,
                    false,
                    ("Fix this", "src/Foo.cs", 1, "intelligencex-review")));
            }
            return null;
        });
        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));

        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 10,
            ReviewThreadsAutoResolveBotsOnly = true
        };
        var staleBotComment = new PullRequestReviewThreadComment(null, null, "Fix this", "intelligencex-review", "src/Foo.cs", 1);
        var threads = new[] {
            new PullRequestReviewThread("thread-old", false, true, 1, new[] { staleBotComment })
        };

        var originalError = Console.Error;
        using var errorWriter = new StringWriter();
        Console.SetError(errorWriter);
        try {
            CallAutoResolveStaleThreads(github, threads, settings);
        } finally {
            Console.SetError(originalError);
        }

        var errorOutput = errorWriter.ToString();
        AssertEqual(1, resolveAttempts, "stale auto-resolve already-resolved resolve attempts");
        AssertEqual(1, stateLookupAttempts, "stale auto-resolve already-resolved state lookup attempts");
        AssertContainsText(errorOutput, "resolved=1", "stale auto-resolve already-resolved summary resolved");
        AssertContainsText(errorOutput, "failed=0", "stale auto-resolve already-resolved summary failed");
        if (errorOutput.Contains("Failed to resolve review thread", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected already-resolved thread to avoid failure logging.");
        }
    }

    private static void TestAutoResolveMissingInlineEmptyKeys() {
        var resolved = 0;
        var resolvePayloadObserved = false;
        var resolvedThreadIdObserved = false;
        var inlineBody = $"{ReviewFormatter.InlineMarker}\nFix it.";
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (TryGetResolveThreadIdFromGraphQlPayload(request.Body, out var threadId)) {
                resolvePayloadObserved = true;
                resolvedThreadIdObserved = string.Equals(threadId, "thread1", StringComparison.Ordinal);
                resolved++;
                return new HttpResponse("{\"data\":{\"resolveReviewThread\":{\"thread\":{\"id\":\"thread1\",\"isResolved\":true}}}}");
            }
            return new HttpResponse(BuildGraphQlThreadsResponse(inlineBody));
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 1,
            ReviewThreadsMax = 1,
            ReviewThreadsMaxComments = 1,
            ReviewThreadsAutoResolveBotsOnly = false
        };

        CallAutoResolveMissingInlineThreads(github, context, new HashSet<string>(StringComparer.OrdinalIgnoreCase), settings);
        AssertEqual(1, resolved, "auto resolve missing inline empty keys");
        AssertEqual(true, resolvePayloadObserved, "auto resolve missing inline payload observed");
        AssertEqual(true, resolvedThreadIdObserved, "auto resolve missing inline targets thread1");
    }

    private static void TestAutoResolveMissingInlineBotsOnlySkipsHydratedNonBotThread() {
        var resolved = 0;
        var resolvePayloadObserved = false;
        var hydrationObserved = false;
        var inlineBody = $"{ReviewFormatter.InlineMarker}\nFix it.";
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                resolvePayloadObserved = true;
                resolved++;
                return new HttpResponse("{\"data\":{\"resolveReviewThread\":{\"thread\":{\"id\":\"thread1\",\"isResolved\":true}}}}");
            }
            if (request.Body.Contains("node(id:$id)", StringComparison.Ordinal)) {
                hydrationObserved = true;
                return new HttpResponse(BuildGraphQlHydratedThreadResponse(
                    "thread1",
                    (inlineBody, "src/Foo.cs", 10, "intelligencex-review"),
                    ("Please keep this open.", "src/Foo.cs", 10, "alice")));
            }
            return new HttpResponse(BuildGraphQlThreadsResponse(
                inlineBody,
                "src/Foo.cs",
                10,
                "intelligencex-review",
                "thread1",
                totalComments: 2));
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 1,
            ReviewThreadsMax = 1,
            ReviewThreadsMaxComments = 1,
            ReviewThreadsAutoResolveBotsOnly = true
        };

        CallAutoResolveMissingInlineThreads(github, context, new HashSet<string>(StringComparer.OrdinalIgnoreCase), settings);
        AssertEqual(true, hydrationObserved, "auto resolve missing inline hydration observed");
        AssertEqual(0, resolved, "auto resolve missing inline bots-only skips mixed author thread");
        AssertEqual(false, resolvePayloadObserved, "auto resolve missing inline bots-only no resolve payload");
    }

    private static void TestAutoResolveMissingInlineSkipsShiftedLineWithinWindow() {
        var resolved = 0;
        var resolvePayloadObserved = false;
        var inlineBody = $"{ReviewFormatter.InlineMarker}\nLegacy wording.";
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                resolvePayloadObserved = true;
                resolved++;
                return new HttpResponse("{\"data\":{\"resolveReviewThread\":{\"thread\":{\"id\":\"thread-shifted\",\"isResolved\":true}}}}");
            }
            return new HttpResponse(BuildGraphQlThreadsResponse(inlineBody, "src/Foo.cs", 12, "intelligencex-review",
                "thread-shifted"));
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 1,
            ReviewThreadsMax = 1,
            ReviewThreadsMaxComments = 1,
            ReviewThreadsAutoResolveBotsOnly = false
        };
        var expected = new HashSet<string>(CallBuildInlineMatchKeys("src/Foo.cs", 10, "Updated wording", null,
            "if (value == null) return;"), StringComparer.OrdinalIgnoreCase);

        CallAutoResolveMissingInlineThreads(github, context, expected, settings);
        AssertEqual(0, resolved, "auto resolve missing inline shifted line skipped");
        AssertEqual(false, resolvePayloadObserved, "auto resolve missing inline shifted line no resolve payload");
    }

    private static void TestAutoResolveMissingInlineSkipsSignatureMatchForRewordedBody() {
        var resolved = 0;
        var resolvePayloadObserved = false;
        var signatureMarker = CallBuildInlineSignatureMarker("src/Foo.cs", 24, "Original wording", null,
            "if (value is null) return;");
        AssertNotNull(signatureMarker, "signature marker");
        var inlineBody = $"{ReviewFormatter.InlineMarker}\n{signatureMarker}\nOriginal wording";
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (TryGetResolveThreadIdFromGraphQlPayload(request.Body, out _)) {
                resolvePayloadObserved = true;
                resolved++;
                return new HttpResponse("{\"data\":{\"resolveReviewThread\":{\"thread\":{\"id\":\"thread-signature\",\"isResolved\":true}}}}");
            }
            return new HttpResponse(BuildGraphQlThreadsResponse(inlineBody, "src/Foo.cs", 31, "intelligencex-review",
                "thread-signature"));
        });

        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMax = 1,
            ReviewThreadsMax = 1,
            ReviewThreadsMaxComments = 1,
            ReviewThreadsAutoResolveBotsOnly = false
        };
        var expected = new HashSet<string>(CallBuildInlineMatchKeys("src/Foo.cs", 24, "Completely reworded comment",
            null, "if (value is null) return;"), StringComparer.OrdinalIgnoreCase);

        CallAutoResolveMissingInlineThreads(github, context, expected, settings);
        AssertEqual(0, resolved, "auto resolve missing inline signature skipped");
        AssertEqual(false, resolvePayloadObserved, "auto resolve missing inline signature no resolve payload");
    }

    private static void TestResolveThreadPayloadParserRejectsInvalidJson() {
        var emptyPayloadResult = TryGetResolveThreadIdFromGraphQlPayload(string.Empty, out var emptyPayloadThreadId);
        AssertEqual(false, emptyPayloadResult, "resolve payload empty rejected");
        AssertEqual<string?>(null, emptyPayloadThreadId, "resolve payload empty id");

        var malformedPayloadResult = TryGetResolveThreadIdFromGraphQlPayload("{not-json}", out var malformedPayloadThreadId);
        AssertEqual(false, malformedPayloadResult, "resolve payload malformed rejected");
        AssertEqual<string?>(null, malformedPayloadThreadId, "resolve payload malformed id");

        const string noThreadIdPayload = "{\"query\":\"mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ id } } }\",\"variables\":{}}";
        var noThreadIdResult = TryGetResolveThreadIdFromGraphQlPayload(noThreadIdPayload, out var noThreadId);
        AssertEqual(false, noThreadIdResult, "resolve payload missing id rejected");
        AssertEqual<string?>(null, noThreadId, "resolve payload missing id value");
    }

    private static void TestThreadResolveIntegrationForbiddenDetection() {
        var ex = new InvalidOperationException("GitHub GraphQL request returned errors: {\"errors\":[{\"type\":\"INSUFFICIENT_SCOPES\"}]}");
        var forbidden = CallIsIntegrationForbidden(ex);
        AssertEqual(true, forbidden, "integration forbidden detects insufficient scopes");
    }

    private static void TestThreadResolveErrorFormattingIncludesFallback() {
        var message = CallBuildThreadResolveError(new InvalidOperationException("primary failed"),
            new InvalidOperationException("fallback failed"));
        AssertContainsText(message, "primary: primary failed", "thread resolve error includes primary");
        AssertContainsText(message, "fallback: fallback failed", "thread resolve error includes fallback");
    }

    private static bool TryGetResolveThreadIdFromGraphQlPayload(string body, out string? threadId) {
        threadId = null;
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        JsonObject? payload;
        try {
            payload = JsonLite.Parse(body).AsObject();
        } catch (FormatException) {
            return false;
        } catch (ArgumentNullException) {
            return false;
        }
        if (payload is null) {
            return false;
        }
        var query = payload.GetString("query");
        if (string.IsNullOrWhiteSpace(query) || !query.Contains("resolveReviewThread", StringComparison.Ordinal)) {
            return false;
        }
        threadId = payload.GetObject("variables")?.GetString("id");
        return !string.IsNullOrWhiteSpace(threadId);
    }

    private static string BuildGraphQlHydratedThreadResponse(string threadId,
        params (string Body, string Path, int Line, string Author)[] comments) {
        return BuildGraphQlHydratedThreadResponse(threadId, false, false, comments);
    }

    private static string BuildGraphQlHydratedThreadResponse(string threadId, bool isResolved, bool isOutdated,
        params (string Body, string Path, int Line, string Author)[] comments) {
        var sb = new StringBuilder();
        sb.Append("{\"data\":{\"node\":{\"id\":\"")
            .Append(EscapeJson(threadId))
            .Append("\",\"isResolved\":")
            .Append(isResolved ? "true" : "false")
            .Append(",\"isOutdated\":")
            .Append(isOutdated ? "true" : "false")
            .Append(",\"comments\":{\"totalCount\":")
            .Append(comments.Length)
            .Append(",\"nodes\":[");
        for (var i = 0; i < comments.Length; i++) {
            if (i > 0) {
                sb.Append(',');
            }
            var comment = comments[i];
            sb.Append("{\"databaseId\":")
                .Append(i + 1)
                .Append(",\"createdAt\":\"2024-01-01T00:00:00Z\",\"body\":\"")
                .Append(EscapeJson(comment.Body))
                .Append("\",\"path\":\"")
                .Append(EscapeJson(comment.Path))
                .Append("\",\"line\":")
                .Append(comment.Line.ToString())
                .Append(",\"author\":{\"login\":\"")
                .Append(EscapeJson(comment.Author))
                .Append("\"}}");
        }
        sb.Append("],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}");
        return sb.ToString();
    }



}
#endif
