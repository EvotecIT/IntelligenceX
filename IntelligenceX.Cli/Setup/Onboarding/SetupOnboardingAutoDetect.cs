using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli;
using IntelligenceX.Cli.Doctor;

namespace IntelligenceX.Cli.Setup.Onboarding;

internal enum SetupOnboardingCheckStatus {
    Ok,
    Warn,
    Fail
}

internal sealed class SetupOnboardingCheck {
    public string Name { get; set; } = string.Empty;
    public SetupOnboardingCheckStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal sealed class SetupOnboardingAutoDetectResult {
    public string Status { get; set; } = "ok";
    public string Workspace { get; set; } = string.Empty;
    public string? Repo { get; set; }
    public bool LocalWorkflowExists { get; set; }
    public bool LocalConfigExists { get; set; }
    public string RecommendedPath { get; set; } = SetupOnboardingPaths.NewSetup;
    public string RecommendedReason { get; set; } = string.Empty;
    public IReadOnlyList<SetupOnboardingCheck> Checks { get; set; } = Array.Empty<SetupOnboardingCheck>();
    public string RawDoctorOutput { get; set; } = string.Empty;
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
            RecommendedPath = recommendedPath,
            RecommendedReason = reason,
            Checks = checks,
            RawDoctorOutput = output
        };
    }

    private static async Task<(int ExitCode, string Output)> RunDoctorWithCaptureAsync(string workspace, string? repo) {
        var args = new List<string> {
            "--workspace", workspace
        };
        if (!string.IsNullOrWhiteSpace(repo)) {
            args.Add("--repo");
            args.Add(repo);
        }

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = await DoctorRunner.RunAsync(args.ToArray()).ConfigureAwait(false);
            var mergedOutput = stdout.ToString() + Environment.NewLine + stderr.ToString();
            return (exitCode, mergedOutput.Trim());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
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
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = Parse(args);
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
                    if (i + 1 < args.Length) {
                        options.Workspace = args[++i];
                    }
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--json":
                    options.Json = true;
                    break;
                default:
                    options.ShowHelp = true;
                    break;
            }
        }
        return options;
    }

    private static JsonSerializerOptions CreateJsonOptions() {
        var options = new JsonSerializerOptions {
            WriteIndented = true
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
