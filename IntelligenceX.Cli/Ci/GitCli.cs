using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class GitCli {
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string? workingDirectory, params string[] args) {
        return RunAsync(workingDirectory, timeout: null, args);
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
}
