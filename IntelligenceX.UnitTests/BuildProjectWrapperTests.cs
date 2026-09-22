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
    public void NonPublishInvocation_UsesFocusedPackageConfiguration() {
        var repoRoot = FindRepoRoot();
        using var harness = ProjectBuildCaptureHarness.Create();

        var result = harness.Run(repoRoot);

        Assert.Equal(0, result.ExitCode);
        using var capture = harness.ReadCapture();
        Assert.Equal(Path.Combine(repoRoot, "Build", "project.build.json"), capture.RootElement.GetProperty("ConfigPath").GetString());
        Assert.False(capture.RootElement.GetProperty("PublishNuget").GetBoolean());
        Assert.False(capture.RootElement.GetProperty("PublishGitHub").GetBoolean());
    }

    [Fact]
    public void PublishInvocation_ForwardsNugetAndGitHubIntent() {
        var repoRoot = FindRepoRoot();
        using var harness = ProjectBuildCaptureHarness.Create();

        var result = harness.Run(repoRoot, publishNuget: true, publishGitHub: true);

        Assert.Equal(0, result.ExitCode);
        using var capture = harness.ReadCapture();
        Assert.True(capture.RootElement.GetProperty("PublishNuget").GetBoolean());
        Assert.True(capture.RootElement.GetProperty("PublishGitHub").GetBoolean());
    }

    [Fact]
    public void PlanInvocation_ForwardsPlanAndOutputPath() {
        var repoRoot = FindRepoRoot();
        using var harness = ProjectBuildCaptureHarness.Create();
        var planPath = Path.Combine(harness.RootPath, "package-plan.json");

        var result = harness.Run(repoRoot, plan: true, planPath: planPath);

        Assert.Equal(0, result.ExitCode);
        using var capture = harness.ReadCapture();
        Assert.True(capture.RootElement.GetProperty("Plan").GetBoolean());
        Assert.Equal(planPath, capture.RootElement.GetProperty("PlanPath").GetString());
    }

    [Fact]
    public void ProjectConfiguration_SelectsOnlyPublicPackagesInDependencyOrder() {
        var repoRoot = FindRepoRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "Build", "project.build.json")));
        var root = document.RootElement;
        var expectedVersions = root.GetProperty("ExpectedVersionMap");

        Assert.True(root.GetProperty("ExpectedVersionMapAsInclude").GetBoolean());
        Assert.Equal(2, expectedVersions.EnumerateObject().Count());
        Assert.Equal("0.1.X", expectedVersions.GetProperty("IntelligenceX").GetString());
        Assert.Equal("0.1.X", expectedVersions.GetProperty("IntelligenceX.Storage.SQLite").GetString());
        Assert.Equal("IntelligenceX", root.GetProperty("GitHubPrimaryProject").GetString());
        Assert.Equal("../Artifacts/ProjectBuild/project.build.plan.json", root.GetProperty("PlanOutputPath").GetString());
        Assert.Equal("../Artifacts/ProjectBuild/ReleaseZip", root.GetProperty("ReleaseZipOutputPath").GetString());
        Assert.Equal("92e95fb58effa6a4a75e77a33cdd6bfe6dd30f1a", root.GetProperty("CertificateThumbprint").GetString());
        Assert.Equal("CurrentUser", root.GetProperty("CertificateStore").GetString());
        Assert.Equal("http://timestamp.digicert.com", root.GetProperty("TimeStampServer").GetString());
    }

    private static string FindRepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            if (File.Exists(Path.Combine(directory.FullName, "IntelligenceX.sln"))) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }

    private sealed class ProjectBuildCaptureHarness : IDisposable {
        private ProjectBuildCaptureHarness(string rootPath, string modulePath, string capturePath, string runnerPath) {
            RootPath = rootPath;
            ModulePath = modulePath;
            CapturePath = capturePath;
            RunnerPath = runnerPath;
        }

        public string RootPath { get; }
        public string ModulePath { get; }
        public string CapturePath { get; }
        public string RunnerPath { get; }

        public static ProjectBuildCaptureHarness Create() {
            var rootPath = Path.Combine(Path.GetTempPath(), "ix-project-build-wrapper-tests", Guid.NewGuid().ToString("N"));
            var modulePath = Path.Combine(rootPath, "Modules");
            var versionPath = Path.Combine(modulePath, "PSPublishModule", "3.0.126");
            Directory.CreateDirectory(versionPath);

            File.WriteAllText(
                Path.Combine(versionPath, "PSPublishModule.psd1"),
                "@{ RootModule = 'PSPublishModule.psm1'; ModuleVersion = '3.0.126'; FunctionsToExport = @('Invoke-ProjectBuild') }");
            File.WriteAllText(
                Path.Combine(versionPath, "PSPublishModule.psm1"),
                """
function Invoke-ProjectBuild {
    [CmdletBinding()] param(
        [string] $ConfigPath,
        [Nullable[bool]] $UpdateVersions,
        [Nullable[bool]] $Build,
        [Nullable[bool]] $PublishNuget,
        [Nullable[bool]] $PublishGitHub,
        [Nullable[bool]] $Plan,
        [string] $PlanPath
    )
    $capture = @{}
    foreach ($entry in $PSBoundParameters.GetEnumerator()) { $capture[$entry.Key] = $entry.Value }
    $capture | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:IX_PROJECT_BUILD_CAPTURE -NoNewline
    [pscustomobject]@{ Success = $true }
}
Export-ModuleMember -Function Invoke-ProjectBuild
""");

            var runnerPath = Path.Combine(rootPath, "run-build-project.ps1");
            File.WriteAllText(
                runnerPath,
                """
$env:PSModulePath = $env:IX_PROJECT_BUILD_MODULE_PATH + [IO.Path]::PathSeparator + $env:PSModulePath
$parameters = @{}
if ($env:IX_PROJECT_BUILD_PLAN -eq '1') { $parameters.Plan = $true }
if ($env:IX_PROJECT_BUILD_PLAN_PATH) { $parameters.PlanPath = $env:IX_PROJECT_BUILD_PLAN_PATH }
$parameters.PublishNuget = $env:IX_PROJECT_BUILD_PUBLISH_NUGET -eq '1'
$parameters.PublishGitHub = $env:IX_PROJECT_BUILD_PUBLISH_GITHUB -eq '1'
& $env:IX_PROJECT_BUILD_SCRIPT @parameters
""");

            return new ProjectBuildCaptureHarness(
                rootPath,
                modulePath,
                Path.Combine(rootPath, "captured.json"),
                runnerPath);
        }

        public ProcessResult Run(string repoRoot, bool plan = false, string? planPath = null, bool publishNuget = false, bool publishGitHub = false) {
            var startInfo = new ProcessStartInfo {
                FileName = ResolvePwshPath(),
                WorkingDirectory = repoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(RunnerPath);
            startInfo.Environment["IX_PROJECT_BUILD_SCRIPT"] = Path.Combine(repoRoot, "Build", "Build-Project.ps1");
            startInfo.Environment["IX_PROJECT_BUILD_MODULE_PATH"] = ModulePath;
            startInfo.Environment["IX_PROJECT_BUILD_CAPTURE"] = CapturePath;
            startInfo.Environment["IX_PROJECT_BUILD_PLAN"] = plan ? "1" : "0";
            startInfo.Environment["IX_PROJECT_BUILD_PLAN_PATH"] = planPath ?? string.Empty;
            startInfo.Environment["IX_PROJECT_BUILD_PUBLISH_NUGET"] = publishNuget ? "1" : "0";
            startInfo.Environment["IX_PROJECT_BUILD_PUBLISH_GITHUB"] = publishGitHub ? "1" : "0";

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Build-Project.ps1");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync()).GetAwaiter().GetResult();
            return new ProcessResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }

        public JsonDocument ReadCapture() {
            Assert.True(File.Exists(CapturePath), "Expected Invoke-ProjectBuild to capture its parameters.");
            return JsonDocument.Parse(File.ReadAllText(CapturePath));
        }

        public void Dispose() {
            try {
                if (Directory.Exists(RootPath)) {
                    Directory.Delete(RootPath, recursive: true);
                }
            } catch {
                // Best-effort cleanup of the task-owned harness.
            }
        }

        private static string ResolvePwshPath() {
            var pathEnvironment = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var executableName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
            foreach (var path in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                var candidate = Path.Combine(path, executableName);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }

            return executableName;
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
