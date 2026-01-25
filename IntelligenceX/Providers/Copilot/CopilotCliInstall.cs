using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Copilot;

public enum CopilotCliInstallMethod {
    Auto,
    Winget,
    Homebrew,
    Npm,
    Script
}

public sealed class CopilotCliInstallCommand {
    public CopilotCliInstallCommand(CopilotCliInstallMethod method, string fileName, string arguments, string description) {
        Method = method;
        FileName = fileName;
        Arguments = arguments;
        Description = description;
    }

    public CopilotCliInstallMethod Method { get; }
    public string FileName { get; }
    public string Arguments { get; }
    public string Description { get; }
}

public static class CopilotCliInstall {
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
