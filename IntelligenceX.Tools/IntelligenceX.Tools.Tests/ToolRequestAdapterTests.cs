using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolRequestAdapterTests {
    private sealed record StubRequest(int Value);

    [Fact]
    public async Task RunPipelineAsync_WithAdapter_ShouldBindAndExecuteTypedRequest() {
        var adapter = new StubAdapter();
        var tool = new HarnessTool(adapter);
        var arguments = new JsonObject().Add("value", 42);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(42, root.GetProperty("value").GetInt32());
        Assert.Equal(1, adapter.BindCalls);
        Assert.Equal(1, adapter.ExecuteCalls);
    }

    [Fact]
    public async Task RunPipelineAsync_WithAdapterReliability_ShouldRetryTransientErrors() {
        var adapter = new TransientRetryAdapter();
        var tool = new HarnessTool(adapter);

        var json = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(2, root.GetProperty("attempt").GetInt32());
        Assert.Equal(2, adapter.ExecuteCalls);
    }

    private sealed class HarnessTool : ToolBase {
        private readonly ToolRequestAdapter<StubRequest> _adapter;

        public HarnessTool(ToolRequestAdapter<StubRequest> adapter) {
            _adapter = adapter;
        }

        public override ToolDefinition Definition { get; } = new(
            "adapter_harness",
            "Adapter harness",
            ToolSchema.Object().NoAdditionalProperties());

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return RunPipelineAsync(arguments, cancellationToken, _adapter);
        }
    }

    private sealed class StubAdapter : ToolRequestAdapter<StubRequest> {
        public int BindCalls { get; private set; }
        public int ExecuteCalls { get; private set; }

        public override ToolRequestBindingResult<StubRequest> Bind(JsonObject? arguments) {
            BindCalls++;
            var value = (int)(arguments?.GetInt64("value") ?? 0);
            return ToolRequestBindingResult<StubRequest>.Success(new StubRequest(value));
        }

        public override Task<string> ExecuteAsync(
            ToolPipelineContext<StubRequest> context,
            CancellationToken cancellationToken) {
            ExecuteCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ToolResultV2.OkModel(new { Value = context.Request.Value }));
        }
    }

    private sealed class TransientRetryAdapter : ToolRequestAdapter<StubRequest> {
        public int ExecuteCalls { get; private set; }

        public override ToolPipelineReliabilityOptions? Reliability => new() {
            MaxAttempts = 3,
            RetryTransientErrors = true,
            BaseDelayMs = 0,
            MaxDelayMs = 0,
            DelayAsync = static (_, _) => Task.CompletedTask
        };

        public override ToolRequestBindingResult<StubRequest> Bind(JsonObject? arguments) {
            _ = arguments;
            return ToolRequestBindingResult<StubRequest>.Success(new StubRequest(0));
        }

        public override Task<string> ExecuteAsync(
            ToolPipelineContext<StubRequest> context,
            CancellationToken cancellationToken) {
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCalls++;
            if (ExecuteCalls == 1) {
                return Task.FromResult(ToolResponse.Error("timeout", "Temporary timeout.", isTransient: true));
            }

            return Task.FromResult(ToolResultV2.OkModel(new { Attempt = ExecuteCalls }));
        }
    }
}

