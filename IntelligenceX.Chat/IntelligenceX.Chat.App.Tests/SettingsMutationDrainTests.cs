using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies that Settings shutdown drains registered profile and provider mutations.
/// </summary>
public sealed class SettingsMutationDrainTests {
    /// <summary>
    /// Verifies that drain waits for active work and prevents later work from starting.
    /// </summary>
    [Fact]
    public async Task DrainAsync_WaitsForActiveMutationAndRejectsNewWork() {
        var drain = new SettingsMutationDrain();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejectedMutationRan = false;

        var activeMutation = drain.RunAsync(async () => {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var draining = drain.DrainAsync();
        Assert.False(draining.IsCompleted);

        await drain.RunAsync(() => {
            rejectedMutationRan = true;
            return Task.CompletedTask;
        });
        Assert.False(rejectedMutationRan);

        release.TrySetResult();
        await activeMutation.WaitAsync(TimeSpan.FromSeconds(5));
        await draining.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Verifies that a failed mutation still releases the shutdown drain.
    /// </summary>
    [Fact]
    public async Task DrainAsync_CompletesAfterFaultedMutationSettles() {
        var drain = new SettingsMutationDrain();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var activeMutation = drain.RunAsync(async () => {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            throw new InvalidOperationException("Expected test failure.");
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var draining = drain.DrainAsync();
        release.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => activeMutation.WaitAsync(TimeSpan.FromSeconds(5)));
        await draining.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
