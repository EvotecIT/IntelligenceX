using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        var entryAssembly = Assembly.GetEntryAssembly();
        var entryAssemblyLocation = entryAssembly?.Location;
        if (!string.IsNullOrWhiteSpace(entryAssemblyLocation)) {
            if (entryAssemblyLocation.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                return entryAssemblyLocation;
            }

            if (entryAssemblyLocation.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) {
                var appHostPath = Path.ChangeExtension(entryAssemblyLocation, ".exe");
                if (File.Exists(appHostPath)) {
                    return appHostPath;
                }
            }
        }

        var entryAssemblyName = entryAssembly?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(entryAssemblyName) && !string.IsNullOrWhiteSpace(AppContext.BaseDirectory)) {
            var appHostPath = Path.Combine(AppContext.BaseDirectory, entryAssemblyName + ".exe");
            if (File.Exists(appHostPath)) {
                return appHostPath;
            }
        }

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
        if (trimmed.StartsWith(quotedProcessPath, StringComparison.OrdinalIgnoreCase)) {
            return trimmed.Length == quotedProcessPath.Length
                   || char.IsWhiteSpace(trimmed[quotedProcessPath.Length]);
        }

        if (!trimmed.StartsWith(processPath, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return trimmed.Length == processPath.Length
               || char.IsWhiteSpace(trimmed[processPath.Length]);
    }
}
