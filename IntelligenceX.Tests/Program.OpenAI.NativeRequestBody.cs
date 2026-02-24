using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestNativeRequestBodyOmitsPreviousResponseId() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType);
        AssertNotNull(options, "OpenAINativeOptions");

        var transport = Activator.CreateInstance(transportType, options);
        AssertNotNull(transport, "OpenAINativeTransport");

        var wireEnum = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(wireEnum, "ToolWireFormat enum");
        var customParameters = Enum.Parse(wireEnum!, "CustomParameters");

        var method = transportType.GetMethod("BuildRequestBody", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "BuildRequestBody method");

        var messages = new List<JsonObject>();
        var chatOptions = new ChatOptions {
            PreviousResponseId = "prev_123"
        };

        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var prev = body.GetString("previous_response_id");
        AssertEqual(true, prev is null, "previous_response_id is omitted");
    }

    private static void TestNativeRequestBodyNormalizesToolsAndToolChoice() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType);
        AssertNotNull(options, "OpenAINativeOptions");

        var transport = Activator.CreateInstance(transportType, options);
        AssertNotNull(transport, "OpenAINativeTransport");

        var wireEnum = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(wireEnum, "ToolWireFormat enum");
        var customParameters = Enum.Parse(wireEnum!, "CustomParameters");

        var method = transportType.GetMethod("BuildRequestBody", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "BuildRequestBody method");

        var messages = new List<JsonObject>();
        var schema = new JsonObject().Add("type", "object");

        var chatOptions = new ChatOptions {
            Tools = new[] {
                new ToolDefinition("eventlog_live_query", parameters: schema),
                new ToolDefinition("EVENTLOG_LIVE_QUERY", parameters: schema),
                new ToolDefinition("ad_domain_info", parameters: schema)
            },
            ToolChoice = ToolChoice.Custom("missing_tool")
        };

        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var tools = body.GetArray("tools");
        AssertNotNull(tools, "tools array");
        AssertEqual(2, tools!.Count, "duplicate tool names are de-duplicated");
        AssertEqual("auto", body.GetString("tool_choice") ?? string.Empty, "missing custom tool choice falls back to auto");

        chatOptions.ToolChoice = ToolChoice.Custom(" ad_domain_info ");
        body = (JsonObject)method.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var toolChoice = body.GetObject("tool_choice");
        AssertNotNull(toolChoice, "tool_choice object");
        AssertEqual("custom", toolChoice!.GetString("type") ?? string.Empty, "custom choice type");
        AssertEqual("ad_domain_info", toolChoice.GetString("name") ?? string.Empty, "custom tool choice name is normalized");
    }

    private static void TestNativeRequestBodyNormalizesToolReplayInputItems() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType);
        AssertNotNull(options, "OpenAINativeOptions");

        var transport = Activator.CreateInstance(transportType, options);
        AssertNotNull(transport, "OpenAINativeTransport");

        var wireEnum = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(wireEnum, "ToolWireFormat enum");
        var customParameters = Enum.Parse(wireEnum!, "CustomParameters");

        var method = transportType.GetMethod("BuildRequestBody", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "BuildRequestBody method");

        var messages = new List<JsonObject> {
            new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", "call_123")
                .Add("name", "ad_scope_discovery")
                .Add("input", "{\"domain\":\"ad.evotec.xyz\"}")
                .Add("arguments", "{\"domain\":\"ad.evotec.xyz\"}"),
            new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("call_id", "call_123")
                .Add("output", "{\"ok\":true}")
        };

        var chatOptions = new ChatOptions();
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var input = body.GetArray("input");
        AssertNotNull(input, "input array");
        AssertEqual(2, input!.Count, "input item count");

        var first = input[0].AsObject();
        AssertNotNull(first, "first normalized item");
        AssertEqual("custom_tool_call", first!.GetString("type") ?? string.Empty, "normalized tool call type");
        AssertEqual("call_123", first.GetString("call_id") ?? string.Empty, "normalized tool call id");
        var normalizedId = first.GetString("id") ?? string.Empty;
        AssertEqual(true, normalizedId.StartsWith("ctc", StringComparison.OrdinalIgnoreCase), "normalized tool call wire id");
        AssertEqual("ad_scope_discovery", first.GetString("name") ?? string.Empty, "normalized tool call name");
        AssertEqual("{\"domain\":\"ad.evotec.xyz\"}", first.GetString("input") ?? string.Empty, "normalized tool call input");
        AssertEqual(true, first.GetString("arguments") is null, "legacy arguments field removed");

        var second = input[1].AsObject();
        AssertNotNull(second, "second normalized item");
        AssertEqual("custom_tool_call_output", second!.GetString("type") ?? string.Empty, "normalized tool output type");
        AssertEqual("call_123", second.GetString("call_id") ?? string.Empty, "normalized tool output id");
        AssertEqual("{\"ok\":true}", second.GetString("output") ?? string.Empty, "normalized tool output payload");
    }

    private static void TestNativeRequestBodyFiltersUnpairedToolReplayItems() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType);
        AssertNotNull(options, "OpenAINativeOptions");

        var transport = Activator.CreateInstance(transportType, options);
        AssertNotNull(transport, "OpenAINativeTransport");

        var wireEnum = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(wireEnum, "ToolWireFormat enum");
        var customParameters = Enum.Parse(wireEnum!, "CustomParameters");

        var method = transportType.GetMethod("BuildRequestBody", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "BuildRequestBody method");

        var messages = new List<JsonObject> {
            new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", "orphan_call")
                .Add("name", "ad_scope_discovery")
                .Add("input", "{\"domain\":\"ad.evotec.xyz\"}"),
            new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("call_id", "orphan_output")
                .Add("output", "{\"ok\":false}"),
            new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", "paired_call")
                .Add("name", "ad_scope_discovery")
                .Add("input", "{\"domain\":\"ad.evotec.xyz\"}"),
            new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("call_id", "paired_call")
                .Add("output", "{\"ok\":true}")
        };

        var chatOptions = new ChatOptions();
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var input = body.GetArray("input");
        AssertNotNull(input, "input array");
        AssertEqual(2, input!.Count, "only paired replay items are retained");

        var first = input[0].AsObject();
        AssertNotNull(first, "paired call item");
        AssertEqual("paired_call", first!.GetString("call_id") ?? string.Empty, "paired call id");

        var second = input[1].AsObject();
        AssertNotNull(second, "paired output item");
        AssertEqual("paired_call", second!.GetString("call_id") ?? string.Empty, "paired output call id");
    }

    private static void TestNativeRequestBodyNormalizesTypeMissingToolReplayItems() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType);
        AssertNotNull(options, "OpenAINativeOptions");

        var transport = Activator.CreateInstance(transportType, options);
        AssertNotNull(transport, "OpenAINativeTransport");

        var wireEnum = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(wireEnum, "ToolWireFormat enum");
        var customParameters = Enum.Parse(wireEnum!, "CustomParameters");

        var method = transportType.GetMethod("BuildRequestBody", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "BuildRequestBody method");

        var messages = new List<JsonObject> {
            new JsonObject()
                .Add("call_id", "shape_call_1")
                .Add("name", "ad_scope_discovery")
                .Add("arguments", "{\"domain\":\"ad.evotec.xyz\"}"),
            new JsonObject()
                .Add("call_id", "shape_call_1")
                .Add("result", "{\"ok\":true}")
        };

        var chatOptions = new ChatOptions();
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var input = body.GetArray("input");
        AssertNotNull(input, "input array");
        AssertEqual(2, input!.Count, "type-missing replay item count");

        var first = input[0].AsObject();
        AssertNotNull(first, "first type-missing normalized item");
        AssertEqual("custom_tool_call", first!.GetString("type") ?? string.Empty, "type-missing call normalized type");
        AssertEqual("shape_call_1", first.GetString("call_id") ?? string.Empty, "type-missing call normalized id");
        AssertEqual(true, first.GetString("arguments") is null, "type-missing call drops legacy arguments");
        AssertEqual("{\"domain\":\"ad.evotec.xyz\"}", first.GetString("input") ?? string.Empty, "type-missing call normalized input");

        var second = input[1].AsObject();
        AssertNotNull(second, "second type-missing normalized item");
        AssertEqual("custom_tool_call_output", second!.GetString("type") ?? string.Empty, "type-missing output normalized type");
        AssertEqual("shape_call_1", second.GetString("call_id") ?? string.Empty, "type-missing output normalized id");
        AssertEqual("{\"ok\":true}", second.GetString("output") ?? string.Empty, "type-missing output normalized payload");
    }

    private static void TestNativeRequestBodyDeduplicatesReplayPairsAndStripsLegacyArguments() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var options = Activator.CreateInstance(optionsType);
        AssertNotNull(options, "OpenAINativeOptions");

        var transport = Activator.CreateInstance(transportType, options);
        AssertNotNull(transport, "OpenAINativeTransport");

        var wireEnum = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(wireEnum, "ToolWireFormat enum");
        var customParameters = Enum.Parse(wireEnum!, "CustomParameters");

        var method = transportType.GetMethod("BuildRequestBody", BindingFlags.Instance | BindingFlags.NonPublic);
        AssertNotNull(method, "BuildRequestBody method");

        var messages = new List<JsonObject> {
            new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", "call_dup")
                .Add("name", "ad_scope_discovery")
                .Add("input", "{\"domain\":\"ad.evotec.xyz\"}")
                .Add("arguments", "{\"domain\":\"ad.evotec.xyz\"}"),
            new JsonObject()
                .Add("type", "tool_call")
                .Add("call_id", "call_dup")
                .Add("name", "ad_scope_discovery")
                .Add("arguments", "{\"domain\":\"ad.evotec.xyz\"}"),
            new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("call_id", "call_dup")
                .Add("output", "{\"ok\":true}"),
            new JsonObject()
                .Add("type", "diagnostic")
                .Add("arguments", "{\"unexpected\":true}")
                .Add("value", "kept")
        };

        var chatOptions = new ChatOptions();
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.3-codex", messages, "session", chatOptions, customParameters })!;
        var input = body.GetArray("input");
        AssertNotNull(input, "input array");
        AssertEqual(3, input!.Count, "deduplicated input item count");

        var callCount = 0;
        var outputCount = 0;
        var diagnosticCount = 0;
        for (var i = 0; i < input.Count; i++) {
            var item = input[i].AsObject();
            AssertNotNull(item, "normalized replay item object");
            var type = item!.GetString("type") ?? string.Empty;
            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                callCount++;
                AssertEqual("call_dup", item.GetString("call_id") ?? string.Empty, "dedup call id");
                AssertEqual(true, item.GetString("arguments") is null, "dedup call strips arguments field");
            } else if (string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                outputCount++;
                AssertEqual("call_dup", item.GetString("call_id") ?? string.Empty, "dedup output call id");
            } else if (string.Equals(type, "diagnostic", StringComparison.OrdinalIgnoreCase)) {
                diagnosticCount++;
                AssertEqual(true, item.GetString("arguments") is null, "non-tool replay strips legacy arguments");
                AssertEqual("kept", item.GetString("value") ?? string.Empty, "non-tool replay keeps payload");
            }
        }

        AssertEqual(1, callCount, "exactly one replay call retained");
        AssertEqual(1, outputCount, "exactly one replay output retained");
        AssertEqual(1, diagnosticCount, "diagnostic item retained");
    }
}
