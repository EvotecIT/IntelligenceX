using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
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
#if INTELLIGENCEX_REVIEWER
        failed += Run("Cleanup normalize allowed edits", TestCleanupNormalizeAllowedEdits);
        failed += Run("Cleanup clamp confidence", TestCleanupClampConfidence);
        failed += Run("Cleanup result parse fenced", TestCleanupResultParseFenced);
        failed += Run("Cleanup result parse embedded", TestCleanupResultParseEmbedded);
        failed += Run("Cleanup template path guard", TestCleanupTemplatePathGuard);
        failed += Run("Inline comments extract", TestInlineCommentsExtract);
        failed += Run("Inline comments backticks", TestInlineCommentsBackticks);
        failed += Run("Inline comments snippet header", TestInlineCommentsSnippetHeader);
        failed += Run("Review retry transient", TestReviewRetryTransient);
        failed += Run("Review retry non-transient", TestReviewRetryNonTransient);
        failed += Run("Review retry rethrows", TestReviewRetryRethrows);
        failed += Run("Context deny invalid regex", TestContextDenyInvalidRegex);
        failed += Run("Context deny timeout", TestContextDenyTimeout);
        failed += Run("Review summary parser", TestReviewSummaryParser);
#endif

        Console.WriteLine(failed == 0 ? "All tests passed." : $"{failed} test(s) failed.");
        return failed == 0 ? 0 : 1;
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
            CancellationToken.None).GetAwaiter().GetResult();

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
                CancellationToken.None).GetAwaiter().GetResult();
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
                CancellationToken.None).GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected exception.");
        } catch (IOException caught) {
            AssertEqual(true, ReferenceEquals(ex, caught), "retry exception instance");
        }

        AssertEqual(2, attempts, "retry attempts rethrow");
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
}
