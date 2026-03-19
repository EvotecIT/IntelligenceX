using System.Diagnostics;
using Microsoft.Win32;

namespace IntelligenceX.Tray.Services;

public sealed class WindowsStartupRegistrationService {
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "IntelligenceX Tray";

    public bool IsEnabled() {
        if (!OperatingSystem.IsWindows()) {
            return false;
        }

        try {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = runKey?.GetValue(EntryName) as string;
            return CommandTargetsProcessPath(value, ResolveProcessPath());
        } catch {
            return false;
        }
    }

    public bool SetEnabled(bool enabled) {
        if (!OperatingSystem.IsWindows()) {
            return false;
        }

        try {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (runKey is null) {
                return false;
            }

            if (enabled) {
                runKey.SetValue(EntryName, BuildCommand(), RegistryValueKind.String);
            } else {
                runKey.DeleteValue(EntryName, throwOnMissingValue: false);
            }

            return true;
        } catch {
            return false;
        }
    }

    private static string BuildCommand() {
        return "\"" + ResolveProcessPath() + "\"";
    }

    private static string ResolveProcessPath() {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) {
            processPath = Process.GetCurrentProcess().MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(processPath)) {
            throw new InvalidOperationException("Unable to resolve the current tray executable path for startup registration.");
        }

        return processPath;
    }

    internal static bool CommandTargetsProcessPath(string? commandValue, string processPath) {
        if (string.IsNullOrWhiteSpace(processPath)) {
            return false;
        }

        var trimmed = commandValue?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return false;
        }

        var quotedProcessPath = "\"" + processPath + "\"";
        return StartsWithCommandPath(trimmed, quotedProcessPath)
               || StartsWithCommandPath(trimmed, processPath);
    }

    private static bool StartsWithCommandPath(string commandValue, string candidatePath) {
        if (!commandValue.StartsWith(candidatePath, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return commandValue.Length == candidatePath.Length
               || char.IsWhiteSpace(commandValue[candidatePath.Length]);
    }
}
