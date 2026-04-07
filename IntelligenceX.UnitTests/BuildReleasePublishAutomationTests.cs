using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class BuildReleasePublishAutomationTests {
    [Fact]
    public void PublishReleaseOutputs_SyncsWinget_AndInvokesGitHubAndNuGet() {
        var repoRoot = FindRepoRoot();
        using var harness = PublishHarness.Create();

        RunPublishReleaseOutputs(
            repoRoot,
            harness,
            "-RepoRoot", repoRoot,
            "-ConfigPath", harness.ConfigPath,
            "-StageRoot", harness.StageRoot,
            "-SyncWinget",
            "-PublishGitHub",
            "-PublishNuget",
            "-GitHubCommand", harness.GitHubScriptPath,
            "-DotNetCommand", harness.DotNetScriptPath);

        Assert.True(File.Exists(Path.Combine(harness.StageRoot, "Winget", "EvotecIT.IntelligenceX.Chat.yaml")));
        Assert.True(File.Exists(Path.Combine(harness.StageRoot, "Winget", "EvotecIT.IntelligenceX.Tray.yaml")));

        var ghCalls = harness.ReadGhCalls().Select(element => element.EnumerateArray().Select(value => value.GetString()).ToArray()).ToArray();
        Assert.Equal(3, ghCalls.Length);
        Assert.Equal("release", ghCalls[0][0]);
        Assert.Equal("view", ghCalls[0][1]);
        Assert.Equal("release", ghCalls[1][0]);
        Assert.Equal("create", ghCalls[1][1]);
        Assert.Equal("release", ghCalls[2][0]);
        Assert.Equal("upload", ghCalls[2][1]);
        Assert.Contains(ghCalls[2], value => string.Equals(value, Path.Combine(harness.StageRoot, "GitHub", "IntelligenceX.Chat-Portable-win-x64.zip"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ghCalls[2], value => string.Equals(value, Path.Combine(harness.StageRoot, "Winget", "EvotecIT.IntelligenceX.Chat.yaml"), StringComparison.OrdinalIgnoreCase));

        var ghTokens = harness.ReadGitHubTokens().Select(element => element.GetString()).ToArray();
        Assert.Equal(3, ghTokens.Length);
        Assert.All(ghTokens, token => Assert.Equal("gh-token-value", token));

        var dotnetCalls = harness.ReadDotNetCalls().Select(element => element.EnumerateArray().Select(value => value.GetString()).ToArray()).ToArray();
        Assert.Single(dotnetCalls);
        var dotnetArgs = dotnetCalls[0];
        Assert.Equal("nuget", dotnetArgs[0]);
        Assert.Equal("push", dotnetArgs[1]);
        Assert.EndsWith("IntelligenceX.0.1.0.nupkg", dotnetArgs[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--skip-duplicate", dotnetArgs);
    }

    private static void RunPublishReleaseOutputs(string repoRoot, PublishHarness harness, params string[] scriptArgs) {
        var psi = new ProcessStartInfo {
            FileName = ResolvePwshPath(),
            WorkingDirectory = repoRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "Build", "Internal", "Publish-ReleaseOutputs.ps1"));
        foreach (var arg in scriptArgs) {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Publish-ReleaseOutputs.ps1");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync()).GetAwaiter().GetResult();

        Assert.True(process.ExitCode == 0, $"Publish-ReleaseOutputs.ps1 failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdoutTask.Result}{Environment.NewLine}STDERR:{Environment.NewLine}{stderrTask.Result}");
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir.FullName, "IntelligenceX.sln"))) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }

    private static string ResolvePwshPath() {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathSegments = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidates = new[] {
            OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh",
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe")
                : "/usr/local/bin/pwsh",
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7-preview", "pwsh.exe")
                : "/opt/homebrew/bin/pwsh"
        };

        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value))) {
            if (Path.IsPathRooted(candidate)) {
                if (File.Exists(candidate)) {
                    return candidate;
                }

                continue;
            }

            foreach (var segment in pathSegments) {
                var fullPath = Path.Combine(segment, candidate);
                if (File.Exists(fullPath)) {
                    return fullPath;
                }
            }
        }

        return OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
    }

    private sealed class PublishHarness : IDisposable {
        private PublishHarness(
            string rootPath,
            string configPath,
            string stageRoot,
            string gitHubScriptPath,
            string dotNetScriptPath,
            string ghCallsPath,
            string ghTokensPath,
            string dotnetCallsPath) {
            RootPath = rootPath;
            ConfigPath = configPath;
            StageRoot = stageRoot;
            GitHubScriptPath = gitHubScriptPath;
            DotNetScriptPath = dotNetScriptPath;
            GhCallsPath = ghCallsPath;
            GhTokensPath = ghTokensPath;
            DotNetCallsPath = dotnetCallsPath;
        }

        public string RootPath { get; }
        public string ConfigPath { get; }
        public string StageRoot { get; }
        public string GitHubScriptPath { get; }
        public string DotNetScriptPath { get; }
        public string GhCallsPath { get; }
        public string GhTokensPath { get; }
        public string DotNetCallsPath { get; }

        public static PublishHarness Create() {
            var rootPath = Path.Combine(Path.GetTempPath(), "ix-release-publish-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);

            var configRoot = Path.Combine(rootPath, "config");
            var stageRoot = Path.Combine(rootPath, "stage");
            var wingetRoot = Path.Combine(configRoot, "WingetOut");
            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(stageRoot);
            Directory.CreateDirectory(wingetRoot);
            Directory.CreateDirectory(Path.Combine(stageRoot, "GitHub"));
            Directory.CreateDirectory(Path.Combine(stageRoot, "NuGet"));

            File.WriteAllText(Path.Combine(stageRoot, "GitHub", "IntelligenceX.Chat-Portable-win-x64.zip"), "zip");
            File.WriteAllText(Path.Combine(stageRoot, "NuGet", "IntelligenceX.0.1.0.nupkg"), "nupkg");
            File.WriteAllText(Path.Combine(stageRoot, "release-manifest.json"), """
{
  "packages": {
    "ResolvedVersion": "0.1.0"
  }
}
""");
            File.WriteAllText(Path.Combine(stageRoot, "SHA256SUMS.txt"), "sha");
            File.WriteAllText(Path.Combine(wingetRoot, "EvotecIT.IntelligenceX.Chat.yaml"), "chat");
            File.WriteAllText(Path.Combine(wingetRoot, "EvotecIT.IntelligenceX.Tray.yaml"), "tray");

            var tokenPath = Path.Combine(configRoot, "github-token.txt");
            var nugetKeyPath = Path.Combine(configRoot, "nuget-key.txt");
            File.WriteAllText(tokenPath, "gh-token-value");
            File.WriteAllText(nugetKeyPath, "nuget-key-value");

            var configPath = Path.Combine(configRoot, "release.json");
            File.WriteAllText(configPath, $$"""
{
  "Packages": {
    "GitHubUsername": "EvotecIT",
    "GitHubRepositoryName": "IntelligenceX",
    "GitHubGenerateReleaseNotes": true,
    "GitHubIsPreRelease": false,
    "GitHubAccessTokenFilePath": "{{tokenPath.Replace("\\", "\\\\")}}",
    "PublishSource": "https://api.nuget.org/v3/index.json",
    "PublishApiKeyFilePath": "{{nugetKeyPath.Replace("\\", "\\\\")}}",
    "SkipDuplicate": true
  },
  "Winget": {
    "Enabled": true,
    "OutputPath": "WingetOut"
  }
}
""");

            var ghCallsPath = Path.Combine(rootPath, "gh-calls.json");
            var ghTokensPath = Path.Combine(rootPath, "gh-tokens.json");
            var dotnetCallsPath = Path.Combine(rootPath, "dotnet-calls.json");
            var gitHubScriptPath = Path.Combine(rootPath, "fake-gh.ps1");
            var dotNetScriptPath = Path.Combine(rootPath, "fake-dotnet.ps1");

            File.WriteAllText(
                gitHubScriptPath,
                $$"""
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $ArgsFromCaller)
$callsPath = "{{ghCallsPath}}"
$tokensPath = "{{ghTokensPath}}"
Add-Content -LiteralPath $callsPath -Value (($ArgsFromCaller | ConvertTo-Json -Compress))
Add-Content -LiteralPath $tokensPath -Value (($env:GH_TOKEN | ConvertTo-Json -Compress))
if ($ArgsFromCaller.Length -ge 2 -and $ArgsFromCaller[0] -eq 'release' -and $ArgsFromCaller[1] -eq 'view') {
    exit 1
}
exit 0
""");

            File.WriteAllText(
                dotNetScriptPath,
                $$"""
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $ArgsFromCaller)
$callsPath = "{{dotnetCallsPath}}"
Add-Content -LiteralPath $callsPath -Value (($ArgsFromCaller | ConvertTo-Json -Compress))
exit 0
""");

            return new PublishHarness(rootPath, configPath, stageRoot, gitHubScriptPath, dotNetScriptPath, ghCallsPath, ghTokensPath, dotnetCallsPath);
        }

        public JsonElement[] ReadGhCalls() => ReadJsonLines(GhCallsPath);
        public JsonElement[] ReadGitHubTokens() => ReadJsonLines(GhTokensPath);
        public JsonElement[] ReadDotNetCalls() => ReadJsonLines(DotNetCallsPath);

        public void Dispose() {
            try {
                if (Directory.Exists(RootPath)) {
                    Directory.Delete(RootPath, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }

        private static JsonElement[] ReadJsonLines(string path) {
            Assert.True(File.Exists(path), $"Expected JSON capture file to exist: {path}");
            return File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => {
                    using var doc = JsonDocument.Parse(line);
                    return doc.RootElement.Clone();
                })
                .ToArray();
        }
    }
}
