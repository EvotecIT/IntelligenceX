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
            return CommandTargetsCurrentProcess(value);
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

    private static bool CommandTargetsCurrentProcess(string? commandValue) {
        var processPath = ResolveProcessPath();
        var candidate = ExtractExecutablePath(commandValue);
        return !string.IsNullOrWhiteSpace(candidate)
               && string.Equals(candidate, processPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractExecutablePath(string? commandValue) {
        var trimmed = commandValue?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return null;
        }

        if (trimmed[0] == '"') {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            if (closingQuoteIndex > 1) {
                return trimmed[1..closingQuoteIndex];
            }

            return trimmed.Trim('"');
        }

        var separatorIndex = trimmed.IndexOf(' ');
        return separatorIndex > 0 ? trimmed[..separatorIndex] : trimmed;
    }
}
