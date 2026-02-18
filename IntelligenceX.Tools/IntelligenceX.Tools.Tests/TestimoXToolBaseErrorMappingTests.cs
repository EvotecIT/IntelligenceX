using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class TestimoXToolBaseErrorMappingTests {
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
    public void ErrorFromException_ShouldHideUnhandledExceptionDetails() {
        var tool = new HarnessTool();

        var json = tool.Map(new Exception("secret-token=abc123"), "TestimoX execution failed.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("TestimoX execution failed.", root.GetProperty("error").GetString());
    }

    [Fact]
    public void ErrorFromException_ShouldAllowFallbackCodeOverride() {
        var tool = new HarnessTool();

        var json = tool.Map(new Exception("details"), "TestimoX execution failed.", fallbackErrorCode: "execution_failed");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("execution_failed", root.GetProperty("error_code").GetString());
        Assert.Equal("TestimoX execution failed.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TryDiscoverBuiltinRuleNamesAsync_ShouldReturnMappedError_WhenDiscoveryThrows() {
        var tool = new HarnessTool();

        var (names, error) = await tool.TryDiscoverBuiltinAsync(
            CancellationToken.None,
            _ => throw new InvalidOperationException("loader crash"));

        Assert.Null(names);
        Assert.NotNull(error);
        using var doc = JsonDocument.Parse(error!);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Equal("loader crash", root.GetProperty("error").GetString());
    }

    private sealed class HarnessTool : TestimoXToolBase {
        private static readonly ToolDefinition DefinitionValue = new(
            "testimox_test_harness",
            "Test harness tool.",
            ToolSchema.Object().NoAdditionalProperties());

        public HarnessTool() : base(new TestimoXToolOptions()) { }

        public override ToolDefinition Definition => DefinitionValue;

        public string Map(
            Exception ex,
            string defaultMessage,
            string fallbackErrorCode = "query_failed") {
            return ErrorFromException(ex, defaultMessage, fallbackErrorCode);
        }

        public Task<(HashSet<string>? RuleNames, string? ErrorResponse)> TryDiscoverBuiltinAsync(
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<HashSet<string>>> discoverFunc) {
            return TryDiscoverBuiltinRuleNamesAsync(
                cancellationToken,
                defaultErrorMessage: "TestimoX builtin rule discovery failed.",
                discoverFunc: discoverFunc);
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }
    }
}
