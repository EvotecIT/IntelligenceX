using System.Text.Json;
using System.IO;
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

    [Fact]
    public async Task Reliability_ShouldRetryTransientEnvelopeAndEventuallySucceed() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var attempts = 0;
        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
            terminal: (_, _) => {
                attempts++;
                return Task.FromResult(
                    attempts == 1
                        ? ToolResponse.Error("timeout", "Temporary timeout.", isTransient: true)
                        : ToolResultV2.OkModel(new { Attempt = attempts }));
            },
            reliability: new ToolPipelineReliabilityOptions {
                MaxAttempts = 3,
                RetryTransientErrors = true,
                BaseDelayMs = 0,
                MaxDelayMs = 0,
                DelayAsync = static (_, _) => Task.CompletedTask
            });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(2, root.GetProperty("attempt").GetInt32());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Reliability_ShouldNotRetryNonTransientEnvelope() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var attempts = 0;
        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
            terminal: (_, _) => {
                attempts++;
                return Task.FromResult(ToolResponse.Error("invalid_argument", "Invalid input."));
            },
            reliability: new ToolPipelineReliabilityOptions {
                MaxAttempts = 3,
                RetryTransientErrors = true,
                BaseDelayMs = 0,
                MaxDelayMs = 0,
                DelayAsync = static (_, _) => Task.CompletedTask
            });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Reliability_ShouldRetryTransientExceptionAndEventuallySucceed() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var attempts = 0;
        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
            terminal: (_, _) => {
                attempts++;
                if (attempts == 1) {
                    throw new IOException("I/O probe failed.");
                }

                return Task.FromResult(ToolResultV2.OkModel(new { Attempt = attempts }));
            },
            reliability: new ToolPipelineReliabilityOptions {
                MaxAttempts = 3,
                RetryExceptions = true,
                BaseDelayMs = 0,
                MaxDelayMs = 0,
                DelayAsync = static (_, _) => Task.CompletedTask
            });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(2, root.GetProperty("attempt").GetInt32());
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Reliability_WhenAttemptTimeoutExceeded_ShouldReturnTimeoutError() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
            terminal: async (_, cancellationToken) => {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                return ToolResultV2.OkModel(new { Attempt = 1 });
            },
            reliability: new ToolPipelineReliabilityOptions {
                MaxAttempts = 1,
                AttemptTimeoutMs = 40,
                RetryTransientErrors = false
            });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("timeout", root.GetProperty("error_code").GetString());
        Assert.True(root.GetProperty("is_transient").GetBoolean());
    }

    [Fact]
    public async Task Reliability_WhenCircuitOpens_ShouldShortCircuitUntilCooldownExpires() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var now = new DateTimeOffset(2026, 2, 21, 0, 0, 0, TimeSpan.Zero);
        var circuitKey = "pipeline_test_circuit_" + Guid.NewGuid().ToString("N");
        var options = new ToolPipelineReliabilityOptions {
            MaxAttempts = 1,
            RetryTransientErrors = false,
            EnableCircuitBreaker = true,
            CircuitFailureThreshold = 2,
            CircuitOpenMs = 30_000,
            CircuitKey = circuitKey,
            UtcNowProvider = () => now
        };

        var terminalCalls = 0;

        static ToolRequestBindingResult<StubRequest> BindStub(JsonObject? _) =>
            ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7));

        async Task<string> InvokeOnce(bool transientFailure) {
            return await ToolPipeline.RunAsync(
                definition: definition,
                arguments: null,
                cancellationToken: CancellationToken.None,
                binder: BindStub,
                terminal: (_, _) => {
                    terminalCalls++;
                    return Task.FromResult(
                        transientFailure
                            ? ToolResponse.Error("timeout", "Temporary timeout.", isTransient: true)
                            : ToolResultV2.OkModel(new { Attempt = terminalCalls }));
                },
                reliability: options);
        }

        var first = await InvokeOnce(transientFailure: true);
        var second = await InvokeOnce(transientFailure: true);
        var third = await InvokeOnce(transientFailure: false);

        using var firstDoc = JsonDocument.Parse(first);
        using var secondDoc = JsonDocument.Parse(second);
        using var thirdDoc = JsonDocument.Parse(third);

        Assert.False(firstDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(secondDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(thirdDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("circuit_open", thirdDoc.RootElement.GetProperty("error_code").GetString());
        Assert.Equal(2, terminalCalls);

        now = now.AddSeconds(31);
        var fourth = await InvokeOnce(transientFailure: false);
        using var fourthDoc = JsonDocument.Parse(fourth);
        Assert.True(fourthDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(3, terminalCalls);
    }
}
