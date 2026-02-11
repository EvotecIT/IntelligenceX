using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private static string[] BuildSetupArgs(SetupRequest request, bool dryRun, string repo) {
        var args = new List<string> {
            "--repo", repo
        };
        if (!string.IsNullOrWhiteSpace(request.GitHubToken)) {
            args.Add("--github-token");
            args.Add(request.GitHubToken!);
        } else if (!string.IsNullOrWhiteSpace(request.GitHubClientId)) {
            args.Add("--github-client-id");
            args.Add(request.GitHubClientId!);
        }
        var withConfig = request.WithConfig ||
                         !string.IsNullOrWhiteSpace(request.ConfigJson) ||
                         !string.IsNullOrWhiteSpace(request.ConfigPath);
        var hasConfigOverride = !string.IsNullOrWhiteSpace(request.ConfigJson) || !string.IsNullOrWhiteSpace(request.ConfigPath);
        var isSetup = !request.Cleanup && !request.UpdateSecret;
        var analysisApplies = isSetup && withConfig && !hasConfigOverride;
        if (withConfig) {
            args.Add("--with-config");
        }
        if (!string.IsNullOrWhiteSpace(request.AuthB64)) {
            args.Add("--auth-b64");
            args.Add(request.AuthB64!);
        }
        if (!string.IsNullOrWhiteSpace(request.AuthB64Path)) {
            args.Add("--auth-b64-path");
            args.Add(request.AuthB64Path!);
        }
        if (!string.IsNullOrWhiteSpace(request.ConfigJson)) {
            args.Add("--config-json");
            args.Add(request.ConfigJson!);
        }
        if (!string.IsNullOrWhiteSpace(request.ConfigPath)) {
            args.Add("--config-path");
            args.Add(request.ConfigPath!);
        }
        if (!string.IsNullOrWhiteSpace(request.Provider)) {
            args.Add("--provider");
            args.Add(request.Provider!);
        }
        if (!string.IsNullOrWhiteSpace(request.ReviewProfile)) {
            args.Add("--review-profile");
            args.Add(request.ReviewProfile!);
        }
        if (!string.IsNullOrWhiteSpace(request.ReviewMode)) {
            args.Add("--review-mode");
            args.Add(request.ReviewMode!);
        }
        if (!string.IsNullOrWhiteSpace(request.ReviewCommentMode)) {
            args.Add("--review-comment-mode");
            args.Add(request.ReviewCommentMode!);
        }
        if (analysisApplies && request.AnalysisEnabled.HasValue) {
            args.Add("--analysis-enabled");
            args.Add(request.AnalysisEnabled.Value ? "true" : "false");
        }
        if (analysisApplies && request.AnalysisEnabled == true && request.AnalysisGateEnabled.HasValue) {
            args.Add("--analysis-gate");
            args.Add(request.AnalysisGateEnabled.Value ? "true" : "false");
        }
        if (analysisApplies && request.AnalysisEnabled == true && !string.IsNullOrWhiteSpace(request.AnalysisPacks)) {
            args.Add("--analysis-packs");
            args.Add(request.AnalysisPacks!);
        }
        if (analysisApplies && request.AnalysisEnabled == true && !string.IsNullOrWhiteSpace(request.AnalysisExportPath)) {
            args.Add("--analysis-export-path");
            args.Add(request.AnalysisExportPath!);
        }
        if (request.SkipSecret) {
            args.Add("--skip-secret");
        }
        if (request.ManualSecret && !request.UpdateSecret) {
            args.Add("--manual-secret");
        }
        if (request.ExplicitSecrets) {
            args.Add("--explicit-secrets");
        }
        if (request.Upgrade) {
            args.Add("--upgrade");
        }
        if (request.Force) {
            args.Add("--force");
        }
        if (request.UpdateSecret) {
            args.Add("--update-secret");
        }
        if (request.Cleanup) {
            args.Add("--cleanup");
        }
        if (request.KeepSecret) {
            args.Add("--keep-secret");
        }
        if (dryRun || request.DryRun) {
            args.Add("--dry-run");
        }
        if (!string.IsNullOrWhiteSpace(request.BranchName)) {
            args.Add("--branch");
            args.Add(request.BranchName!);
        }
        return args.ToArray();
    }

    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);
    private static readonly TimeSpan ConsoleLockTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SetupProcessTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProcessShutdownGracePeriod = TimeSpan.FromSeconds(2);

    private async Task<SetupResponse> RunSetupAsync(string[] args) {
        if (!await ConsoleLock.WaitAsync(ConsoleLockTimeout).ConfigureAwait(false)) {
            return new SetupResponse {
                ExitCode = 1,
                Error = "Setup is busy. Please retry in a moment."
            };
        }
        try {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) {
                exePath = "dotnet";
            }

            var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssemblyPath)) {
                entryAssemblyPath = typeof(WebApi).Assembly.Location;
            }

            var psi = new ProcessStartInfo {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.CurrentDirectory
            };

            var isDotNetHost = Path.GetFileNameWithoutExtension(exePath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            if (isDotNetHost) {
                psi.ArgumentList.Add(entryAssemblyPath);
            }

            psi.ArgumentList.Add("setup");
            foreach (var arg in args) {
                psi.ArgumentList.Add(arg);
            }

            var result = await RunProcessWithTimeoutAsync(psi, SetupProcessTimeout).ConfigureAwait(false);
            if (!result.Started) {
                return new SetupResponse {
                    ExitCode = 1,
                    Error = result.Error
                };
            }

            return new SetupResponse {
                ExitCode = result.ExitCode,
                Output = result.Output,
                Error = result.Error
            };
        } finally {
            ConsoleLock.Release();
        }
    }

    internal static async Task<(int ExitCode, string StdOut, string StdErr, bool TimedOut)> RunSetupProcessForTests(
        string fileName,
        IReadOnlyList<string> args,
        int timeoutMs) {
        var psi = new ProcessStartInfo {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        foreach (var arg in args ?? Array.Empty<string>()) {
            psi.ArgumentList.Add(arg);
        }

        var timeout = timeoutMs <= 0 ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(timeoutMs);
        var result = await RunProcessWithTimeoutAsync(psi, timeout).ConfigureAwait(false);
        return (result.ExitCode, result.Output, result.Error, result.TimedOut);
    }

    private static async Task<SetupProcessResult> RunProcessWithTimeoutAsync(ProcessStartInfo psi, TimeSpan timeout) {
        Process? process;
        try {
            process = Process.Start(psi);
        } catch (Exception ex) {
            return new SetupProcessResult(false, 1, string.Empty,
                $"Failed to start setup process: {ex.Message}", false);
        }
        if (process is null) {
            return new SetupProcessResult(false, 1, string.Empty, "Failed to start setup process.", false);
        }

        using (process) {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var completionTask = Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
            var timedOut = false;
            try {
                await completionTask.WaitAsync(timeout).ConfigureAwait(false);
            } catch (TimeoutException) {
                timedOut = true;
                TryKillProcess(process);
                try {
                    await Task.WhenAny(completionTask, Task.Delay(ProcessShutdownGracePeriod)).ConfigureAwait(false);
                } catch {
                    // Best effort drain after timeout.
                }
            } catch (Exception ex) {
                var output = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
                var error = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
                var message = string.IsNullOrWhiteSpace(error)
                    ? ex.Message
                    : error.TrimEnd() + Environment.NewLine + ex.Message;
                return new SetupProcessResult(true, 1, output, message, false);
            }

            var finalOutput = outputTask.IsCompletedSuccessfully ? outputTask.Result : string.Empty;
            var finalError = errorTask.IsCompletedSuccessfully ? errorTask.Result : string.Empty;
            if (timedOut) {
                var timeoutMessage =
                    $"Setup process timed out after {Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds))}s.";
                finalError = string.IsNullOrWhiteSpace(finalError)
                    ? timeoutMessage
                    : finalError.TrimEnd() + Environment.NewLine + timeoutMessage;
                return new SetupProcessResult(true, 124, finalOutput, finalError, true);
            }

            return new SetupProcessResult(true, process.ExitCode, finalOutput, finalError, false);
        }
    }

    private static void TryKillProcess(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
        } catch {
            // Best effort cleanup on timeout.
        }
    }

    private sealed record SetupProcessResult(bool Started, int ExitCode, string Output, string Error, bool TimedOut);
}
