using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Copilot;

/// <summary>
/// Supported Copilot CLI install methods.
/// </summary>
public enum CopilotCliInstallMethod {
    /// <summary>
    /// Automatically choose the best method for the OS.
    /// </summary>
    Auto,
    /// <summary>
    /// Install via WinGet.
    /// </summary>
    Winget,
    /// <summary>
    /// Install via Homebrew.
    /// </summary>
    Homebrew,
    /// <summary>
    /// Install via npm.
    /// </summary>
    Npm,
    /// <summary>
    /// Install via script.
    /// </summary>
    Script
}

/// <summary>
/// Describes a CLI install command.
/// </summary>
public sealed class CopilotCliInstallCommand {
    /// <summary>
    /// Initializes a new install command descriptor.
    /// </summary>
    public CopilotCliInstallCommand(CopilotCliInstallMethod method, string fileName, string arguments, string description) {
        Method = method;
        FileName = fileName;
        Arguments = arguments;
        Description = description;
    }

    /// <summary>
    /// Gets the install method.
    /// </summary>
    public CopilotCliInstallMethod Method { get; }
    /// <summary>
    /// Gets the executable name.
    /// </summary>
    public string FileName { get; }
    /// <summary>
    /// Gets the command arguments.
    /// </summary>
    public string Arguments { get; }
    /// <summary>
    /// Gets the human-readable description.
    /// </summary>
    public string Description { get; }
}

