using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal static class GhCli {
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args) {
        var psi = new ProcessStartInfo {
            FileName = "gh",
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
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync().ConfigureAwait(false);
        return (proc.ExitCode, stdout.ToString(), stderr.ToString());
    }
}

