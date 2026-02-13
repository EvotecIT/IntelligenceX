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
}
