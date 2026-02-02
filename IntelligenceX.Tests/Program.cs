using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !NET472
using IntelligenceX.Cli;
using IntelligenceX.Cli.Release;
using IntelligenceX.Cli.Setup.Host;
#endif
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Rpc;
#if INTELLIGENCEX_REVIEWER
using IntelligenceX.Reviewer;
#endif

namespace IntelligenceX.Tests;

internal static class Program {
    private static int Main() {
        var failed = 0;
        failed += Run("Parse basic object", TestParseBasicObject);
        failed += Run("Serialize roundtrip", TestSerializeRoundtrip);
        failed += Run("Escape handling", TestEscapeHandling);
        failed += Run("RPC malformed JSON", TestRpcMalformedJson);
        failed += Run("RPC unknown shape", TestRpcUnknownShape);
        failed += Run("RPC notification", TestRpcNotification);
        failed += Run("RPC error hints", TestRpcErrorHints);
        failed += Run("Header transport message", TestHeaderTransportMessage);
        failed += Run("Header transport truncated", TestHeaderTransportTruncated);
        failed += Run("Copilot idle event", TestCopilotIdleEvent);
        failed += Run("ChatGPT usage parse", TestChatGptUsageParse);
        failed += Run("ChatGPT usage cache invalid JSON", TestChatGptUsageCacheInvalidJson);
#if !NET472
        failed += Run("Setup args reject skip+update", TestSetupArgsRejectSkipUpdate);
        failed += Run("GitHub secrets reject empty value", TestGitHubSecretsRejectEmptyValue);
        failed += Run("Release reviewer env token", TestReleaseReviewerEnvToken);
#endif
#if INTELLIGENCEX_REVIEWER
        failed += Run("Cleanup normalize allowed edits", TestCleanupNormalizeAllowedEdits);
        failed += Run("Cleanup clamp confidence", TestCleanupClampConfidence);
        failed += Run("Cleanup result parse fenced", TestCleanupResultParseFenced);
        failed += Run("Cleanup result parse embedded", TestCleanupResultParseEmbedded);
        failed += Run("Cleanup template path guard", TestCleanupTemplatePathGuard);
        failed += Run("Inline comments extract", TestInlineCommentsExtract);
        failed += Run("Inline comments backticks", TestInlineCommentsBackticks);
        failed += Run("Inline comments snippet header", TestInlineCommentsSnippetHeader);
        failed += Run("Review thread inline key", TestReviewThreadInlineKey);
        failed += Run("Review thread inline key bots only", TestReviewThreadInlineKeyBotsOnly);
        failed += Run("Review retry transient", TestReviewRetryTransient);
        failed += Run("Review retry non-transient", TestReviewRetryNonTransient);
        failed += Run("Review retry rethrows", TestReviewRetryRethrows);
        failed += Run("Review retry extra attempt", TestReviewRetryExtraAttempt);
        failed += Run("Review failure marker", TestReviewFailureMarker);
        failed += Run("Review fail-open only transient", TestReviewFailOpenTransientOnly);
        failed += Run("Review threads diff range normalize", TestReviewThreadsDiffRangeNormalize);
        failed += Run("Resolve-threads option parsing", TestResolveThreadsOptionParsing);
        failed += Run("Resolve-threads GHES endpoint", TestResolveThreadsEndpointResolution);
        failed += Run("Context deny invalid regex", TestContextDenyInvalidRegex);
        failed += Run("Context deny timeout", TestContextDenyTimeout);
        failed += Run("Review summary parser", TestReviewSummaryParser);
#endif

        Console.WriteLine(failed == 0 ? "All tests passed." : $"{failed} test(s) failed.");
        return failed == 0 ? 0 : 1;
    }

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

    private static int Run(string name, Action test) {
        try {
            test();
            Console.WriteLine($"[PASS] {name}");
            return 0;
        } catch (Exception ex) {
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            return 1;
        }
    }

    private static void TestParseBasicObject() {
        const string json = "{\"a\":1,\"b\":\"x\",\"c\":[true,null]}";
        var value = JsonLite.Parse(json).AsObject();
        AssertNotNull(value, "root");
        var root = value!;
        AssertEqual(1L, root.GetInt64("a"), "a");
        AssertEqual("x", root.GetString("b"), "b");
        var array = root.GetArray("c");
        AssertNotNull(array, "c");
        var items = array!;
        AssertEqual(JsonValueKind.Boolean, items[0].Kind, "c[0]");
        AssertEqual(true, items[0].AsBoolean(), "c[0] bool");
        AssertEqual(JsonValueKind.Null, items[1].Kind, "c[1]");
    }

