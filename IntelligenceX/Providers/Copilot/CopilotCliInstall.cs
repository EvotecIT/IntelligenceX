using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Copilot;

/// <summary>
/// Describes the installation method for the Copilot CLI.
/// </summary>
public enum CopilotCliInstallMethod {
    /// <summary>Automatically choose the best method for the platform.</summary>
    Auto,
    /// <summary>Install via WinGet.</summary>
    Winget,
    /// <summary>Install via Homebrew.</summary>
    Homebrew,
    /// <summary>Install via npm.</summary>
    Npm,
    /// <summary>Install via the official shell script.</summary>
    Script
}

/// <summary>
/// Represents a single installation command for the Copilot CLI.
/// </summary>
public sealed class CopilotCliInstallCommand {
    /// <summary>Creates a new install command descriptor.</summary>
    /// <param name="method">The installation method.</param>
    /// <param name="fileName">The executable name.</param>
    /// <param name="arguments">The arguments to pass.</param>
    /// <param name="description">A human-friendly description.</param>
    public CopilotCliInstallCommand(CopilotCliInstallMethod method, string fileName, string arguments, string description) {
        Method = method;
        FileName = fileName;
        Arguments = arguments;
        Description = description;
    }

    /// <summary>Gets the installation method.</summary>
    public CopilotCliInstallMethod Method { get; }
    /// <summary>Gets the executable name to run.</summary>
    public string FileName { get; }
    /// <summary>Gets the arguments passed to the executable.</summary>
    public string Arguments { get; }
    /// <summary>Gets the display description for this command.</summary>
    public string Description { get; }
}

/// <summary>
/// Provides helpers for installing the Copilot CLI.
/// </summary>
public static class CopilotCliInstall {
    /// <summary>Gets all recommended install commands for the current platform.</summary>
    /// <param name="prerelease">Whether to use prerelease packages.</param>
    public static IReadOnlyList<CopilotCliInstallCommand> GetInstallCommands(bool prerelease = false) {
        var list = new List<CopilotCliInstallCommand>();
        if (IsWindows()) {
            list.Add(new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Winget,
                "winget",
                prerelease ? "install GitHub.Copilot.Prerelease" : "install GitHub.Copilot",
                "WinGet"));
            list.Add(Npm(prerelease));
        } else if (IsMac() || IsLinux()) {
            list.Add(new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Homebrew,
                "brew",
                prerelease ? "install copilot-cli@prerelease" : "install copilot-cli",
                "Homebrew"));
            list.Add(Npm(prerelease));
            list.Add(new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Script,
                "bash",
                "-c \"curl -fsSL https://gh.io/copilot-install | bash\"",
                "Install script (macOS/Linux)"));
        } else {
            list.Add(Npm(prerelease));
        }
        return list;
    }

    /// <summary>Gets the default install command for the current platform.</summary>
    /// <param name="prerelease">Whether to use prerelease packages.</param>
    public static CopilotCliInstallCommand GetDefaultCommand(bool prerelease = false) {
        if (IsWindows()) {
            return new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Winget,
                "winget",
                prerelease ? "install GitHub.Copilot.Prerelease" : "install GitHub.Copilot",
                "WinGet");
        }
        if (IsMac() || IsLinux()) {
            return new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Homebrew,
                "brew",
                prerelease ? "install copilot-cli@prerelease" : "install copilot-cli",
                "Homebrew");
        }
        return Npm(prerelease);
    }

    /// <summary>Gets the install command for the specified method.</summary>
    /// <param name="method">The installation method.</param>
    /// <param name="prerelease">Whether to use prerelease packages.</param>
    public static CopilotCliInstallCommand GetCommand(CopilotCliInstallMethod method, bool prerelease = false) {
        if (method == CopilotCliInstallMethod.Auto) {
            return GetDefaultCommand(prerelease);
        }
        return method switch {
            CopilotCliInstallMethod.Winget => new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Winget,
                "winget",
                prerelease ? "install GitHub.Copilot.Prerelease" : "install GitHub.Copilot",
                "WinGet"),
            CopilotCliInstallMethod.Homebrew => new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Homebrew,
                "brew",
                prerelease ? "install copilot-cli@prerelease" : "install copilot-cli",
                "Homebrew"),
            CopilotCliInstallMethod.Npm => Npm(prerelease),
            CopilotCliInstallMethod.Script => new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Script,
                "bash",
                "-c \"curl -fsSL https://gh.io/copilot-install | bash\"",
                "Install script (macOS/Linux)"),
            _ => GetDefaultCommand(prerelease)
        };
    }

    /// <summary>Runs the install command and returns the exit code.</summary>
    /// <param name="command">The install command to run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<int> InstallAsync(CopilotCliInstallCommand command, CancellationToken cancellationToken = default) {
        if (command is null) {
            throw new ArgumentNullException(nameof(command));
        }
        var startInfo = new ProcessStartInfo {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) {
            throw new InvalidOperationException("Failed to start installer process.");
        }
        while (!process.HasExited) {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
        return process.ExitCode;
    }

    /// <summary>Builds a human-friendly string listing available install commands.</summary>
    /// <param name="prerelease">Whether to use prerelease packages.</param>
    public static string GetInstallInstructions(bool prerelease = false) {
        var commands = GetInstallCommands(prerelease);
        var lines = new List<string>();
        foreach (var cmd in commands) {
            lines.Add($"{cmd.Description}: {cmd.FileName} {cmd.Arguments}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static CopilotCliInstallCommand Npm(bool prerelease) {
        return new CopilotCliInstallCommand(
            CopilotCliInstallMethod.Npm,
            "npm",
            prerelease ? "install -g @github/copilot@prerelease" : "install -g @github/copilot",
            "npm (Node.js 22+)");
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool IsMac() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
