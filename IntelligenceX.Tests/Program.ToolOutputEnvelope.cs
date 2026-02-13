using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestToolOutputEnvelopeErrorOmitsMetaWhenNull() {
        var envelope = ToolOutputEnvelope.ErrorObject(
            errorCode: "invalid_argument",
            error: "Missing required value.",
            hints: new[] { "Provide name." },
            isTransient: false,
            meta: null);

        AssertEqual(false, envelope.GetBoolean("ok"), "ok");
        AssertEqual("invalid_argument", envelope.GetString("error_code") ?? string.Empty, "error_code");
        AssertEqual(false, envelope.TryGetValue("meta", out _), "meta omitted");
    }

    private static void TestToolOutputEnvelopeErrorIncludesMetaWhenProvided() {
        var meta = new JsonObject()
            .Add("error", new JsonObject()
                .Add("category", "contract_mismatch")
                .Add("retryable", false));

        var envelope = ToolOutputEnvelope.ErrorObject(
            errorCode: "invalid_contract",
            error: "Contract check failed.",
            meta: meta);

        AssertEqual(true, envelope.TryGetValue("meta", out _), "meta present");
        var metaObject = envelope.GetObject("meta");
        AssertNotNull(metaObject, "meta object");
        var errorObject = metaObject!.GetObject("error");
        AssertNotNull(errorObject, "meta.error object");
        AssertEqual("contract_mismatch", errorObject!.GetString("category") ?? string.Empty, "meta.error.category");
    }

    private static void TestToolOutputEnvelopeErrorStringIncludesMetaWhenProvided() {
        var meta = new JsonObject()
            .Add("error", new JsonObject().Add("category", "environment"));

        var json = ToolOutputEnvelope.Error(
            errorCode: "tool_failed",
            error: "Tool execution failed.",
            meta: meta);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        AssertEqual(false, root.GetProperty("ok").GetBoolean(), "ok");
        AssertEqual("tool_failed", root.GetProperty("error_code").GetString() ?? string.Empty, "error_code");
        AssertEqual("environment", root.GetProperty("meta").GetProperty("error").GetProperty("category").GetString() ?? string.Empty,
            "meta.error.category");
    }
}