    private static void TestSerializeRoundtrip() {
        var obj = new JsonObject()
            .Add("name", "codex")
            .Add("count", 3L)
            .Add("items", new JsonArray().Add("a").Add("b"));

        var json = JsonLite.Serialize(obj);
        var parsed = JsonLite.Parse(json).AsObject();
        AssertNotNull(parsed, "parsed");
        var parsedObj = parsed!;
        AssertEqual("codex", parsedObj.GetString("name"), "name");
        AssertEqual(3L, parsedObj.GetInt64("count"), "count");
        var items = parsedObj.GetArray("items");
        AssertNotNull(items, "items");
        var parsedItems = items!;
        AssertEqual("a", parsedItems[0].AsString(), "items[0]");
        AssertEqual("b", parsedItems[1].AsString(), "items[1]");
    }

    private static void TestChatGptUsageParse() {
        const string json = "{"
            + "\"plan_type\":\"pro\","
            + "\"rate_limit\":{\"allowed\":true,\"limit_reached\":false,\"primary_window\":{\"used_percent\":12.5,\"limit_window_seconds\":18000,\"reset_after_seconds\":120}},"
            + "\"code_review_rate_limit\":{\"allowed\":true,\"limit_reached\":false},"
            + "\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":4.52,\"approx_local_messages\":[1,6]}"
            + "}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "usage json");
        var snapshot = ChatGptUsageSnapshot.FromJson(obj!);
        AssertEqual("pro", snapshot.PlanType, "plan type");
        AssertNotNull(snapshot.RateLimit, "rate limit");
        AssertEqual(true, snapshot.RateLimit!.Allowed, "rate allowed");
        AssertNotNull(snapshot.RateLimit.PrimaryWindow, "primary window");
        AssertEqual(18000L, snapshot.RateLimit.PrimaryWindow!.LimitWindowSeconds, "window seconds");
        AssertNotNull(snapshot.CodeReviewRateLimit, "code review rate limit");
        AssertNotNull(snapshot.Credits, "credits");
        AssertEqual(true, snapshot.Credits!.HasCredits, "credits has");
    }

