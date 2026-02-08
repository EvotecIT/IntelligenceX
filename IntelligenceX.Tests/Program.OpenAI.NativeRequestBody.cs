using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

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
}

