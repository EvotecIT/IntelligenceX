using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Tools;
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
        failed += Run("Tool call parsing", TestToolCallParsing);
        failed += Run("Tool call invalid JSON", TestToolCallParsingInvalidJson);
        failed += Run("Tool output input", TestToolOutputInput);
        failed += Run("Turn response_id parsing", TestTurnResponseIdParsing);
        failed += Run("Tool definitions ordered", TestToolDefinitionOrdering);
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
        failed += Run("Review fail-open decision", TestReviewFailOpenDecision);
        failed += Run("Review config validator allows additional", TestReviewConfigValidatorAllowsAdditionalProperties);
        failed += Run("Review config validator invalid enum", TestReviewConfigValidatorInvalidEnum);
        failed += Run("Structured findings block", TestStructuredFindingsBlock);
        failed += Run("Trim patch hunk boundary", TestTrimPatchStopsAtHunkBoundary);
        failed += Run("Trim patch CRLF", TestTrimPatchPreservesCrlf);
        failed += Run("Trim patch keeps last hunk", TestTrimPatchKeepsLastHunk);
        failed += Run("Review intent applies focus", TestReviewIntentAppliesFocus);
        failed += Run("Review intent respects focus", TestReviewIntentRespectsFocus);
        failed += Run("Triage-only loads threads", TestTriageOnlyLoadsThreads);
        failed += Run("Review code host env", TestReviewCodeHostEnv);
        failed += Run("Azure auth scheme env", TestAzureAuthSchemeEnv);
        failed += Run("Azure auth scheme invalid env", TestAzureAuthSchemeInvalidEnv);
        failed += Run("Review threads diff range normalize", TestReviewThreadsDiffRangeNormalize);
        failed += Run("Copilot env allowlist config", TestCopilotEnvAllowlistConfig);
        failed += Run("Copilot inherit env default", TestCopilotInheritEnvironmentDefault);
        failed += Run("Copilot direct timeout validation", TestCopilotDirectTimeoutValidation);
        failed += Run("Resolve-threads option parsing", TestResolveThreadsOptionParsing);
        failed += Run("Resolve-threads GHES endpoint", TestResolveThreadsEndpointResolution);
        failed += Run("Filter files include-only", TestFilterFilesIncludeOnly);
        failed += Run("Filter files exclude-only", TestFilterFilesExcludeOnly);
        failed += Run("Filter files include+exclude", TestFilterFilesIncludeExclude);
        failed += Run("Filter files glob patterns", TestFilterFilesGlobPatterns);
        failed += Run("Filter files empty filters", TestFilterFilesEmptyFilters);
        failed += Run("Prompt language hints", TestPromptBuilderLanguageHints);
        failed += Run("Prompt language hints disabled", TestPromptBuilderLanguageHintsDisabled);
        failed += Run("Azure DevOps changes pagination", TestAzureDevOpsChangesPagination);
        failed += Run("Azure DevOps diff note zero iterations", TestAzureDevOpsDiffNoteZeroIterations);
        failed += Run("Azure DevOps error sanitization", TestAzureDevOpsErrorSanitization);
        failed += Run("Context deny invalid regex", TestContextDenyInvalidRegex);
        failed += Run("Context deny timeout", TestContextDenyTimeout);
        failed += Run("Review summary parser", TestReviewSummaryParser);
        failed += Run("Review usage summary line", TestReviewUsageSummaryLine);
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

    private static void TestToolCallParsing() {
        var output = new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("call_id", "call_1")
            .Add("name", "wsl_status")
            .Add("input", "{\"name\":\"Ubuntu\"}");
        var turn = TurnInfo.FromJson(new JsonObject()
            .Add("id", "turn1")
            .Add("output", new JsonArray().Add(output)));

        var calls = ToolCallParser.Extract(turn);
        AssertEqual(1, calls.Count, "tool call count");
        AssertEqual("call_1", calls[0].CallId, "tool call id");
        AssertEqual("wsl_status", calls[0].Name, "tool call name");
        AssertEqual("Ubuntu", calls[0].Arguments?.GetString("name"), "tool call arg");
    }

    private static void TestToolCallParsingInvalidJson() {
        var output = new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("call_id", "call_2")
            .Add("name", "wsl_status")
            .Add("input", "{invalid");
        var turn = TurnInfo.FromJson(new JsonObject()
            .Add("id", "turn2")
            .Add("output", new JsonArray().Add(output)));

        var calls = ToolCallParser.Extract(turn);
        AssertEqual(1, calls.Count, "tool call invalid count");
        AssertEqual("call_2", calls[0].CallId, "tool call invalid id");
        AssertEqual("wsl_status", calls[0].Name, "tool call invalid name");
        AssertEqual(null, calls[0].Arguments, "tool call invalid args");
    }

    private static void TestToolOutputInput() {
        var input = new ChatInput().AddToolOutput("call_42", "ok");
        var json = CallChatInputToJson(input);
        var item = json[0].AsObject();
        AssertNotNull(item, "tool output item");
        AssertEqual("custom_tool_call_output", item!.GetString("type"), "tool output type");
        AssertEqual("call_42", item.GetString("call_id"), "tool output call id");
        AssertEqual("ok", item.GetString("output"), "tool output value");
    }

    private static void TestTurnResponseIdParsing() {
        var turn = TurnInfo.FromJson(new JsonObject()
            .Add("id", "turn-response")
            .Add("response_id", "resp_snake"));
        AssertEqual("resp_snake", turn.ResponseId, "response_id");
    }

    private static void TestToolDefinitionOrdering() {
        var registry = new ToolRegistry();
        registry.Register(new TestTool("zeta"));
        registry.Register(new TestTool("Alpha"));
        registry.Register(new TestTool("beta"));

        var names = new List<string>();
        foreach (var definition in registry.GetDefinitions()) {
            names.Add(definition.Name);
        }

        AssertSequenceEqual(new[] { "Alpha", "beta", "zeta" }, names, "tool definition order");
    }

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

    private static void TestStructuredFindingsBlock() {
        var comments = new List<InlineReviewComment> {
            new("src/app.cs", 42, "Null check is missing."),
            new("", 0, "Snippet-only comment", "var x = 1;")
        };
        var block = ReviewFindingsBuilder.Build(comments);
        AssertEqual(true, block.Contains("<!-- intelligencex:findings -->", StringComparison.Ordinal), "findings marker");
        AssertEqual(true, block.Contains("\"path\":\"src/app.cs\"", StringComparison.Ordinal), "findings path");
        AssertEqual(true, block.Contains("\"line\":42", StringComparison.Ordinal), "findings line");
        AssertEqual(false, block.Contains("Snippet-only", StringComparison.Ordinal), "findings skips snippet-only");
    }

    private static void TestTrimPatchStopsAtHunkBoundary() {
        var patch = string.Join("\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a",
            "@@ -10,2 +10,2 @@",
            "-line10",
            "+line10a"
        });
        var cutIndex = patch.IndexOf("@@ -10", StringComparison.Ordinal);
        var trimmed = CallTrimPatch(patch, cutIndex + 4);
        AssertEqual(true, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk kept");
        AssertEqual(false, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "second hunk removed");
    }

    private static void TestTrimPatchPreservesCrlf() {
        var patch = string.Join("\r\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a"
        });
        var trimmed = CallTrimPatch(patch, patch.Length - 2);
        AssertEqual(true, trimmed.Contains("\r\n", StringComparison.Ordinal), "crlf preserved");
    }

    private static void TestTrimPatchKeepsLastHunk() {
        var patch = string.Join("\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,1 +1,1 @@",
            "-line1",
            "+line1a",
            "@@ -10,1 +10,1 @@",
            "-line10",
            "+line10a",
            "@@ -20,1 +20,1 @@",
            "-line20",
            "+line20a"
        });

        var header = string.Join("\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt"
        });
        var first = string.Join("\n", new[] {
            "@@ -1,1 +1,1 @@",
            "-line1",
            "+line1a"
        });
        var last = string.Join("\n", new[] {
            "@@ -20,1 +20,1 @@",
            "-line20",
            "+line20a"
        });
        var marker = "... (truncated) ...";
        var target = string.Join("\n", new[] { header, first, marker, last });
        var maxChars = target.Length + 1;

        var trimmed = CallTrimPatch(patch, maxChars);
        AssertEqual(true, trimmed.Contains("@@ -1,1 +1,1 @@", StringComparison.Ordinal), "first hunk kept");
        AssertEqual(true, trimmed.Contains("@@ -20,1 +20,1 @@", StringComparison.Ordinal), "last hunk kept");
        AssertEqual(false, trimmed.Contains("@@ -10,1 +10,1 @@", StringComparison.Ordinal), "middle hunk skipped");
        AssertEqual(true, trimmed.Contains(marker, StringComparison.Ordinal), "marker present");
    }

    private static void TestReviewIntentAppliesFocus() {
        var settings = new ReviewSettings();
        ReviewIntents.Apply("security", settings);
        AssertSequenceEqual(new[] { "security", "auth", "secrets" }, settings.Focus, "intent focus");
    }

    private static void TestReviewIntentRespectsFocus() {
        var settings = new ReviewSettings { Focus = new[] { "custom" } };
        ReviewIntents.Apply("performance", settings);
        AssertSequenceEqual(new[] { "custom" }, settings.Focus, "intent preserves focus");
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
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base", Array.Empty<string>());
        var extras = CallBuildExtrasAsync(github, context, settings, true);
        AssertEqual(1, extras.ReviewThreads.Count, "triage-only forces thread load");
    }

    private static void TestReviewThreadsDiffRangeNormalize() {
        AssertEqual("current", ReviewSettings.NormalizeDiffRange("current", "pr-base"), "diff current");
        AssertEqual("pr-base", ReviewSettings.NormalizeDiffRange("pr_base", "current"), "diff pr-base");
        AssertEqual("first-review", ReviewSettings.NormalizeDiffRange("first_review", "current"), "diff first-review");
        AssertEqual("current", ReviewSettings.NormalizeDiffRange("unknown", "current"), "diff fallback");
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

    private static ReviewConfigValidationResult? RunConfigValidation(string json) {
        var previousPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var tempDir = Path.Combine(Path.GetTempPath(), $"ix-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "reviewer.json");
        File.WriteAllText(configPath, json);
        Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
        try {
            return ReviewConfigValidator.ValidateCurrent();
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousPath);
            try {
                Directory.Delete(tempDir, true);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static string CallTrimPatch(string patch, int maxChars) {
        var method = typeof(ReviewerApp).GetMethod("TrimPatch", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("TrimPatch method not found.");
        }
        var result = method.Invoke(null, new object?[] { patch, maxChars }) as string;
        return result ?? string.Empty;
    }

    private static string CallFormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var method = typeof(ReviewerApp).GetMethod("FormatUsageSummary", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("FormatUsageSummary method not found.");
        }
        var result = method.Invoke(null, new object?[] { snapshot }) as string;
        return result ?? string.Empty;
    }

    private static ReviewContextExtras CallBuildExtrasAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, bool forceReviewThreads) {
        var method = typeof(ReviewerApp).GetMethod("BuildExtrasAsync", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("BuildExtrasAsync method not found.");
        }
        var task = method.Invoke(null, new object?[] {
            github,
            null,
            context,
            settings,
            CancellationToken.None,
            forceReviewThreads
        }) as Task<ReviewContextExtras>;
        if (task is null) {
            throw new InvalidOperationException("BuildExtrasAsync did not return a task.");
        }
        return task.GetAwaiter().GetResult();
    }

    private static string BuildGraphQlThreadsResponse() {
        return "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[{\"id\":\"thread1\",\"isResolved\":false,\"isOutdated\":false,\"comments\":{\"totalCount\":1,\"nodes\":[{\"databaseId\":1,\"createdAt\":\"2024-01-01T00:00:00Z\",\"body\":\"test\",\"path\":\"file.txt\",\"line\":10,\"author\":{\"login\":\"bot\"}}]}}],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}}";
    }

    private sealed record HttpRequest(string Method, string Path, string Body);
    private sealed record HttpResponse(string Body, IReadOnlyDictionary<string, string>? Headers = null);

    private sealed class LocalHttpServer : IDisposable {
        private readonly TcpListener _listener;
        private readonly Func<HttpRequest, HttpResponse?> _handler;
        private readonly Task _loopTask;
        private bool _disposed;

        public LocalHttpServer(Func<HttpRequest, string?> handler)
            : this(request => {
                var body = handler(request);
                return body is null ? null : new HttpResponse(body);
            }) { }

        public LocalHttpServer(Func<HttpRequest, HttpResponse?> handler) {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _loopTask = Task.Run(LoopAsync);
        }

        public Uri BaseUri { get; }

        private async Task LoopAsync() {
            try {
                while (true) {
                    TcpClient client;
                    try {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    } catch (ObjectDisposedException) {
                        break;
                    }
                    await HandleClientAsync(client).ConfigureAwait(false);
                }
            } catch {
                // Ignore listener failures in tests.
            }
        }

        private async Task HandleClientAsync(TcpClient client) {
            using var _ = client;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine)) {
                return;
            }
            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                return;
            }
            var method = parts[0];
            var path = parts[1];

            var contentLength = 0;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false))) {
                var headerParts = line.Split(':', 2);
                if (headerParts.Length == 2 &&
                    headerParts[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                    int.TryParse(headerParts[1].Trim(), out contentLength);
                }
            }

            var body = string.Empty;
            if (contentLength > 0) {
                var buffer = new char[contentLength];
                var read = 0;
                while (read < contentLength) {
                    var count = await reader.ReadAsync(buffer, read, contentLength - read).ConfigureAwait(false);
                    if (count == 0) {
                        break;
                    }
                    read += count;
                }
                body = new string(buffer, 0, read);
            }

            var response = _handler(new HttpRequest(method, path, body));
            if (response is null) {
                await WriteResponseAsync(stream, 404, "Not Found", "{}").ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(stream, 200, "OK", response.Body, response.Headers).ConfigureAwait(false);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusText, string body,
            IReadOnlyDictionary<string, string>? headers = null) {
            var payload = Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder();
            sb.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            sb.AppendLine("Content-Type: application/json");
            sb.AppendLine($"Content-Length: {payload.Length}");
            if (headers is not null) {
                foreach (var header in headers) {
                    sb.AppendLine($"{header.Key}: {header.Value}");
                }
            }
            sb.AppendLine("Connection: close");
            sb.AppendLine();
            var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _listener.Stop();
            try {
                _loopTask.GetAwaiter().GetResult();
            } catch {
                // ignored
            }
        }
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
            Array.Empty<string>());
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

    private static void AssertContainsText(string value, string expected, string name) {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf(expected, StringComparison.Ordinal) < 0) {
            throw new InvalidOperationException($"Expected {name} to contain '{expected}'.");
        }
    }

    private static void AssertNotNull(object? value, string name) {
        if (value is null) {
            throw new InvalidOperationException($"Expected {name} to be non-null.");
        }
    }

    private sealed class TestTool : ITool {
        public TestTool(string name) {
            Definition = new ToolDefinition(name, "test tool");
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(string.Empty);
        }
    }

    private static JsonArray CallChatInputToJson(ChatInput input) {
        var method = typeof(ChatInput).GetMethod("ToJson", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null) {
            throw new InvalidOperationException("ChatInput.ToJson method not found.");
        }
        var result = method.Invoke(input, Array.Empty<object>()) as JsonArray;
        return result ?? new JsonArray();
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
