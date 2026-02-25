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
        return WorkspaceContainsAnySourceFile(workspace, out _, extensions);
    }

    private static bool WorkspaceContainsAnySourceFile(string workspace, out int skippedEnumerations, params string[] extensions) {
        skippedEnumerations = 0;
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

        var pending = new Stack<string>();
        pending.Push(workspace);

        while (pending.Count > 0) {
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
                if (IsPathUnderExcludedDirectorySegment(workspace, path)) {
                    continue;
                }

                var extension = Path.GetExtension(path);
                if (extensionSet.Contains(extension)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsExcludedDirectoryName(string path) {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
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