/// <summary>
/// Utilities for installing the Copilot CLI.
/// </summary>
public static class CopilotCliInstall {
    /// <summary>
    /// Returns install commands suitable for the current OS.
    /// </summary>
    /// <param name="prerelease">Whether to use prerelease versions.</param>
    public static IReadOnlyList<CopilotCliInstallCommand> GetInstallCommands(bool prerelease = false) {
        var list = new List<CopilotCliInstallCommand>();
        if (IsWindows()) {
            list.Add(new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Winget,
                "winget",
                prerelease ? "install GitHub.Copilot.Prerelease" : "install GitHub.Copilot",
                "WinGet"));
            list.Add(Npm(prerelease));
        } else if (IsMac()) {
            list.Add(new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Homebrew,
                "brew",
                prerelease ? "install copilot-cli@prerelease" : "install copilot-cli",
                "Homebrew"));
            list.Add(Npm(prerelease));
            list.Add(Script());
        } else if (IsLinux()) {
            if (prerelease) {
                list.Add(Npm(prerelease));
            } else {
                list.Add(Script());
                list.Add(Npm(prerelease));
            }
            list.Add(new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Homebrew,
                "brew",
                prerelease ? "install copilot-cli@prerelease" : "install copilot-cli",
                "Homebrew"));
        } else {
            list.Add(Npm(prerelease));
        }
        return list;
    }

    /// <summary>
    /// Returns the default install command for the current OS.
    /// </summary>
    /// <param name="prerelease">Whether to use prerelease versions.</param>
    public static CopilotCliInstallCommand GetDefaultCommand(bool prerelease = false) {
        return GetDefaultCommandForPlatform(IsWindows(), IsMac(), IsLinux(), prerelease);
    }

    internal static CopilotCliInstallCommand GetDefaultCommandForPlatform(bool isWindows, bool isMac, bool isLinux,
        bool prerelease = false) {
        if (isWindows) {
            return new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Winget,
                "winget",
                prerelease ? "install GitHub.Copilot.Prerelease" : "install GitHub.Copilot",
                "WinGet");
        }
        if (isMac) {
            return new CopilotCliInstallCommand(
                CopilotCliInstallMethod.Homebrew,
                "brew",
                prerelease ? "install copilot-cli@prerelease" : "install copilot-cli",
                "Homebrew");
        }
        if (isLinux) {
            return prerelease ? Npm(prerelease) : Script();
        }
        return Npm(prerelease);
    }

    /// <summary>
    /// Returns a specific install command.
    /// </summary>
    /// <param name="method">Install method.</param>
    /// <param name="prerelease">Whether to use prerelease versions.</param>
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
            CopilotCliInstallMethod.Script => Script(),
            _ => GetDefaultCommand(prerelease)
        };
    }

    /// <summary>
    /// Executes the provided install command.
    /// </summary>
    /// <param name="command">Install command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    /// <summary>
    /// Returns human-readable install instructions.
    /// </summary>
    /// <param name="prerelease">Whether to use prerelease versions.</param>
    public static string GetInstallInstructions(bool prerelease = false) {
        var commands = GetInstallCommands(prerelease);
        var lines = new List<string>();
        foreach (var cmd in commands) {
            lines.Add($"{cmd.Description}: {cmd.FileName} {cmd.Arguments}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Attempts to resolve a Copilot CLI binary from common install locations even when PATH is stale.
    /// </summary>
    /// <param name="cliPath">CLI executable name or path.</param>
    /// <returns>The resolved absolute path when found; otherwise <c>null</c>.</returns>
    public static string? TryResolveInstalledCliPath(string cliPath) {
        if (string.IsNullOrWhiteSpace(cliPath)) {
            return null;
        }

        if (Path.IsPathRooted(cliPath) ||
            cliPath.Contains(Path.DirectorySeparatorChar) ||
            cliPath.Contains(Path.AltDirectorySeparatorChar)) {
            return File.Exists(cliPath) ? cliPath : null;
        }

        var home = GetPreferredHomeDirectory();
        foreach (var directory in GetKnownInstallDirectories(home)) {
            if (string.IsNullOrWhiteSpace(directory)) {
                continue;
            }

            foreach (var candidate in ExpandCandidates(directory, cliPath)) {
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static CopilotCliInstallCommand Npm(bool prerelease) {
        return new CopilotCliInstallCommand(
            CopilotCliInstallMethod.Npm,
            "npm",
            prerelease ? "install -g @github/copilot@prerelease" : "install -g @github/copilot",
            "npm (Node.js 22+)");
    }

    private static CopilotCliInstallCommand Script() {
        return new CopilotCliInstallCommand(
            CopilotCliInstallMethod.Script,
            "bash",
            "-c \"curl -fsSL https://gh.io/copilot-install | bash\"",
            "Install script (macOS/Linux)");
    }

    private static string? GetPreferredHomeDirectory() {
        var envHome = Environment.GetEnvironmentVariable(IsWindows() ? "USERPROFILE" : "HOME");
        if (!string.IsNullOrWhiteSpace(envHome)) {
            return envHome;
        }

        var alternateEnvHome = Environment.GetEnvironmentVariable(IsWindows() ? "HOME" : "USERPROFILE");
        if (!string.IsNullOrWhiteSpace(alternateEnvHome)) {
            return alternateEnvHome;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static IEnumerable<string> GetKnownInstallDirectories(string? home) {
        if (!string.IsNullOrWhiteSpace(home)) {
            if (IsLinux() || IsMac()) {
                yield return Path.Combine(home!, ".local", "bin");
                yield return Path.Combine(home!, ".npm-global", "bin");
                yield return Path.Combine(home!, ".yarn", "bin");
                yield return Path.Combine(home!, ".config", "yarn", "global", "node_modules", ".bin");
            }

            if (IsWindows()) {
                yield return Path.Combine(home!, "AppData", "Local", "Programs", "GitHub Copilot");
            }
        }

        if (IsMac()) {
            yield return "/opt/homebrew/bin";
        }

        if (IsLinux()) {
            yield return "/home/linuxbrew/.linuxbrew/bin";
        }

        yield return "/usr/local/bin";
        yield return "/usr/bin";
    }

    private static IEnumerable<string> ExpandCandidates(string directory, string cliPath) {
        yield return Path.Combine(directory, cliPath);

        if (IsWindows()) {
            yield return Path.Combine(directory, cliPath + ".exe");
            yield return Path.Combine(directory, cliPath + ".cmd");
            yield return Path.Combine(directory, cliPath + ".bat");
        }
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool IsMac() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
