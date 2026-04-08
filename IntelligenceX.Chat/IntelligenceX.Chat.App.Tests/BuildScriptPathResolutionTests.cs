using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards worktree-aware sibling repo discovery used by the local chat launcher.
/// </summary>
public sealed class BuildScriptPathResolutionTests {
    private static readonly string WorkspaceRepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>
    /// Verifies the shared TestimoX root resolver can discover the sibling repo when the caller repo is itself a worktree checkout.
    /// </summary>
    [Fact]
    public void ResolveTestimoXRootScript_FindsSiblingRepoFromWorktreeRoot() {
        using var layout = TemporaryWorkspaceLayout.Create();
        layout.CreateTestimoXMarkers();

        var scriptPath = Path.Combine(layout.RepoRoot, "Build", "Internal", "Resolve-TestimoXRoot.ps1");
        var result = InvokePwsh(
            $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapeForSingleQuotedPowerShell(scriptPath)}}'
            $resolved = Resolve-TestimoXRoot -RepoRoot '{{EscapeForSingleQuotedPowerShell(layout.WorktreeRepoRoot)}}'
            [Console]::Out.Write($resolved)
            """);

        Assert.Equal(AppendDirectorySeparator(layout.TestimoXRoot), result.Trim());
    }

    /// <summary>
    /// Verifies optional sibling repo discovery also finds a local OfficeIMO checkout from the same worktree-style layout.
    /// </summary>
    [Fact]
    public void ResolveOptionalSiblingRepoRoot_FindsOfficeImoFromWorktreeRoot() {
        using var layout = TemporaryWorkspaceLayout.Create();
        layout.CreateOfficeImoMarkers();

        var scriptPath = Path.Combine(layout.RepoRoot, "Build", "Internal", "Resolve-TestimoXRoot.ps1");
        var result = InvokePwsh(
            $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapeForSingleQuotedPowerShell(scriptPath)}}'
            $resolved = Resolve-OptionalSiblingRepoRoot -RepoRoot '{{EscapeForSingleQuotedPowerShell(layout.WorktreeRepoRoot)}}' -RepoNames @('OfficeIMO') -MarkerRelativePaths @('OfficeIMO.MarkdownRenderer\OfficeIMO.MarkdownRenderer.csproj')
            [Console]::Out.Write($resolved)
            """);

        Assert.Equal(AppendDirectorySeparator(layout.OfficeImoRoot), result.Trim());
    }

    /// <summary>
    /// Verifies the chat launcher now uses the shared worktree-aware sibling repo resolver for local OfficeIMO discovery.
    /// </summary>
    [Fact]
    public void RunChatApp_UsesWorktreeAwareSiblingResolverForOfficeImo() {
        var scriptPath = Path.Combine(WorkspaceRepoRoot, "Build", "Chat", "Run-ChatApp.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("Resolve-OptionalSiblingRepoRoot", script, StringComparison.Ordinal);
        Assert.Contains("OfficeIMO.MarkdownRenderer\\OfficeIMO.MarkdownRenderer.csproj", script, StringComparison.Ordinal);
    }

    private static string InvokePwsh(string command) {
        var psi = new ProcessStartInfo {
            FileName = ResolvePwshPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi);
        Assert.NotNull(process);
        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"pwsh exited {process.ExitCode}: {stderr}");
        return stdout;
    }

    private static string ResolvePwshPath() {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var bundled = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        return File.Exists(bundled) ? bundled : "pwsh";
    }

    private static string AppendDirectorySeparator(string path) {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string EscapeForSingleQuotedPowerShell(string text) =>
        text.Replace("'", "''", StringComparison.Ordinal);

    private sealed class TemporaryWorkspaceLayout : IDisposable {
        private TemporaryWorkspaceLayout(string rootPath) {
            RootPath = rootPath;
            WorkspaceRoot = Path.Combine(rootPath, "workspace");
            RepoRoot = Path.Combine(WorkspaceRoot, "IntelligenceX");
            WorktreeRepoRoot = Path.Combine(RepoRoot, ".worktrees", "chat-regression");
            TestimoXRoot = Path.Combine(WorkspaceRoot, "TestimoX");
            OfficeImoRoot = Path.Combine(WorkspaceRoot, "OfficeIMO");
        }

        public string RootPath { get; }
        public string WorkspaceRoot { get; }
        public string RepoRoot { get; }
        public string WorktreeRepoRoot { get; }
        public string TestimoXRoot { get; }
        public string OfficeImoRoot { get; }

        public static TemporaryWorkspaceLayout Create() {
            var rootPath = Path.Combine(Path.GetTempPath(), "ix-chat-run-script-" + Guid.NewGuid().ToString("N"));
            var layout = new TemporaryWorkspaceLayout(rootPath);
            Directory.CreateDirectory(layout.WorktreeRepoRoot);
            Directory.CreateDirectory(Path.Combine(layout.RepoRoot, "Build", "Internal"));
            File.Copy(
                Path.Combine(WorkspaceRepoRoot, "Build", "Internal", "Resolve-TestimoXRoot.ps1"),
                Path.Combine(layout.RepoRoot, "Build", "Internal", "Resolve-TestimoXRoot.ps1"));
            return layout;
        }

        public void CreateTestimoXMarkers() {
            Directory.CreateDirectory(Path.Combine(TestimoXRoot, "ADPlayground"));
            Directory.CreateDirectory(Path.Combine(TestimoXRoot, "ComputerX", "Features"));
            Directory.CreateDirectory(Path.Combine(TestimoXRoot, "ComputerX", "PowerShellRuntime"));
            File.WriteAllText(Path.Combine(TestimoXRoot, "ADPlayground", "ADPlayground.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(TestimoXRoot, "ComputerX", "Features", "FeatureInventoryQuery.cs"), "// marker");
            File.WriteAllText(Path.Combine(TestimoXRoot, "ComputerX", "PowerShellRuntime", "PowerShellCommandQuery.cs"), "// marker");
        }

        public void CreateOfficeImoMarkers() {
            Directory.CreateDirectory(Path.Combine(OfficeImoRoot, "OfficeIMO.MarkdownRenderer"));
            File.WriteAllText(Path.Combine(OfficeImoRoot, "OfficeIMO.MarkdownRenderer", "OfficeIMO.MarkdownRenderer.csproj"), "<Project />");
        }

        public void Dispose() {
            try {
                if (Directory.Exists(RootPath)) {
                    Directory.Delete(RootPath, recursive: true);
                }
            } catch {
                // Best effort temp cleanup.
            }
        }
    }
}
