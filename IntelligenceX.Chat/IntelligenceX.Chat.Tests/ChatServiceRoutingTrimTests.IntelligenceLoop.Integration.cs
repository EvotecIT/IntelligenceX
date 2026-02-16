using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo RunPhaseProgressLoopAsyncMethod =
        typeof(ChatServiceSession).GetMethod("RunPhaseProgressLoopAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RunPhaseProgressLoopAsync not found.");

    [Fact]
    public async Task PhaseProgressLoop_EmitsPlanExecuteReviewInOrder() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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

    private static async Task InvokePhaseProgressLoopAsync(ChatServiceSession session, StreamWriter writer, string phaseStatus, string phaseMessage,
        string heartbeatLabel, int heartbeatSeconds, Task phaseTask) {
        var args = new object?[] {
            writer,
            "req-intelligence-loop",
            "thread-intelligence-loop",
            phaseStatus,
            phaseMessage,
            heartbeatLabel,
            heartbeatSeconds,
            CancellationToken.None,
            phaseTask
        };

        var invoked = RunPhaseProgressLoopAsyncMethod.Invoke(session, args);
        var task = Assert.IsAssignableFrom<Task>(invoked);
        await task;
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
