using System;
using System.Reflection;
using IntelligenceX.OpenAI;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestNativeToolSchemaFallbackDetectsIndex() {
        var fallback = DetectFallbackKind("Unknown parameter: 'tools[1].parameters'.");
        AssertEqual("InputSchema", fallback, "fallbackKind");

        fallback = DetectFallbackKind("Unrecognized request argument: tools[12].input_schema");
        AssertEqual("Parameters", fallback, "fallbackKind");
    }

    private static void TestNativeToolSchemaFallbackDetectsDotIndex() {
        var fallback = DetectFallbackKind("Unknown field tools.2.parameters");
        AssertEqual("InputSchema", fallback, "fallbackKind");

        fallback = DetectFallbackKind("Unknown parameter tools.0.input_schema");
        AssertEqual("Parameters", fallback, "fallbackKind");
    }

    private static void TestNativeToolSchemaFallbackIgnoresUnrelated() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var method = transportType.GetMethod("TryGetToolSchemaFallbackKind", BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(method, "TryGetToolSchemaFallbackKind method");

        var args = new object?[] { "Unknown parameter: 'foo'.", null };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(false, ok, "ok");
    }

    private static void TestNativeToolSchemaSerializationSwitchesFieldName() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var enumType = transportType.GetNestedType("ToolSchemaKind", BindingFlags.NonPublic);
        AssertNotNull(enumType, "ToolSchemaKind enum");

        var serialize = transportType.GetMethod("SerializeToolDefinition", BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(serialize, "SerializeToolDefinition method");

        var schema = new JsonObject().Add("type", "object");
        var tool = new ToolDefinition("test-tool", parameters: schema);

        var parametersKind = Enum.Parse(enumType!, "Parameters");
        var inputSchemaKind = Enum.Parse(enumType!, "InputSchema");

        var withParameters = (JsonObject)serialize!.Invoke(null, new object?[] { tool, parametersKind })!;
        AssertEqual(true, withParameters.TryGetValue("parameters", out _), "parameters present");
        AssertEqual(false, withParameters.TryGetValue("input_schema", out _), "input_schema absent");

        var withInputSchema = (JsonObject)serialize!.Invoke(null, new object?[] { tool, inputSchemaKind })!;
        AssertEqual(false, withInputSchema.TryGetValue("parameters", out _), "parameters absent");
        AssertEqual(true, withInputSchema.TryGetValue("input_schema", out _), "input_schema present");
    }

    private static string DetectFallbackKind(string message) {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var method = transportType.GetMethod("TryGetToolSchemaFallbackKind", BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(method, "TryGetToolSchemaFallbackKind method");

        var args = new object?[] { message, null };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(true, ok, "ok");

        var kind = args[1]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(kind)) {
            throw new InvalidOperationException("Expected fallback kind to be non-empty.");
        }
        return kind;
    }
}
