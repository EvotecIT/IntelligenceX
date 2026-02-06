using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private const int MaxTagWarningDetails = 5;

    private static IReadOnlyList<AnalysisFindingItem> RunInternalMaintainabilityChecks(string workspace,
        string outputDirectory,
        IReadOnlyList<AnalysisPolicyRule> rules, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var maxLinesRule = ResolveMaxLinesRule(rules);
        if (maxLinesRule is null) {
            return findings;
        }
        var maxLinesLimit = ResolveMaxLinesLimit(maxLinesRule.Rule, warnings);

        var severity = NormalizeSeverity(maxLinesRule.Severity);
        if (string.IsNullOrWhiteSpace(severity)) {
            Console.WriteLine($"Internal maintainability rule {maxLinesRule.Rule.Id} is disabled by policy severity.");
            return findings;
        }
        ValidateInternalMaintainabilityTags(maxLinesRule.Rule, warnings);
        var generatedSuffixes = ResolveGeneratedSuffixes(maxLinesRule.Rule, warnings);
        var generatedHeaderMarkers = ResolveGeneratedHeaderMarkers(maxLinesRule.Rule, warnings);
        var generatedHeaderLinesToInspect = ResolveGeneratedHeaderLinesToInspect(maxLinesRule.Rule, warnings);
        var excludedDirectorySegments = ResolveExcludedDirectorySegments(maxLinesRule.Rule, warnings);
        var excludedOutputPath = TryGetRelativePathWithinWorkspace(workspace, outputDirectory);
        var emittedRuleId = string.IsNullOrWhiteSpace(maxLinesRule.Rule.ToolRuleId)
            ? maxLinesRule.Rule.Id
            : maxLinesRule.Rule.ToolRuleId;
        var emittedTool = string.IsNullOrWhiteSpace(maxLinesRule.Rule.Tool)
            ? InternalToolName
            : maxLinesRule.Rule.Tool;

        var sourceFiles = EnumerateCSharpFiles(workspace, excludedDirectorySegments, excludedOutputPath, warnings)
            .Select(path => Path.GetFullPath(path))
            .Select(fullPath => new {
                FullPath = fullPath,
                RelativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/')
            })
            .Where(file => !IsExcludedSourceFile(file.FullPath, file.RelativePath, generatedSuffixes, generatedHeaderMarkers,
                excludedDirectorySegments,
                generatedHeaderLinesToInspect, excludedOutputPath));

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

    private static AnalysisPolicyRule? ResolveMaxLinesRule(IReadOnlyList<AnalysisPolicyRule> rules) {
        if (rules is null || rules.Count == 0) {
            return null;
        }

        AnalysisPolicyRule? fallbackRule = null;
        foreach (var rule in rules) {
            if (rule?.Rule is null) {
                continue;
            }
            fallbackRule ??= rule;
            if (HasTagWithPrefix(rule.Rule.Tags, MaxLinesTagPrefix)) {
                return rule;
            }
        }

        return fallbackRule;
    }

    private static int ResolveMaxLinesLimit(AnalysisRule rule, List<string> warnings) {
        var limit = DefaultMaxFileLinesLimit;
        if (rule is null || rule.Tags is null || rule.Tags.Count == 0) {
            return limit;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(MaxLinesTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var value = tag.Substring(MaxLinesTagPrefix.Length).Trim();
            if (int.TryParse(value, out var parsed) && parsed > 0) {
                return parsed;
            }
            warnings.Add(
                $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{MaxLinesTagPrefix}<positive-int>'; using {DefaultMaxFileLinesLimit}.");
            return limit;
        }
        return limit;
    }

    private static IReadOnlyCollection<string> ResolveGeneratedSuffixes(AnalysisRule rule, List<string> warnings) {
        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        if (rule?.Tags is not null && rule.Tags.Count > 0) {
            foreach (var tag in rule.Tags) {
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(GeneratedSuffixTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var value = NormalizeGeneratedSuffixTagValue(tag.Substring(GeneratedSuffixTagPrefix.Length));
                if (!string.IsNullOrWhiteSpace(value)) {
                    suffixes.Add(value);
                } else {
                    malformedTags.Add(tag);
                }
            }
        }
        AddMalformedTagWarning(rule?.Id, malformedTags, GeneratedSuffixTagPrefix, warnings);

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

    private static IReadOnlyCollection<string> ResolveGeneratedHeaderMarkers(AnalysisRule rule, List<string> warnings) {
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        if (rule?.Tags is not null && rule.Tags.Count > 0) {
            foreach (var tag in rule.Tags) {
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(GeneratedMarkerTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var marker = NormalizeGeneratedHeaderMarkerTagValue(tag.Substring(GeneratedMarkerTagPrefix.Length));
                if (!string.IsNullOrWhiteSpace(marker)) {
                    markers.Add(marker);
                } else {
                    malformedTags.Add(tag);
                }
            }
        }
        AddMalformedTagWarning(rule?.Id, malformedTags, GeneratedMarkerTagPrefix, warnings);
        return markers;
    }

    private static int ResolveGeneratedHeaderLinesToInspect(AnalysisRule rule, List<string> warnings) {
        if (rule is null || rule.Tags is null || rule.Tags.Count == 0) {
            return GeneratedHeaderLinesToInspect;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(GeneratedHeaderLinesTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var value = tag.Substring(GeneratedHeaderLinesTagPrefix.Length).Trim();
            if (int.TryParse(value, out var parsed) && parsed >= 0) {
                return parsed;
            }
            warnings.Add(
                $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{GeneratedHeaderLinesTagPrefix}<non-negative-int>'; using {GeneratedHeaderLinesToInspect}.");
            return GeneratedHeaderLinesToInspect;
        }
        return GeneratedHeaderLinesToInspect;
    }

    private static void ValidateInternalMaintainabilityTags(AnalysisRule rule, List<string> warnings) {
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return;
        }

        var unknownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }
            if (tag.StartsWith(MaxLinesTagPrefix, StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith(GeneratedSuffixTagPrefix, StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith(GeneratedMarkerTagPrefix, StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith(GeneratedHeaderLinesTagPrefix, StringComparison.OrdinalIgnoreCase) ||
                tag.StartsWith(ExcludedDirectoryTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!tag.Contains(':', StringComparison.Ordinal)) {
                continue;
            }
            unknownTags.Add(tag);
        }
        if (unknownTags.Count == 0) {
            return;
        }

        var sample = string.Join(", ", unknownTags.Take(MaxTagWarningDetails).Select(tag => $"'{tag}'"));
        var suffix = unknownTags.Count > MaxTagWarningDetails
            ? $" (+{unknownTags.Count - MaxTagWarningDetails} more)"
            : string.Empty;
        warnings.Add(
            $"Rule {rule.Id} has unknown maintainability tags: {sample}{suffix}. Supported prefixes: {MaxLinesTagPrefix}, {GeneratedSuffixTagPrefix}, {GeneratedMarkerTagPrefix}, {GeneratedHeaderLinesTagPrefix}, {ExcludedDirectoryTagPrefix}.");
    }

    private static void AddMalformedTagWarning(string? ruleId, IReadOnlyList<string> malformedTags, string expectedPrefix,
        List<string> warnings) {
        if (malformedTags is null || malformedTags.Count == 0) {
            return;
        }

        var sample = string.Join(", ", malformedTags.Take(MaxTagWarningDetails).Select(tag => $"'{tag}'"));
        var suffix = malformedTags.Count > MaxTagWarningDetails
            ? $" (+{malformedTags.Count - MaxTagWarningDetails} more)"
            : string.Empty;
        warnings.Add(
            $"Rule {ruleId ?? "<unknown>"} has malformed tags: {sample}{suffix}. Expected '{expectedPrefix}<value>'.");
    }

    private static IReadOnlySet<string> ResolveExcludedDirectorySegments(AnalysisRule rule, List<string> warnings) {
        var segments = new HashSet<string>(DefaultExcludedDirectorySegments, StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return segments;
        }

        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(ExcludedDirectoryTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var segment = NormalizeExcludedDirectoryTagValue(tag.Substring(ExcludedDirectoryTagPrefix.Length));
            if (!string.IsNullOrWhiteSpace(segment)) {
                segments.Add(segment);
            } else {
                malformedTags.Add(tag);
            }
        }
        AddMalformedTagWarning(rule.Id, malformedTags, ExcludedDirectoryTagPrefix, warnings);

        return segments;
    }

    private static string? NormalizeExcludedDirectoryTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim().Replace('\\', '/').Trim('/');
        if (value.Length == 0) {
            return null;
        }
        if (value.Contains('/', StringComparison.Ordinal)) {
            return null;
        }
        return value;
    }

    private static string? NormalizeGeneratedHeaderMarkerTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim();
        return value.Length == 0 ? null : value;
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string workspace, IReadOnlySet<string> excludedDirectorySegments,
        string? excludedOutputPath, List<string> warnings) {
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
                if (!IsExcludedDirectory(workspace, subdirectory, excludedDirectorySegments, excludedOutputPath)) {
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

    private static bool IsExcludedDirectory(string workspace, string fullPath, IReadOnlySet<string> excludedDirectorySegments,
        string? excludedOutputPath) {
        var relativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/');
        return ContainsExcludedDirectorySegment(relativePath, excludedDirectorySegments) ||
            IsPathUnderRelativeRoot(relativePath, excludedOutputPath);
    }

    private static bool IsExcludedSourceFile(string fullPath, string relativePath, IReadOnlyCollection<string> generatedSuffixes,
        IReadOnlyCollection<string> generatedHeaderMarkers, IReadOnlySet<string> excludedDirectorySegments,
        int generatedHeaderLinesToInspect, string? excludedOutputPath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return true;
        }
        if (ContainsExcludedDirectorySegment(relativePath, excludedDirectorySegments)) {
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

        return HasGeneratedFileHeader(fullPath, generatedHeaderMarkers, generatedHeaderLinesToInspect);
    }

    private static bool ContainsExcludedDirectorySegment(string relativePath, IReadOnlySet<string> excludedDirectorySegments) {
        var segments = relativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => excludedDirectorySegments.Contains(segment));
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

    private static bool HasGeneratedFileHeader(string fullPath, IReadOnlyCollection<string> generatedHeaderMarkers,
        int generatedHeaderLinesToInspect) {
        if (generatedHeaderLinesToInspect <= 0) {
            return false;
        }
        try {
            using var reader = new StreamReader(fullPath);
            var inBlockComment = false;
            for (var i = 0; i < generatedHeaderLinesToInspect; i++) {
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

    private static bool HasTagWithPrefix(IReadOnlyList<string>? tags, string prefix) {
        if (tags is null || tags.Count == 0 || string.IsNullOrWhiteSpace(prefix)) {
            return false;
        }
        foreach (var tag in tags) {
            if (!string.IsNullOrWhiteSpace(tag) && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
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
