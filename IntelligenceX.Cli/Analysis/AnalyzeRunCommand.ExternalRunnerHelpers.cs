using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static string BuildExternalRunnerFailureMessage(string languageLabel, string command, string optionName, CommandResult result) {
        if (IsCommandUnavailable(result)) {
            return $"{languageLabel} analysis command '{command}' is unavailable (exit code {result.ExitCode}). " +
                   $"Install/configure the tool or override it with {optionName}.";
        }

        return $"{languageLabel} analysis returned exit code {result.ExitCode}.";
    }

    private static bool IsCommandUnavailable(CommandResult result) {
        if (result.ExitCode == 127) {
            return true;
        }

        var text = ((result.StdErr ?? string.Empty) + "\n" + (result.StdOut ?? string.Empty)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        return text.Contains("not recognized as an internal or external command", StringComparison.Ordinal) ||
               text.Contains("is not recognized as an internal or external command", StringComparison.Ordinal) ||
               text.Contains("no such file or directory", StringComparison.Ordinal) ||
               text.Contains("cannot find the file", StringComparison.Ordinal) ||
               text.Contains("the system cannot find the file specified", StringComparison.Ordinal) ||
               text.Contains("command not found", StringComparison.Ordinal) ||
               text.Contains("could not determine executable to run", StringComparison.Ordinal);
    }

    private static bool WorkspaceContainsAnySourceFile(string workspace, params string[] extensions) {
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) {
            return false;
        }

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
        if (extensionSet.Count == 0) {
            return false;
        }

        try {
            foreach (var path in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)) {
                if (IsPathUnderExcludedDirectorySegment(workspace, path)) {
                    continue;
                }

                var extension = Path.GetExtension(path);
                if (extensionSet.Contains(extension)) {
                    return true;
                }
            }
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
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

            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            var segments = relative.Split(separators, StringSplitOptions.RemoveEmptyEntries);
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
        } catch {
            return false;
        }

        return false;
    }
}