    private static void TestChatGptUsageCacheInvalidJson() {
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-usage-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{invalid");
            var ok = ChatGptUsageCache.TryLoad(out var entry, path);
            AssertEqual(false, ok, "cache parse ok");
            AssertEqual(null, entry, "cache entry null");
        } finally {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestEscapeHandling() {
        const string json = "{\"text\":\"line1\\nline2\\t\\\\\"}";
        var obj = JsonLite.Parse(json).AsObject();
        AssertNotNull(obj, "text root");
        var textObj = obj!;
        var text = textObj.GetString("text");
        AssertEqual("line1\nline2\t\\", text, "text");
        var serialized = JsonLite.Serialize(textObj);
        var roundtrip = JsonLite.Parse(serialized).AsObject();
        AssertNotNull(roundtrip, "roundtrip");
        var roundtripObj = roundtrip!;
        AssertEqual(text, roundtripObj.GetString("text"), "roundtrip text");
    }

    private static void TestRpcMalformedJson() {
        var client = new JsonRpcClient(_ => Task.CompletedTask);
        var fired = false;
        client.ProtocolError += (_, _) => fired = true;
        client.HandleLine("{invalid");
        AssertEqual(true, fired, "protocol error fired");
    }

    private static void TestRpcUnknownShape() {
        var client = new JsonRpcClient(_ => Task.CompletedTask);
        var fired = false;
        client.ProtocolError += (_, _) => fired = true;
        client.HandleLine("{\"foo\":1}");
        AssertEqual(true, fired, "protocol error fired");
    }

    private static void TestRpcNotification() {
        var client = new JsonRpcClient(_ => Task.CompletedTask);
        string? method = null;
        JsonValue? param = null;
        client.NotificationReceived += (_, args) => {
            method = args.Method;
            param = args.Params;
        };
        client.HandleLine("{\"method\":\"notify\",\"params\":{\"value\":123}}");
        AssertEqual("notify", method, "method");
        AssertNotNull(param, "params");
        AssertEqual(123L, param!.AsObject()?.GetInt64("value"), "params.value");
    }

    private static void TestRpcErrorHints() {
        var hint = IntelligenceX.Rpc.JsonRpcErrorHints.GetHint(-32601);
        AssertEqual("Method not found", hint, "hint");
        var ex = new IntelligenceX.Rpc.JsonRpcException("test.method", new IntelligenceX.Rpc.JsonRpcError(-32601, "nope", null));
        AssertNotNull(ex.Hint, "exception hint");
    }

    private static void TestHeaderTransportMessage() {
        var payload = "{\"method\":\"ping\"}";
        var header = $"Content-Length: {Encoding.UTF8.GetByteCount(payload)}\r\n\r\n";
        var data = Encoding.UTF8.GetBytes(header + payload);
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        var transport = new HeaderDelimitedMessageTransport(input, output);
        string? message = null;
        transport.ReadLoopAsync(msg => message = msg, CancellationToken.None).GetAwaiter().GetResult();
        AssertEqual(payload, message, "message");
    }

    private static void TestHeaderTransportTruncated() {
        var payload = "{\"method\":\"ping\"}";
        var header = $"Content-Length: {Encoding.UTF8.GetByteCount(payload) + 10}\r\n\r\n";
        var data = Encoding.UTF8.GetBytes(header + payload);
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        var transport = new HeaderDelimitedMessageTransport(input, output);
        var thrown = false;
        try {
            transport.ReadLoopAsync(_ => { }, CancellationToken.None).GetAwaiter().GetResult();
        } catch (EndOfStreamException) {
            thrown = true;
        }
        AssertEqual(true, thrown, "truncated message");
    }

    private static void TestCopilotIdleEvent() {
        var json = new JsonObject()
            .Add("type", "session.idle")
            .Add("data", new JsonObject());
        var evt = CopilotSessionEvent.FromJson(json);
        AssertEqual(true, evt.IsIdle, "idle");
    }

#if INTELLIGENCEX_REVIEWER
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
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
            if (Directory.Exists(outsideRoot)) {
                Directory.Delete(outsideRoot, true);
            }
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

    private static void TestReviewFailOpenTransientOnly() {
        var transient = new HttpRequestException("network");
        var responseEnded = new IOException("ResponseEnded");
        var nonTransient = new InvalidOperationException("logic");
        AssertEqual(true, ReviewRunner.IsTransient(transient), "transient true");
        AssertEqual(true, ReviewRunner.IsTransient(responseEnded), "response ended transient");
        AssertEqual(false, ReviewRunner.IsTransient(nonTransient), "non-transient false");
    }

    private static void TestReviewThreadsDiffRangeNormalize() {
        AssertEqual("current", ReviewSettings.NormalizeDiffRange("current", "pr-base"), "diff current");
        AssertEqual("pr-base", ReviewSettings.NormalizeDiffRange("pr_base", "current"), "diff pr-base");
        AssertEqual("first-review", ReviewSettings.NormalizeDiffRange("first_review", "current"), "diff first-review");
        AssertEqual("current", ReviewSettings.NormalizeDiffRange("unknown", "current"), "diff fallback");
    }

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
#endif

    private static void AssertEqual<T>(T expected, T? actual, string name) {
        if (!Equals(expected, actual)) {
            throw new InvalidOperationException($"Expected {name} to be '{expected}', got '{actual}'.");
        }
    }

    private static void AssertSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string name) {
        if (expected.Count != actual.Count) {
            throw new InvalidOperationException($"Expected {name} length {expected.Count}, got {actual.Count}.");
        }
        for (var i = 0; i < expected.Count; i++) {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Expected {name}[{i}] to be '{expected[i]}', got '{actual[i]}'.");
            }
        }
    }

    private static void AssertContains(IReadOnlyList<string> values, string expected, string name) {
        foreach (var value in values) {
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
        }
        throw new InvalidOperationException($"Expected {name} to contain '{expected}'.");
    }

    private static void AssertNotNull(object? value, string name) {
        if (value is null) {
            throw new InvalidOperationException($"Expected {name} to be non-null.");
        }
    }

    private static void AssertThrows<T>(Action action, string name) where T : Exception {
        try {
            action();
        } catch (T) {
            return;
        }
        throw new InvalidOperationException($"Expected {name} to throw {typeof(T).Name}.");
    }
}
