using System;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestAppServerTransportNormalizesReplayInputItems() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Transport.AppServerTransport", throwOnError: true)!;
        var normalizeMethod = transportType.GetMethod(
            "NormalizeAndFilterReplayInputItems",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(normalizeMethod, "AppServerTransport.NormalizeAndFilterReplayInputItems");

        var replayItems = new JsonArray()
            .Add(new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", "call_dup")
                .Add("name", "ad_scope_discovery")
                .Add("input", "{\"domain\":\"ad.evotec.xyz\"}")
                .Add("arguments", "{\"domain\":\"ad.evotec.xyz\"}"))
            .Add(new JsonObject()
                .Add("type", "tool_call")
                .Add("call_id", "call_dup")
                .Add("name", "ad_scope_discovery")
                .Add("arguments", "{\"domain\":\"ad.evotec.xyz\"}"))
            .Add(new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("call_id", "call_dup")
                .Add("output", "{\"ok\":true}"))
            .Add(new JsonObject()
                .Add("type", "diagnostic")
                .Add("arguments", "{\"unexpected\":true}")
                .Add("value", "kept"));

        var normalizedObj = normalizeMethod!.Invoke(null, new object?[] { replayItems });
        var normalized = normalizedObj as JsonArray;
        AssertNotNull(normalized, "normalized replay items");
        AssertEqual(3, normalized!.Count, "normalized replay item count");

        var callCount = 0;
        var outputCount = 0;
        var diagnosticCount = 0;
        for (var i = 0; i < normalized.Count; i++) {
            var item = normalized[i].AsObject();
            AssertNotNull(item, "normalized replay item");
            var type = item!.GetString("type") ?? string.Empty;
            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                callCount++;
                AssertEqual("call_dup", item.GetString("call_id") ?? string.Empty, "normalized call id");
                AssertEqual(true, item.GetString("arguments") is null, "normalized call strips arguments");
            } else if (string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                outputCount++;
                AssertEqual("call_dup", item.GetString("call_id") ?? string.Empty, "normalized output call id");
            } else if (string.Equals(type, "diagnostic", StringComparison.OrdinalIgnoreCase)) {
                diagnosticCount++;
                AssertEqual(true, item.GetString("arguments") is null, "non-tool strips arguments");
                AssertEqual("kept", item.GetString("value") ?? string.Empty, "non-tool value retained");
            }
        }

        AssertEqual(1, callCount, "exactly one replay call retained");
        AssertEqual(1, outputCount, "exactly one replay output retained");
        AssertEqual(1, diagnosticCount, "diagnostic item retained");
    }
}
