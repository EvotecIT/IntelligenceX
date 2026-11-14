using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Connects the stdio host lifetime to its original launching process.</summary>
internal sealed class McpProcessLifetime : IDisposable {
    internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly Process? _parent;
    private readonly DateTime _parentStartedAtUtc;
    private readonly TaskCompletionSource<DisconnectReason> _clientExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Stream StandardInput { get; }

    private McpProcessLifetime(Process? parent, DateTime startedAtUtc = default, bool parentExited = false) {
        _parent = parent;
        _parentStartedAtUtc = startedAtUtc;
        StandardInput = new LifetimeInputStream(Console.OpenStandardInput(), _clientExited);
        if (parentExited) _clientExited.TrySetResult(DisconnectReason.ParentExited);
    }

    internal static McpProcessLifetime CaptureParent() {
        Process? parent = null;
        try {
            using var current = Process.GetCurrentProcess();
            var parentId = GetParentProcessId(current);
            if (parentId <= 0) return new(null);
            parent = Process.GetProcessById(parentId);
            // Open and retain the handle before reading identity or waiting. PID reuse must
            // never attach this lifetime to a later process with the same numeric ID.
            _ = parent.SafeHandle;
            var startedAtUtc = parent.StartTime.ToUniversalTime();
            if (parent.HasExited || startedAtUtc > current.StartTime.ToUniversalTime()) {
                parent.Dispose();
                return new(null, parentExited: true);
            }
            return new(parent, startedAtUtc);
        } catch (Exception error) when (error is ArgumentException or InvalidOperationException) {
            parent?.Dispose();
            return new(null, parentExited: true);
        } catch (Exception error) when (error is Win32Exception or PlatformNotSupportedException) {
            parent?.Dispose();
            Console.Error.WriteLine("Ledger MCP could not observe its parent process. Shutdown follows stdin EOF.");
            return new(null);
        }
    }

    internal async Task RunAsync(IHost host) {
        using var monitorStop = new CancellationTokenSource();
        // Host shutdown and async disposal can wait for outstanding MCP requests. Arm
        // the deadline before StopApplication, whose cancellation callbacks may block.
        // The deadline exits only this dedicated stdio process.
        using var deadline = new Timer(static _ => Environment.Exit(0), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var monitor = ObserveParentAsync(monitorStop.Token);
        try {
            var running = host.RunAsync();
            if (await Task.WhenAny(running, _clientExited.Task) == _clientExited.Task) {
                deadline.Change(ShutdownTimeout, Timeout.InfiniteTimeSpan);
                // EOF lets the SDK drain requests already read from stdin. Parent exit
                // also cancels active work because the original client has gone away.
                if (await _clientExited.Task == DisconnectReason.ParentExited)
                    host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
            }
            await running;
        } catch (OperationCanceledException) when (_clientExited.Task.IsCompleted) {
        } finally {
            monitorStop.Cancel();
            await monitor;
        }
    }

    private async Task ObserveParentAsync(CancellationToken token) {
        if (_parent is null) return;
        try {
            await _parent.WaitForExitAsync(token);
            _clientExited.TrySetResult(DisconnectReason.ParentExited);
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
        } catch (Exception error) when (error is Win32Exception or InvalidOperationException) {
            Console.Error.WriteLine($"Ledger MCP parent observation failed (PID {_parent.Id}, started {_parentStartedAtUtc:O}). Requesting shutdown.");
            _clientExited.TrySetResult(DisconnectReason.ParentExited);
        }
    }

    public void Dispose() {
        StandardInput.Dispose();
        _parent?.Dispose();
    }

    private enum DisconnectReason { EndOfInput, ParentExited }

    private sealed class LifetimeInputStream(Stream input, TaskCompletionSource<DisconnectReason> disconnected) : Stream {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) {
            var read = input.Read(buffer, offset, count);
            if (read == 0 && count != 0) disconnected.TrySetResult(DisconnectReason.EndOfInput);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            // Console stdin can ignore cancellation. Keep the SDK's cancellable-wait
            // behavior while observing EOF on the same reads that carry requests.
            var pending = input.ReadAsync(buffer, cancellationToken);
            var read = pending.IsCompletedSuccessfully ? pending.Result : await pending.AsTask().WaitAsync(cancellationToken);
            if (read == 0 && !buffer.IsEmpty) disconnected.TrySetResult(DisconnectReason.EndOfInput);
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing) {
            if (disposing) input.Dispose();
            base.Dispose(disposing);
        }
    }

    private static int GetParentProcessId(Process current) {
        if (!OperatingSystem.IsWindows()) return GetParentPid();
        var status = NtQueryInformationProcess(current.Handle, 0, out var information,
            Marshal.SizeOf<ProcessBasicInformation>(), out _);
        if (status != 0) throw new Win32Exception($"Parent process query failed with status {status:X8}.");
        return checked((int)information.InheritedFromUniqueProcessId);
    }

    [DllImport("libc", EntryPoint = "getppid", ExactSpelling = true)]
    private static extern int GetParentPid();

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtQueryInformationProcess(nint processHandle, int informationClass,
        out ProcessBasicInformation information, int informationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation {
        internal nint ExitStatus;
        internal nint PebBaseAddress;
        internal nint AffinityMask;
        internal nint BasePriority;
        internal nint UniqueProcessId;
        internal nint InheritedFromUniqueProcessId;
    }
}
