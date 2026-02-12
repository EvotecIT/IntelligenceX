using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli;
using IntelligenceX.Cli.Doctor;
using IntelligenceX.Setup.Onboarding;

namespace IntelligenceX.Cli.Setup.Onboarding;

internal enum SetupOnboardingCheckStatus {
    Ok,
    Warn,
    Fail
}

internal sealed class SetupOnboardingCheck {
    public string Name { get; init; } = string.Empty;
    public SetupOnboardingCheckStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal sealed class SetupOnboardingAutoDetectResult {
    public string Status { get; init; } = "ok";
    public string Workspace { get; init; } = string.Empty;
    public string? Repo { get; init; }
    public bool LocalWorkflowExists { get; init; }
    public bool LocalConfigExists { get; init; }
    public string ContractVersion { get; init; } = SetupOnboardingContract.ContractVersion;
    public string ContractFingerprint { get; init; } = SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true);
    public SetupOnboardingCommandTemplates CommandTemplates { get; init; } = SetupOnboardingContract.GetCommandTemplates();
    public string RecommendedPath { get; init; } = SetupOnboardingPaths.NewSetup;
    public string RecommendedReason { get; init; } = string.Empty;
    public IReadOnlyList<SetupOnboardingPathContract> Paths { get; init; } = SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
    public IReadOnlyList<SetupOnboardingCheck> Checks { get; init; } = Array.Empty<SetupOnboardingCheck>();
    public string RawDoctorOutput { get; init; } = string.Empty;
}

internal static class SetupOnboardingAutoDetectRunner {
    private static readonly Regex CheckRegex = new(@"^\[(OK|WARN|FAIL)\]\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<SetupOnboardingAutoDetectResult> RunAsync(string workspace, string? repoHint = null) {
        var resolvedWorkspace = string.IsNullOrWhiteSpace(workspace)
            ? Environment.CurrentDirectory
            : workspace;

        resolvedWorkspace = Path.GetFullPath(resolvedWorkspace);
        var repo = !string.IsNullOrWhiteSpace(repoHint)
            ? repoHint.Trim()
            : GitHubRepoDetector.TryDetectRepo(resolvedWorkspace);

        var workflowPath = Path.Combine(resolvedWorkspace, ".github", "workflows", "review-intelligencex.yml");
        var configPath = Path.Combine(resolvedWorkspace, ".intelligencex", "reviewer.json");
        var legacyConfigPath = Path.Combine(resolvedWorkspace, ".intelligencex", "config.json");

        var localWorkflowExists = File.Exists(workflowPath);
        var localConfigExists = File.Exists(configPath) || File.Exists(legacyConfigPath);

        var (exitCode, output) = await RunDoctorWithCaptureAsync(resolvedWorkspace, repo).ConfigureAwait(false);
        var checks = ParseChecks(output, exitCode);
        var status = ResolveStatus(checks, exitCode);
        var (recommendedPath, reason) = RecommendPath(localWorkflowExists, localConfigExists, checks);

        return new SetupOnboardingAutoDetectResult {
            Status = status,
            Workspace = resolvedWorkspace,
            Repo = repo,
            LocalWorkflowExists = localWorkflowExists,
            LocalConfigExists = localConfigExists,
            ContractVersion = SetupOnboardingContract.ContractVersion,
            ContractFingerprint = SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            CommandTemplates = SetupOnboardingContract.GetCommandTemplates(),
            RecommendedPath = recommendedPath,
            RecommendedReason = reason,
            Paths = SetupOnboardingContract.GetPaths(includeMaintenancePath: true),
            Checks = checks,
            RawDoctorOutput = output
        };
    }

    private static async Task<(int ExitCode, string Output)> RunDoctorWithCaptureAsync(string workspace, string? repo) {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) {
            executablePath = "dotnet";
        }

        var cliAssemblyPath = ResolveCliAssemblyPath();

        var processStartInfo = new ProcessStartInfo {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workspace
        };

