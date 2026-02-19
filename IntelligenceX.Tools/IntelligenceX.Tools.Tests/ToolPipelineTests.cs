using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolPipelineTests {
    private sealed record StubRequest(int Value);

    [Fact]
    public async Task RunAsync_WhenBindingFails_ShouldReturnInvalidArgumentEnvelope() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Failure("value is required."),
            terminal: (_, _) => Task.FromResult(ToolResultV2.OkModel(new { Value = 1 })));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Equal("value is required.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RunAsync_ShouldApplyMiddlewareInOrder() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());
        var sequence = new List<string>();

        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
            terminal: (_, _) => {
                sequence.Add("terminal");
                return Task.FromResult(ToolResultV2.OkModel(new { Sequence = string.Join(",", sequence) }));
            },
            middleware: new ToolPipelineMiddleware<StubRequest>[] {
                async (context, cancellationToken, next) => {
                    sequence.Add("a_before");
                    var result = await next(context, cancellationToken).ConfigureAwait(false);
                    sequence.Add("a_after");
                    return result;
                },
                async (context, cancellationToken, next) => {
                    sequence.Add("b_before");
                    var result = await next(context, cancellationToken).ConfigureAwait(false);
                    sequence.Add("b_after");
                    return result;
                }
            });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("a_before,b_before,terminal", root.GetProperty("sequence").GetString());
        Assert.Equal(
            new[] { "a_before", "b_before", "terminal", "b_after", "a_after" },
            sequence);
    }

    [Fact]
    public async Task RunAsync_ShouldExposeBoundRequestToTerminal() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object(("value", ToolSchema.Integer())).NoAdditionalProperties());
        var arguments = new JsonObject().Add("value", 42);

        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: arguments,
            cancellationToken: CancellationToken.None,
            binder: args => {
                var value = args?.GetInt64("value") ?? 0;
                return ToolRequestBindingResult<StubRequest>.Success(new StubRequest((int)value));
            },
            terminal: (context, _) => Task.FromResult(ToolResultV2.OkModel(new {
                Value = context.Request.Value
            })));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(42, root.GetProperty("value").GetInt32());
    }
}
