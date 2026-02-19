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

    private static void TestAutoResolveMissingInlineGateAllowsEmptySet() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMissingInline = true
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shouldRun = CallShouldAutoResolveMissingInlineThreads(settings, context, keys, inlineCommentsCount: 0);
        AssertEqual(true, shouldRun, "auto resolve missing inline gate empty set");
    }

    private static void TestAutoResolveMissingInlineGateRejectsNull() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMissingInline = true
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var shouldRun = CallShouldAutoResolveMissingInlineThreads(settings, context, null, inlineCommentsCount: 0);
        AssertEqual(false, shouldRun, "auto resolve missing inline gate null set");
    }

    private static void TestAutoResolveMissingInlineGateRejectsEmptyWhenInlineCommentsPresent() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMissingInline = true
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shouldRun = CallShouldAutoResolveMissingInlineThreads(settings, context, keys, inlineCommentsCount: 1);
        AssertEqual(false, shouldRun, "auto resolve missing inline gate empty mapped keys");
    }

    private static void TestReviewRetryTransient() {
        var attempts = 0;
        var result = ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                attempts++;
                if (attempts < 3) {
                    throw new IOException("transient");
                }
                return Task.FromResult("ok");
            },
            ex => ex is IOException,
            maxAttempts: 3,
            retryDelaySeconds: 1,
            retryMaxDelaySeconds: 1,
            backoffMultiplier: 2,
            retryJitterMinMs: 0,
            retryJitterMaxMs: 0,
            CancellationToken.None,
            describeError: null,
            extraAttempts: 0,
            extraRetryPredicate: null,
            retryState: null).GetAwaiter().GetResult();

        AssertEqual("ok", result, "retry result");
        AssertEqual(3, attempts, "retry attempts");
    }

    private static void TestReviewRetryNonTransient() {
        var attempts = 0;
        var thrown = false;
        try {
            ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                    attempts++;
                    throw new InvalidOperationException("nope");
                },
                ex => ex is IOException,
                maxAttempts: 3,
                retryDelaySeconds: 1,
                retryMaxDelaySeconds: 1,
                backoffMultiplier: 2,
                retryJitterMinMs: 0,
                retryJitterMaxMs: 0,
                CancellationToken.None,
                describeError: null,
                extraAttempts: 0,
                extraRetryPredicate: null,
                retryState: null).GetAwaiter().GetResult();
        } catch (InvalidOperationException) {
            thrown = true;
        }

        AssertEqual(true, thrown, "non-transient thrown");
        AssertEqual(1, attempts, "non-transient attempts");
    }

    private static void TestReviewRetryRethrows() {
        var attempts = 0;
        var ex = new IOException("boom");
        try {
            ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                    attempts++;
                    throw ex;
                },
                _ => true,
                maxAttempts: 2,
                retryDelaySeconds: 1,
                retryMaxDelaySeconds: 1,
                backoffMultiplier: 2,
                retryJitterMinMs: 0,
                retryJitterMaxMs: 0,
                CancellationToken.None,
                describeError: null,
                extraAttempts: 0,
                extraRetryPredicate: null,
                retryState: null).GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected exception.");
        } catch (IOException caught) {
            AssertEqual(true, ReferenceEquals(ex, caught), "retry exception instance");
        }

        AssertEqual(2, attempts, "retry attempts rethrow");
    }

    private static void TestReviewRetryExtraAttempt() {
        var attempts = 0;
        var result = ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                attempts++;
                if (attempts == 1) {
                    throw new IOException("ResponseEnded");
                }
                return Task.FromResult("ok");
            },
            ex => ex is IOException,
            maxAttempts: 1,
            retryDelaySeconds: 1,
            retryMaxDelaySeconds: 1,
            backoffMultiplier: 2,
            retryJitterMinMs: 0,
            retryJitterMaxMs: 0,
            CancellationToken.None,
            describeError: null,
            extraAttempts: 1,
            extraRetryPredicate: ReviewDiagnostics.IsResponseEnded,
            retryState: null).GetAwaiter().GetResult();

        AssertEqual("ok", result, "retry extra result");
        AssertEqual(2, attempts, "retry extra attempts");
    }

    private static void TestReviewFailureMarker() {
        var settings = new ReviewSettings { Diagnostics = true };
        var body = ReviewDiagnostics.BuildFailureBody(new IOException("ResponseEnded"), settings, null, null);
        AssertEqual(true, ReviewDiagnostics.IsFailureBody(body), "failure marker");
    }

    private static void TestReviewFailureBodyRedactsErrors() {
        var settings = new ReviewSettings { Diagnostics = true };
        var body = ReviewDiagnostics.BuildFailureBody(new InvalidOperationException("Sensitive info"), settings, null, null);
        if (body.Contains("Sensitive info", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected failure body to omit raw exception details.");
        }
    }

    private static void TestFailureSummaryCommentUpdate() {
        var commentId = 42L;
        string? body = null;
        var hits = 0;
        using var server = new LocalHttpServer(request => {
            if (!string.Equals(request.Method, "PATCH", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!string.Equals(request.Path, $"/repos/owner/repo/issues/comments/{commentId}", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            hits++;
            body = request.Body;
            return new HttpResponse("{\"id\":42,\"body\":\"ok\"}");
        });

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", "Body", false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAI,
            OpenAITransport = OpenAITransportKind.Native
        };

        var updated = IntelligenceX.Reviewer.ReviewerApp.TryUpdateFailureSummaryAsync("token", server.BaseUri.ToString().TrimEnd('/'),
                context, settings, commentId, new InvalidOperationException("boom"), false)
            .GetAwaiter().GetResult();
        AssertEqual(true, updated, "failure summary update");
        AssertEqual(1, hits, "failure summary update hits");
        AssertContainsText(body ?? string.Empty, ReviewDiagnostics.FailureMarker, "failure summary marker");
    }

    private static void TestReviewFailOpenTransientOnly() {
        var transient = new HttpRequestException("network");
        var responseEnded = new IOException("ResponseEnded");
        var nonTransient = new InvalidOperationException("logic");
        AssertEqual(true, ReviewRunner.IsTransient(transient), "transient true");
        AssertEqual(true, ReviewRunner.IsTransient(responseEnded), "response ended transient");
        AssertEqual(false, ReviewRunner.IsTransient(nonTransient), "non-transient false");
    }

    private static void TestReviewFailOpenDecision() {
        var transient = new HttpRequestException("network");
        var nonTransient = new InvalidOperationException("logic");
        var settings = new ReviewSettings {
            FailOpen = true,
            FailOpenTransientOnly = true
        };
        AssertEqual(true, ReviewRunner.ShouldFailOpen(settings, transient), "fail-open transient");
        AssertEqual(false, ReviewRunner.ShouldFailOpen(settings, nonTransient), "fail-open non-transient gated");

        settings.FailOpenTransientOnly = false;
        AssertEqual(true, ReviewRunner.ShouldFailOpen(settings, nonTransient), "fail-open non-transient allowed");
    }

    private static void TestPreflightTimeout() {
        using var server = new LocalHttpServer(_ => {
            Thread.Sleep(200);
            return new HttpResponse("{}");
        });
        var options = new OpenAINativeOptions {
            ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/')
        };
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromMilliseconds(50));
            throw new InvalidOperationException("Expected timeout.");
        } catch (TimeoutException) {
            // expected
        }
    }

    private static void TestPreflightSocketFailure() {
        var options = new OpenAINativeOptions {
            ChatGptApiBaseUrl = "http://127.0.0.1:1"
        };
        static bool IsExpectedSocketFailureMessage(string message) {
            return message.Contains("Connectivity preflight failed", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Connectivity preflight timed out", StringComparison.OrdinalIgnoreCase);
        }
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Expected socket failure.");
        } catch (InvalidOperationException ex) {
            var message = ex.Message ?? string.Empty;
            var isSocketFailure = IsExpectedSocketFailureMessage(message);
            AssertEqual(true, isSocketFailure, "preflight socket failure");
        } catch (TimeoutException ex) {
            var message = ex.Message ?? string.Empty;
            var isSocketFailure = IsExpectedSocketFailureMessage(message);
            AssertEqual(true, isSocketFailure, "preflight socket failure timeout");
        }
    }

    private static void TestPreflightAuthStatusesAreReachable() {
        var statuses = new[] {
            (Code: 401, Reason: "Unauthorized"),
            (Code: 403, Reason: "Forbidden")
        };
        foreach (var status in statuses) {
            using var server = new LocalHttpServer(_ => new HttpResponse("{}", null, status.Code, status.Reason));
            var options = new OpenAINativeOptions {
                ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/')
            };
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
        }
    }

    private static void TestPreflightNonSuccessStatus() {
        using var server = new LocalHttpServer(_ => new HttpResponse("{}", null, 500, "Server Error"));
        var options = new OpenAINativeOptions {
            ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/')
        };
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Expected non-success status.");
        } catch (HttpRequestException ex) {
            AssertContainsText(ex.Message, "HTTP 500", "preflight non-2xx");
        }
    }

    private static void TestPreflightDnsFailureMapping() {
        var httpException = new HttpRequestException("dns failed", new SocketException((int)SocketError.HostNotFound));
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), false);
        AssertNotNull(mapped, "preflight dns mapped");
        AssertEqual(true, mapped is InvalidOperationException, "preflight dns mapped type");
        AssertContainsText(mapped!.Message, "DNS resolution", "preflight dns mapped message");
    }

    private static void TestPreflightSocketFailureMapping() {
        var httpException = new HttpRequestException("connect failed", new SocketException((int)SocketError.ConnectionRefused));
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), false);
        AssertNotNull(mapped, "preflight socket mapped");
        AssertEqual(true, mapped is InvalidOperationException, "preflight socket mapped type");
        AssertContainsText(mapped!.Message, "network connectivity", "preflight socket mapped message");
    }

    private static void TestPreflightHttpStatusMappingBypass() {
        var httpException = new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), false);
        AssertEqual<Exception?>(null, mapped, "preflight status mapping bypass");
    }

    private static void TestPreflightCancellationRequestedMappingBypass() {
        var httpException = new HttpRequestException("cancelled", new TaskCanceledException("cancelled"));
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), true);
        AssertEqual<Exception?>(null, mapped, "preflight cancellation mapping bypass");
    }

    private static void TestReviewConfigValidatorAllowsAdditionalProperties() {
        var result = RunConfigValidation("{\"review\":{\"extraSetting\":true}}");
        AssertEqual(true, result is not null, "validator result");
        AssertEqual(0, result!.Warnings.Count, "additional properties should not warn");
        AssertEqual(0, result.Errors.Count, "additional properties should not error");
    }

    private static void TestReviewConfigValidatorInvalidEnum() {
        var result = RunConfigValidation("{\"review\":{\"length\":\"SHORT\"}}");
        AssertEqual(true, result is not null, "validator result");
        AssertEqual(true, result!.Errors.Count > 0, "invalid enum should error");
    }

}
#endif
