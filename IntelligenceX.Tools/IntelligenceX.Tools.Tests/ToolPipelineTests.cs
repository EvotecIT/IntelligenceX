using System.Text.Json;
using System.IO;
using System.Threading;
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
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
    public async Task Reliability_ShouldWrapMiddlewareChainAndRetryTransientMiddlewareFailures() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());

        var middlewareCalls = 0;
        var terminalCalls = 0;

        var json = await ToolPipeline.RunAsync(
            definition: definition,
            arguments: null,
            cancellationToken: CancellationToken.None,
            binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
            terminal: (_, _) => {
                terminalCalls++;
                return Task.FromResult(ToolResultV2.OkModel(new {
                    MiddlewareCalls = middlewareCalls,
                    TerminalCalls = terminalCalls
                }));
            },
            reliability: new ToolPipelineReliabilityOptions {
                MaxAttempts = 3,
                RetryTransientErrors = true,
                BaseDelayMs = 0,
                MaxDelayMs = 0,
                DelayAsync = static (_, _) => Task.CompletedTask
            },
            middleware: new ToolPipelineMiddleware<StubRequest>[] {
                (context, token, next) => {
                    middlewareCalls++;
                    if (middlewareCalls == 1) {
                        return Task.FromResult(ToolResponse.Error("timeout", "Transient middleware failure.", isTransient: true));
                    }

                    return next(context, token);
                }
            });

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(2, root.GetProperty("middleware_calls").GetInt32());
        Assert.Equal(1, root.GetProperty("terminal_calls").GetInt32());
        Assert.Equal(2, middlewareCalls);
        Assert.Equal(1, terminalCalls);
    }

    [Fact]
    public async Task Reliability_WhenCallerCancellationOccurs_ShouldPropagateWithoutRetry() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());
        using var cancellationSource = new CancellationTokenSource();
        var attempts = 0;

        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            await ToolPipeline.RunAsync(
                definition: definition,
                arguments: null,
                cancellationToken: cancellationSource.Token,
                binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
                terminal: (_, cancellationToken) => {
                    attempts++;
                    cancellationSource.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(ToolResultV2.OkModel(new { Attempt = attempts }));
                },
                reliability: new ToolPipelineReliabilityOptions {
                    MaxAttempts = 3,
                    RetryTransientErrors = true,
                    RetryExceptions = true,
                    BaseDelayMs = 0,
                    MaxDelayMs = 0,
                    DelayAsync = static (_, _) => Task.CompletedTask
                });
        });

        Assert.Equal(cancellationSource.Token, cancellation.CancellationToken);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Reliability_WhenCallerCancellationOccurs_ShouldNotInvokeRetryDelay() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());
        using var cancellationSource = new CancellationTokenSource();
        var delayCalls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            await ToolPipeline.RunAsync(
                definition: definition,
                arguments: null,
                cancellationToken: cancellationSource.Token,
                binder: _ => ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7)),
                terminal: (_, cancellationToken) => {
                    cancellationSource.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.FromResult(ToolResultV2.OkModel(new { Attempt = 1 }));
                },
                reliability: new ToolPipelineReliabilityOptions {
                    MaxAttempts = 3,
                    RetryTransientErrors = true,
                    RetryExceptions = true,
                    BaseDelayMs = 1,
                    MaxDelayMs = 1,
                    DelayAsync = (_, _) => {
                        Interlocked.Increment(ref delayCalls);
                        return Task.CompletedTask;
                    }
                });
        });

        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public async Task Reliability_WhenTokenIsPreCanceled_ShouldNotInvokeBinderOrRetryDelay() {
        var definition = new ToolDefinition(
            "pipeline_stub",
            "Pipeline stub",
            ToolSchema.Object().NoAdditionalProperties());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var binderCalls = 0;
        var delayCalls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(async () => {
            await ToolPipeline.RunAsync(
                definition: definition,
                arguments: null,
                cancellationToken: cancellationSource.Token,
                binder: _ => {
                    binderCalls++;
                    return ToolRequestBindingResult<StubRequest>.Success(new StubRequest(7));
                },
                terminal: (_, _) => Task.FromResult(ToolResultV2.OkModel(new { Attempt = 1 })),
                reliability: new ToolPipelineReliabilityOptions {
                    MaxAttempts = 3,
                    RetryTransientErrors = true,
                    RetryExceptions = true,
                    BaseDelayMs = 1,
                    MaxDelayMs = 1,
                    DelayAsync = (_, _) => {
                        Interlocked.Increment(ref delayCalls);
                        return Task.CompletedTask;
                    }
                });
        });

        Assert.Equal(0, binderCalls);
        Assert.Equal(0, delayCalls);
    }

    [Fact]
    public void ReliabilityProfiles_ShouldReturnEquivalentButDistinctInstances() {
        var firstReadOnly = ToolPipelineReliabilityProfiles.ReadOnlyQuery;
        var secondReadOnly = ToolPipelineReliabilityProfiles.ReadOnlyQuery;
        Assert.NotSame(firstReadOnly, secondReadOnly);
        AssertOptionsEquivalent(firstReadOnly, secondReadOnly);

        var firstProbe = ToolPipelineReliabilityProfiles.FastNetworkProbe;
        var secondProbe = ToolPipelineReliabilityProfiles.FastNetworkProbe;
        Assert.NotSame(firstProbe, secondProbe);
        AssertOptionsEquivalent(firstProbe, secondProbe);

        var clone = firstProbe.Clone();
        Assert.NotSame(firstProbe, clone);
        AssertOptionsEquivalent(firstProbe, clone);
    }

    [Fact]
    public void ReliabilityOptionsClone_ShouldKeepDelegateReferences() {
        Func<DateTimeOffset> nowProvider = () => DateTimeOffset.UtcNow;
        Func<TimeSpan, CancellationToken, Task> delayAsync = (_, _) => Task.CompletedTask;
        var options = new ToolPipelineReliabilityOptions {
            MaxAttempts = 2,
            RetryTransientErrors = true,
            RetryExceptions = true,
            RetryNonTransientExceptions = false,
            AttemptTimeoutMs = 1_000,
            BaseDelayMs = 50,
            MaxDelayMs = 250,
            JitterRatio = 0.15d,
            EnableCircuitBreaker = true,
            CircuitFailureThreshold = 3,
            CircuitOpenMs = 7_500,
            CircuitKey = "probe",
            UtcNowProvider = nowProvider,
            DelayAsync = delayAsync
        };

        var clone = options.Clone();
        AssertOptionsEquivalent(options, clone);
        Assert.Same(nowProvider, clone.UtcNowProvider);
        Assert.Same(delayAsync, clone.DelayAsync);
    }

    [Fact]
    public void ReliabilityOptionsWith_ShouldReturnCustomizedCopyWithoutMutatingOriginal() {
        var baseline = ToolPipelineReliabilityProfiles.ReadOnlyQuery;

        var customized = baseline.With(static options => {
            options.MaxAttempts = 6;
            options.AttemptTimeoutMs = 2_500;
            options.CircuitKey = "custom_readonly";
        });

        Assert.NotSame(baseline, customized);
        Assert.Equal(6, customized.MaxAttempts);
        Assert.Equal(2_500, customized.AttemptTimeoutMs);
        Assert.Equal("custom_readonly", customized.CircuitKey);

        Assert.Equal(3, baseline.MaxAttempts);
        Assert.Equal(0, baseline.AttemptTimeoutMs);
        Assert.Null(baseline.CircuitKey);
        Assert.Equal(baseline.BaseDelayMs, customized.BaseDelayMs);
        Assert.Equal(baseline.MaxDelayMs, customized.MaxDelayMs);
        Assert.Equal(baseline.JitterRatio, customized.JitterRatio);
    }

    [Fact]
    public void ReliabilityOptionsWith_ShouldThrowOnNullConfigure() {
        var baseline = ToolPipelineReliabilityProfiles.ReadOnlyQuery;
        Assert.Throws<ArgumentNullException>(() => baseline.With(null!));
    }

    [Fact]
    public void ReliabilityOptionsWith_ShouldProduceIndependentCopiesAcrossInvocations() {
        var baseline = ToolPipelineReliabilityProfiles.ReadOnlyQuery;

        var first = baseline.With(static options => {
            options.MaxAttempts = 2;
            options.CircuitKey = "first";
        });
        var second = baseline.With(static options => {
            options.MaxAttempts = 7;
            options.CircuitKey = "second";
        });

        Assert.NotSame(first, second);
        Assert.Equal(2, first.MaxAttempts);
        Assert.Equal("first", first.CircuitKey);
        Assert.Equal(7, second.MaxAttempts);
        Assert.Equal("second", second.CircuitKey);
        Assert.Equal(3, baseline.MaxAttempts);
        Assert.Null(baseline.CircuitKey);
    }

    [Fact]
    public void ReliabilityProfilesWithOverrides_ShouldThrowOnNullConfigure() {
        Assert.Throws<ArgumentNullException>(() => ToolPipelineReliabilityProfiles.ReadOnlyQueryWith(null!));
        Assert.Throws<ArgumentNullException>(() => ToolPipelineReliabilityProfiles.FastNetworkProbeWith(null!));
    }

    [Fact]
    public void ReliabilityProfilesWithOverrides_ShouldStartFromTemplateDefaults() {
        var customizedReadOnly = ToolPipelineReliabilityProfiles.ReadOnlyQueryWith(static options => {
            options.MaxAttempts = 5;
        });

        Assert.Equal(5, customizedReadOnly.MaxAttempts);
        Assert.Equal(120, customizedReadOnly.BaseDelayMs);
        Assert.Equal(1200, customizedReadOnly.MaxDelayMs);
        Assert.True(customizedReadOnly.RetryExceptions);
        Assert.True(customizedReadOnly.EnableCircuitBreaker);
    }

    [Fact]
    public async Task ReliabilityProfilesWithOverrides_ShouldKeepTemplatesUnchangedAcrossConcurrentCalls() {
        var tasks = new List<Task<ToolPipelineReliabilityOptions>>();
        for (var i = 0; i < 20; i++) {
            var local = i;
            tasks.Add(Task.Run(() => ToolPipelineReliabilityProfiles.ReadOnlyQueryWith(options => {
                options.MaxAttempts = (local % 10) + 1;
                options.CircuitKey = $"concurrent_{local}";
            })));
        }

        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < results.Length; i++) {
            var result = results[i];
            Assert.Equal((i % 10) + 1, result.MaxAttempts);
            Assert.Equal($"concurrent_{i}", result.CircuitKey);
            Assert.Equal(120, result.BaseDelayMs);
            Assert.Equal(1200, result.MaxDelayMs);
            Assert.True(result.EnableCircuitBreaker);
        }

        var baseline = ToolPipelineReliabilityProfiles.ReadOnlyQuery;
        Assert.Equal(3, baseline.MaxAttempts);
        Assert.Null(baseline.CircuitKey);
        Assert.Equal(120, baseline.BaseDelayMs);
        Assert.Equal(1200, baseline.MaxDelayMs);
        Assert.True(baseline.EnableCircuitBreaker);
    }

    [Fact]
    public void ReliabilityOptionsBuilderBuild_ShouldNormalizeOutOfRangeValues() {
        var options = new ToolPipelineReliabilityOptionsBuilder {
            MaxAttempts = 99,
            AttemptTimeoutMs = -1,
            BaseDelayMs = -5,
            MaxDelayMs = -10,
            JitterRatio = 2.0d,
            CircuitFailureThreshold = 0,
            CircuitOpenMs = 1,
            CircuitKey = "  sample_key  "
        }.Build();

        Assert.Equal(10, options.MaxAttempts);
        Assert.Equal(0, options.AttemptTimeoutMs);
        Assert.Equal(0, options.BaseDelayMs);
        Assert.Equal(0, options.MaxDelayMs);
        Assert.Equal(0.5d, options.JitterRatio);
        Assert.Equal(1, options.CircuitFailureThreshold);
        Assert.Equal(100, options.CircuitOpenMs);
        Assert.Equal("sample_key", options.CircuitKey);
    }

    [Fact]
    public void ReliabilityProfilesWithOverrides_ShouldCustomizeCopyWithoutMutatingTemplates() {
        var customizedReadOnly = ToolPipelineReliabilityProfiles.ReadOnlyQueryWith(static options => {
            options.MaxAttempts = 5;
            options.BaseDelayMs = 10;
            options.MaxDelayMs = 20;
        });
        Assert.Equal(5, customizedReadOnly.MaxAttempts);
        Assert.Equal(10, customizedReadOnly.BaseDelayMs);
        Assert.Equal(20, customizedReadOnly.MaxDelayMs);

        var defaultReadOnly = ToolPipelineReliabilityProfiles.ReadOnlyQuery;
        Assert.Equal(3, defaultReadOnly.MaxAttempts);
        Assert.Equal(120, defaultReadOnly.BaseDelayMs);
        Assert.Equal(1200, defaultReadOnly.MaxDelayMs);

        var customizedProbe = ToolPipelineReliabilityProfiles.FastNetworkProbeWith(static options => {
            options.AttemptTimeoutMs = 2_000;
            options.CircuitOpenMs = 2_500;
        });
        Assert.Equal(2_000, customizedProbe.AttemptTimeoutMs);
        Assert.Equal(2_500, customizedProbe.CircuitOpenMs);

        var defaultProbe = ToolPipelineReliabilityProfiles.FastNetworkProbe;
        Assert.Equal(12_000, defaultProbe.AttemptTimeoutMs);
        Assert.Equal(10_000, defaultProbe.CircuitOpenMs);
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

    private static void AssertOptionsEquivalent(
        ToolPipelineReliabilityOptions expected,
        ToolPipelineReliabilityOptions actual) {
        Assert.Equal(expected.MaxAttempts, actual.MaxAttempts);
        Assert.Equal(expected.RetryTransientErrors, actual.RetryTransientErrors);
        Assert.Equal(expected.RetryExceptions, actual.RetryExceptions);
        Assert.Equal(expected.RetryNonTransientExceptions, actual.RetryNonTransientExceptions);
        Assert.Equal(expected.AttemptTimeoutMs, actual.AttemptTimeoutMs);
        Assert.Equal(expected.BaseDelayMs, actual.BaseDelayMs);
        Assert.Equal(expected.MaxDelayMs, actual.MaxDelayMs);
        Assert.Equal(expected.JitterRatio, actual.JitterRatio);
        Assert.Equal(expected.EnableCircuitBreaker, actual.EnableCircuitBreaker);
        Assert.Equal(expected.CircuitFailureThreshold, actual.CircuitFailureThreshold);
        Assert.Equal(expected.CircuitOpenMs, actual.CircuitOpenMs);
        Assert.Equal(expected.CircuitKey, actual.CircuitKey);
        Assert.Equal(expected.UtcNowProvider, actual.UtcNowProvider);
        Assert.Equal(expected.DelayAsync, actual.DelayAsync);
    }
}
