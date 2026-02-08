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
        var method = GetStringFallbackMethod(transportType);

        var args = new object?[] { "Unknown parameter: 'foo'.", null };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(false, ok, "ok");
        AssertEqual("Parameters", args[1]?.ToString() ?? string.Empty, "fallbackKind");
    }

    private static void TestNativeToolSchemaSerializationSwitchesFieldName() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var enumType = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(enumType, "ToolWireFormat enum");

        var serialize = transportType.GetMethod("SerializeToolDefinition", BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(serialize, "SerializeToolDefinition method");

        var schema = new JsonObject().Add("type", "object");
        var tool = new ToolDefinition("test-tool", parameters: schema);

        var customParameters = Enum.Parse(enumType!, "CustomParameters");
        var customInputSchema = Enum.Parse(enumType!, "CustomInputSchema");
        var functionFlatParameters = Enum.Parse(enumType!, "FunctionFlatParameters");
        var functionFlatInputSchema = Enum.Parse(enumType!, "FunctionFlatInputSchema");

        var withCustomParameters = (JsonObject)serialize!.Invoke(null, new object?[] { tool, customParameters })!;
        AssertEqual("custom", withCustomParameters.GetString("type") ?? string.Empty, "type");
        AssertEqual(true, withCustomParameters.TryGetValue("parameters", out _), "parameters present");
        AssertEqual(false, withCustomParameters.TryGetValue("input_schema", out _), "input_schema absent");

        var withCustomInputSchema = (JsonObject)serialize!.Invoke(null, new object?[] { tool, customInputSchema })!;
        AssertEqual("custom", withCustomInputSchema.GetString("type") ?? string.Empty, "type");
        AssertEqual(false, withCustomInputSchema.TryGetValue("parameters", out _), "parameters absent");
        AssertEqual(true, withCustomInputSchema.TryGetValue("input_schema", out _), "input_schema present");

        var withFunctionFlatParameters = (JsonObject)serialize!.Invoke(null, new object?[] { tool, functionFlatParameters })!;
        AssertEqual("function", withFunctionFlatParameters.GetString("type") ?? string.Empty, "type");
        AssertEqual(true, withFunctionFlatParameters.TryGetValue("name", out _), "name present");
        AssertEqual(true, withFunctionFlatParameters.TryGetValue("parameters", out _), "parameters present");
        AssertEqual(false, withFunctionFlatParameters.TryGetValue("input_schema", out _), "input_schema absent");

        var withFunctionFlatInputSchema = (JsonObject)serialize!.Invoke(null, new object?[] { tool, functionFlatInputSchema })!;
        AssertEqual("function", withFunctionFlatInputSchema.GetString("type") ?? string.Empty, "type");
        AssertEqual(true, withFunctionFlatInputSchema.TryGetValue("name", out _), "name present");
        AssertEqual(false, withFunctionFlatInputSchema.TryGetValue("parameters", out _), "parameters absent");
        AssertEqual(true, withFunctionFlatInputSchema.TryGetValue("input_schema", out _), "input_schema present");
    }

    private static string DetectFallbackKind(string message) {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var method = GetStringFallbackMethod(transportType);

        var args = new object?[] { message, null };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(true, ok, "ok");

        var kind = args[1]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(kind)) {
            throw new InvalidOperationException("Expected fallback kind to be non-empty.");
        }
        return kind;
    }

    private static MethodInfo GetStringFallbackMethod(Type transportType) {
        var methods = transportType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var m in methods) {
            if (!string.Equals(m.Name, "TryGetToolSchemaKeyFallback", StringComparison.Ordinal)) {
                continue;
            }
            var ps = m.GetParameters();
            if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].IsOut) {
                return m;
            }
        }
        throw new InvalidOperationException("TryGetToolSchemaKeyFallback(string, out ...) method not found.");
    }
}
