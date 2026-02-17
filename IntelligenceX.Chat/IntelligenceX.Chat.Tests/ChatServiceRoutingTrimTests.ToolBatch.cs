using System;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void ResolveParallelToolConcurrency_HandlesSmallCallCounts(int callCount, int expected) {
        var result = ResolveParallelToolConcurrencyMethod.Invoke(null, new object?[] { callCount });
        Assert.Equal(expected, Assert.IsType<int>(result));
    }

    [Fact]
    public void ResolveParallelToolConcurrency_CapsLargeBatchesToSafeRange() {
        var result = ResolveParallelToolConcurrencyMethod.Invoke(null, new object?[] { 128 });
        var value = Assert.IsType<int>(result);

        Assert.InRange(value, 2, 6);
    }

    [Fact]
    public void BuildToolBatchStartedMessage_DescribesBatchConcurrencyWhenCapped() {
        var result = BuildToolBatchStartedMessageMethod.Invoke(null, new object?[] { 9, 4 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("9 tool calls", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parallel batches", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 at a time", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolBatchProgressMessage_ReportsFailuresAndParallelism() {
        var result = BuildToolBatchProgressMessageMethod.Invoke(null, new object?[] { 3, 9, 4, 1 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("3/9", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 max parallel", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolBatchHeartbeatMessage_ReportsActiveQueueAndElapsed() {
        var result = BuildToolBatchHeartbeatMessageMethod.Invoke(null, new object?[] { 2, 8, 3, 5, 1, 17 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("2/8", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 active", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 queued", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("17s elapsed", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolBatchCompletedMessage_ReportsCompleteBatchWithoutFailures() {
        var result = BuildToolBatchCompletedMessageMethod.Invoke(null, new object?[] { 5, 3, 0 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("5/5", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 max parallel", text, StringComparison.OrdinalIgnoreCase);
    }
}
