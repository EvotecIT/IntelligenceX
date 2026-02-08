using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal static class GhCli {
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args) {
        return RunAsync(timeout: null, args);
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(TimeSpan? timeout, params string[] args) {
        var psi = new ProcessStartInfo {
            FileName = "gh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args) {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => {
            if (e.Data is not null) {
                stdout.AppendLine(e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) => {
            if (e.Data is not null) {
                stderr.AppendLine(e.Data);
            }
        };

        try {
            proc.Start();
        } catch (Exception ex) {
            return (127, string.Empty, ex.Message);
        }
        try {
            proc.StandardInput.Close();
        } catch {
            // ignore
        }
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var effectiveTimeout = ResolveTimeout(timeout);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        try {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            try {
                proc.Kill(entireProcessTree: true);
            } catch {
                // ignore
            }
            var message =
                $"gh command timed out after {effectiveTimeout.TotalSeconds:0}s. " +
                "If this is due to authentication, run `gh auth status` / `gh auth login`. " +
                "Otherwise check network connectivity and try again.";
            return (124, stdout.ToString(), (stderr.ToString() + "\n" + message).Trim());
        }
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static TimeSpan ResolveTimeout(TimeSpan? timeout) {
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero) {
            return timeout.Value;
        }
        var fromEnv = Environment.GetEnvironmentVariable("INTELLIGENCEX_GH_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(fromEnv) && int.TryParse(fromEnv, out var seconds) && seconds > 0) {
            return TimeSpan.FromSeconds(seconds);
        }
        return DefaultTimeout;
    }
}
