using System;
using System.Collections.Generic;
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

    private static void TestNativeToolChoiceSerializationMatchesWireFormat() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var enumType = transportType.GetNestedType("ToolWireFormat", BindingFlags.NonPublic);
        AssertNotNull(enumType, "ToolWireFormat enum");

        var method = transportType.GetMethod("SerializeToolChoice", BindingFlags.NonPublic | BindingFlags.Static);
        AssertNotNull(method, "SerializeToolChoice method");

        var customParameters = Enum.Parse(enumType!, "CustomParameters");
        var functionFlatParameters = Enum.Parse(enumType!, "FunctionFlatParameters");

        var forced = ToolChoice.Custom("test-tool");

        var functionChoice = method!.Invoke(null, new object?[] { forced, functionFlatParameters });
        var functionObj = functionChoice as JsonObject;
        AssertNotNull(functionObj, "function tool_choice as JsonObject");
        AssertEqual("function", functionObj!.GetString("type") ?? string.Empty, "type");
        var function = functionObj.GetObject("function");
        AssertNotNull(function, "function object");
        AssertEqual("test-tool", function!.GetString("name") ?? string.Empty, "name");

        var customChoice = (JsonObject)method!.Invoke(null, new object?[] { forced, customParameters })!;
        AssertEqual("custom", customChoice.GetString("type") ?? string.Empty, "type");
        AssertEqual("test-tool", customChoice.GetString("name") ?? string.Empty, "name");

        var autoChoice = (string)method!.Invoke(null, new object?[] { ToolChoice.Auto, functionFlatParameters })!;
        AssertEqual("auto", autoChoice, "tool_choice");
    }

    private static void TestNativeToolSchemaFallbackHandlesAggregateException() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var methods = transportType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? method = null;
        foreach (var m in methods) {
            if (!string.Equals(m.Name, "TryGetToolSchemaKeyFallback", StringComparison.Ordinal)) {
                continue;
            }
            var ps = m.GetParameters();
            if (ps.Length == 2 &&
                ps[0].ParameterType == typeof(Exception) &&
                ps[1].IsOut &&
                ps[1].ParameterType.IsByRef &&
                (ps[1].ParameterType.GetElementType()?.IsEnum ?? false)) {
                method = m;
                break;
            }
        }
        AssertNotNull(method, "TryGetToolSchemaKeyFallback(Exception, out enum)");

        var errorExType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeErrorResponseException", throwOnError: true)!;
        var ctor = errorExType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(System.Net.HttpStatusCode), typeof(bool) },
            modifiers: null);
        AssertNotNull(ctor, "OpenAINativeErrorResponseException ctor");

        var errorEx = (Exception)ctor!.Invoke(new object?[] {
            "Server rejected tool schema.",
            "raw payload",
            "unknown_parameter",
            "tools[0].parameters",
            System.Net.HttpStatusCode.BadRequest,
            false
        })!;

        var aggregate = new AggregateException(errorEx);
        var args = new object?[] { aggregate, CreateOutSlot(method!) };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(true, ok, "ok");
        AssertEqual("InputSchema", args[1]?.ToString() ?? string.Empty, "fallbackKind");
    }

    private static void TestNativeToolSchemaFallbackUsesStructuredErrorData() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;

        var methods = transportType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? method = null;
        foreach (var m in methods) {
            if (!string.Equals(m.Name, "TryGetToolSchemaKeyFallback", StringComparison.Ordinal)) {
                continue;
            }
            var ps = m.GetParameters();
            if (ps.Length == 2 &&
                ps[0].ParameterType == typeof(InvalidOperationException) &&
                ps[1].IsOut &&
                ps[1].ParameterType.IsByRef &&
                (ps[1].ParameterType.GetElementType()?.IsEnum ?? false)) {
                method = m;
                break;
            }
        }
        AssertNotNull(method, "TryGetToolSchemaKeyFallback(InvalidOperationException, out ToolSchemaKey)");

        var errorExType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeErrorResponseException", throwOnError: true)!;
        var ctor = errorExType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(System.Net.HttpStatusCode), typeof(bool) },
            modifiers: null);
        AssertNotNull(ctor, "OpenAINativeErrorResponseException ctor");

        var errorEx = (Exception)ctor!.Invoke(new object?[] {
            "Server rejected tool schema.",
            "raw payload",
            "unknown_parameter",
            "tools[0].parameters",
            System.Net.HttpStatusCode.BadRequest,
            false
        })!;

        var args = new object?[] { errorEx, CreateOutSlot(method!) };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(true, ok, "ok");
        AssertEqual("InputSchema", args[1]?.ToString() ?? string.Empty, "fallbackKind");

        // Guard against false positives: arbitrary InvalidOperationException.Data should not trigger fallback.
        var unrelated = new InvalidOperationException("Server rejected tool schema.");
        unrelated.Data["openai:error_param"] = "tools[0].parameters";
        unrelated.Data["openai:error_code"] = "unknown_parameter";
        args = new object?[] { unrelated, CreateOutSlot(method!) };
        ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(false, ok, "ok");

        // If a wrapper preserves native diagnostic data, fallback should still trigger.
        unrelated.Data["openai:native_transport"] = true;
        args = new object?[] { unrelated, CreateOutSlot(method!) };
        ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(true, ok, "ok");

        // Without an unknown-parameter code, we should not attempt schema-key retries.
        unrelated.Data.Remove("openai:error_code");
        args = new object?[] { unrelated, CreateOutSlot(method!) };
        ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(false, ok, "ok");
    }

    private static void TestNativeToolSchemaFallbackIgnoresUnrelated() {
        var ix = typeof(IntelligenceXClient).Assembly;
        var transportType = ix.GetType("IntelligenceX.OpenAI.Native.OpenAINativeTransport", throwOnError: true)!;
        var method = GetStringFallbackMethod(transportType);

        var args = new object?[] { "Unknown parameter: 'foo'.", CreateOutSlot(method) };
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

        var args = new object?[] { message, CreateOutSlot(method) };
        var ok = (bool)method!.Invoke(null, args)!;
        AssertEqual(true, ok, "ok");

        var kind = args[1]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(kind)) {
            throw new InvalidOperationException("Expected fallback kind to be non-empty.");
        }
        return kind;
    }

    private static object CreateOutSlot(MethodInfo method) {
        var ps = method.GetParameters();
        if (ps.Length != 2) {
            throw new InvalidOperationException("Expected TryGetToolSchemaKeyFallback to have exactly two parameters.");
        }
        var byRef = ps[1].ParameterType;
        var type = byRef.IsByRef ? byRef.GetElementType() : byRef;
        if (type is null) {
            throw new InvalidOperationException("Failed to determine out parameter type for TryGetToolSchemaKeyFallback.");
        }
        return Activator.CreateInstance(type)!;
    }

    private static MethodInfo GetStringFallbackMethod(Type transportType) {
        var methods = transportType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        var candidates = new List<MethodInfo>();
        foreach (var m in methods) {
            if (!string.Equals(m.Name, "TryGetToolSchemaKeyFallback", StringComparison.Ordinal)) {
                continue;
            }
            var ps = m.GetParameters();
            if (ps.Length == 2 &&
                ps[0].ParameterType == typeof(string) &&
                ps[1].IsOut &&
                ps[1].ParameterType.IsByRef &&
                (ps[1].ParameterType.GetElementType()?.IsEnum ?? false)) {
                candidates.Add(m);
            }
        }

        if (candidates.Count == 1) {
            return candidates[0];
        }

        // If there are multiple candidates, select the one that actually detects a tool-schema key swap.
        foreach (var candidate in candidates) {
            var args = new object?[] { "Unknown parameter: 'tools[1].parameters'.", CreateOutSlot(candidate) };
            var ok = (bool)candidate.Invoke(null, args)!;
            if (ok) {
                return candidate;
            }
        }

        throw new InvalidOperationException("TryGetToolSchemaKeyFallback(string, out enum) method not found.");
    }
}
