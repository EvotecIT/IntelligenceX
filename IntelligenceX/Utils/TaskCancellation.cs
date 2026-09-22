using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Utils;

/// <summary>Bounds a caller's wait while observing non-cooperative work and releasing any late resource result.</summary>
internal static class TaskCancellation {
    internal static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken, Action<T>? releaseAbandoned = null) {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult(true));
        if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task || cancellationToken.IsCancellationRequested) {
            Task cleanup = task.ContinueWith(completed => {
                if (completed.Status == TaskStatus.RanToCompletion) releaseAbandoned?.Invoke(completed.Result);
                else _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = cleanup.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new OperationCanceledException(cancellationToken);
        }
        return await task.ConfigureAwait(false);
    }

    internal static async Task WaitAsync(Task task, CancellationToken cancellationToken) {
        async Task<bool> Complete() { await task.ConfigureAwait(false); return true; }
        await WaitAsync(Complete(), cancellationToken).ConfigureAwait(false);
    }
}
