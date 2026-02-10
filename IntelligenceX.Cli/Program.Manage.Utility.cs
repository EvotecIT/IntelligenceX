using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Auth;
using IntelligenceX.OpenAI.Auth;
using Spectre.Console;

namespace IntelligenceX.Cli;

internal static partial class Program {
    private static List<(string Repo, DateTimeOffset? PushedAt, bool IsPrivate)> ParseRepoList(string json) {
        var list = new List<(string Repo, DateTimeOffset? PushedAt, bool IsPrivate)>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return list;
        }

        foreach (var item in doc.RootElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }

            if (!item.TryGetProperty("nameWithOwner", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) {
                continue;
            }
            var repo = nameProp.GetString();
            if (string.IsNullOrWhiteSpace(repo)) {
                continue;
            }

            DateTimeOffset? pushed = null;
            if (item.TryGetProperty("pushedAt", out var pushedProp) && pushedProp.ValueKind == JsonValueKind.String) {
                var raw = pushedProp.GetString();
                if (DateTimeOffset.TryParse(raw, out var parsed)) {
                    pushed = parsed;
                }
            }

            var isPrivate = item.TryGetProperty("isPrivate", out var privateProp) &&
                            privateProp.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                            privateProp.GetBoolean();

            list.Add((repo, pushed, isPrivate));
        }
        return list;
    }

    private static string? TryGetOwner(string? repo) {
        if (string.IsNullOrWhiteSpace(repo)) {
            return null;
        }
        var normalized = NormalizeRepo(repo);
        var idx = normalized.IndexOf('/');
        if (idx <= 0) {
            return null;
        }
        return normalized.Substring(0, idx);
    }

    private static void ShowCheatSheet() {
        AnsiConsole.Clear();
        RenderTitle($"{Icon("tip")} Command Cheat Sheet");
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Goal")
            .AddColumn("Command");
        table.AddRow("Open management hub", "intelligencex");
        table.AddRow("Explicit hub command", "intelligencex manage");
        table.AddRow("Main shortcuts", "1=QuickFix 2=HealthPipe 3=PRPipe 4=GHMonitor 5=Diagnostics 0=Exit");
        table.AddRow("Guided pipelines", "Hub -> Pipelines -> Daily health check / PR readiness");
        table.AddRow("Quick OpenAI reauth + secret sync", "intelligencex auth login --set-github-secret --repo owner/name");
        table.AddRow("Refresh secret from local auth", "intelligencex setup --update-secret --repo owner/name");
        table.AddRow("Run doctor checks", "intelligencex doctor --repo owner/name");
        table.AddRow("Run reviewer locally", "intelligencex reviewer run");
        table.AddRow("Resolve bot threads (dry-run)", "intelligencex reviewer resolve-threads --repo owner/name --pr 123 --dry-run");
        table.AddRow("List PRs", "gh pr list --repo owner/name --state open");
        table.AddRow("PR checks", "gh pr checks --repo owner/name <pr-number>");
        table.AddRow("Reviewer runs", "gh run list --repo owner/name --workflow review-intelligencex.yml");
        AnsiConsole.Write(table);
    }

    private static string CompactValue(string value, int maxLength) {
        if (string.IsNullOrEmpty(value) || maxLength < 8 || value.Length <= maxLength) {
            return value;
        }
        var head = Math.Max(3, (maxLength - 1) / 2);
        var tail = Math.Max(3, maxLength - head - 1);
        if (head + tail + 1 > value.Length) {
            return value;
        }
        return $"{value.Substring(0, head)}…{value.Substring(value.Length - tail)}";
    }

    private static string FormatDuration(TimeSpan duration) {
        if (duration.TotalSeconds < 1) {
            return $"{duration.TotalMilliseconds:0}ms";
        }
        if (duration.TotalMinutes < 1) {
            return $"{duration.TotalSeconds:0.0}s";
        }
        if (duration.TotalHours < 1) {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
        return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
    }

    private static string? ResolveDefaultRepo() {
        var envRepo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO")
                      ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(envRepo) && TryParseRepo(envRepo, out _, out _)) {
            return envRepo;
        }
        var detected = GitHubRepoDetector.TryDetectRepo(Environment.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(detected) && TryParseRepo(detected, out _, out _)) {
            return detected;
        }
        return null;
    }

    private static ManagePreferences LoadPreferences() {
        try {
            var path = GetPreferencesPath();
            if (!File.Exists(path)) {
                return new ManagePreferences();
            }
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) {
                return new ManagePreferences();
            }
            var prefs = JsonSerializer.Deserialize<ManagePreferences>(json);
            return prefs ?? new ManagePreferences();
        } catch {
            return new ManagePreferences();
        }
    }

    private static void SavePreferences(ManageState state) {
        try {
            var prefs = new ManagePreferences {
                ActiveRepo = state.ActiveRepo,
                LastOwner = state.LastOwner ?? TryGetOwner(state.ActiveRepo)
            };
            var path = GetPreferencesPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        } catch {
            // Best effort; ignore persistence failures.
        }
    }

    private static string GetPreferencesPath() {
        var authPath = AuthPaths.ResolveAuthPath();
        var authDir = Path.GetDirectoryName(authPath);
        if (string.IsNullOrWhiteSpace(authDir)) {
            authDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".intelligencex");
        }
        return Path.Combine(authDir, "manage-hub.json");
    }

    private static string NormalizeRepo(string value) {
        var repo = value.Trim();
        while (repo.EndsWith("/") || repo.EndsWith("\\")) {
            repo = repo.Substring(0, repo.Length - 1);
        }
        return repo;
    }

    private static List<OpenAiBundleStatus> ReadOpenAiBundles() {
        var path = AuthPaths.ResolveAuthPath();
        if (!File.Exists(path)) {
            return new List<OpenAiBundleStatus>();
        }

        try {
            var raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw)) {
                return new List<OpenAiBundleStatus>();
            }

            var json = AuthStoreUtils.DecryptAuthStoreIfNeeded(raw);
            return AuthStoreUtils.ParseAuthStoreEntries(json)
                .Where(e => string.Equals(e.Provider, "openai-codex", StringComparison.OrdinalIgnoreCase))
                .Select(e => new OpenAiBundleStatus(e.Provider, e.AccountId, e.ExpiresAt))
                .ToList();
        } catch {
            return new List<OpenAiBundleStatus>();
        }
    }

    private static (bool Installed, bool Authenticated) GetGitHubCliStatus() {
        var token = TryReadGhToken();
        if (!string.IsNullOrWhiteSpace(token)) {
            return (true, true);
        }

        var result = RunExternalCommand("gh", "auth status");
        if (result.ExitCode == int.MinValue) {
            return (false, false);
        }
        return (true, false);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunExternalCommand(string fileName, string arguments) {
        return RunExternalCommand(fileName, arguments, TimeSpan.FromSeconds(30));
    }

    internal static (int ExitCode, string StdOut, string StdErr) RunExternalCommandForTests(
        string fileName,
        string arguments,
        int timeoutMs) {
        return RunExternalCommand(fileName, arguments, TimeSpan.FromMilliseconds(timeoutMs));
    }

    private static (int ExitCode, string StdOut, string StdErr) RunExternalCommand(
        string fileName,
        string arguments,
        TimeSpan timeout) {
        try {
            return RunExternalCommandAsync(fileName, arguments, timeout).GetAwaiter().GetResult();
        } catch (Exception ex) {
            return (int.MinValue, string.Empty, ex.Message);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunExternalCommandAsync(
        string fileName,
        string arguments,
        TimeSpan timeout) {
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        if (process is null) {
            return (int.MinValue, string.Empty, "Failed to start process.");
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        var completionTask = WaitForProcessAndStreamDrainAsync(process, stdOutTask, stdErrTask);
        var timeoutTask = Task.Delay(timeout);

        var completed = await Task.WhenAny(completionTask, timeoutTask).ConfigureAwait(false);
        if (completed != completionTask) {
            try {
                process.Kill(entireProcessTree: true);
            } catch {
                // ignore kill failures
            }
            try {
                await WaitForProcessAndStreamDrainAsync(process, stdOutTask, stdErrTask)
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            } catch (TimeoutException) {
                // best effort: process may already be terminating
            } catch (InvalidOperationException) {
                // process was not started or already exited
            }

            var drainTask = Task.WhenAll(stdOutTask, stdErrTask);
            var drainCompleted = await Task.WhenAny(drainTask, Task.Delay(1000)).ConfigureAwait(false);
            var stdOut = stdOutTask.IsCompletedSuccessfully ? stdOutTask.Result : string.Empty;
            var stdErr = stdErrTask.IsCompletedSuccessfully ? stdErrTask.Result : string.Empty;
            if (drainCompleted == drainTask) {
                await drainTask.ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(stdErr)) {
                stdErr = $"Command timed out after {Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds))}s.";
            }
            return (124, stdOut, stdErr);
        }

        await completionTask.ConfigureAwait(false);
        return (process.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }

    private static async Task WaitForProcessAndStreamDrainAsync(Process process, Task<string> stdOutTask, Task<string> stdErrTask) {
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
    }

    private static void RenderTitle(string title) {
        AnsiConsole.Write(new Rule(title).LeftJustified().RuleStyle("grey"));
    }

    private static void PauseForMenu() {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
        Console.ReadKey(intercept: true);
    }

    private static string Icon(string name) {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        return name switch {
            "ok" => unicode ? "✓" : "[OK]",
            "fail" => unicode ? "✗" : "[X]",
            "lock" => unicode ? "🔐" : "[AUTH]",
            "box" => unicode ? "📦" : "[BUNDLE]",
            "stethoscope" => unicode ? "🩺" : "[DOCTOR]",
            "robot" => unicode ? "🤖" : "[REVIEW]",
            "compass" => unicode ? "🧭" : "[SETUP]",
            "globe" => unicode ? "🌐" : "[WEB]",
            "repo" => unicode ? "📁" : "[REPO]",
            "refresh" => unicode ? "♻️" : "[REFRESH]",
            "bolt" => unicode ? "🛠" : "[FIX]",
            "process" => unicode ? "⚙️" : "[PROCESS]",
            "gh" => unicode ? "🐙" : "[GH]",
            "arrow" => unicode ? "→" : "->",
            "tip" => unicode ? "💡" : "[TIP]",
            "back" => unicode ? "↩" : "[BACK]",
            "exit" => unicode ? "❌" : "[EXIT]",
            "tool" => unicode ? "🛠" : "[TOOL]",
            "check" => unicode ? "✅" : "[CHECK]",
            "pipeline" => unicode ? "🧪" : "[PIPE]",
            "star" => unicode ? "⭐" : "[FAV]",
            _ => "*"
        };
    }

    private static void PrintManageHelp() {
        Console.WriteLine("IntelligenceX management hub");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex manage");
        Console.WriteLine("  intelligencex manage --help");
        Console.WriteLine();
        Console.WriteLine("No arguments in interactive terminals also open this hub:");
        Console.WriteLine("  intelligencex");
    }
}
