using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class BuildProjectWrapperTests {
    [Fact]
    public void PackagesOnly_DefaultConfig_UsesReleasePackagesConfig_AndRepoRootPaths() {
        var repoRoot = FindRepoRoot();
        using var harness = CliCaptureHarness.Create();
        var stageRoot = @".\Artifacts\WrapperTests\packages-only";
        var manifestPath = @".\Artifacts\WrapperTests\packages-only\manifest.json";
        var checksumsPath = @".\Artifacts\WrapperTests\packages-only\SHA256SUMS.txt";

        RunBuildProject(
            repoRoot,
            harness,
            "-Plan",
            "-PackagesOnly",
            "-StageRoot", stageRoot,
            "-ManifestJsonPath", manifestPath,
            "-ChecksumsPath", checksumsPath);

        var args = harness.ReadCapturedArgs();
        Assert.Contains("release", args);
        AssertContainsOption(args, "--config", Path.Combine(repoRoot, "Build", "release.packages.json"));
        AssertContainsOption(args, "--stage-root", Path.Combine(repoRoot, "Artifacts", "WrapperTests", "packages-only"));
        AssertContainsOption(args, "--manifest-json", Path.Combine(repoRoot, "Artifacts", "WrapperTests", "packages-only", "manifest.json"));
        AssertContainsOption(args, "--checksums-path", Path.Combine(repoRoot, "Artifacts", "WrapperTests", "packages-only", "SHA256SUMS.txt"));
        Assert.Contains("--packages-only", args);
    }

    [Fact]
    public void PackagesOnly_ExplicitConfig_DoesNotAutoSwitch() {
        var repoRoot = FindRepoRoot();
        using var harness = CliCaptureHarness.Create();

        RunBuildProject(
            repoRoot,
            harness,
            "-Plan",
            "-PackagesOnly",
            "-ConfigPath", @".\Build\release.json");

        var args = harness.ReadCapturedArgs();
        AssertContainsOption(args, "--config", Path.Combine(repoRoot, "Build", "release.json"));
        Assert.DoesNotContain(Path.Combine(repoRoot, "Build", "release.packages.json"), args, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseConfigs_KeepUploadReadyOutputsScopedPerRun() {
        var repoRoot = FindRepoRoot();
        var releaseJson = File.ReadAllText(Path.Combine(repoRoot, "Build", "release.json"));
        using var releaseDoc = JsonDocument.Parse(releaseJson);
        Assert.Equal("../Artifacts/UploadReady", releaseDoc.RootElement.GetProperty("Outputs").GetProperty("Staging").GetProperty("RootPath").GetString());
        Assert.Equal("Winget", releaseDoc.RootElement.GetProperty("Winget").GetProperty("OutputPath").GetString());

        var packagesJson = File.ReadAllText(Path.Combine(repoRoot, "Build", "release.packages.json"));
        using var packagesDoc = JsonDocument.Parse(packagesJson);
        Assert.Equal("../Artifacts/UploadReady", packagesDoc.RootElement.GetProperty("Outputs").GetProperty("Staging").GetProperty("RootPath").GetString());
        Assert.False(packagesDoc.RootElement.TryGetProperty("Winget", out _));
    }

    private static void RunBuildProject(string repoRoot, CliCaptureHarness harness, params string[] scriptArgs) {
        var psi = new ProcessStartInfo {
            FileName = "pwsh",
            WorkingDirectory = repoRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(repoRoot, "Build", "Build-Project.ps1"));
        foreach (var arg in scriptArgs) {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment["POWERFORGE_CLI_PATH"] = harness.ScriptPath;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Build-Project.ps1");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Build-Project.ps1 failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
    }

    private static void AssertContainsOption(string[] args, string optionName, string expectedValue) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase)) {
                Assert.Equal(expectedValue, args[i + 1]);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException($"Option '{optionName}' was not found in captured args: {string.Join(" ", args)}");
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

    private sealed class CliCaptureHarness : IDisposable {
        private CliCaptureHarness(string rootPath, string scriptPath, string capturePath) {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            CapturePath = capturePath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string CapturePath { get; }

        public static CliCaptureHarness Create() {
            var rootPath = Path.Combine(Path.GetTempPath(), "ix-build-wrapper-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);

            var capturePath = Path.Combine(rootPath, "captured-args.json");
            var scriptPath = Path.Combine(rootPath, "fake-powerforge.ps1");
            File.WriteAllText(
                scriptPath,
                """
param([Parameter(ValueFromRemainingArguments = $true)][string[]] $ArgsFromCaller)
$capturePath = Join-Path $PSScriptRoot 'captured-args.json'
$json = $ArgsFromCaller | ConvertTo-Json -Compress
Set-Content -LiteralPath $capturePath -Value $json -NoNewline
exit 0
""");

            return new CliCaptureHarness(rootPath, scriptPath, capturePath);
        }

        public string[] ReadCapturedArgs() {
            Assert.True(File.Exists(CapturePath), "Expected fake PowerForge CLI to capture arguments.");
            using var doc = JsonDocument.Parse(File.ReadAllText(CapturePath));
            return doc.RootElement.EnumerateArray().Select(element => element.GetString() ?? string.Empty).ToArray();
        }

        public void Dispose() {
            try {
                if (Directory.Exists(RootPath)) {
                    Directory.Delete(RootPath, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }
}
