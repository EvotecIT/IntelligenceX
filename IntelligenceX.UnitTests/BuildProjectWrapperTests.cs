using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class BuildProjectWrapperTests {
    [Fact]
    public void PackagesOnly_DefaultConfig_UsesFocusedProjectBuildLane() {
        var repoRoot = FindRepoRoot();
        using var harness = ProjectBuildCaptureHarness.Create();

        RunPackageBuildProject(repoRoot, harness);

        var invocation = harness.ReadInvocation();
        Assert.Equal(Path.Combine(repoRoot, "Build", "project.build.json"), invocation.GetProperty("ConfigPath").GetString());
        Assert.True(invocation.GetProperty("Build").GetBoolean());
        Assert.True(invocation.GetProperty("Plan").GetBoolean());
        Assert.False(invocation.GetProperty("PublishNuget").GetBoolean());
        Assert.False(invocation.GetProperty("PublishGitHub").GetBoolean());

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "Build", "project.build.json")));
        var versions = config.RootElement.GetProperty("ExpectedVersionMap");
        Assert.Equal("0.1.X", versions.GetProperty("IntelligenceX").GetString());
        Assert.Equal("0.1.X", versions.GetProperty("IntelligenceX.Storage.SQLite").GetString());
        Assert.True(config.RootElement.GetProperty("ExpectedVersionMapAsInclude").GetBoolean());
        Assert.True(config.RootElement.GetProperty("IncludeSymbols").GetBoolean());
    }

    [Fact]
    public void PackagesOnly_WithReleaseStagingArguments_UsesReleaseGraphWithoutDroppingThem() {
        var repoRoot = FindRepoRoot();
        using var harness = CliCaptureHarness.Create();
        var stageRoot = Path.Combine(".", "Artifacts", "WrapperTests", "packages-only");
        var manifestPath = Path.Combine(stageRoot, "manifest.json");
        var checksumsPath = Path.Combine(stageRoot, "SHA256SUMS.txt");

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
            "-ConfigPath", Path.Combine(".", "Build", "release.json"));

        var args = harness.ReadCapturedArgs();
        AssertContainsOption(args, "--config", Path.Combine(repoRoot, "Build", "release.json"));
        Assert.DoesNotContain(Path.Combine(repoRoot, "Build", "release.packages.json"), args, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagesOnly_ExplicitCliRoute_PreservesWiderReleaseCompatibility() {
        var repoRoot = FindRepoRoot();
        using var harness = CliCaptureHarness.Create();

        RunBuildProject(repoRoot, harness, "-Plan", "-PackagesOnly");

        var args = harness.ReadCapturedArgs();
        Assert.Contains("release", args);
        AssertContainsOption(args, "--config", Path.Combine(repoRoot, "Build", "release.packages.json"));
        Assert.Contains("--packages-only", args);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "Build", "release.packages.json")));
        Assert.Equal("oss", config.RootElement.GetProperty("WorkspaceValidation").GetProperty("Profile").GetString());
    }

    [Fact]
    public void PackagesOnly_CliPublishNuget_RequiresDependencyOrderCapableCli() {
        var repoRoot = FindRepoRoot();
        using var harness = CliCaptureHarness.Create();

        var result = RunBuildProjectProcess(repoRoot, harness, "3.0.125", "-PackagesOnly", "-PublishNuget");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("3.0.126", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(harness.CapturePath));
    }

    [Fact]
    public void PackagesOnly_CliPublishNuget_AllowsDependencyOrderCapableCli() {
        var repoRoot = FindRepoRoot();
        using var harness = CliCaptureHarness.Create();

        var result = RunBuildProjectProcess(repoRoot, harness, "3.0.126", "-Plan", "-PackagesOnly", "-PublishNuget");

        Assert.Equal(0, result.ExitCode);
        var args = harness.ReadCapturedArgs();
        Assert.Contains("--publish-nuget", args);
        Assert.Contains("--packages-only", args);
        AssertContainsOption(args, "--config", Path.Combine(repoRoot, "Build", "release.packages.json"));
    }

    [Fact]
    public void PackagesOnly_PublishNuget_RequiresDependencyOrderCapableModule() {
        var repoRoot = FindRepoRoot();
        using var harness = ProjectBuildCaptureHarness.Create();

        var result = RunPackageBuildProjectProcess(repoRoot, harness, publishNuget: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("3.0.126", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(harness.CapturePath));
    }

    [Fact]
    public void ReleaseConfigs_KeepUploadReadyOutputsScopedPerRun() {
        var repoRoot = FindRepoRoot();
        var releaseJson = File.ReadAllText(Path.Combine(repoRoot, "Build", "release.json"));
        using var releaseDoc = JsonDocument.Parse(releaseJson);
        Assert.Equal("../Artifacts/UploadReady", releaseDoc.RootElement.GetProperty("Outputs").GetProperty("Staging").GetProperty("RootPath").GetString());
        Assert.Equal("Winget", releaseDoc.RootElement.GetProperty("Winget").GetProperty("OutputPath").GetString());
        Assert.Equal("IntelligenceX", releaseDoc.RootElement.GetProperty("Packages").GetProperty("GitHubPrimaryProject").GetString());
        var releaseVersions = releaseDoc.RootElement.GetProperty("Packages").GetProperty("ExpectedVersionMap");
        Assert.Equal("0.1.X", releaseVersions.GetProperty("IntelligenceX").GetString());
        Assert.Equal("0.1.X", releaseVersions.GetProperty("IntelligenceX.Storage.SQLite").GetString());
        Assert.True(releaseDoc.RootElement.GetProperty("Packages").GetProperty("IncludeSymbols").GetBoolean());

        foreach (var wingetPackage in releaseDoc.RootElement.GetProperty("Winget").GetProperty("Packages").EnumerateArray()) {
            Assert.False(wingetPackage.TryGetProperty("PackageVersion", out _));
        }

        var packagesJson = File.ReadAllText(Path.Combine(repoRoot, "Build", "release.packages.json"));
        using var packagesDoc = JsonDocument.Parse(packagesJson);
        Assert.Equal("../Artifacts/UploadReady", packagesDoc.RootElement.GetProperty("Outputs").GetProperty("Staging").GetProperty("RootPath").GetString());
        Assert.False(packagesDoc.RootElement.TryGetProperty("Winget", out _));
        Assert.Equal("IntelligenceX", packagesDoc.RootElement.GetProperty("Packages").GetProperty("GitHubPrimaryProject").GetString());
        var packageVersions = packagesDoc.RootElement.GetProperty("Packages").GetProperty("ExpectedVersionMap");
        Assert.Equal("0.1.X", packageVersions.GetProperty("IntelligenceX").GetString());
        Assert.Equal("0.1.X", packageVersions.GetProperty("IntelligenceX.Storage.SQLite").GetString());
        Assert.True(packagesDoc.RootElement.GetProperty("Packages").GetProperty("IncludeSymbols").GetBoolean());
    }

    private static void RunBuildProject(string repoRoot, CliCaptureHarness harness, params string[] scriptArgs) {
        var result = RunBuildProjectProcess(repoRoot, harness, "3.0.126", scriptArgs);

        Assert.True(result.ExitCode == 0, $"Build-Project.ps1 failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
    }

    private static ProcessResult RunBuildProjectProcess(string repoRoot, CliCaptureHarness harness, string cliVersion, params string[] scriptArgs) {
        var psi = new ProcessStartInfo {
            FileName = ResolvePwshPath(),
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
        psi.Environment["IX_FAKE_POWERFORGE_VERSION"] = cliVersion;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Build-Project.ps1");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync()).GetAwaiter().GetResult();

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static void RunPackageBuildProject(string repoRoot, ProjectBuildCaptureHarness harness) {
        var result = RunPackageBuildProjectProcess(repoRoot, harness, publishNuget: false);

        Assert.True(result.ExitCode == 0, $"Build-Project.ps1 failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
        Assert.True(File.Exists(harness.CapturePath), $"Expected fake PSPublishModule to capture the package build invocation.{Environment.NewLine}Module root: {harness.ModuleRoot}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}");
    }

    private static ProcessResult RunPackageBuildProjectProcess(string repoRoot, ProjectBuildCaptureHarness harness, bool publishNuget) {
        var psi = new ProcessStartInfo {
            FileName = ResolvePwshPath(),
            WorkingDirectory = repoRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(harness.WrapperPath);
        psi.Environment["IX_BUILD_PROJECT_SCRIPT"] = Path.Combine(repoRoot, "Build", "Build-Project.ps1");
        psi.Environment["IX_PROJECT_BUILD_CAPTURE"] = harness.CapturePath;
        psi.Environment["IX_TEST_PUBLISH_NUGET"] = publishNuget ? "1" : "0";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Build-Project.ps1");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync()).GetAwaiter().GetResult();

        return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static ProcessStartInfo CreateBuildProjectStartInfo(string repoRoot, string[] scriptArgs) {
        var psi = new ProcessStartInfo {
            FileName = ResolvePwshPath(),
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
        return psi;
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
if ($ArgsFromCaller.Count -eq 1 -and $ArgsFromCaller[0] -eq '--version') {
    Write-Output $env:IX_FAKE_POWERFORGE_VERSION
    exit 0
}
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

    private sealed class ProjectBuildCaptureHarness : IDisposable {
        private ProjectBuildCaptureHarness(string rootPath, string moduleRoot, string capturePath, string wrapperPath) {
            RootPath = rootPath;
            ModuleRoot = moduleRoot;
            CapturePath = capturePath;
            WrapperPath = wrapperPath;
        }

        public string RootPath { get; }
        public string ModuleRoot { get; }
        public string CapturePath { get; }
        public string WrapperPath { get; }

        public static ProjectBuildCaptureHarness Create() {
            var rootPath = Path.Combine(Path.GetTempPath(), "ix-project-build-tests", Guid.NewGuid().ToString("N"));
            var moduleRoot = Path.Combine(rootPath, "modules");
            var modulePath = Path.Combine(moduleRoot, "PSPublishModule");
            Directory.CreateDirectory(modulePath);
            var capturePath = Path.Combine(rootPath, "invocation.json");
            var wrapperPath = Path.Combine(rootPath, "invoke-build-project.ps1");
            File.WriteAllText(
                wrapperPath,
                """
$env:PSModulePath = Join-Path $PSScriptRoot 'modules'
if ($env:IX_TEST_PUBLISH_NUGET -eq '1') {
    & $env:IX_BUILD_PROJECT_SCRIPT -PackagesOnly -PublishNuget
} else {
    & $env:IX_BUILD_PROJECT_SCRIPT -Plan -PackagesOnly
}
exit $LASTEXITCODE
""");
            File.WriteAllText(
                Path.Combine(modulePath, "PSPublishModule.psm1"),
                """
function Invoke-ProjectBuild {
    param(
        [string] $ConfigPath,
        [Nullable[bool]] $Build,
        [Nullable[bool]] $PublishNuget,
        [Nullable[bool]] $PublishGitHub,
        [Nullable[bool]] $Plan
    )
    [ordered]@{
        ConfigPath = $ConfigPath
        Build = [bool] $Build
        PublishNuget = [bool] $PublishNuget
        PublishGitHub = [bool] $PublishGitHub
        Plan = [bool] $Plan
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:IX_PROJECT_BUILD_CAPTURE -NoNewline
}
Export-ModuleMember -Function Invoke-ProjectBuild
""");
            File.WriteAllText(
                Path.Combine(modulePath, "PSPublishModule.psd1"),
                """
@{
    RootModule = 'PSPublishModule.psm1'
    ModuleVersion = '0.0.1'
    GUID = '958439a3-eedb-4f78-87d6-f1da1eea73bc'
    FunctionsToExport = @('Invoke-ProjectBuild')
}
""");
            return new ProjectBuildCaptureHarness(rootPath, moduleRoot, capturePath, wrapperPath);
        }

        public JsonElement ReadInvocation() {
            Assert.True(File.Exists(CapturePath), "Expected fake PSPublishModule to capture the package build invocation.");
            using var document = JsonDocument.Parse(File.ReadAllText(CapturePath));
            return document.RootElement.Clone();
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
