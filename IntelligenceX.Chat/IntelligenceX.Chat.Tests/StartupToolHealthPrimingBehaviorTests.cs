using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupToolHealthPrimingBehaviorTests {
    [Fact]
    public async Task RunStartupToolHealthPrimingAsync_PrimingFaultIsNonFatalAndRecorded() {
        var warnings = new List<string>();

        await ChatServiceSession.RunStartupToolHealthPrimingAsync(
            _ => throw new InvalidOperationException("simulated priming fault"),
            warnings.Add,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Single(warnings);
        Assert.Contains("Startup probe priming failed", warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("simulated priming fault", warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunStartupToolHealthPrimingAsync_SessionCancellationSkipsWarning() {
        var warnings = new List<string>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await ChatServiceSession.RunStartupToolHealthPrimingAsync(
            token => Task.Delay(TimeSpan.FromSeconds(5), token),
            warnings.Add,
            TimeSpan.FromSeconds(1),
            cts.Token);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task AwaitStartupToolHealthPrimingForHelloAsync_ReturnsAfterWaitBudget() {
        var priming = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();

        await ChatServiceSession.AwaitStartupToolHealthPrimingForHelloAsync(
            priming.Task,
            TimeSpan.FromMilliseconds(40),
            CancellationToken.None);

        stopwatch.Stop();
        Assert.False(priming.Task.IsCompleted);
        Assert.InRange(stopwatch.ElapsedMilliseconds, 0, 1500);
    }

    [Fact]
    public async Task AwaitStartupToolHealthPrimingForHelloAsync_StopsWaitingWhenCanceled() {
        var priming = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var waitTask = ChatServiceSession.AwaitStartupToolHealthPrimingForHelloAsync(
            priming.Task,
            TimeSpan.FromSeconds(5),
            cts.Token);

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(waitTask, completed);
        await waitTask;
        Assert.False(priming.Task.IsCompleted);
    }
}