        var isDotNetHost = Path.GetFileNameWithoutExtension(executablePath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        if (isDotNetHost) {
            if (string.IsNullOrWhiteSpace(cliAssemblyPath)) {
                return (1, "Failed to resolve IntelligenceX.Cli assembly path for doctor process.");
            }
            processStartInfo.ArgumentList.Add(cliAssemblyPath);
        }

        processStartInfo.ArgumentList.Add("doctor");
        processStartInfo.ArgumentList.Add("--workspace");
        processStartInfo.ArgumentList.Add(workspace);
        if (!string.IsNullOrWhiteSpace(repo)) {
            processStartInfo.ArgumentList.Add("--repo");
            processStartInfo.ArgumentList.Add(repo);
        }

        try {
            using var process = Process.Start(processStartInfo);
            if (process is null) {
                return (1, "Failed to start doctor process.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                timedOut = true;
                TryKillProcess(process);
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            var mergedOutput = stdoutTask.Result + Environment.NewLine + stderrTask.Result;
            if (timedOut) {
                mergedOutput = mergedOutput + Environment.NewLine + "Doctor process timed out after 120 seconds.";
                return (1, mergedOutput.Trim());
            }

            var exitCode = process.ExitCode;
            return (exitCode, mergedOutput.Trim());
        } catch (Exception ex) {
            return (1, $"Doctor process failed to run: {ex.Message}");
        }
    }

    private static string ResolveCliAssemblyPath() {
        var doctorAssemblyPath = typeof(DoctorRunner).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(doctorAssemblyPath) && File.Exists(doctorAssemblyPath)) {
            return doctorAssemblyPath;
        }

        var executingAssemblyPath = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(executingAssemblyPath) && File.Exists(executingAssemblyPath)) {
            return executingAssemblyPath;
        }

        var defaultCandidate = Path.Combine(AppContext.BaseDirectory, "IntelligenceX.Cli.dll");
        if (File.Exists(defaultCandidate)) {
            return defaultCandidate;
        }

        return string.Empty;
    }

