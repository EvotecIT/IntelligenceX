using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ActiveDirectoryToolBaseErrorMappingTests {
    [Fact]
    public void ErrorFromException_ShouldMapInvalidOperationToInvalidArgument() {
        var tool = new HarnessTool();

        var json = tool.Map(new InvalidOperationException("Bad input\r\nline2"), "Fallback.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Equal("Bad input line2", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ErrorFromException_ShouldMapTimeoutToTransientTimeoutError() {
        var tool = new HarnessTool();

        var json = tool.Map(new TimeoutException("Timed out talking to DC"), "Fallback.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("timeout", root.GetProperty("error_code").GetString());
        Assert.True(root.GetProperty("is_transient").GetBoolean());
    }

    [Fact]
    public void ErrorFromException_ShouldAllowInvalidOperationOverride() {
        var tool = new HarnessTool();

        var json = tool.Map(new InvalidOperationException("Service unavailable"), "Fallback.", invalidOperationErrorCode: "query_failed");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("Service unavailable", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ErrorFromException_ShouldHideUnhandledExceptionDetails() {
        var tool = new HarnessTool();

        var json = tool.Map(new Exception("secret-token=abc123"), "GPO list query failed.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("GPO list query failed.", root.GetProperty("error").GetString());
    }

    private sealed class HarnessTool : ActiveDirectoryToolBase {
        private static readonly ToolDefinition DefinitionValue = new(
            "ad_test_harness",
            "Test harness tool.",
            ToolSchema.Object().NoAdditionalProperties());

        public HarnessTool() : base(new ActiveDirectoryToolOptions()) { }

        public override ToolDefinition Definition => DefinitionValue;

        public string Map(
            Exception ex,
            string defaultMessage,
            string fallbackErrorCode = "query_failed",
            string invalidOperationErrorCode = "invalid_argument") {
            return ErrorFromException(
                ex,
                defaultMessage,
                fallbackErrorCode,
                invalidOperationErrorCode);
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }
    }
}
