using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class GitCli {
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private const int DefaultMaxOutputBytes = 25 * 1024 * 1024;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan KillReapTimeout = TimeSpan.FromSeconds(5);

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string? workingDirectory, params string[] args) {
        return RunAsync(workingDirectory, timeout: null, args);
    }

    public static Task<(int ExitCode, byte[] StdOut, byte[] StdErr)> RunBytesAsync(string? workingDirectory, params string[] args) {
        return RunBytesAsync(workingDirectory, timeout: null, args);
    }

    public static async Task<(int ExitCode, byte[] StdOut, byte[] StdErr)> RunBytesAsync(string? workingDirectory, TimeSpan? timeout, params string[] args) {
        var psi = new ProcessStartInfo {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) {
            psi.WorkingDirectory = workingDirectory;
        }
        foreach (var a in args) {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };
        try {
            proc.Start();
        } catch {
            return (127, Array.Empty<byte>(), Array.Empty<byte>());
        }

        var effectiveTimeout = ResolveTimeout(timeout);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        using var drainCts = new CancellationTokenSource();
        var maxBytes = ResolveMaxOutputBytes();
        // IMPORTANT: stdout/stderr draining must not be coupled to the timeout token; otherwise we can treat
        // large-but-valid output as a timeout even when the process exited successfully.
        var stdoutTask = ReadAllBytesAsync(proc.StandardOutput.BaseStream, maxBytes, drainCts.Token);
        var stderrTask = ReadAllBytesAsync(proc.StandardError.BaseStream, maxBytes, drainCts.Token);
        try {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            TryKill(proc);
            await TryReapAsync(proc).ConfigureAwait(false);
            // Ensure stderr/stdout drains can't hang the caller after a timeout/kill.
            drainCts.Cancel();
            return (124, Array.Empty<byte>(), Array.Empty<byte>());
        } catch {
            TryKill(proc);
            await TryReapAsync(proc).ConfigureAwait(false);
            drainCts.Cancel();
            return (125, Array.Empty<byte>(), Array.Empty<byte>());
        } finally {
            try {
                if (drainCts.IsCancellationRequested) {
                    // Best-effort: don't allow a broken stream to hang the caller after kill/cancel.
                    await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                } else {
                    await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                }
            } catch {
                // ignore
            }
        }

        var (stdout, stdoutTruncated) = SafeTaskResult(stdoutTask);
        var (stderr, stderrTruncated) = SafeTaskResult(stderrTask);
        if (stdoutTruncated || stderrTruncated) {
            return (126, Array.Empty<byte>(), Utf8NoBom.GetBytes($"git output exceeded {maxBytes} bytes and was truncated."));
        }

        return (proc.ExitCode, stdout, stderr);
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string? workingDirectory, TimeSpan? timeout, params string[] args) {
        // Reuse the bounded-output implementation to avoid pipe deadlocks/hangs.
        var result = await RunBytesAsync(workingDirectory, timeout, args).ConfigureAwait(false);
        return (result.ExitCode, DecodeUtf8(result.StdOut), DecodeUtf8(result.StdErr));
    }

    private static void TryKill(Process proc) {
        try {
            if (!proc.HasExited) {
                proc.Kill(entireProcessTree: true);
            }
        } catch {
            // ignore
        }
    }

    private static async Task TryReapAsync(Process proc) {
        try {
            // After Kill, ensure the process is reaped so it can't keep pipes open and hang drains.
            // This is bounded to avoid deadlocking the caller if the process can't be terminated.
            await proc.WaitForExitAsync().WaitAsync(KillReapTimeout).ConfigureAwait(false);
        } catch {
            // ignore
        }
    }

    private static string DecodeUtf8(byte[] bytes) {
        if (bytes.Length == 0) {
            return string.Empty;
        }
        try {
            return Utf8NoBom.GetString(bytes);
        } catch {
            // Defensive: GetString shouldn't throw for UTF-8, but avoid bubbling errors from diagnostics paths.
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static (byte[] Data, bool Truncated) SafeTaskResult(Task<(byte[] Data, bool Truncated)> task) {
        try {
            return task.GetAwaiter().GetResult();
        } catch {
            return (Array.Empty<byte>(), false);
        }
    }

    private static async Task<(byte[] Data, bool Truncated)> ReadAllBytesAsync(Stream stream, int maxBytes, CancellationToken cancellationToken) {
        // Stream.ReadAllBytesAsync isn't available on all TFMs we target.
        var truncated = false;
        using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true) {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read <= 0) {
                break;
            }
            if (!truncated) {
                var take = Math.Min(read, Math.Max(0, maxBytes - total));
                if (take > 0) {
                    ms.Write(buffer, 0, take);
                    total += take;
                }
                if (total >= maxBytes) {
                    truncated = true;
                }
            }
            // If truncated, keep draining without buffering to avoid deadlocks from full pipes.
        }
        return (ms.ToArray(), truncated);
    }

    private static TimeSpan ResolveTimeout(TimeSpan? timeout) {
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero) {
            return timeout.Value;
        }
        var fromEnv = Environment.GetEnvironmentVariable("INTELLIGENCEX_GIT_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(fromEnv) && int.TryParse(fromEnv, out var seconds) && seconds > 0) {
            return TimeSpan.FromSeconds(seconds);
        }
        return DefaultTimeout;
    }

    private static int ResolveMaxOutputBytes() {
        var fromEnv = Environment.GetEnvironmentVariable("INTELLIGENCEX_GIT_MAX_OUTPUT_BYTES");
        if (!string.IsNullOrWhiteSpace(fromEnv) && int.TryParse(fromEnv, out var bytes) && bytes > 0) {
            return bytes;
        }
        return DefaultMaxOutputBytes;
    }
}
