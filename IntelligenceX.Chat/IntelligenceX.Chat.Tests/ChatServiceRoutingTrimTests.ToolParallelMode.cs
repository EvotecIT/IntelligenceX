using System;
using IntelligenceX.Chat.Abstractions.Protocol;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData("allow_parallel", "allow_parallel")]
    [InlineData("allow-parallel", "allow_parallel")]
    [InlineData("on", "allow_parallel")]
    [InlineData("force_serial", "force_serial")]
    [InlineData("serial", "force_serial")]
    [InlineData("off", "force_serial")]
    [InlineData("auto", "auto")]
    [InlineData("default", "auto")]
    [InlineData("", "auto")]
    public void NormalizeParallelToolMode_NormalizesLegacyAndCurrentLabels(string? raw, string expected) {
        var result = NormalizeParallelToolModeMethod.Invoke(null, new object?[] { raw });
        var text = Assert.IsType<string>(result);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void ResolveParallelToolExecutionMode_RespectsExplicitParallelToolsFlagInAutoMode() {
        var options = new ChatRequestOptions {
            ParallelTools = false,
            ParallelToolMode = "auto"
        };

        var result = ResolveParallelToolExecutionModeMethod.Invoke(null, new object?[] { options, true, false });
        var decision = Assert.IsType<ValueTuple<bool, bool, string>>(result);

        Assert.False(decision.Item1);
        Assert.False(decision.Item2);
        Assert.Equal("auto", decision.Item3);
    }

    [Fact]
    public void ResolveParallelToolExecutionMode_UsesServiceDefaultWhenOptionsMissing() {
        var result = ResolveParallelToolExecutionModeMethod.Invoke(null, new object?[] { null, false, false });
        var decision = Assert.IsType<ValueTuple<bool, bool, string>>(result);

        Assert.False(decision.Item1);
        Assert.False(decision.Item2);
        Assert.Equal("auto", decision.Item3);
    }

    [Fact]
    public void ResolveParallelToolExecutionMode_ForcesSerialWhenRequested() {
        var options = new ChatRequestOptions {
            ParallelTools = true,
            ParallelToolMode = "force_serial"
        };

        var result = ResolveParallelToolExecutionModeMethod.Invoke(null, new object?[] { options, true, true });
        var decision = Assert.IsType<ValueTuple<bool, bool, string>>(result);

        Assert.False(decision.Item1);
        Assert.False(decision.Item2);
        Assert.Equal("force_serial", decision.Item3);
    }

    [Fact]
    public void ResolveParallelToolExecutionMode_AllowsMutatingParallelWhenRequested() {
        var options = new ChatRequestOptions {
            ParallelTools = false,
            ParallelToolMode = "allow_parallel"
        };

        var result = ResolveParallelToolExecutionModeMethod.Invoke(null, new object?[] { options, false, false });
        var decision = Assert.IsType<ValueTuple<bool, bool, string>>(result);

        Assert.True(decision.Item1);
        Assert.True(decision.Item2);
        Assert.Equal("allow_parallel", decision.Item3);
    }

    [Fact]
    public void ResolveParallelToolExecutionMode_FallsBackToLegacyParallelToolsFlagWhenModeMissing() {
        var options = new ChatRequestOptions {
            ParallelTools = false
        };

        var result = ResolveParallelToolExecutionModeMethod.Invoke(null, new object?[] { options, true, false });
        var decision = Assert.IsType<ValueTuple<bool, bool, string>>(result);

        Assert.False(decision.Item1);
        Assert.False(decision.Item2);
        Assert.Equal("auto", decision.Item3);
    }

    [Fact]
    public void ResolveParallelToolExecutionMode_UsesServiceDefaultMutatingPolicyInAutoMode() {
        var result = ResolveParallelToolExecutionModeMethod.Invoke(null, new object?[] { null, true, true });
        var decision = Assert.IsType<ValueTuple<bool, bool, string>>(result);

        Assert.True(decision.Item1);
        Assert.True(decision.Item2);
        Assert.Equal("auto", decision.Item3);
    }
}
