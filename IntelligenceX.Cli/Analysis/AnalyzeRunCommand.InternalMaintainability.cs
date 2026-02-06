using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static IReadOnlyList<AnalysisFindingItem> RunInternalMaintainabilityChecks(string workspace,
        string outputDirectory,
        IReadOnlyList<AnalysisPolicyRule> rules, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var maxLinesRule = ResolveMaxLinesRule(rules, out var maxLinesLimit);
        if (maxLinesRule is null) {
            return findings;
        }

        var severity = NormalizeSeverity(maxLinesRule.Severity);
        if (string.IsNullOrWhiteSpace(severity)) {
            Console.WriteLine($"Internal maintainability rule {maxLinesRule.Rule.Id} is disabled by policy severity.");
            return findings;
        }
        var generatedSuffixes = ResolveGeneratedSuffixes(maxLinesRule.Rule);
        var generatedHeaderMarkers = ResolveGeneratedHeaderMarkers(maxLinesRule.Rule);
        var excludedOutputPath = TryGetRelativePathWithinWorkspace(workspace, outputDirectory);
        var emittedRuleId = string.IsNullOrWhiteSpace(maxLinesRule.Rule.ToolRuleId)
            ? maxLinesRule.Rule.Id
            : maxLinesRule.Rule.ToolRuleId;
        var emittedTool = string.IsNullOrWhiteSpace(maxLinesRule.Rule.Tool)
            ? InternalToolName
            : maxLinesRule.Rule.Tool;

        var sourceFiles = EnumerateCSharpFiles(workspace, excludedOutputPath, warnings)
            .Select(path => Path.GetFullPath(path))
            .Select(fullPath => new {
                FullPath = fullPath,
                RelativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/')
            })
            .Where(file => !IsExcludedSourceFile(file.FullPath, file.RelativePath, generatedSuffixes, generatedHeaderMarkers,
                excludedOutputPath));

        foreach (var sourceFile in sourceFiles) {
            var relativePath = sourceFile.RelativePath;

            int lineCount;
            try {
                lineCount = CountFileLines(sourceFile.FullPath);
            } catch (Exception ex) {
                warnings.Add($"Failed to read file for line-count check ({relativePath}): {ex.Message}");
                continue;
            }
            if (lineCount <= maxLinesLimit) {
                continue;
            }

            findings.Add(new AnalysisFindingItem {
                Path = relativePath,
                Line = 1,
                Severity = severity,
                Message = $"File has {lineCount} lines (limit {maxLinesLimit}). Split into smaller units.",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{maxLinesRule.Rule.Id}:{relativePath}:{lineCount}:{maxLinesLimit}"
            });
        }

        Console.WriteLine($"Internal maintainability findings: {findings.Count} item(s).");
        return findings;
    }

    private static AnalysisPolicyRule? ResolveMaxLinesRule(IReadOnlyList<AnalysisPolicyRule> rules, out int maxLinesLimit) {
        maxLinesLimit = DefaultMaxFileLinesLimit;
        if (rules is null || rules.Count == 0) {
            return null;
        }

        AnalysisPolicyRule? fallbackRule = null;
        foreach (var rule in rules) {
            if (rule?.Rule is null) {
                continue;
            }
            fallbackRule ??= rule;
            if (TryResolveLineLimit(rule.Rule, out var parsedLimit)) {
                maxLinesLimit = parsedLimit;
                return rule;
            }
        }

        return fallbackRule;
    }

    private static bool TryResolveLineLimit(AnalysisRule rule, out int limit) {
        limit = DefaultMaxFileLinesLimit;
        if (rule is null || rule.Tags is null || rule.Tags.Count == 0) {
            return false;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(MaxLinesTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var value = tag.Substring(MaxLinesTagPrefix.Length).Trim();
            if (int.TryParse(value, out var parsed) && parsed > 0) {
                limit = parsed;
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyCollection<string> ResolveGeneratedSuffixes(AnalysisRule rule) {
        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rule?.Tags is not null && rule.Tags.Count > 0) {
            foreach (var tag in rule.Tags) {
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(GeneratedSuffixTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var value = NormalizeGeneratedSuffixTagValue(tag.Substring(GeneratedSuffixTagPrefix.Length));
                if (!string.IsNullOrWhiteSpace(value)) {
                    suffixes.Add(value);
                }
            }
        }

        if (suffixes.Count > 0) {
            return suffixes;
        }
        foreach (var suffix in DefaultGeneratedSuffixes) {
            suffixes.Add(suffix);
        }
        return suffixes;
    }

    private static string? NormalizeGeneratedSuffixTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim();
        while (value.StartsWith("*", StringComparison.Ordinal)) {
            value = value.Substring(1);
        }
        while (value.StartsWith("/", StringComparison.Ordinal)) {
            value = value.Substring(1);
        }
        if (value.Length == 0) {
            return null;
        }
        if (!value.StartsWith(".", StringComparison.Ordinal)) {
            value = "." + value;
        }
        return value;
    }

    private static IReadOnlyCollection<string> ResolveGeneratedHeaderMarkers(AnalysisRule rule) {
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rule?.Tags is not null && rule.Tags.Count > 0) {
            foreach (var tag in rule.Tags) {
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(GeneratedMarkerTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var marker = NormalizeGeneratedHeaderMarkerTagValue(tag.Substring(GeneratedMarkerTagPrefix.Length));
                if (!string.IsNullOrWhiteSpace(marker)) {
                    markers.Add(marker);
                }
            }
        }
        if (markers.Count > 0) {
            return markers;
        }
        foreach (var marker in DefaultGeneratedHeaderMarkers) {
            markers.Add(marker);
        }
        return markers;
    }

    private static string? NormalizeGeneratedHeaderMarkerTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim();
        return value.Length == 0 ? null : value;
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string workspace, string? excludedOutputPath, List<string> warnings) {
        var pending = new Stack<string>();
        pending.Push(workspace);

        while (pending.Count > 0) {
            var currentDirectory = pending.Pop();

            IEnumerable<string> subdirectories;
            try {
                subdirectories = Directory.EnumerateDirectories(currentDirectory);
            } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException) {
                var relativePath = Path.GetRelativePath(workspace, currentDirectory).Replace('\\', '/');
                warnings.Add($"Skipped inaccessible directory during line-count scan ({relativePath}): {ex.Message}");
                continue;
            }

            foreach (var subdirectory in subdirectories) {
                if (!IsExcludedDirectory(workspace, subdirectory, excludedOutputPath)) {
                    pending.Push(subdirectory);
                }
            }

            IEnumerable<string> files;
            try {
                files = Directory.EnumerateFiles(currentDirectory, "*.cs", SearchOption.TopDirectoryOnly);
            } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException) {
                var relativePath = Path.GetRelativePath(workspace, currentDirectory).Replace('\\', '/');
                warnings.Add($"Skipped inaccessible directory during line-count scan ({relativePath}): {ex.Message}");
                continue;
            }

            foreach (var file in files) {
                yield return file;
            }
        }
    }

    private static bool IsExcludedDirectory(string workspace, string fullPath, string? excludedOutputPath) {
        var relativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/');
        return ContainsExcludedDirectorySegment(relativePath) ||
            IsPathUnderRelativeRoot(relativePath, excludedOutputPath);
    }

    private static bool IsExcludedSourceFile(string fullPath, string relativePath, IReadOnlyCollection<string> generatedSuffixes,
        IReadOnlyCollection<string> generatedHeaderMarkers, string? excludedOutputPath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return true;
        }
        if (ContainsExcludedDirectorySegment(relativePath)) {
            return true;
        }
        if (IsPathUnderRelativeRoot(relativePath, excludedOutputPath)) {
            return true;
        }

        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if ((generatedSuffixes ?? Array.Empty<string>()).Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        return HasGeneratedFileHeader(fullPath, generatedHeaderMarkers);
    }

    private static bool ContainsExcludedDirectorySegment(string relativePath) {
        var segments = relativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => ExcludedDirectorySegments.Contains(segment));
    }

    private static string? TryGetRelativePathWithinWorkspace(string workspace, string path) {
        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        try {
            var workspaceFullPath = Path.GetFullPath(workspace);
            var candidateFullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(workspaceFullPath, candidateFullPath).Replace('\\', '/');
            if (relativePath.StartsWith("../", StringComparison.Ordinal) || relativePath.Equals("..", StringComparison.Ordinal)) {
                return null;
            }
            return relativePath.Trim('/');
        } catch {
            return null;
        }
    }

    private static bool IsPathUnderRelativeRoot(string relativePath, string? rootRelativePath) {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(rootRelativePath)) {
            return false;
        }
        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        var normalizedRoot = rootRelativePath.Replace('\\', '/').Trim('/');
        if (normalizedPath.Length == 0 || normalizedRoot.Length == 0) {
            return false;
        }
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGeneratedFileHeader(string fullPath, IReadOnlyCollection<string> generatedHeaderMarkers) {
        try {
            using var reader = new StreamReader(fullPath);
            var inBlockComment = false;
            for (var i = 0; i < GeneratedHeaderLinesToInspect; i++) {
                var line = reader.ReadLine();
                if (line is null) {
                    break;
                }
                var normalized = line.Trim();
                if (normalized.Length == 0) {
                    continue;
                }

                var isLineComment = normalized.StartsWith("//", StringComparison.Ordinal);
                if (normalized.StartsWith("/*", StringComparison.Ordinal)) {
                    inBlockComment = true;
                }
                var isCommentContext = inBlockComment || isLineComment || normalized.StartsWith("*", StringComparison.Ordinal);
                if (!isCommentContext) {
                    break;
                }

                foreach (var marker in generatedHeaderMarkers ?? Array.Empty<string>()) {
                    if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }

                if (inBlockComment && normalized.Contains("*/", StringComparison.Ordinal)) {
                    inBlockComment = false;
                }
            }
        } catch {
            // Treat read failures as non-generated here; caller will report read failure during counting.
        }
        return false;
    }

    private static int CountFileLines(string path) {
        var count = 0;
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is not null) {
            count++;
        }
        return count;
    }

    private static string? NormalizeSeverity(string? severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return "warning";
        }
        return severity.Trim().ToLowerInvariant() switch {
            "none" => null,
            "off" => null,
            "disable" => null,
            "disabled" => null,
            "suppress" => null,
            "critical" => "error",
            "high" => "error",
            "error" => "error",
            "warning" => "warning",
            "warn" => "warning",
            "medium" => "warning",
            "info" => "info",
            "information" => "info",
            "low" => "info",
            _ => "warning"
        };
    }
}
