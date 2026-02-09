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
        var maxBytes = ResolveMaxOutputBytes();
        // IMPORTANT: stdout/stderr draining must not be coupled to the timeout token; otherwise we can treat
        // large-but-valid output as a timeout even when the process exited successfully.
        var stdoutTask = ReadAllBytesAsync(proc.StandardOutput.BaseStream, maxBytes, CancellationToken.None);
        var stderrTask = ReadAllBytesAsync(proc.StandardError.BaseStream, maxBytes, CancellationToken.None);
        try {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            try {
                proc.Kill(entireProcessTree: true);
            } catch {
                // ignore
            }
            return (124, Array.Empty<byte>(), Array.Empty<byte>());
        } catch {
            try {
                proc.Kill(entireProcessTree: true);
            } catch {
                // ignore
            }
            return (125, Array.Empty<byte>(), Array.Empty<byte>());
        } finally {
            try {
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
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
        } catch (Exception ex) {
            return (127, string.Empty, ex.Message);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        var effectiveTimeout = ResolveTimeout(timeout);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        try {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            // Ensure we drain stdout/stderr after process exit; otherwise we can return truncated output.
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            try {
                proc.Kill(entireProcessTree: true);
            } catch {
                // ignore
            }
            return (124, string.Empty, $"git command timed out after {effectiveTimeout.TotalSeconds:0}s.");
        } catch (Exception ex) {
            try {
                proc.Kill(entireProcessTree: true);
            } catch {
                // ignore
            }
            return (125, string.Empty, ex.Message);
        } finally {
            try {
                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            } catch {
                // ignore
            }
        }

        return (proc.ExitCode, SafeTaskResult(stdoutTask), SafeTaskResult(stderrTask));
    }

    private static string SafeTaskResult(Task<string> task) {
        try {
            return task.GetAwaiter().GetResult();
        } catch {
            return string.Empty;
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
