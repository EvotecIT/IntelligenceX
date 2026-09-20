using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace IntelligenceX.Desktop;

/// <summary>The two native desktop surfaces in a paired IntelligenceX bundle.</summary>
public enum DesktopCompanion {
    /// <summary>Interactive conversations and tools.</summary>
    Chat,
    /// <summary>Background usage and account monitoring.</summary>
    Tray
}

/// <summary>Launches a companion from the current bundle, without searching PATH or unrelated installations.</summary>
public static class DesktopCompanionLauncher {
    /// <summary>
    /// Resolves a same-directory executable or a paired Chat/Tray subdirectory.
    /// The executable handles activation of an existing instance; no credentials or profile are passed.
    /// </summary>
    public static string? ResolveExecutable(string baseDirectory, DesktopCompanion companion) {
        if (string.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("A bundle directory is required.", nameof(baseDirectory));
        if (!Enum.IsDefined(typeof(DesktopCompanion), companion)) throw new ArgumentOutOfRangeException(nameof(companion));
        var directory = Path.GetFullPath(baseDirectory);
        if (directory.Length > (Path.GetPathRoot(directory)?.Length ?? 0)) {
            directory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        var file = companion == DesktopCompanion.Chat ? "IntelligenceX.Chat.App.exe" : "IntelligenceX.Tray.exe";
        var folder = companion == DesktopCompanion.Chat ? "Chat" : "Tray";
        var direct = Path.Combine(directory, file);
        if (File.Exists(direct)) return direct;
        var child = Path.Combine(directory, folder, file);
        if (File.Exists(child)) return child;
        var currentFolder = Path.GetFileName(directory);
        if (string.Equals(currentFolder, "Chat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentFolder, "Tray", StringComparison.OrdinalIgnoreCase)) {
            var parent = Path.GetDirectoryName(directory);
            if (parent is not null) {
                var sibling = Path.Combine(parent, folder, file);
                if (File.Exists(sibling)) return sibling;
            }
        }
        return null;
    }

    /// <summary>Starts the paired app with an explicit open request; returns false only when it is absent.</summary>
    public static bool TryOpen(DesktopCompanion companion) {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("Native companions require Windows.");
        var executable = ResolveExecutable(AppContext.BaseDirectory, companion);
        if (executable is null) return false;
        using var process = Process.Start(new ProcessStartInfo {
            FileName = executable,
            Arguments = "--open",
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        });
        if (process is null) throw new InvalidOperationException("The companion app could not be started.");
        return true;
    }
}
