using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Launch;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Starts the local chat service sidecar for the native WinUI shell.
/// </summary>
internal sealed class NativeChatServiceProcessHost : IDisposable {
    private const int StartupExitProbeDelayMs = 100;
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public async Task<bool> EnsureRunningAsync(
        string pipeName,
        Func<string, Task> status,
        CancellationToken cancellationToken) {
        if (IsRunning) {
            return true;
        }

        var serviceDir = ResolveServiceDirectory();
        if (string.IsNullOrWhiteSpace(serviceDir)) {
            await status("Local chat service payload was not found.").ConfigureAwait(false);
            return false;
        }

        var exe = Path.Combine(serviceDir, "IntelligenceX.Chat.Service.exe");
        var dll = Path.Combine(serviceDir, "IntelligenceX.Chat.Service.dll");
        if (!File.Exists(exe) && !File.Exists(dll)) {
            await status("Local chat service executable was not found.").ConfigureAwait(false);
            return false;
        }

        await status("Starting local chat service...").ConfigureAwait(false);
        var detached = IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_DETACHED_SERVICE"));
        var pluginPaths = MainWindow.ResolveServiceLaunchPluginPaths(serviceDir);
        var probePaths = MainWindow.ResolveServiceLaunchBuiltInToolProbePaths(serviceDir);
        var args = ServiceLaunchArguments.Build(
            pipeName,
            detached,
            Environment.ProcessId,
            additionalPluginPaths: pluginPaths,
            additionalBuiltInToolProbePaths: probePaths,
            enableWorkspaceBuiltInToolOutputProbing: MainWindow.ShouldEnableWorkspaceBuiltInToolOutputProbing(probePaths));

        var hasExe = File.Exists(exe);
        var psi = new ProcessStartInfo {
            FileName = hasExe ? exe : "dotnet",
            WorkingDirectory = serviceDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!hasExe) {
            psi.ArgumentList.Add(dll);
        }

        foreach (var arg in args) {
            psi.ArgumentList.Add(arg);
        }

        var process = new Process {
            StartInfo = psi,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) => HandleServiceLine(e.Data, status);
        process.ErrorDataReceived += (_, e) => HandleServiceLine(e.Data, status);
        process.Exited += (_, _) => {
            ReportStatus(status, "Local chat service exited.");
        };

        try {
            if (!process.Start()) {
                await status("Local chat service did not start.").ConfigureAwait(false);
                process.Dispose();
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;

            await Task.Delay(StartupExitProbeDelayMs, cancellationToken).ConfigureAwait(false);
            if (process.HasExited) {
                await status(
                        "Local chat service exited during startup with code "
                        + process.ExitCode.ToString(CultureInfo.InvariantCulture)
                        + ".")
                    .ConfigureAwait(false);
                _process = null;
                process.Dispose();
                return false;
            }

            return true;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            await status("Local chat service start failed: " + ex.Message).ConfigureAwait(false);
            process.Dispose();
            return false;
        }
    }

    public void Dispose() {
        var process = _process;
        _process = null;
        if (process is null) {
            return;
        }

        try {
            if (!process.HasExited && !IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_DETACHED_SERVICE"))) {
                process.Kill(entireProcessTree: true);
            }
        } catch {
            // Best effort cleanup on app shutdown.
        } finally {
            process.Dispose();
        }
    }

    private static string? ResolveServiceDirectory() {
        var bestDir = string.Empty;
        var bestTicks = long.MinValue;

        TryPick(Path.Combine(AppContext.BaseDirectory, "service"), ref bestDir, ref bestTicks);
        TryPick(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service")), ref bestDir, ref bestTicks);

        return string.IsNullOrWhiteSpace(bestDir) ? null : bestDir;
    }

    private static void TryPick(string dir, ref string bestDir, ref long bestTicks) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return;
        }

        var exe = Path.Combine(dir, "IntelligenceX.Chat.Service.exe");
        var dll = Path.Combine(dir, "IntelligenceX.Chat.Service.dll");
        if (!File.Exists(exe) && !File.Exists(dll)) {
            return;
        }

        var marker = File.Exists(dll) ? dll : exe;
        var ticks = File.GetLastWriteTimeUtc(marker).Ticks;
        if (ticks > bestTicks) {
            bestTicks = ticks;
            bestDir = dir;
        }
    }

    private static void HandleServiceLine(string? line, Func<string, Task> status) {
        if (string.IsNullOrWhiteSpace(line)) {
            return;
        }

        if (MainWindow.TryBuildServiceBootstrapStatus(line, out var statusText)) {
            ReportStatus(status, statusText);
        }
    }

    private static void ReportStatus(Func<string, Task> status, string statusText) {
        _ = ReportStatusAsync(status, statusText);
    }

    private static async Task ReportStatusAsync(Func<string, Task> status, string statusText) {
        try {
            await status(statusText).ConfigureAwait(false);
        } catch (Exception ex) {
            StartupLog.Write("Native chat service status callback failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool IsTruthy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = value.Trim();
        return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase);
    }
}
