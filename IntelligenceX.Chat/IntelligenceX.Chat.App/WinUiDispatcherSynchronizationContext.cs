using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Keeps code-only WinUI continuations on the application dispatcher.
/// </summary>
internal sealed class WinUiDispatcherSynchronizationContext : SynchronizationContext {
    private readonly DispatcherQueue _dispatcher;

    public WinUiDispatcherSynchronizationContext(DispatcherQueue dispatcher) {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public override SynchronizationContext CreateCopy() => new WinUiDispatcherSynchronizationContext(_dispatcher);

    public override void Post(SendOrPostCallback callback, object? state) {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_dispatcher.TryEnqueue(() => callback(state))) {
            throw new InvalidOperationException("The WinUI dispatcher queue is no longer available.");
        }
    }

    public override void Send(SendOrPostCallback callback, object? state) {
        ArgumentNullException.ThrowIfNull(callback);
        if (_dispatcher.HasThreadAccess) {
            callback(state);
            return;
        }

        Exception? dispatchException = null;
        using var completed = new ManualResetEventSlim();
        if (!_dispatcher.TryEnqueue(() => {
                try {
                    callback(state);
                } catch (Exception ex) {
                    dispatchException = ex;
                } finally {
                    completed.Set();
                }
            })) {
            throw new InvalidOperationException("The WinUI dispatcher queue is no longer available.");
        }

        completed.Wait();
        if (dispatchException is not null) {
            ExceptionDispatchInfo.Capture(dispatchException).Throw();
        }
    }
}
