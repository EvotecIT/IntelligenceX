using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
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

    private static void ValidateInternalMaintainabilityTags(AnalysisRule rule, IReadOnlyCollection<string> supportedPrefixes,
        List<string> warnings) {
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return;
        }

        var unknownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPrefixes = (supportedPrefixes ?? Array.Empty<string>())
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .ToArray();
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }
            var isSupported = normalizedPrefixes.Any(prefix => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (isSupported) {
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
        var supported = normalizedPrefixes.Length == 0 ? "<none>" : string.Join(", ", normalizedPrefixes);
        warnings.Add(
            $"Rule {rule.Id} has unknown maintainability tags: {sample}{suffix}. Supported prefixes: {supported}.");
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

    private static IReadOnlySet<string> ResolveIncludedSourceExtensionsForRules(IEnumerable<AnalysisRule> rules,
        List<string> warnings) {
        var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rules is not null) {
            foreach (var rule in rules.Where(static item => item is not null)) {
                foreach (var extension in ResolveIncludedSourceExtensionsForRule(rule, warnings)) {
                    union.Add(extension);
                }
            }
        }

        if (union.Count == 0) {
            foreach (var extension in DefaultIncludedSourceExtensions) {
                union.Add(extension);
            }
        }
        return union;
    }

    private static IReadOnlySet<string> ResolveIncludedSourceExtensionsForRule(AnalysisRule rule, List<string> warnings) {
        var defaults = new HashSet<string>(DefaultIncludedSourceExtensions, StringComparer.OrdinalIgnoreCase);
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return defaults;
        }

        var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        var sawIncludeTag = false;
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(IncludeExtensionTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            sawIncludeTag = true;
            var normalized = NormalizeIncludeExtensionTagValue(tag.Substring(IncludeExtensionTagPrefix.Length));
            if (!string.IsNullOrWhiteSpace(normalized)) {
                configured.Add(normalized);
            } else {
                malformedTags.Add(tag);
            }
        }

        AddMalformedTagWarning(rule.Id, malformedTags, IncludeExtensionTagPrefix, warnings);
        if (sawIncludeTag && configured.Count > 0) {
            return configured;
        }
        return defaults;
    }

    private static IReadOnlyList<SourceFileEntry> FilterSourceFilesForRule(AnalysisRule rule,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        if (sourceFiles is null || sourceFiles.Count == 0) {
            return Array.Empty<SourceFileEntry>();
        }

        var includedExtensions = ResolveIncludedSourceExtensionsForRule(rule, warnings);
        var generatedSuffixes = ResolveGeneratedSuffixes(rule, warnings);
        var generatedHeaderMarkers = ResolveGeneratedHeaderMarkers(rule, warnings);
        var generatedHeaderLinesToInspect = ResolveGeneratedHeaderLinesToInspect(rule, warnings);
        var excludedDirectorySegments = ResolveExcludedDirectorySegments(rule, warnings);

        return sourceFiles
            .Where(file => IsPathInIncludedExtensions(file.RelativePath, includedExtensions))
            .Where(file => !IsExcludedSourceFile(file.FullPath, file.RelativePath, generatedSuffixes, generatedHeaderMarkers,
                excludedDirectorySegments, generatedHeaderLinesToInspect, excludedOutputPath))
            .ToList();
    }

    private static bool IsPathInIncludedExtensions(string path, IReadOnlySet<string> includedExtensions) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return includedExtensions?.Contains(extension) == true;
    }

    private static string? NormalizeIncludeExtensionTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim().Replace('*', ' ').Trim();
        if (value.Length == 0) {
            return null;
        }
        if (value.StartsWith("/", StringComparison.Ordinal)) {
            return null;
        }
        if (!value.StartsWith(".", StringComparison.Ordinal)) {
            value = "." + value;
        }
        return value.ToLowerInvariant();
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

    private static IEnumerable<string> EnumerateSourceFiles(string workspace, IReadOnlySet<string> includedExtensions,
        IReadOnlySet<string> excludedDirectorySegments, string? excludedOutputPath, List<string> warnings) {
        var pending = new Stack<string>();
        pending.Push(workspace);
        var normalizedExtensions = new HashSet<string>(
            includedExtensions
            .Where(static ext => !string.IsNullOrWhiteSpace(ext))
            .Select(static ext => ext.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        if (normalizedExtensions.Count == 0) {
            foreach (var extension in DefaultIncludedSourceExtensions) {
                normalizedExtensions.Add(extension);
            }
        }

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
                files = Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException) {
                var relativePath = Path.GetRelativePath(workspace, currentDirectory).Replace('\\', '/');
                warnings.Add($"Skipped inaccessible directory during line-count scan ({relativePath}): {ex.Message}");
                continue;
            }

            foreach (var file in files) {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (!normalizedExtensions.Contains(extension)) {
                    continue;
                }
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

                var isLineComment = normalized.StartsWith("//", StringComparison.Ordinal) ||
                                    normalized.StartsWith("#", StringComparison.Ordinal);
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

    private static string ResolveLanguageFromPath(string? path) {
        var extension = Path.GetExtension(path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension)) {
            return "unknown";
        }
        return extension.ToLowerInvariant() switch {
            ".cs" => "csharp",
            ".ps1" => "powershell",
            ".psm1" => "powershell",
            ".psd1" => "powershell",
            ".js" => "javascript",
            ".jsx" => "javascript",
            ".mjs" => "javascript",
            ".cjs" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".py" => "python",
            _ => "unknown"
        };
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
