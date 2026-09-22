namespace IntelligenceX.Chat.App;

/// <summary>
/// Tracks settings mutations so window shutdown can wait for their persistence and rollback work.
/// </summary>
internal sealed class SettingsMutationDrain {
    private readonly object _sync = new();
    private int _activeCount;
    private bool _draining;
    private TaskCompletionSource? _drainCompletion;

    /// <summary>
    /// Runs a mutation when shutdown has not started and tracks it through completion.
    /// </summary>
    internal Task RunAsync(Func<Task> mutation) {
        ArgumentNullException.ThrowIfNull(mutation);

        lock (_sync) {
            if (_draining) {
                return Task.CompletedTask;
            }

            _activeCount++;
        }

        return RunTrackedAsync(mutation);
    }

    /// <summary>
    /// Stops accepting new mutations and completes after every registered mutation settles.
    /// </summary>
    internal Task DrainAsync() {
        lock (_sync) {
            _draining = true;
            if (_activeCount == 0) {
                return Task.CompletedTask;
            }

            _drainCompletion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drainCompletion.Task;
        }
    }

    private async Task RunTrackedAsync(Func<Task> mutation) {
        try {
            await mutation().ConfigureAwait(false);
        } finally {
            TaskCompletionSource? completion = null;
            lock (_sync) {
                _activeCount--;
                if (_draining && _activeCount == 0) {
                    completion = _drainCompletion;
                }
            }

            completion?.TrySetResult();
        }
    }
}
