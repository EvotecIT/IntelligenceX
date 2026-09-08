using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IntelligenceX.Copilot;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotProcessStartupTests {
    [Fact]
    public async Task FailedConnectionAttemptsTerminateEveryStartedProcess() {
        string directory = Path.Combine(Path.GetTempPath(), "ix-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string log = Path.Combine(directory, "pids.txt");
        string script = Path.Combine(directory, OperatingSystem.IsWindows() ? "runner.ps1" : "runner.sh");
        string launcher = OperatingSystem.IsWindows() ? Path.Combine(directory, "runner.cmd") : script;
        using var reservedPort = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reservedPort.Bind(new IPEndPoint(IPAddress.Loopback, 0)); // Bound, deliberately not listening.
        var observed = new List<Process>();
        try {
            if (OperatingSystem.IsWindows()) {
                await File.WriteAllTextAsync(script, "[IO.File]::AppendAllText($env:IX_TEST_PID_LOG, [string]$PID + [Environment]::NewLine)\nWrite-Output ('listening on port ' + $env:IX_TEST_PORT)\nStart-Sleep -Seconds 30\n");
                await File.WriteAllTextAsync(launcher, "@echo off\r\npowershell.exe -NoProfile -NonInteractive -File \"%~dp0runner.ps1\"\r\n");
            } else {
                await File.WriteAllTextAsync(script, "#!/bin/sh\necho $$ >> \"$IX_TEST_PID_LOG\"\necho \"listening on port $IX_TEST_PORT\"\nsleep 30\n");
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var options = new CopilotClientOptions {
                CliPath = launcher, AutoInstallCli = false, UseStdio = false, WorkingDirectory = directory,
                ConnectRetryCount = 1, ConnectRetryInitialDelay = TimeSpan.Zero, ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            options.Environment["IX_TEST_PID_LOG"] = log;
            options.Environment["IX_TEST_PORT"] = ((IPEndPoint)reservedPort.LocalEndPoint!).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            Exception? failure = await Record.ExceptionAsync(() => CopilotClient.StartAsync(options, deadline.Token));
            // A bound non-listening port is refused on some hosts and times out on others.
            // Both paths must dispose both failed attempts before returning to the caller.
            Assert.True(failure is SocketException or OperationCanceledException, failure?.ToString());
            Assert.False(deadline.IsCancellationRequested, "The caller deadline expired before both connection attempts settled.");
            string[] ids = await File.ReadAllLinesAsync(log);
            Assert.Equal(2, ids.Length);
            foreach (string id in ids) {
                try { observed.Add(Process.GetProcessById(int.Parse(id, System.Globalization.CultureInfo.InvariantCulture))); }
                catch (ArgumentException) { continue; } // The process has already been reaped.
                Assert.True(observed[^1].WaitForExit(2000), "A failed connection attempt left its process running.");
            }
        } finally {
            foreach (var process in observed) {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); }
                process.Dispose();
            }
            File.Delete(log); File.Delete(script);
            if (launcher != script) File.Delete(launcher);
            Directory.Delete(directory);
        }
    }
}
