using System;
using System.Reflection;
using IntelligenceX.OpenAI;

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