    private static void TryKillProcess(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill(entireProcessTree: true);
            }
        } catch {
            // Best-effort cleanup on timeout.
        }
    }

    private static IReadOnlyList<SetupOnboardingCheck> ParseChecks(string output, int exitCode) {
        var checks = new List<SetupOnboardingCheck>();
        if (!string.IsNullOrWhiteSpace(output)) {
            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines) {
                var match = CheckRegex.Match(line.Trim());
                if (!match.Success) {
                    continue;
                }

                var rawStatus = match.Groups[1].Value;
                var status = rawStatus switch {
                    "FAIL" => SetupOnboardingCheckStatus.Fail,
                    "WARN" => SetupOnboardingCheckStatus.Warn,
                    _ => SetupOnboardingCheckStatus.Ok
                };

                checks.Add(new SetupOnboardingCheck {
                    Name = "doctor",
                    Status = status,
                    Message = match.Groups[2].Value.Trim()
                });
            }
        }

        if (checks.Count == 0) {
            checks.Add(new SetupOnboardingCheck {
                Name = "doctor",
                Status = exitCode == 0 ? SetupOnboardingCheckStatus.Ok : SetupOnboardingCheckStatus.Fail,
                Message = exitCode == 0 ? "Doctor completed." : "Doctor reported failures."
            });
        }

        return checks;
    }

    private static string ResolveStatus(IReadOnlyList<SetupOnboardingCheck> checks, int exitCode) {
        if (exitCode != 0) {
            return "fail";
        }

        foreach (var check in checks) {
            if (check.Status == SetupOnboardingCheckStatus.Fail) {
                return "fail";
            }
        }
        foreach (var check in checks) {
            if (check.Status == SetupOnboardingCheckStatus.Warn) {
                return "warn";
            }
        }
        return "ok";
    }

    private static (string Path, string Reason) RecommendPath(
        bool localWorkflowExists,
        bool localConfigExists,
        IReadOnlyList<SetupOnboardingCheck> checks) {
        var hasAuthIssue = HasMatch(checks, "openai auth store") || HasMatch(checks, "no openai bundles") || HasMatch(checks, "expires soon");
        if (hasAuthIssue) {
            if (localWorkflowExists || localConfigExists) {
                return (SetupOnboardingPaths.RefreshAuth, "Detected existing setup and OpenAI auth issue/expiry.");
            }
            return (SetupOnboardingPaths.NewSetup, "OpenAI auth is missing and no existing setup was detected.");
        }

        if (localWorkflowExists || localConfigExists) {
            return (SetupOnboardingPaths.Maintenance, "Detected existing workflow/config in the current workspace.");
        }

        return (SetupOnboardingPaths.NewSetup, "No existing setup files detected in the current workspace.");
    }

    private static bool HasMatch(IReadOnlyList<SetupOnboardingCheck> checks, string needle) {
        foreach (var check in checks) {
            if (check.Message.Contains(needle, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }
}

internal static class SetupOnboardingAutoDetectCliRunner {
    private sealed class Options {
        public string Workspace { get; set; } = Environment.CurrentDirectory;
        public string? Repo { get; set; }
        public bool Json { get; set; }
        public bool ShowHelp { get; set; }
        public string? ParseError { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = Parse(args);
        if (!string.IsNullOrWhiteSpace(options.ParseError)) {
            Console.Error.WriteLine(options.ParseError);
            PrintHelp();
            return 1;
        }
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        var result = await SetupOnboardingAutoDetectRunner.RunAsync(options.Workspace, options.Repo).ConfigureAwait(false);
        if (options.Json) {
            var json = SerializeForTests(result);
            Console.WriteLine(json);
        } else {
            Console.WriteLine($"Status: {result.Status}");
            Console.WriteLine($"Workspace: {result.Workspace}");
            Console.WriteLine($"Repo: {result.Repo ?? "(not detected)"}");
            Console.WriteLine($"Local workflow: {(result.LocalWorkflowExists ? "yes" : "no")}");
            Console.WriteLine($"Local config: {(result.LocalConfigExists ? "yes" : "no")}");
            Console.WriteLine($"Recommended path: {result.RecommendedPath}");
            Console.WriteLine($"Reason: {result.RecommendedReason}");
            Console.WriteLine();
            Console.WriteLine("Checks:");
            foreach (var check in result.Checks) {
                Console.WriteLine($"- [{check.Status}] {check.Message}");
            }
        }

        return string.Equals(result.Status, "fail", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    internal static string SerializeForTests(SetupOnboardingAutoDetectResult result) {
        return JsonSerializer.Serialize(result, CreateJsonOptions());
    }

    private static Options Parse(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--workspace":
                    if (TryGetOptionValue(args, ref i, out var workspace)) {
                        options.Workspace = workspace;
                    } else {
                        options.ParseError = "Missing value for --workspace.";
                        return options;
                    }
                    break;
                case "--repo":
                    if (TryGetOptionValue(args, ref i, out var repo)) {
                        options.Repo = repo;
                    } else {
                        options.ParseError = "Missing value for --repo.";
                        return options;
                    }
                    break;
                case "--json":
                    options.Json = true;
                    break;
                default:
                    options.ParseError = $"Unknown option: {arg}";
                    return options;
            }
        }
        return options;
    }

    private static bool TryGetOptionValue(string[] args, ref int index, out string value) {
        value = string.Empty;
        if (index + 1 >= args.Length) {
            return false;
        }

        var candidate = args[index + 1];
        if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("-", StringComparison.Ordinal)) {
            return false;
        }

        index++;
        value = candidate;
        return true;
    }

    private static JsonSerializerOptions CreateJsonOptions() {
        var options = new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage: intelligencex setup autodetect [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --workspace <path>   Workspace root to inspect (default current directory)");
        Console.WriteLine("  --repo <owner/name>  Optional repo hint for GitHub doctor probes");
        Console.WriteLine("  --json               Emit JSON output");
        Console.WriteLine("  --help               Show this help");
    }
}
