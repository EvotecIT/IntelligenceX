namespace IntelligenceX.Tests;

internal static partial class Program {
#if NET8_0_OR_GREATER
    private static void TestPathSafetyBlocksSymlinkTraversal() {
        var root = Path.Combine(Path.GetTempPath(), $"ix-workspace-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"ix-outside-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);

            var outsideFile = Path.Combine(outside, "outside.txt");
            File.WriteAllText(outsideFile, "x");

            var linkDir = Path.Combine(root, "link");
            try {
                Directory.CreateSymbolicLink(linkDir, outside);
            } catch {
                // Symlink creation can be restricted on some Windows environments. Treat as non-actionable.
                return;
            }

            var escapedPath = Path.Combine(linkDir, "outside.txt");
            AssertEqual(true, File.Exists(escapedPath), "escaped file exists via link");
            AssertThrows<InvalidOperationException>(() => PathSafety.EnsureUnderRoot(escapedPath, root), "symlink traversal blocked");
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
            try {
                if (Directory.Exists(outside)) {
                    Directory.Delete(outside, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
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

        var functionCall = new JsonObject()
            .Add("type", "function_call")
            .Add("call_id", "call_2")
            .Add("function", new JsonObject().Add("name", "ad_whoami"))
            .Add("arguments", "{\"x\":1}");
        var turn2 = TurnInfo.FromJson(new JsonObject()
            .Add("id", "turn2")
            .Add("output", new JsonArray().Add(functionCall)));

        var calls2 = ToolCallParser.Extract(turn2);
        AssertEqual(1, calls2.Count, "function call count");
        AssertEqual("call_2", calls2[0].CallId, "function call id");
        AssertEqual("ad_whoami", calls2[0].Name, "function call name");
        AssertEqual(1, (int)(calls2[0].Arguments?.GetInt64("x") ?? 0), "function call arg");
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

    private static int GetRpcPendingCount(JsonRpcClient client) {
        var field = typeof(JsonRpcClient).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance);
        AssertNotNull(field, "_pending field");
        var value = field!.GetValue(client);
        AssertNotNull(value, "_pending value");
        var prop = value!.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        AssertNotNull(prop, "_pending.Count property");
        return (int)(prop!.GetValue(value) ?? 0);
    }

    private static void TestRpcCallCancellationCleansPending() {
        var client = new JsonRpcClient(_ => Task.CompletedTask);
        long id = 0;
        client.CallStarted += (_, args) => id = args.RequestId ?? 0;

        var protocolError = false;
        client.ProtocolError += (_, _) => protocolError = true;

        using var cts = new CancellationTokenSource();
        var task = client.CallAsync("test", (JsonValue?)null, cts.Token);
        cts.Cancel();

        AssertThrows<OperationCanceledException>(() => task.GetAwaiter().GetResult(), "rpc call canceled");
        AssertEqual(0, GetRpcPendingCount(client), "pending empty after cancellation");

        // Late responses to a locally-canceled call should be ignored (not treated as protocol errors).
        client.HandleLine($"{{\"id\":{id},\"result\":123}}");
        AssertEqual(false, protocolError, "late response ignored");
    }

    private static void TestRpcCallSendFailureCleansPending() {
        var sendEx = new InvalidOperationException("send failed");
        var client = new JsonRpcClient(_ => Task.FromException(sendEx));
        var callCompleted = false;
        client.CallCompleted += (_, args) => callCompleted = !args.Success;

        long id = 0;
        client.CallStarted += (_, args) => id = args.RequestId ?? 0;

        AssertThrows<InvalidOperationException>(() => client.CallAsync("test", (JsonValue?)null, CancellationToken.None).GetAwaiter().GetResult(),
            "rpc send failure");
        AssertEqual(true, id > 0, "id assigned");
        AssertEqual(true, callCompleted, "call completed fired");
        AssertEqual(0, GetRpcPendingCount(client), "pending empty after send failure");
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

}
