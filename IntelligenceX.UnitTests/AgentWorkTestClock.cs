using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

namespace IntelligenceX.UnitTests;

// Handshakes keep virtual-time advances after the worker has registered its delay/deadline.
internal sealed class AgentWorkTestClock : TimeProvider {
    private readonly FakeTimeProvider _clock = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, int> _counts = new();
    private readonly Dictionary<(long, int), TaskCompletionSource<bool>> _scheduled = new();
    public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();
    public override long GetTimestamp() => _clock.GetTimestamp();
    public override long TimestampFrequency => _clock.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
        var timer = _clock.CreateTimer(callback, state, dueTime, period);
        lock (_gate) {
            _counts.TryGetValue(dueTime.Ticks, out var count);
            _counts[dueTime.Ticks] = ++count;
            if (_scheduled.TryGetValue((dueTime.Ticks, count), out var ready)) ready.TrySetResult(true);
        }
        return timer;
    }
    internal Task Scheduled(TimeSpan due, int count = 1) {
        lock (_gate) {
            if (_counts.TryGetValue(due.Ticks, out var current) && current >= count) return Task.CompletedTask;
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _scheduled[(due.Ticks, count)] = ready;
            return ready.Task;
        }
    }
    internal void Advance(TimeSpan amount) => _clock.Advance(amount);
}
