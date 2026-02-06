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
using IntelligenceX.Analysis;
using IntelligenceX.Cli;
using IntelligenceX.Cli.Release;
using IntelligenceX.Cli.Setup.Host;
#endif
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.OpenAI.Tools;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;
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
        failed += Run("Config load invalid JSON", TestConfigLoadInvalidJsonThrows);
        failed += Run("Copilot idle event", TestCopilotIdleEvent);
        failed += Run("ChatGPT usage parse", TestChatGptUsageParse);
        failed += Run("ChatGPT usage cache invalid JSON", TestChatGptUsageCacheInvalidJson);
        failed += Run("Tool call parsing", TestToolCallParsing);
        failed += Run("Tool call invalid JSON", TestToolCallParsingInvalidJson);
        failed += Run("Tool output input", TestToolOutputInput);
        failed += Run("Turn response_id parsing", TestTurnResponseIdParsing);
        failed += Run("Tool definitions ordered", TestToolDefinitionOrdering);
        failed += Run("Tool runner max rounds", TestToolRunnerMaxRounds);
        failed += Run("Tool runner unregistered tool", TestToolRunnerUnregisteredTool);
        failed += Run("Tool runner parallel execution", TestToolRunnerParallelExecution);
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
        failed += Run("GitHub event fork parsing", TestGitHubEventForkParsing);
        failed += Run("Thread assessment evidence parse", TestThreadAssessmentEvidenceParse);
        failed += Run("Thread triage fallback summary", TestThreadTriageFallbackSummary);
        failed += Run("Review thread inline key allowlist", TestReviewThreadInlineKeyAllowlist);
        failed += Run("Thread auto-resolve summary comment", TestThreadAutoResolveSummaryComment);
        failed += Run("Thread triage embed placement", TestThreadTriageEmbedPlacement);
        failed += Run("Auto-resolve missing inline empty keys", TestAutoResolveMissingInlineEmptyKeys);
        failed += Run("Review retry transient", TestReviewRetryTransient);
        failed += Run("Review retry non-transient", TestReviewRetryNonTransient);
        failed += Run("Review retry rethrows", TestReviewRetryRethrows);
        failed += Run("Review retry extra attempt", TestReviewRetryExtraAttempt);
        failed += Run("Review failure marker", TestReviewFailureMarker);
        failed += Run("Review failure body redacts errors", TestReviewFailureBodyRedactsErrors);
        failed += Run("Failure summary comment update", TestFailureSummaryCommentUpdate);
        failed += Run("Review fail-open only transient", TestReviewFailOpenTransientOnly);
        failed += Run("Review fail-open decision", TestReviewFailOpenDecision);
        failed += Run("Preflight timeout", TestPreflightTimeout);
        failed += Run("Preflight socket failure", TestPreflightSocketFailure);
        failed += Run("Preflight non-2xx", TestPreflightNonSuccessStatus);
        failed += Run("Review config validator allows additional", TestReviewConfigValidatorAllowsAdditionalProperties);
        failed += Run("Review config validator invalid enum", TestReviewConfigValidatorInvalidEnum);
        failed += Run("Analysis severity critical", TestAnalysisSeverityCritical);
        failed += Run("Analysis config export tool ids", TestAnalysisConfigExportToolIds);
        failed += Run("Analysis policy resolves overrides", TestAnalysisPolicyResolvesOverrides);
        failed += Run("Analysis policy disable tool rule id", TestAnalysisPolicyDisableToolRuleId);
        failed += Run("Analyze run disabled writes empty findings", TestAnalyzeRunDisabledWritesEmptyFindings);
        failed += Run("Analyze run internal file size rule", TestAnalyzeRunInternalFileSizeRule);
        failed += Run("Analyze run internal file size severity none", TestAnalyzeRunInternalFileSizeRuleDisabledBySeverity);
        failed += Run("Structured findings block", TestStructuredFindingsBlock);
        failed += Run("Trim patch hunk boundary", TestTrimPatchStopsAtHunkBoundary);
        failed += Run("Trim patch tail hunk", TestTrimPatchKeepsTailHunk);
        failed += Run("Trim patch tail hunk (two hunks)", TestTrimPatchKeepsTailHunkTwoHunks);
        failed += Run("Trim patch CRLF", TestTrimPatchPreservesCrlf);
        failed += Run("Trim patch keeps last hunk", TestTrimPatchKeepsLastHunk);
        failed += Run("Review intent applies focus", TestReviewIntentAppliesFocus);
        failed += Run("Review intent respects focus", TestReviewIntentRespectsFocus);
        failed += Run("Review provider alias parsing", TestReviewProviderAliasParsing);
        failed += Run("Review provider contract capabilities", TestReviewProviderContractCapabilities);
        failed += Run("Review provider config alias", TestReviewProviderConfigAlias);
        failed += Run("Review provider fallback env", TestReviewProviderFallbackEnv);
        failed += Run("Review provider fallback config", TestReviewProviderFallbackConfig);
        failed += Run("Review provider fallback plan", TestReviewProviderFallbackPlan);
        failed += Run("Review provider health env", TestReviewProviderHealthEnv);
        failed += Run("Review provider health config", TestReviewProviderHealthConfig);
        failed += Run("Review provider circuit breaker", TestReviewProviderCircuitBreaker);
        failed += Run("Review intent applies defaults", TestReviewIntentAppliesDefaults);
        failed += Run("Review intent respects settings", TestReviewIntentRespectsSettings);
        failed += Run("Review intent perf alias", TestReviewIntentPerfAlias);
        failed += Run("Review intent null settings", TestReviewIntentNullSettings);
        failed += Run("Triage-only loads threads", TestTriageOnlyLoadsThreads);
        failed += Run("Review code host env", TestReviewCodeHostEnv);
        failed += Run("GitHub context cache", TestGitHubContextCache);
        failed += Run("GitHub concurrency env", TestGitHubConcurrencyEnv);
        failed += Run("GitHub client concurrency", TestGitHubClientConcurrency);
        failed += Run("GitHub code host reader smoke", TestGitHubCodeHostReaderSmoke);
        failed += Run("GitHub compare truncation", TestGitHubCompareTruncation);
        failed += Run("Diff range compare truncation", TestDiffRangeCompareTruncation);
        failed += Run("Azure auth scheme env", TestAzureAuthSchemeEnv);
        failed += Run("Azure auth scheme invalid env", TestAzureAuthSchemeInvalidEnv);
        failed += Run("Azure code host reader smoke", TestAzureDevOpsCodeHostReaderSmoke);
        failed += Run("Review threads diff range normalize", TestReviewThreadsDiffRangeNormalize);
        failed += Run("Copilot env allowlist config", TestCopilotEnvAllowlistConfig);
        failed += Run("Copilot inherit env default", TestCopilotInheritEnvironmentDefault);
        failed += Run("Copilot direct timeout validation", TestCopilotDirectTimeoutValidation);
        failed += Run("Copilot chat timeout validation", TestCopilotChatTimeoutValidation);
        failed += Run("Copilot direct auth conflict", TestCopilotDirectAuthorizationConflict);
        failed += Run("Copilot CLI path requires env", TestCopilotCliPathRequiresEnvironment);
        failed += Run("Copilot CLI path optional with url", TestCopilotCliPathOptionalWithUrl);
        failed += Run("Copilot CLI url validation", TestCopilotCliUrlValidation);
        failed += Run("Resolve-threads option parsing", TestResolveThreadsOptionParsing);
        failed += Run("Resolve-threads GHES endpoint", TestResolveThreadsEndpointResolution);
        failed += Run("Filter files include-only", TestFilterFilesIncludeOnly);
        failed += Run("Filter files exclude-only", TestFilterFilesExcludeOnly);
        failed += Run("Filter files include+exclude", TestFilterFilesIncludeExclude);
        failed += Run("Filter files glob patterns", TestFilterFilesGlobPatterns);
        failed += Run("Filter files empty filters", TestFilterFilesEmptyFilters);
        failed += Run("Filter files skip binary", TestFilterFilesSkipBinary);
        failed += Run("Filter files skip binary case-insensitive", TestFilterFilesSkipBinaryCaseInsensitive);
        failed += Run("Filter files skip generated", TestFilterFilesSkipGenerated);
        failed += Run("Filter files skip before include", TestFilterFilesSkipBeforeInclude);
        failed += Run("Filter files generated globs extend", TestFilterFilesGeneratedGlobsExtend);
        failed += Run("Workflow changes detection", TestWorkflowChangesDetection);
        failed += Run("Secrets audit records", TestSecretsAuditRecords);
        failed += Run("Prompt language hints", TestPromptBuilderLanguageHints);
        failed += Run("Prompt language hints disabled", TestPromptBuilderLanguageHintsDisabled);
        failed += Run("Redaction defaults", TestRedactionDefaults);
        failed += Run("Review budget note", TestReviewBudgetNote);
        failed += Run("Review budget note empty", TestReviewBudgetNoteEmpty);
        failed += Run("Review budget note comment", TestReviewBudgetNoteComment);
        failed += Run("Review retry backoff multiplier config", TestReviewRetryBackoffMultiplierConfig);
        failed += Run("Review retry backoff multiplier env", TestReviewRetryBackoffMultiplierEnv);
        failed += Run("Prepare files max files zero", TestPrepareFilesMaxFilesZero);
        failed += Run("Prepare files max files negative", TestPrepareFilesMaxFilesNegative);
        failed += Run("Azure DevOps changes pagination", TestAzureDevOpsChangesPagination);
        failed += Run("Azure DevOps diff note zero iterations", TestAzureDevOpsDiffNoteZeroIterations);
        failed += Run("Azure DevOps error sanitization", TestAzureDevOpsErrorSanitization);
        failed += Run("Context deny invalid regex", TestContextDenyInvalidRegex);
        failed += Run("Context deny timeout", TestContextDenyTimeout);
        failed += Run("Review summary parser", TestReviewSummaryParser);
        failed += Run("Review usage summary line", TestReviewUsageSummaryLine);
        failed += Run("Review usage summary disambiguates code review weekly", TestReviewUsageSummaryDisambiguatesCodeReviewWeekly);
        failed += Run("Review usage summary disambiguates code review weekly secondary", TestReviewUsageSummaryDisambiguatesCodeReviewWeeklySecondary);
        failed += Run("Review usage summary prefixes non-weekly code review", TestReviewUsageSummaryPrefixesNonWeeklyCodeReview);
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

    private static void TestToolRunnerMaxRounds() {
        using var client = CreateToolRunnerClient(BuildToolCallTurn(("call_1", "echo")));
        var registry = new ToolRegistry();
        registry.Register(new StubTool("echo"));
        var input = ChatInput.FromText("Run tools");
        var options = new ChatOptions { Model = "gpt-5.3-codex" };

        AssertThrows<InvalidOperationException>(() =>
                ToolRunner.RunAsync(client, input, options, registry,
                        new ToolRunnerOptions { MaxRounds = 1 })
                    .GetAwaiter().GetResult(),
            "tool runner max rounds");
    }

    private static void TestToolRunnerUnregisteredTool() {
        using var client = CreateToolRunnerClient(BuildToolCallTurn(("call_2", "missing_tool")));
        var registry = new ToolRegistry();
        var input = ChatInput.FromText("Run tools");
        var options = new ChatOptions { Model = "gpt-5.3-codex" };

        AssertThrows<InvalidOperationException>(() =>
                ToolRunner.RunAsync(client, input, options, registry,
                        new ToolRunnerOptions { MaxRounds = 1 })
                    .GetAwaiter().GetResult(),
            "tool runner unregistered tool");
    }

    private static void TestToolRunnerParallelExecution() {
        using var client = CreateToolRunnerClient(BuildToolCallTurn(("call_1", "tool_a"), ("call_2", "tool_b")));
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var registry = new ToolRegistry();
        registry.Register(new GateTool("tool_a", startGate, releaseGate, () => Interlocked.Increment(ref started), 2));
        registry.Register(new GateTool("tool_b", startGate, releaseGate, () => Interlocked.Increment(ref started), 2));

        var input = ChatInput.FromText("Run tools");
        var options = new ChatOptions { Model = "gpt-5.3-codex", ParallelToolCalls = true };

        var runnerTask = ToolRunner.RunAsync(client, input, options, registry,
            new ToolRunnerOptions { MaxRounds = 1, ParallelToolCalls = true });

        AssertCompletes(startGate.Task, 1000, "tool runner parallel start");
        releaseGate.TrySetResult(true);

        AssertThrows<InvalidOperationException>(() => runnerTask.GetAwaiter().GetResult(), "tool runner parallel");
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

    private static void TestConfigLoadInvalidJsonThrows() {
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-config-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{invalid");
            AssertThrows<InvalidDataException>(() =>
                IntelligenceX.Configuration.IntelligenceXConfig.Load(path), "config invalid json");
        } finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
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

    private static void TestAutoResolveMissingInlineEmptyKeys() {
        var resolved = 0;
        var inlineBody = $"{ReviewFormatter.InlineMarker}\nFix it.";
        using var server = new LocalHttpServer(request => {
            if (!request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (request.Body.Contains("resolveReviewThread", StringComparison.Ordinal)) {
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
            ReviewThreadsMaxComments = 1
        };

        CallAutoResolveMissingInlineThreads(github, context, new HashSet<string>(StringComparer.OrdinalIgnoreCase), settings);
        AssertEqual(1, resolved, "auto resolve missing inline empty keys");
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
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Expected socket failure.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "Connectivity preflight failed", "preflight socket failure");
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

    private static void TestAnalysisSeverityCritical() {
        AssertEqual("error", AnalysisSeverity.Normalize("critical"), "severity critical normalize");
        AssertEqual(3, AnalysisSeverity.Rank("critical"), "severity critical rank");
        AssertEqual("warning", AnalysisSeverity.Normalize("medium"), "severity medium normalize");
    }

    private static void TestAnalysisConfigExportToolIds() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
                ["IX001"] = new AnalysisRule(
                    "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                    "Reliability", "warning", Array.Empty<string>(), null, null),
                ["IX002"] = new AnalysisRule(
                    "IX002", "cs", "roslyn", "CA1062", "Validate arguments", "Validate argument null checks",
                    "Reliability", "warning", Array.Empty<string>(), null, null),
                ["PS001"] = new AnalysisRule(
                    "PS001", "powershell", "psscriptanalyzer", "PSAvoidUsingWriteHost",
                    "Avoid Write-Host", "Use Write-Output instead", "BestPractices", "warning",
                    Array.Empty<string>(), null, null),
                ["PS002"] = new AnalysisRule(
                    "PS002", "ps", "psscriptanalyzer", "PSUseSupportsShouldProcess",
                    "Use SupportsShouldProcess", "Add SupportsShouldProcess when needed", "BestPractices", "warning",
                    Array.Empty<string>(), null, null)
            };
            var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
                ["pack"] = new AnalysisPack(
                    "pack", "Pack", "Test pack", new[] { "IX001", "IX002", "PS001", "PS002" },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null)
            };
            var catalog = new AnalysisCatalog(rules, packs);
            var settings = new AnalysisSettings { Packs = new[] { "pack" } };

            AnalysisConfigExporter.Export(settings, catalog, temp);

            var editorConfig = File.ReadAllText(Path.Combine(temp, ".editorconfig"));
            AssertEqual(true, editorConfig.Contains("dotnet_diagnostic.CA2000.severity", StringComparison.Ordinal),
                "editorconfig CA2000");
            AssertEqual(true, editorConfig.Contains("dotnet_diagnostic.CA1062.severity", StringComparison.Ordinal),
                "editorconfig CA1062");

            var psConfig = File.ReadAllText(Path.Combine(temp, "PSScriptAnalyzerSettings.psd1"));
            AssertEqual(true, psConfig.Contains("PSAvoidUsingWriteHost", StringComparison.Ordinal),
                "psconfig PSAvoidUsingWriteHost");
            AssertEqual(true, psConfig.Contains("PSUseSupportsShouldProcess", StringComparison.Ordinal),
                "psconfig PSUseSupportsShouldProcess");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyResolvesOverrides() {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
            ["IX001"] = new AnalysisRule(
                "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                "Reliability", "warning", Array.Empty<string>(), null, null),
            ["IX002"] = new AnalysisRule(
                "IX002", "powershell", "psscriptanalyzer", "PSAvoidUsingWriteHost", "Avoid Write-Host",
                "Use Write-Output", "BestPractices", "warning", Array.Empty<string>(), null, null)
        };
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
            ["default"] = new AnalysisPack(
                "default", "Default", "Test pack", new[] { "IX001", "IX002" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["IX001"] = "error"
                }, null)
        };
        var catalog = new AnalysisCatalog(rules, packs);
        var settings = new AnalysisSettings {
            Packs = new[] { "default" },
            SeverityOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["CA2000"] = "warning",
                ["IX002"] = "error"
            }
        };

        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);
        AssertEqual(0, policy.Warnings.Count, "policy warnings");
        AssertEqual(2, policy.Rules.Count, "policy selected count");
        AssertEqual("warning", policy.Rules["IX001"].Severity, "policy override tool rule id");
        AssertEqual("error", policy.Rules["IX002"].Severity, "policy override catalog id");
    }

    private static void TestAnalysisPolicyDisableToolRuleId() {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase) {
            ["IX001"] = new AnalysisRule(
                "IX001", "csharp", "roslyn", "CA2000", "Dispose objects", "Ensure Dispose is called",
                "Reliability", "warning", Array.Empty<string>(), null, null)
        };
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase) {
            ["default"] = new AnalysisPack(
                "default", "Default", "Test pack", new[] { "IX001" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null)
        };
        var catalog = new AnalysisCatalog(rules, packs);
        var settings = new AnalysisSettings {
            Packs = new[] { "default" },
            DisabledRules = new[] { "CA2000" }
        };

        var policy = IntelligenceX.Analysis.AnalysisPolicyBuilder.Build(settings, catalog);
        AssertEqual(0, policy.Warnings.Count, "policy warnings");
        AssertEqual(0, policy.Rules.Count, "policy disabled by tool rule id");
    }

    private static void TestAnalyzeRunDisabledWritesEmptyFindings() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-run-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(temp, "artifacts");
        var config = Path.Combine(temp, "reviewer.json");
        Directory.CreateDirectory(temp);
        try {
            File.WriteAllText(config, "{ \"analysis\": { \"enabled\": false } }");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", config,
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run disabled exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(true, content.Contains("intelligencex.findings.v1", StringComparison.Ordinal),
                "analyze run schema");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalFileSizeRule() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001"]
}
""");

            var largeFile = Path.Combine(temp, "LargeFile.cs");
            var lines = Enumerable.Repeat("public class X { }", 705);
            File.WriteAllText(largeFile, string.Join('\n', lines) + "\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal rule exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(true, content.Contains("IXLOC001", StringComparison.Ordinal), "analyze run internal rule id");
            AssertEqual(true, content.Contains("LargeFile.cs", StringComparison.Ordinal), "analyze run internal file path");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalyzeRunInternalFileSizeRuleDisabledBySeverity() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-size-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal"));
            Directory.CreateDirectory(Path.Combine(temp, "Analysis", "Packs"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"],
    "severityOverrides": {
      "IXLOC001": "none"
    }
  }
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Catalog", "rules", "internal", "IXLOC001.json"), """
{
  "id": "IXLOC001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXLOC001",
  "title": "Source files should stay below 700 lines",
  "description": "Flags oversized source files.",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");

            File.WriteAllText(Path.Combine(temp, "Analysis", "Packs", "intelligencex-maintainability-default.json"), """
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXLOC001"]
}
""");

            var largeFile = Path.Combine(temp, "LargeFile.cs");
            var lines = Enumerable.Repeat("public class X { }", 705);
            File.WriteAllText(largeFile, string.Join('\n', lines) + "\n");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run internal severity none exit");
            var findingsPath = Path.Combine(output, "intelligencex.findings.json");
            AssertEqual(true, File.Exists(findingsPath), "analyze run internal severity none findings exists");
            var content = File.ReadAllText(findingsPath);
            AssertEqual(false, content.Contains("IXLOC001", StringComparison.Ordinal), "analyze run internal severity none suppresses rule");
        } finally {
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
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
        AssertEqual(false, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk removed");
        AssertEqual(true, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "tail hunk kept");
    }

    private static void TestTrimPatchKeepsTailHunk() {
        var newline = "\n";
        var headerLines = new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt"
        };
        var hunk1Lines = new[] {
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a"
        };
        var hunk2Lines = new[] {
            "@@ -10,2 +10,2 @@",
            "-line10",
            "+line10a"
        };
        var hunk3Lines = new[] {
            "@@ -20,2 +20,2 @@",
            "-line20",
            "+line20a"
        };
        var header = string.Join(newline, headerLines);
        var hunk1 = string.Join(newline, hunk1Lines);
        var hunk2 = string.Join(newline, hunk2Lines);
        var hunk3 = string.Join(newline, hunk3Lines);
        var patch = string.Join(newline, headerLines)
                    + newline + hunk1
                    + newline + hunk2
                    + newline + hunk3;
        var marker = "... (truncated) ...";
        var maxChars = header.Length
                       + newline.Length + hunk1.Length
                       + newline.Length + marker.Length
                       + newline.Length + hunk3.Length;
        var trimmed = CallTrimPatch(patch, maxChars);
        AssertEqual(true, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk kept");
        AssertEqual(false, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "middle hunk removed");
        AssertEqual(true, trimmed.Contains("@@ -20,2 +20,2 @@", StringComparison.Ordinal), "tail hunk kept");
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

    private static void TestTrimPatchKeepsTailHunkTwoHunks() {
        var newline = "\n";
        var headerLines = new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt"
        };
        var hunk1Lines = new[] {
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a"
        };
        var hunk2Lines = new[] {
            "@@ -10,2 +10,2 @@",
            "-line10",
            "+line10a"
        };
        var header = string.Join(newline, headerLines);
        var hunk1 = string.Join(newline, hunk1Lines);
        var hunk2 = string.Join(newline, hunk2Lines);
        var patch = string.Join(newline, headerLines)
                    + newline + hunk1
                    + newline + hunk2;
        var marker = "... (truncated) ...";
        var maxChars = header.Length
                       + newline.Length + marker.Length
                       + newline.Length + hunk2.Length;
        var trimmed = CallTrimPatch(patch, maxChars);
        AssertEqual(false, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk removed");
        AssertEqual(true, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "tail hunk kept");
        AssertEqual(true, trimmed.Contains(marker, StringComparison.Ordinal), "marker added");
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

    private static void TestReviewProviderAliasParsing() {
        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("openai", out var openai), "provider openai alias");
        AssertEqual(ReviewProvider.OpenAI, openai, "provider openai value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("OpenAI", out var openaiMixedCase), "provider openai mixed case alias");
        AssertEqual(ReviewProvider.OpenAI, openaiMixedCase, "provider openai mixed case value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("codex", out var codex), "provider codex alias");
        AssertEqual(ReviewProvider.OpenAI, codex, "provider codex value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("CODEX", out var codexUpper), "provider codex uppercase alias");
        AssertEqual(ReviewProvider.OpenAI, codexUpper, "provider codex uppercase value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("copilot", out var copilot), "provider copilot alias");
        AssertEqual(ReviewProvider.Copilot, copilot, "provider copilot value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("Copilot", out var copilotMixedCase), "provider copilot mixed case alias");
        AssertEqual(ReviewProvider.Copilot, copilotMixedCase, "provider copilot mixed case value");

        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias(null, out _), "provider null alias unsupported");
        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias("", out _), "provider empty alias unsupported");
        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias("   ", out _), "provider whitespace alias unsupported");
        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias("azure", out _), "provider azure alias unsupported");
    }

    private static void TestReviewProviderContractCapabilities() {
        var openai = ReviewProviderContracts.Get(ReviewProvider.OpenAI);
        AssertEqual(true, openai.SupportsUsageApi, "openai usage api");
        AssertEqual(true, openai.SupportsReasoningControls, "openai reasoning");
        AssertEqual(true, openai.RequiresOpenAiAuthStore, "openai auth");
        AssertEqual(true, openai.SupportsStreaming, "openai streaming");
        AssertEqual(true, openai.MaxRecommendedRetryCount > 0, "openai retry limit");

        var copilot = ReviewProviderContracts.Get(ReviewProvider.Copilot);
        AssertEqual(false, copilot.SupportsUsageApi, "copilot usage api");
        AssertEqual(false, copilot.SupportsReasoningControls, "copilot reasoning");
        AssertEqual(false, copilot.RequiresOpenAiAuthStore, "copilot auth");
        AssertEqual(true, copilot.SupportsStreaming, "copilot streaming");
        AssertEqual(true, copilot.MaxRecommendedRetryCount > 0, "copilot retry limit");

        AssertThrows<NotSupportedException>(() => ReviewProviderContracts.Get((ReviewProvider)999), "unknown provider contract");
    }

    private static void TestReviewProviderConfigAlias() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"codex\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(ReviewProvider.OpenAI, settings.Provider, "provider codex config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderFallbackEnv() {
        var previousProvider = Environment.GetEnvironmentVariable("REVIEW_PROVIDER");
        var previousFallback = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK");
        try {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", "openai");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", "copilot");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(ReviewProvider.OpenAI, settings.Provider, "provider fallback env primary");
            AssertEqual(ReviewProvider.Copilot, settings.ProviderFallback, "provider fallback env value");

            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", "azure");
            settings = ReviewSettings.FromEnvironment();
            AssertEqual(null, settings.ProviderFallback, "provider fallback env invalid");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", previousFallback);
        }
    }

    private static void TestReviewProviderFallbackConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"openai\", \"providerFallback\": \"copilot\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(ReviewProvider.OpenAI, settings.Provider, "provider fallback config primary");
            AssertEqual(ReviewProvider.Copilot, settings.ProviderFallback, "provider fallback config value");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderFallbackPlan() {
        AssertEqual(null, ReviewRunner.ResolveFallbackProvider(ReviewProvider.OpenAI, null), "provider fallback none");
        AssertEqual(null, ReviewRunner.ResolveFallbackProvider(ReviewProvider.OpenAI, ReviewProvider.OpenAI), "provider fallback same");
        AssertEqual(ReviewProvider.Copilot, ReviewRunner.ResolveFallbackProvider(ReviewProvider.OpenAI, ReviewProvider.Copilot),
            "provider fallback selected");
        AssertEqual(false, ReviewRunner.ShouldFallbackOnResult("Regular review content"), "provider fallback regular output");
        AssertEqual(true, ReviewRunner.ShouldFallbackOnResult($"x {ReviewDiagnostics.FailureMarker} y"), "provider fallback failure marker");
    }

    private static void TestReviewIntentAppliesDefaults() {
        var settings = new ReviewSettings();
        ReviewIntents.Apply("security", settings);
        AssertEqual("strict", settings.Strictness, "intent strictness");
        AssertContainsText(settings.Notes ?? string.Empty, "auth", "intent notes");
    }

    private static void TestReviewIntentRespectsSettings() {
        var settings = new ReviewSettings {
            Strictness = "custom",
            Notes = "custom notes"
        };
        ReviewIntents.Apply("maintainability", settings);
        AssertEqual("custom", settings.Strictness, "intent strictness preserved");
        AssertEqual("custom notes", settings.Notes, "intent notes preserved");
    }

    private static void TestReviewProviderHealthEnv() {
        var previousHealthChecks = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECKS");
        var previousHealthTimeout = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS");
        var previousBreakerFailures = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES");
        var previousBreakerOpen = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS");
        try {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECKS", "false");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS", "7");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES", "5");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS", "90");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(false, settings.ProviderHealthChecks, "provider health checks env");
            AssertEqual(7, settings.ProviderHealthCheckTimeoutSeconds, "provider health timeout env");
            AssertEqual(5, settings.ProviderCircuitBreakerFailures, "provider breaker failures env");
            AssertEqual(90, settings.ProviderCircuitBreakerOpenSeconds, "provider breaker open env");

            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES", "-1");
            settings = ReviewSettings.FromEnvironment();
            AssertEqual(3, settings.ProviderCircuitBreakerFailures, "provider breaker failures env invalid");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECKS", previousHealthChecks);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS", previousHealthTimeout);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES", previousBreakerFailures);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS", previousBreakerOpen);
        }
    }

    private static void TestReviewProviderHealthConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path,
                "{ \"review\": { \"providerHealthChecks\": false, \"providerHealthCheckTimeoutSeconds\": 9, " +
                "\"providerCircuitBreakerFailures\": 4, \"providerCircuitBreakerOpenSeconds\": 75 } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(false, settings.ProviderHealthChecks, "provider health checks config");
            AssertEqual(9, settings.ProviderHealthCheckTimeoutSeconds, "provider health timeout config");
            AssertEqual(4, settings.ProviderCircuitBreakerFailures, "provider breaker failures config");
            AssertEqual(75, settings.ProviderCircuitBreakerOpenSeconds, "provider breaker open config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderCircuitBreaker() {
        ReviewProviderCircuitBreaker.Reset();
        var provider = ReviewProvider.OpenAI;
        var now = DateTimeOffset.UtcNow;

        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker initially closed");

        ReviewProviderCircuitBreaker.RecordFailure(provider, 2, TimeSpan.FromSeconds(30), now);
        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker first failure");

        ReviewProviderCircuitBreaker.RecordFailure(provider, 2, TimeSpan.FromSeconds(30), now);
        AssertEqual(true, ReviewProviderCircuitBreaker.IsOpen(provider, now, out var remaining), "provider breaker opens");
        AssertEqual(true, remaining.TotalSeconds > 0, "provider breaker remaining");

        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now.AddSeconds(31), out _), "provider breaker closes");

        ReviewProviderCircuitBreaker.RecordFailure(provider, 1, TimeSpan.FromSeconds(30), now);
        AssertEqual(true, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker immediate open");
        ReviewProviderCircuitBreaker.RecordSuccess(provider);
        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker reset by success");
        ReviewProviderCircuitBreaker.Reset();
    }

    private static void TestReviewIntentPerfAlias() {
        var settings = new ReviewSettings();
        ReviewIntents.Apply("perf", settings);
        AssertEqual("balanced", settings.Strictness, "perf alias strictness");
        AssertContainsText(settings.Notes ?? string.Empty, "allocations", "perf alias notes");
    }

    private static void TestReviewIntentNullSettings() {
        AssertThrows<ArgumentNullException>(() => ReviewIntents.Apply("security", null!), "intent null settings");
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

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) CallPrepareFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        var method = typeof(ReviewerApp).GetMethod("PrepareFiles", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("PrepareFiles method not found.");
        }
        var result = method.Invoke(null, new object?[] { files, maxFiles, maxPatchChars });
        if (result is ValueTuple<IReadOnlyList<PullRequestFile>, string> tuple) {
            return tuple;
        }
        throw new InvalidOperationException("PrepareFiles method returned unexpected result.");
    }

    private static string CallFormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var method = typeof(ReviewerApp).GetMethod("FormatUsageSummary", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("FormatUsageSummary method not found.");
        }
        var result = method.Invoke(null, new object?[] { snapshot }) as string;
        return result ?? string.Empty;
    }

    private static void CallPreflightNativeConnectivity(OpenAINativeOptions options, TimeSpan timeout) {
        var runner = new ReviewRunner(new ReviewSettings());
        var method = typeof(ReviewRunner).GetMethod("PreflightNativeConnectivityAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null) {
            throw new InvalidOperationException("PreflightNativeConnectivityAsync method not found.");
        }
        var task = method.Invoke(runner, new object?[] { options, timeout, CancellationToken.None }) as Task;
        if (task is null) {
            throw new InvalidOperationException("PreflightNativeConnectivityAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
    }

    private static ReviewContextExtras CallBuildExtrasAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, bool forceReviewThreads) {
        var method = typeof(ReviewerApp).GetMethod("BuildExtrasAsync", BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("BuildExtrasAsync method not found.");
        }
        var codeHostReader = new GitHubCodeHostReader(github);
        var task = method.Invoke(null, new object?[] {
            codeHostReader,
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

    private static void CallAutoResolveMissingInlineThreads(GitHubClient github, PullRequestContext context,
        HashSet<string>? expectedKeys, ReviewSettings settings) {
        var method = typeof(ReviewerApp).GetMethod("AutoResolveMissingInlineThreadsAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("AutoResolveMissingInlineThreadsAsync method not found.");
        }
        var codeHostReader = new GitHubCodeHostReader(github);
        var task = method.Invoke(null, new object?[] {
            codeHostReader,
            github,
            null,
            context,
            expectedKeys,
            settings,
            CancellationToken.None
        }) as Task;
        if (task is null) {
            throw new InvalidOperationException("AutoResolveMissingInlineThreadsAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
    }

    private static (IReadOnlyList<PullRequestFile> Files, string Note) CallResolveDiffRangeFiles(GitHubClient github,
        PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles, ReviewSettings settings) {
        var method = typeof(ReviewerApp).GetMethod("ResolveDiffRangeFilesAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method is null) {
            throw new InvalidOperationException("ResolveDiffRangeFilesAsync method not found.");
        }
        var codeHostReader = new GitHubCodeHostReader(github);
        var task = method.Invoke(null, new object?[] {
            codeHostReader,
            context,
            range,
            currentFiles,
            settings,
            CancellationToken.None
        }) as Task;
        if (task is null) {
            throw new InvalidOperationException("ResolveDiffRangeFilesAsync did not return a task.");
        }
        task.GetAwaiter().GetResult();
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty is null) {
            throw new InvalidOperationException("ResolveDiffRangeFilesAsync Result not found.");
        }
        var result = resultProperty.GetValue(task);
        if (result is ValueTuple<IReadOnlyList<PullRequestFile>, string> tuple) {
            return tuple;
        }
        throw new InvalidOperationException("ResolveDiffRangeFilesAsync returned unexpected result.");
    }

    private static string BuildGraphQlThreadsResponse() {
        return BuildGraphQlThreadsResponse("test");
    }

    private static string BuildGraphQlThreadsResponse(string body) {
        return "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[{\"id\":\"thread1\",\"isResolved\":false,\"isOutdated\":false,\"comments\":{\"totalCount\":1,\"nodes\":[{\"databaseId\":1,\"createdAt\":\"2024-01-01T00:00:00Z\",\"body\":\""
            + EscapeJson(body)
            + "\",\"path\":\"file.txt\",\"line\":10,\"author\":{\"login\":\"bot\"}}]}}],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}}";
    }

    private static string EscapeJson(string value) {
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n");
    }

    private static int GetQueryInt(string path, string key, int fallback) {
        var queryStart = path.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == path.Length - 1) {
            return fallback;
        }
        var query = path.Substring(queryStart + 1);
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            var kvp = part.Split('=', 2);
            if (kvp.Length == 2 && string.Equals(kvp[0], key, StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(kvp[1], out var value)) {
                    return value;
                }
            }
        }
        return fallback;
    }

    private static string BuildCompareFilesPage(int startIndex, int count) {
        var sb = new StringBuilder();
        sb.Append("{\"files\":[");
        for (var i = 0; i < count; i++) {
            if (i > 0) {
                sb.Append(",");
            }
            var name = $"file{startIndex + i}.txt";
            sb.Append("{\"filename\":\"").Append(name).Append("\",\"status\":\"modified\",\"patch\":\"@@\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private sealed record HttpRequest(string Method, string Path, string Body);
    private sealed record HttpResponse(string Body, IReadOnlyDictionary<string, string>? Headers = null,
        int StatusCode = 200, string StatusText = "OK");

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

            await WriteResponseAsync(stream, response.StatusCode, response.StatusText, response.Body, response.Headers)
                .ConfigureAwait(false);
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
#endif

    private static IntelligenceXClient CreateToolRunnerClient(TurnInfo turn) {
        var transport = new FakeToolTransport(turn);
        var ctor = typeof(IntelligenceXClient).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(IOpenAITransport), typeof(string), typeof(string), typeof(string), typeof(SandboxPolicy) },
            null);
        if (ctor is null) {
            throw new InvalidOperationException("IntelligenceXClient constructor not found.");
        }
        return (IntelligenceXClient)ctor.Invoke(new object?[] { transport, "gpt-5.3-codex", null, null, null });
    }

    private static TurnInfo BuildToolCallTurn(params (string CallId, string ToolName)[] calls) {
        if (calls is null || calls.Length == 0) {
            throw new InvalidOperationException("Tool call list cannot be empty.");
        }
        var output = new JsonArray();
        foreach (var call in calls) {
            output.Add(new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", call.CallId)
                .Add("name", call.ToolName)
                .Add("input", "{}"));
        }
        return TurnInfo.FromJson(new JsonObject()
            .Add("id", "turn_" + calls[0].CallId)
            .Add("output", output));
    }

    private sealed class StubTool : ITool {
        private readonly ToolDefinition _definition;

        public StubTool(string name) {
            _definition = new ToolDefinition(name, "Stub tool", new JsonObject().Add("type", "object"));
        }

        public ToolDefinition Definition => _definition;

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken)
            => Task.FromResult("ok");
    }

    private sealed class GateTool : ITool {
        private readonly ToolDefinition _definition;
        private readonly TaskCompletionSource<bool> _startGate;
        private readonly TaskCompletionSource<bool> _releaseGate;
        private readonly Func<int> _increment;
        private readonly int _expected;

        public GateTool(string name, TaskCompletionSource<bool> startGate, TaskCompletionSource<bool> releaseGate,
            Func<int> increment, int expected) {
            _definition = new ToolDefinition(name, "Gate tool", new JsonObject().Add("type", "object"));
            _startGate = startGate;
            _releaseGate = releaseGate;
            _increment = increment;
            _expected = expected;
        }

        public ToolDefinition Definition => _definition;

        public async Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            var count = _increment();
            if (count >= _expected) {
                _startGate.TrySetResult(true);
            }
            await _startGate.Task.ConfigureAwait(false);
            await _releaseGate.Task.ConfigureAwait(false);
            return "ok";
        }
    }

    private sealed class FakeToolTransport : IOpenAITransport {
        private readonly TurnInfo _turn;

        public FakeToolTransport(TurnInfo turn) {
            _turn = turn;
        }

        public OpenAITransportKind Kind => OpenAITransportKind.Native;
        public AppServerClient? RawAppServerClient => null;

#pragma warning disable CS0067
        public event EventHandler<string>? DeltaReceived;
        public event EventHandler<LoginEventArgs>? LoginStarted;
        public event EventHandler<LoginEventArgs>? LoginCompleted;
        public event EventHandler<string>? ProtocolLineReceived;
        public event EventHandler<string>? StandardErrorReceived;
        public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
        public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;
#pragma warning restore CS0067

        public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken)
            => Task.FromResult(new HealthCheckResult(true));
        public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AccountInfo(null, null, null, new JsonObject(), null));
        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ModelListResult(Array.Empty<ModelInfo>(), null, new JsonObject(), null));
        public Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
            bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(new ChatGptLoginStart("login", "https://example", new JsonObject(), null));
        public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
            string? sandbox, CancellationToken cancellationToken)
            => Task.FromResult(new ThreadInfo("thread1", null, null, null, null, new JsonObject(), null));
        public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
            => Task.FromResult(new ThreadInfo(threadId, null, null, null, null, new JsonObject(), null));
        public Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
            string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken)
            => Task.FromResult(_turn);

        public void Dispose() { }
    }

    private static List<string> ParseUsageSummaryParts(string line) {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) {
            return result;
        }
        const string prefix = "Usage: ";
        const string separator = " | ";
        var body = line.StartsWith(prefix, StringComparison.Ordinal)
            ? line.Substring(prefix.Length)
            : line;
        foreach (var part in body.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)) {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed)) {
                result.Add(trimmed);
            }
        }
        return result;
    }

    private static bool ContainsUsageSummaryPart(IReadOnlyList<string> parts, string expected) {
        foreach (var part in parts) {
            if (string.Equals(part, expected, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

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

    private static void AssertCompletes(Task task, int timeoutMs, string name) {
        if (task is null) {
            throw new InvalidOperationException($"Expected {name} task to be non-null.");
        }
        var completed = Task.WhenAny(task, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
        if (!ReferenceEquals(completed, task)) {
            throw new InvalidOperationException($"Expected {name} to complete within {timeoutMs}ms.");
        }
        task.GetAwaiter().GetResult();
    }
}
