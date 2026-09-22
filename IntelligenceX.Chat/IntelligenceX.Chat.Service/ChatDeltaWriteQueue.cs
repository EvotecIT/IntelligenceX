using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Service;

/// <summary>
/// Preserves provider delta order while keeping synchronous provider callbacks non-blocking.
/// </summary>
internal sealed class ChatDeltaWriteQueue {
    private readonly Channel<string> _channel;
    private readonly Func<string, Task> _writeAsync;
    private readonly Task _pumpTask;
    private int _completed;

    public ChatDeltaWriteQueue(Func<string, Task> writeAsync) {
        _writeAsync = writeAsync ?? throw new ArgumentNullException(nameof(writeAsync));
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _pumpTask = PumpAsync();
    }

    public bool TryEnqueue(string delta) =>
        Volatile.Read(ref _completed) == 0 && _channel.Writer.TryWrite(delta ?? string.Empty);

    public async Task CompleteAsync() {
        if (Interlocked.Exchange(ref _completed, 1) == 0) {
            _channel.Writer.TryComplete();
        }

        await _pumpTask.ConfigureAwait(false);
    }

    private async Task PumpAsync() {
        await foreach (var delta in _channel.Reader.ReadAllAsync().ConfigureAwait(false)) {
            await _writeAsync(delta).ConfigureAwait(false);
        }
    }
}
