using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Locates the packaged chat service and its optional runtime extension roots.
/// </summary>
internal static class ChatServiceRuntimeLocator {
    private const string ServiceExecutableName = "IntelligenceX.Chat.Service.exe";
    private const string ServiceAssemblyName = "IntelligenceX.Chat.Service.dll";
    private const string ToolAssemblySearchPattern = "IntelligenceX.Tools.*.dll";
    private const string ToolContractAssemblyName = "IntelligenceX.Tools";
    private const string ToolCommonAssemblyName = "IntelligenceX.Tools.Common";

    /// <summary>
    /// Selects the newest valid packaged service directory near the desktop application.
    /// </summary>
    public static string? ResolveSourceDirectory(string? appBaseDirectory = null) {
        var baseDirectory = NormalizeBaseDirectory(appBaseDirectory);
        var bestDirectory = string.Empty;
        var bestWriteTicks = long.MinValue;

        TryPick(Path.Combine(baseDirectory, "service"), ref bestDirectory, ref bestWriteTicks);
        TryPick(Path.GetFullPath(Path.Combine(baseDirectory, "..", "service")), ref bestDirectory, ref bestWriteTicks);

        return bestDirectory.Length == 0 ? null : bestDirectory;
    }

    /// <summary>
    /// Resolves plugin roots that should be supplied to the local service.
    /// </summary>
    public static IReadOnlyList<string> ResolvePluginPaths(string? serviceSourceDirectory, string? appBaseDirectory = null) {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(serviceSourceDirectory)) {
            try {
                var normalizedSourceDirectory = Path.GetFullPath(serviceSourceDirectory);
                var sourceParent = Path.GetDirectoryName(normalizedSourceDirectory);
                if (!string.IsNullOrWhiteSpace(sourceParent)) {
                    TryAddExistingDirectory(paths, seen, Path.Combine(sourceParent, "plugins"));
                }
            } catch {
                // Ignore malformed source paths and retain the app-local fallback.
            }
        }

        TryAddExistingDirectory(paths, seen, Path.Combine(NormalizeBaseDirectory(appBaseDirectory), "plugins"));
        return paths;
    }

    /// <summary>
    /// Resolves built-in tool assembly probe roots for the local service.
    /// </summary>
    public static IReadOnlyList<string> ResolveBuiltInToolProbePaths(string? serviceSourceDirectory) {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryAddBuiltInToolDirectory(paths, seen, serviceSourceDirectory);
        if (!string.IsNullOrWhiteSpace(serviceSourceDirectory)) {
            try {
                TryAddBuiltInToolDirectory(paths, seen, Path.Combine(Path.GetFullPath(serviceSourceDirectory), "tools"));
            } catch {
                // Ignore malformed source paths and keep any valid source root.
            }
        }

        return paths;
    }

    /// <summary>
    /// Determines whether workspace output probing is needed when no packaged probe root exists.
    /// </summary>
    public static bool ShouldEnableWorkspaceBuiltInToolOutputProbing(IReadOnlyCollection<string>? probePaths) =>
        probePaths is null || probePaths.Count == 0;

    /// <summary>
    /// Determines whether a directory contains a launchable chat service payload.
    /// </summary>
    public static bool HasServicePayload(string? directory) {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) {
            return false;
        }

        return File.Exists(Path.Combine(directory, ServiceExecutableName))
               || File.Exists(Path.Combine(directory, ServiceAssemblyName));
    }

    private static string NormalizeBaseDirectory(string? appBaseDirectory) {
        var candidate = string.IsNullOrWhiteSpace(appBaseDirectory) ? AppContext.BaseDirectory : appBaseDirectory;
        return Path.GetFullPath(candidate!);
    }

    private static void TryPick(string directory, ref string bestDirectory, ref long bestWriteTicks) {
        if (!HasServicePayload(directory)) {
            return;
        }

        var executablePath = Path.Combine(directory, ServiceExecutableName);
        var assemblyPath = Path.Combine(directory, ServiceAssemblyName);
        var marker = File.Exists(assemblyPath) ? assemblyPath : executablePath;
        long writeTicks;
        try {
            writeTicks = File.GetLastWriteTimeUtc(marker).Ticks;
        } catch {
            writeTicks = long.MinValue;
        }

        if (writeTicks <= bestWriteTicks) {
            return;
        }

        bestWriteTicks = writeTicks;
        bestDirectory = Path.GetFullPath(directory);
    }

    private static void TryAddExistingDirectory(List<string> paths, HashSet<string> seen, string? candidate) {
        if (string.IsNullOrWhiteSpace(candidate)) {
            return;
        }

        string fullPath;
        try {
            fullPath = Path.GetFullPath(candidate);
        } catch {
            return;
        }

        if (Directory.Exists(fullPath) && seen.Add(fullPath)) {
            paths.Add(fullPath);
        }
    }

    private static void TryAddBuiltInToolDirectory(List<string> paths, HashSet<string> seen, string? candidate) {
        if (string.IsNullOrWhiteSpace(candidate)) {
            return;
        }

        string fullPath;
        try {
            fullPath = Path.GetFullPath(candidate);
        } catch {
            return;
        }

        if (!Directory.Exists(fullPath) || !ContainsBuiltInToolPackAssembly(fullPath) || !seen.Add(fullPath)) {
            return;
        }

        paths.Add(fullPath);
    }

    private static bool ContainsBuiltInToolPackAssembly(string directory) {
        try {
            foreach (var candidate in Directory.EnumerateFiles(directory, ToolAssemblySearchPattern, SearchOption.TopDirectoryOnly)) {
                var assemblyName = Path.GetFileNameWithoutExtension(candidate);
                if (!string.Equals(assemblyName, ToolContractAssemblyName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(assemblyName, ToolCommonAssemblyName, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        } catch {
            // Treat inaccessible or unstable payload roots as unavailable.
        }

        return false;
    }
}
