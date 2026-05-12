using System;
using System.Collections.Generic;
using System.IO;
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

        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
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

        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
        var tools = body.GetArray("tools");
        AssertNotNull(tools, "tools array");
        AssertEqual(2, tools!.Count, "duplicate tool names are de-duplicated");
        AssertEqual("auto", body.GetString("tool_choice") ?? string.Empty, "missing custom tool choice falls back to auto");

        chatOptions.ToolChoice = ToolChoice.Custom(" ad_domain_info ");
        body = (JsonObject)method.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
        var toolChoice = body.GetObject("tool_choice");
        AssertNotNull(toolChoice, "tool_choice object");
        AssertEqual("custom", toolChoice!.GetString("type") ?? string.Empty, "custom choice type");
        AssertEqual("ad_domain_info", toolChoice.GetString("name") ?? string.Empty, "custom tool choice name is normalized");
    }

    private static void TestNativeRequestBodyIncludesImageGenerationTool() {
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

        var body = (JsonObject)method!.Invoke(transport, new object?[] {
            "gpt-5.5",
            new List<JsonObject>(),
            "session",
            new ChatOptions {
                ImageGeneration = new ImageGenerationOptions {
                    Enabled = true,
                    Quality = "high",
                    Size = "1536x1024",
                    OutputFormat = "jpg",
                    OutputCompression = 200,
                    Background = "auto"
                }
            },
            customParameters
        })!;

        var tools = body.GetArray("tools");
        AssertNotNull(tools, "tools array");
        AssertEqual(1, tools!.Count, "image generation tool count");
        var imageTool = tools[0].AsObject();
        AssertNotNull(imageTool, "image generation tool object");
        AssertEqual("image_generation", imageTool!.GetString("type") ?? string.Empty, "tool type");
        AssertEqual("high", imageTool.GetString("quality") ?? string.Empty, "quality");
        AssertEqual("1536x1024", imageTool.GetString("size") ?? string.Empty, "size");
        AssertEqual("jpeg", imageTool.GetString("output_format") ?? string.Empty, "output format");
        AssertEqual("auto", imageTool.GetString("background") ?? string.Empty, "background");
        AssertEqual(false, imageTool.TryGetValue("output_compression", out _), "invalid output compression omitted");
        AssertEqual(false, body.TryGetValue("tool_choice", out _), "tool_choice omitted when only hosted image tool is present");
    }

    private static void TestNativeImageGenerationOutputSavesBase64Payload() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var optionsType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeOptions", throwOnError: true)!;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var outputRoot = Path.Combine(Path.GetTempPath(), "ix-imagegen-" + Guid.NewGuid().ToString("N"));
        try {
            var options = Activator.CreateInstance(optionsType);
            AssertNotNull(options, "OpenAINativeOptions");

            var transport = Activator.CreateInstance(transportType, options);
            AssertNotNull(transport, "OpenAINativeTransport");

            var method = transportType.GetMethod("ParseOutputsFromResponse", BindingFlags.Instance | BindingFlags.NonPublic);
            AssertNotNull(method, "ParseOutputsFromResponse method");

            var response = new JsonObject()
                .Add("output", new JsonArray {
                    new JsonObject()
                        .Add("type", "image_generation_call")
                        .Add("id", "../ig 1")
                        .Add("status", "completed")
                        .Add("revised_prompt", "A blue square")
                        .Add("result", "Zm9v")
                });

            var outputs = (List<JsonObject>)method!.Invoke(transport, new object?[] {
                response,
                "session/../1",
                new ChatOptions {
                    ImageGeneration = new ImageGenerationOptions {
                        Enabled = true,
                        OutputFormat = "png",
                        OutputDirectory = outputRoot
                    }
                }
            })!;

            AssertEqual(1, outputs.Count, "image output count");
            var output = outputs[0];
            AssertEqual("image", output.GetString("type") ?? string.Empty, "output type");
            AssertEqual("Zm9v", output.GetString("base64") ?? string.Empty, "base64");
            var path = output.GetString("path");
            AssertNotNull(path, "saved image path");
            AssertEqual(true, File.Exists(path!), "saved image file exists");
            AssertEqual("foo", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path!)), "saved image bytes");
            AssertEqual(true, path!.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase), "saved image stays under output root");

            var noSaveResponse = new JsonObject()
                .Add("output", new JsonArray {
                    new JsonObject()
                        .Add("type", "image_generation_call")
                        .Add("status", "completed")
                        .Add("result", "YmFy")
                });

            var noSaveOutputs = (List<JsonObject>)method!.Invoke(transport, new object?[] {
                noSaveResponse,
                "session-no-save",
                new ChatOptions {
                    ImageGeneration = new ImageGenerationOptions {
                        Enabled = false,
                        OutputFormat = "png",
                        OutputDirectory = outputRoot
                    }
                }
            })!;

            AssertEqual(1, noSaveOutputs.Count, "disabled image output count");
            AssertEqual(false, noSaveOutputs[0].TryGetValue("path", out _), "disabled image generation does not save output");

            var fallbackIdResponse = new JsonObject()
                .Add("output", new JsonArray {
                    new JsonObject()
                        .Add("type", "image_generation_call")
                        .Add("status", "completed")
                        .Add("result", "YmF6"),
                    new JsonObject()
                        .Add("type", "image_generation_call")
                        .Add("status", "completed")
                        .Add("result", "cXV4")
                });

            var fallbackOutputs = (List<JsonObject>)method!.Invoke(transport, new object?[] {
                fallbackIdResponse,
                "session-fallback",
                new ChatOptions {
                    ImageGeneration = new ImageGenerationOptions {
                        Enabled = true,
                        OutputFormat = "png",
                        OutputDirectory = outputRoot
                    }
                }
            })!;

            AssertEqual(2, fallbackOutputs.Count, "fallback image output count");
            var firstFallbackPath = fallbackOutputs[0].GetString("path");
            var secondFallbackPath = fallbackOutputs[1].GetString("path");
            AssertNotNull(firstFallbackPath, "first fallback image path");
            AssertNotNull(secondFallbackPath, "second fallback image path");
            AssertEqual(false, string.Equals(firstFallbackPath, secondFallbackPath, StringComparison.OrdinalIgnoreCase), "fallback image paths are unique");
            AssertEqual(true, File.Exists(firstFallbackPath!), "first fallback image file exists");
            AssertEqual(true, File.Exists(secondFallbackPath!), "second fallback image file exists");
        } finally {
            if (Directory.Exists(outputRoot)) {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
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
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
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
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
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
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
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
        var body = (JsonObject)method!.Invoke(transport, new object?[] { "gpt-5.4", messages, "session", chatOptions, customParameters })!;
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

    private static void TestNativeInputNormalizationConvertsFunctionCallToCustomToolCall() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var normalizeMethod = transportType.GetMethod(
            "NormalizeInputItemForResponsesRequest",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(normalizeMethod, "NormalizeInputItemForResponsesRequest method");

        var source = new JsonObject()
            .Add("type", "function_call")
            .Add("id", "call_123")
            .Add("function", new JsonObject()
                .Add("name", "ad_replication_summary")
                .Add("arguments", "{\"scope\":\"forest\"}"))
            .Add("arguments", "{\"scope\":\"forest\"}");

        var normalized = (JsonObject)normalizeMethod!.Invoke(null, new object?[] { source })!;
        AssertEqual("custom_tool_call", normalized.GetString("type") ?? string.Empty, "normalized type");
        AssertEqual("call_123", normalized.GetString("call_id") ?? string.Empty, "normalized call id");
        AssertEqual("ad_replication_summary", normalized.GetString("name") ?? string.Empty, "normalized name");
        AssertEqual("{\"scope\":\"forest\"}", normalized.GetString("input") ?? string.Empty, "normalized input");
        AssertEqual(false, normalized.TryGetValue("arguments", out _), "arguments removed");
    }

    private static void TestNativeInputNormalizationConvertsFunctionCallOutputToCustomOutput() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var normalizeMethod = transportType.GetMethod(
            "NormalizeInputItemForResponsesRequest",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(normalizeMethod, "NormalizeInputItemForResponsesRequest method");

        var source = new JsonObject()
            .Add("type", "function_call_output")
            .Add("call_id", "call_200")
            .Add("output", "{\"ok\":true}");

        var normalized = (JsonObject)normalizeMethod!.Invoke(null, new object?[] { source })!;
        AssertEqual("custom_tool_call_output", normalized.GetString("type") ?? string.Empty, "normalized output type");
        AssertEqual("call_200", normalized.GetString("call_id") ?? string.Empty, "normalized output call id");
        AssertEqual("{\"ok\":true}", normalized.GetString("output") ?? string.Empty, "normalized output payload");
    }

    private static void TestNativeBuildCanonicalRequestMessagesNormalizesHistoryToolCalls() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var buildMethod = transportType.GetMethod(
            "BuildCanonicalRequestMessages",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(buildMethod, "BuildCanonicalRequestMessages method");

        var history = new List<JsonObject> {
            new JsonObject()
                .Add("type", "function_call")
                .Add("call_id", "call_hist")
                .Add("function", new JsonObject()
                    .Add("name", "ad_scope_discovery")
                    .Add("arguments", "{\"include_trusts\":true}"))
        };
        var inputItems = new List<JsonObject> {
            new JsonObject()
                .Add("type", "custom_tool_call_output")
                .Add("call_id", "call_hist")
                .Add("output", "{\"ok\":true}")
        };

        var canonical = (IReadOnlyList<JsonObject>)buildMethod!.Invoke(null, new object?[] { history, inputItems })!;
        AssertEqual(2, canonical.Count, "canonical message count");

        var first = canonical[0];
        AssertEqual("custom_tool_call", first.GetString("type") ?? string.Empty, "canonical tool call type");
        AssertEqual(false, first.TryGetValue("arguments", out _), "canonical tool call omits arguments");

        var second = canonical[1];
        AssertEqual("custom_tool_call_output", second.GetString("type") ?? string.Empty, "canonical tool output type");
        AssertEqual("call_hist", second.GetString("call_id") ?? string.Empty, "canonical tool output call id");
    }
}
