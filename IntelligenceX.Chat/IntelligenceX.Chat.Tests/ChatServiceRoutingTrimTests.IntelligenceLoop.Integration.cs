using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public async Task PhaseProgressLoop_ThrowsWhenWriterIsNull() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => session.RunPhaseProgressLoopAsync(
            null!,
            "req-intelligence-loop",
            "thread-intelligence-loop",
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            0,
            CancellationToken.None,
            Task.CompletedTask));

        Assert.Equal("writer", ex.ParamName);
    }

    [Fact]
    public async Task PhaseProgressLoop_ThrowsWhenPhaseTaskIsNull() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => session.RunPhaseProgressLoopAsync(
            writer,
            "req-intelligence-loop",
            "thread-intelligence-loop",
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            0,
            CancellationToken.None,
            null!));

        Assert.Equal("phaseTask", ex.ParamName);
    }

    [Fact]
    public async Task PhaseProgressLoopForTesting_ThrowsWhenHeartbeatTaskFactoryIsNull() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(() => session.RunPhaseProgressLoopForTestingAsync(
            writer,
            "req-intelligence-loop",
            "thread-intelligence-loop",
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            1,
            CancellationToken.None,
            Task.CompletedTask,
            null!));

        Assert.Equal("heartbeatTaskFactory", ex.ParamName);
    }

    [Fact]
    public async Task PhaseProgressLoop_EmitsPlanExecuteReviewInOrder() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokePhaseProgressLoopAsync(session, writer, "phase_plan", "Planning...", "Planning", 0, Task.CompletedTask);
        await InvokePhaseProgressLoopAsync(session, writer, "phase_execute", "Executing...", "Executing", 0, Task.CompletedTask);
        await InvokePhaseProgressLoopAsync(session, writer, "phase_review", "Reviewing...", "Reviewing", 0, Task.CompletedTask);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(new[] { "phase_plan", "phase_execute", "phase_review" }, statuses);
    }

    [Fact]
    public async Task PhaseProgressLoop_EmitsHeartbeatForLongRunningPhase() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var invokeTask = InvokePhaseProgressLoopAsync(session, writer, "phase_review", "Reviewing...", "Reviewing response", 1, completion.Task);
        await WaitForStatusAsync(capture, "phase_heartbeat", TimeSpan.FromSeconds(5));
        completion.TrySetResult(null);
        await invokeTask;

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Contains("phase_review", statuses);
        Assert.Contains("phase_heartbeat", statuses);
        var reviewIndex = statuses.IndexOf("phase_review");
        var heartbeatIndex = statuses.IndexOf("phase_heartbeat");
        Assert.True(reviewIndex >= 0 && heartbeatIndex > reviewIndex);
    }

    [Fact]
    public async Task PhaseProgressLoop_DoesNotEmitHeartbeatWhenDisabled() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var invokeTask = InvokePhaseProgressLoopAsync(
            session,
            writer,
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            0,
            completion.Task);
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        completion.TrySetResult(null);
        await invokeTask;

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Contains("phase_review", statuses);
        Assert.DoesNotContain("phase_heartbeat", statuses);
    }

    [Fact]
    public async Task PhaseProgressLoop_PropagatesCancellationWhenPhaseTaskIsCanceled() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        using var cts = new CancellationTokenSource();

        var phaseTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        var invokeTask = InvokePhaseProgressLoopAsync(
            session,
            writer,
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            1,
            phaseTask,
            cts.Token);

        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invokeTask);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Contains("phase_review", statuses);
    }

    [Fact]
    public async Task PhaseProgressLoop_PropagatesPhaseTaskFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        var phaseTask = Task.FromException(new InvalidOperationException("phase-failed"));
        var invokeTask = InvokePhaseProgressLoopAsync(
            session,
            writer,
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            1,
            phaseTask);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => invokeTask);
        Assert.Equal("phase-failed", ex.Message);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Contains("phase_review", statuses);
    }

    [Fact]
    public async Task PhaseProgressLoop_HeartbeatFailureDoesNotOverridePhaseFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new FailOnWriteNumberCaptureStream(2);
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        async Task FailingPhaseAsync() {
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            throw new InvalidOperationException("phase-failed");
        }

        var invokeTask = InvokePhaseProgressLoopAsync(
            session,
            writer,
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            1,
            FailingPhaseAsync());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => invokeTask);
        Assert.Equal("phase-failed", ex.Message);
    }

    [Fact]
    public async Task PhaseProgressLoop_UnexpectedHeartbeatFailureIsRethrownWhenPhaseSucceeds() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        async Task FaultingHeartbeatAsync(CancellationToken _) {
            await Task.Yield();
            throw new InvalidOperationException("heartbeat-failed");
        }

        var invokeTask = InvokePhaseProgressLoopAsync(
            session,
            writer,
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            1,
            Task.CompletedTask,
            heartbeatTaskFactory: FaultingHeartbeatAsync);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => invokeTask);
        Assert.Equal("heartbeat-failed", ex.Message);
    }

    [Fact]
    public async Task PhaseProgressLoop_SuppressesCanceledHeartbeatFailureWhenOuterTokenIsCanceled() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();

        Task CanceledHeartbeatAsync(CancellationToken _) =>
            Task.FromException(new OperationCanceledException("outer-canceled", innerException: null, outerCts.Token));

        await InvokePhaseProgressLoopAsync(
            session,
            writer,
            "phase_review",
            "Reviewing...",
            "Reviewing response",
            1,
            Task.CompletedTask,
            outerCts.Token,
            CanceledHeartbeatAsync);
    }

    [Fact]
    public void ShouldSuppressPhaseHeartbeatFailure_RequiresMatchingCanceledToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        using var unrelatedCts = new CancellationTokenSource();

        heartbeatCts.Cancel();
        outerCts.Cancel();
        unrelatedCts.Cancel();

        Assert.True(ChatServiceSession.ShouldSuppressPhaseHeartbeatFailure(
            new IOException("io"),
            heartbeatCts.Token,
            outerCts.Token));
        Assert.True(ChatServiceSession.ShouldSuppressPhaseHeartbeatFailure(
            new OperationCanceledException("heartbeat", innerException: null, heartbeatCts.Token),
            heartbeatCts.Token,
            outerCts.Token));
        Assert.True(ChatServiceSession.ShouldSuppressPhaseHeartbeatFailure(
            new OperationCanceledException("outer", innerException: null, outerCts.Token),
            heartbeatCts.Token,
            outerCts.Token));
        Assert.False(ChatServiceSession.ShouldSuppressPhaseHeartbeatFailure(
            new OperationCanceledException("unrelated", innerException: null, unrelatedCts.Token),
            heartbeatCts.Token,
            outerCts.Token));
    }

    [Fact]
    public async Task ToolRoundStatusLifecycle_EmitsRoundStatusesInDeterministicOrder() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await session.WriteToolRoundStartedStatusAsync(
            writer,
            requestId: "req-rounds",
            threadId: "thread-rounds",
            roundNumber: 1,
            maxRounds: 2,
            callCount: 3,
            parallelTools: true,
            allowMutatingParallel: false);
        await session.WriteToolRoundCompletedStatusAsync(
            writer,
            requestId: "req-rounds",
            threadId: "thread-rounds",
            roundNumber: 1,
            maxRounds: 2,
            callCount: 3,
            failedCalls: 0);
        await session.WriteToolRoundStartedStatusAsync(
            writer,
            requestId: "req-rounds",
            threadId: "thread-rounds",
            roundNumber: 2,
            maxRounds: 2,
            callCount: 2,
            parallelTools: true,
            allowMutatingParallel: false);
        await session.WriteToolRoundCompletedStatusAsync(
            writer,
            requestId: "req-rounds",
            threadId: "thread-rounds",
            roundNumber: 2,
            maxRounds: 2,
            callCount: 2,
            failedCalls: 1);
        await session.WriteToolRoundLimitReachedStatusAsync(
            writer,
            requestId: "req-rounds",
            threadId: "thread-rounds",
            maxRounds: 2,
            totalToolCalls: 5,
            totalToolOutputs: 5);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(
            new[] {
                "tool_round_started",
                "tool_round_completed",
                "tool_round_started",
                "tool_round_completed",
                "tool_round_limit_reached"
            },
            statuses);
    }

    [Fact]
    public async Task SynchronizedCaptureStream_FlushAsync_CanceledTokenReturnsCanceledTask() {
        using var stream = new SynchronizedCaptureStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.FlushAsync(cts.Token));
    }

    [Fact]
    public async Task SynchronizedCaptureStream_WriteAsync_CanceledTokenReturnsCanceledTask() {
        using var stream = new SynchronizedCaptureStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var payload = new byte[] { 1, 2, 3 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.WriteAsync(payload, 0, payload.Length, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.WriteAsync(payload.AsMemory(), cts.Token).AsTask());
    }

    private static async Task InvokePhaseProgressLoopAsync(ChatServiceSession session, StreamWriter writer, string phaseStatus, string phaseMessage,
        string heartbeatLabel, int heartbeatSeconds, Task phaseTask, CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? heartbeatTaskFactory = null) {
        if (heartbeatTaskFactory is null) {
            await session.RunPhaseProgressLoopAsync(
                writer,
                "req-intelligence-loop",
                "thread-intelligence-loop",
                phaseStatus,
                phaseMessage,
                heartbeatLabel,
                heartbeatSeconds,
                cancellationToken,
                phaseTask);
            return;
        }

        await session.RunPhaseProgressLoopForTestingAsync(
            writer,
            "req-intelligence-loop",
            "thread-intelligence-loop",
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            cancellationToken,
            phaseTask,
            heartbeatTaskFactory);
    }

    private static List<string> ParseStatuses(byte[] snapshotBytes) {
        using var snapshot = new MemoryStream(snapshotBytes, writable: false);
        using var reader = new StreamReader(snapshot, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var statuses = new List<string>();
        while (!reader.EndOfStream) {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!TryGetPropertyIgnoreCase(root, "type", out var typeEl)
                || !string.Equals(typeEl.GetString(), "chat_status", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (TryGetPropertyIgnoreCase(root, "status", out var statusEl)) {
                var status = statusEl.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(status)) {
                    statuses.Add(status.Trim());
                }
            }
        }

        return statuses;
    }

    private static async Task WaitForStatusAsync(SynchronizedCaptureStream stream, string status, TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            var statuses = ParseStatuses(stream.Snapshot());
            if (statuses.Contains(status, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var finalStatuses = ParseStatuses(stream.Snapshot());
        if (finalStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) {
            return;
        }

        throw new TimeoutException($"Timed out waiting for status '{status}'.");
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value) {
        foreach (var property in obj.EnumerateObject()) {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class FailOnWriteNumberCaptureStream : Stream {
        private readonly MemoryStream _inner = new();
        private readonly object _sync = new();
        private readonly int _failOnWriteNumber;
        private readonly Func<Exception> _exceptionFactory;
        private int _writeCount;

        public FailOnWriteNumberCaptureStream(int failOnWriteNumber, Func<Exception>? exceptionFactory = null) {
            _failOnWriteNumber = Math.Max(1, failOnWriteNumber);
            _exceptionFactory = exceptionFactory ?? (() => new IOException("Simulated heartbeat write failure."));
        }

        public byte[] Snapshot() {
            lock (_sync) {
                return _inner.ToArray();
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() {
            // No-op for capture stream.
        }

        public override Task FlushAsync(CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) {
            lock (_sync) {
                _writeCount++;
                if (_writeCount >= _failOnWriteNumber) {
                    throw _exceptionFactory();
                }

                _inner.Write(buffer, offset, count);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }

    private sealed class SynchronizedCaptureStream : Stream {
        private readonly MemoryStream _inner = new();
        private readonly object _sync = new();

        public byte[] Snapshot() {
            lock (_sync) {
                return _inner.ToArray();
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length {
            get {
                lock (_sync) {
                    return _inner.Length;
                }
            }
        }

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() {
            lock (_sync) {
                _inner.Flush();
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            lock (_sync) {
                _inner.Flush();
            }
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) {
            lock (_sync) {
                _inner.SetLength(value);
            }
        }

        public override void Write(byte[] buffer, int offset, int count) {
            lock (_sync) {
                _inner.Write(buffer, offset, count);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested) {
                return Task.FromCanceled(cancellationToken);
            }

            lock (_sync) {
                _inner.Write(buffer, offset, count);
            }
            return Task.CompletedTask;
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        public override void Write(ReadOnlySpan<byte> buffer) {
            lock (_sync) {
                _inner.Write(buffer);
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            if (cancellationToken.IsCancellationRequested) {
                return ValueTask.FromCanceled(cancellationToken);
            }

            lock (_sync) {
                _inner.Write(buffer.Span);
            }
            return ValueTask.CompletedTask;
        }
#endif

        protected override void Dispose(bool disposing) {
            if (disposing) {
                lock (_sync) {
                    _inner.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
