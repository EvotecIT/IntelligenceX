using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private const string CommandUnavailableMarkersEnvVar = "INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS";
    private const string CommandUnavailableMarkersEnvVarPrefix = "INTELLIGENCEX_ANALYSIS_COMMAND_UNAVAILABLE_MARKERS_";
    private const string SourceInventoryMaxFilesEnvVar = "INTELLIGENCEX_ANALYSIS_SOURCE_SCAN_MAX_FILES";
    private const int DefaultSourceInventoryMaxFiles = 200000;
    private static readonly string[] CommonCommandUnavailableMarkers = {
        "not recognized as an internal or external command",
        "is not recognized as an internal or external command",
        "no such file or directory",
        "cannot find the file",
        "the system cannot find the file specified",
        "command not found",
        "could not determine executable to run"
    };
    private static readonly Dictionary<string, string[]> CommandUnavailableMarkersByTool =
        new(StringComparer.OrdinalIgnoreCase) {
            ["npx"] = new[] {
                "npm err! could not determine executable to run"
            },
            ["ruff"] = new[] {
                "no module named ruff"
            }
        };
    private static readonly object CommandUnavailableMarkerCacheLock = new();
    private static readonly Dictionary<string, (string RawValue, IReadOnlyList<string> Markers)> CommandUnavailableMarkerCache =
        new(StringComparer.Ordinal);
    private static readonly char[] PathSeparators = {
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    };

    private sealed class WorkspaceSourceInventory {
        public WorkspaceSourceInventory(HashSet<string> extensions, int skippedEnumerations, bool scanLimitReached, int maxScannedFiles) {
            Extensions = extensions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SkippedEnumerations = Math.Max(0, skippedEnumerations);
            ScanLimitReached = scanLimitReached;
            MaxScannedFiles = Math.Max(0, maxScannedFiles);
        }

        public HashSet<string> Extensions { get; }
        public int SkippedEnumerations { get; }
        public bool ScanLimitReached { get; }
        public int MaxScannedFiles { get; }
    }

    private static string BuildExternalRunnerFailureMessage(string languageLabel, string command, string optionName, CommandResult result) {
        if (IsCommandUnavailable(command, result)) {
            return $"{languageLabel} analysis command '{command}' is unavailable (exit code {result.ExitCode}). " +
                   $"Install/configure the tool or override it with {optionName}.";
        }

        return $"{languageLabel} analysis returned exit code {result.ExitCode}.";
    }

    private static bool IsCommandUnavailable(string command, CommandResult result) {
        if (result.ExitCode == 127) {
            return true;
        }

        var text = ((result.StdErr ?? string.Empty) + "\n" + (result.StdOut ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        return ContainsAnyCommandUnavailableMarker(command, text);
    }

    private static bool ContainsAnyCommandUnavailableMarker(string command, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        foreach (var marker in CommonCommandUnavailableMarkers) {
            if (text.Contains(marker, StringComparison.Ordinal)) {
                return true;
            }
        }

        if (TryGetCommandUnavailableMarkers(command, out var commandSpecificMarkers)) {
            foreach (var marker in commandSpecificMarkers) {
                if (text.Contains(marker, StringComparison.Ordinal)) {
                    return true;
                }
            }
        }

        if (ContainsAnyConfiguredCommandUnavailableMarker(command, text)) {
            return true;
        }

        return false;
    }

    private static bool TryGetCommandUnavailableMarkers(string command, out IReadOnlyList<string> markers) {
        markers = Array.Empty<string>();
        var commandKey = ResolveCommandKey(command);
        if (string.IsNullOrWhiteSpace(commandKey)) {
            return false;
        }

        if (!CommandUnavailableMarkersByTool.TryGetValue(commandKey, out var configuredMarkers) ||
            configuredMarkers is null ||
            configuredMarkers.Length == 0) {
            return false;
        }

        markers = configuredMarkers;
        return true;
    }

    private static string ResolveCommandKey(string command) {
        if (string.IsNullOrWhiteSpace(command)) {
            return string.Empty;
        }

        var trimmed = command.Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }

        var firstToken = trimmed.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var unquoted = firstToken.Trim('\"', '\'');
        if (string.IsNullOrWhiteSpace(unquoted)) {
            return string.Empty;
        }

        var fileName = Path.GetFileName(unquoted);
        if (string.IsNullOrWhiteSpace(fileName)) {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(fileName).Trim().ToLowerInvariant();
    }

    private static bool ContainsAnyConfiguredCommandUnavailableMarker(string command, string text) {
        var globalMarkers = GetCommandUnavailableMarkersFromEnvironment(CommandUnavailableMarkersEnvVar);
        if (ContainsAnyMarker(text, globalMarkers)) {
            return true;
        }

        var commandKey = ResolveCommandKey(command);
        if (string.IsNullOrWhiteSpace(commandKey)) {
            return false;
        }

        var envName = BuildCommandUnavailableMarkersEnvVarName(commandKey);
        if (string.IsNullOrWhiteSpace(envName)) {
            return false;
        }

        var commandMarkers = GetCommandUnavailableMarkersFromEnvironment(envName);
        return ContainsAnyMarker(text, commandMarkers);
    }

    private static string BuildCommandUnavailableMarkersEnvVarName(string commandKey) {
        if (string.IsNullOrWhiteSpace(commandKey)) {
            return string.Empty;
        }

        var chars = commandKey
            .ToUpperInvariant()
            .Select(static c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var normalized = new string(chars).Trim('_');
        if (string.IsNullOrWhiteSpace(normalized)) {
            return string.Empty;
        }

        return CommandUnavailableMarkersEnvVarPrefix + normalized;
    }

    private static IReadOnlyList<string> GetCommandUnavailableMarkersFromEnvironment(string variableName) {
        if (string.IsNullOrWhiteSpace(variableName)) {
            return Array.Empty<string>();
        }

        var raw = Environment.GetEnvironmentVariable(variableName) ?? string.Empty;
        lock (CommandUnavailableMarkerCacheLock) {
            if (CommandUnavailableMarkerCache.TryGetValue(variableName, out var cached) &&
                string.Equals(cached.RawValue, raw, StringComparison.Ordinal)) {
                return cached.Markers;
            }

            var parsed = ParseCommandUnavailableMarkers(raw);
            CommandUnavailableMarkerCache[variableName] = (raw, parsed);
            return parsed;
        }
    }

    private static IReadOnlyList<string> ParseCommandUnavailableMarkers(string raw) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<string>();
        var parts = raw.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            var normalized = (part ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) {
                continue;
            }
            parsed.Add(normalized);
        }

        return parsed;
    }

    private static bool ContainsAnyMarker(string text, IReadOnlyList<string> markers) {
        if (string.IsNullOrWhiteSpace(text) || markers is null || markers.Count == 0) {
            return false;
        }

        foreach (var marker in markers) {
            if (text.Contains(marker, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static int ResolveSourceInventoryMaxFiles() {
        var raw = Environment.GetEnvironmentVariable(SourceInventoryMaxFilesEnvVar);
        if (string.IsNullOrWhiteSpace(raw)) {
            return DefaultSourceInventoryMaxFiles;
        }

        if (!int.TryParse(raw.Trim(), out var parsedValue)) {
            return DefaultSourceInventoryMaxFiles;
        }

        return Math.Max(0, parsedValue);
    }

    private static bool WorkspaceContainsAnySourceFile(string workspace, params string[] extensions) {
        return WorkspaceContainsAnySourceFile(workspace, out _, extensions);
    }

    private static bool WorkspaceContainsAnySourceFile(string workspace, out int skippedEnumerations, params string[] extensions) {
        var sourceInventory = DiscoverWorkspaceSourceInventory(workspace);
        return WorkspaceContainsAnySourceFile(sourceInventory, out skippedEnumerations, extensions);
    }

    private static bool WorkspaceContainsAnySourceFileWithoutScanLimit(string workspace, out int skippedEnumerations,
        params string[] extensions) {
        var sourceInventory = DiscoverWorkspaceSourceInventory(workspace, maxScannedFiles: int.MaxValue);
        return WorkspaceContainsAnySourceFile(sourceInventory, out skippedEnumerations, extensions);
    }

    private static bool WorkspaceContainsAnySourceFile(
        WorkspaceSourceInventory? sourceInventory,
        out int skippedEnumerations,
        params string[] extensions) {
        return WorkspaceContainsAnySourceFile(sourceInventory, out skippedEnumerations, out _, extensions);
    }

    private static bool WorkspaceContainsAnySourceFile(
        WorkspaceSourceInventory? sourceInventory,
        out int skippedEnumerations,
        out bool scanLimitReached,
        params string[] extensions) {
        skippedEnumerations = sourceInventory?.SkippedEnumerations ?? 0;
        scanLimitReached = sourceInventory?.ScanLimitReached ?? false;
        if (sourceInventory is null) {
            return false;
        }

        var extensionSet = NormalizeSourceExtensions(extensions);
        if (extensionSet.Count == 0) {
            return false;
        }

        foreach (var extension in extensionSet) {
            if (sourceInventory.Extensions.Contains(extension)) {
                return true;
            }
        }

        return false;
    }

    private static WorkspaceSourceInventory? DiscoverWorkspaceSourceInventory(string workspace) {
        return DiscoverWorkspaceSourceInventory(workspace, ResolveSourceInventoryMaxFiles());
    }

    private static WorkspaceSourceInventory? DiscoverWorkspaceSourceInventory(string workspace, int maxScannedFiles) {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) {
            return null;
        }

        var skippedEnumerations = 0;
        var discoveredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        maxScannedFiles = Math.Max(0, maxScannedFiles);
        var scanLimitReached = false;
        var scannedFiles = 0;
        if (maxScannedFiles == 0) {
            return new WorkspaceSourceInventory(discoveredExtensions, skippedEnumerations, scanLimitReached: true, maxScannedFiles);
        }
        var pending = new Stack<string>();
        pending.Push(workspace);

        while (pending.Count > 0 && !scanLimitReached) {
            var directory = pending.Pop();

            IEnumerable<string>? subdirectories = null;
            try {
                subdirectories = Directory.EnumerateDirectories(directory);
            } catch (IOException) {
                skippedEnumerations++;
            } catch (UnauthorizedAccessException) {
                skippedEnumerations++;
            }

            if (subdirectories is not null) {
                foreach (var subdirectory in subdirectories) {
                    if (IsExcludedDirectoryName(subdirectory) ||
                        IsPathUnderExcludedDirectorySegment(workspace, subdirectory)) {
                        continue;
                    }
                    pending.Push(subdirectory);
                }
            }

            IEnumerable<string>? files = null;
            try {
                files = Directory.EnumerateFiles(directory);
            } catch (IOException) {
                skippedEnumerations++;
            } catch (UnauthorizedAccessException) {
                skippedEnumerations++;
            }

            if (files is null) {
                continue;
            }

            foreach (var path in files) {
                if (scannedFiles >= maxScannedFiles) {
                    scanLimitReached = true;
                    break;
                }
                scannedFiles++;

                if (IsPathUnderExcludedDirectorySegment(workspace, path)) {
                    continue;
                }

                var extension = Path.GetExtension(path);
                if (SourceLanguageConventions.IsTrackedSourceExtension(extension)) {
                    discoveredExtensions.Add(extension);
                }
            }
        }

        return new WorkspaceSourceInventory(discoveredExtensions, skippedEnumerations, scanLimitReached, maxScannedFiles);
    }

    private static HashSet<string> NormalizeSourceExtensions(params string[] extensions) {
        var extensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensions ?? Array.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(extension)) {
                continue;
            }

            var normalized = extension.Trim();
            if (!normalized.StartsWith(".", StringComparison.Ordinal)) {
                normalized = "." + normalized;
            }
            extensionSet.Add(normalized);
        }
        return extensionSet;
    }

    private static bool IsExcludedDirectoryName(string path) {
        var name = Path.GetFileName(path.TrimEnd(PathSeparators));
        if (string.IsNullOrWhiteSpace(name)) {
            return false;
        }

        foreach (var excluded in DefaultExcludedDirectorySegments) {
            if (name.Equals(excluded, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathUnderExcludedDirectorySegment(string workspace, string path) {
        try {
            var relative = Path.GetRelativePath(workspace, path);
            if (string.IsNullOrWhiteSpace(relative) ||
                relative.StartsWith("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative)) {
                return false;
            }

            var segments = relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 1) {
                return false;
            }

            for (var i = 0; i < segments.Length - 1; i++) {
                foreach (var excluded in DefaultExcludedDirectorySegments) {
                    if (segments[i].Equals(excluded, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }
            }
        } catch (ArgumentException) {
            return false;
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        } catch (NotSupportedException) {
            return false;
        }

        return false;
    }
}
