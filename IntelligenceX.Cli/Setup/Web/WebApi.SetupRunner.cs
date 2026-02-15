using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Host;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private static string[] BuildSetupArgs(SetupRequest request, bool dryRun, string repo) {
        var withConfig = request.WithConfig ||
                         !string.IsNullOrWhiteSpace(request.ConfigJson) ||
                         !string.IsNullOrWhiteSpace(request.ConfigPath);
        var hasConfigOverride = !string.IsNullOrWhiteSpace(request.ConfigJson) || !string.IsNullOrWhiteSpace(request.ConfigPath);
        var isSetup = !request.Cleanup && !request.UpdateSecret;
        var analysisApplies = isSetup && withConfig && !hasConfigOverride;
        var plan = new SetupPlan(repo) {
            GitHubToken = request.GitHubToken,
            GitHubClientId = request.GitHubClientId,
            WithConfig = withConfig,
            TriageBootstrap = request.TriageBootstrap,
            AuthB64 = request.AuthB64,
            AuthB64Path = request.AuthB64Path,
            ConfigJson = request.ConfigJson,
            ConfigPath = request.ConfigPath,
            Provider = request.Provider,
            OpenAIAccountId = request.OpenAIAccountId,
            OpenAIAccountIds = request.OpenAIAccountIds,
            OpenAIAccountRotation = request.OpenAIAccountRotation,
            OpenAIAccountFailover = request.OpenAIAccountFailover,
            ReviewProfile = request.ReviewProfile,
            ReviewMode = request.ReviewMode,
            ReviewCommentMode = request.ReviewCommentMode,
            AnalysisEnabled = analysisApplies ? request.AnalysisEnabled : null,
            AnalysisGateEnabled = analysisApplies && request.AnalysisEnabled == true ? request.AnalysisGateEnabled : null,
            AnalysisRunStrict = analysisApplies && request.AnalysisEnabled == true ? request.AnalysisRunStrict : null,
            AnalysisPacks = analysisApplies && request.AnalysisEnabled == true ? request.AnalysisPacks : null,
            AnalysisExportPath = analysisApplies && request.AnalysisEnabled == true ? request.AnalysisExportPath : null,
            SkipSecret = request.SkipSecret,
            ManualSecret = request.ManualSecret && !request.UpdateSecret,
            ExplicitSecrets = request.ExplicitSecrets,
            Upgrade = request.Upgrade,
            Force = request.Force,
            UpdateSecret = request.UpdateSecret,
            Cleanup = request.Cleanup,
            KeepSecret = request.KeepSecret,
            DryRun = dryRun || request.DryRun,
            BranchName = request.BranchName
        };

        return SetupArgsBuilder.FromPlan(plan);
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
