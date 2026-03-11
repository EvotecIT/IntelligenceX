using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Owns OfficeIMO Word markdown converter capability probing and baseline transcript conversion options.
/// </summary>
internal static class OfficeImoWordMarkdownRuntimeContract {
    private const int MinDocxVisualMaxWidthPx = 320;
    private const int MaxDocxVisualMaxWidthPx = 2000;
    private const int DefaultDocxVisualMaxWidthPx = 760;
    private static readonly Version MinimumWordMarkdownVersion = new(1, 0, 6);
    private static readonly Lazy<bool> PreservesGroupedDefinitionLikeParagraphsLazy = new(DetectGroupedDefinitionLikeParagraphSupport);

    public static MarkdownToWordOptions CreateTranscriptMarkdownToWordOptions(
        IReadOnlyList<string>? allowedImageDirectories,
        int? docxVisualMaxWidthPx) {
        var options = new MarkdownToWordOptions {
            FontFamily = "Calibri",
            AllowLocalImages = allowedImageDirectories is { Count: > 0 },
            PreferNarrativeSingleLineDefinitions = true,
            FitImagesToContextWidth = true,
            MaxImageWidthPercentOfContent = 100d,
            FitImagesToPageContentWidth = true,
            MaxImageWidthPixels = NormalizeDocxVisualMaxWidthPx(docxVisualMaxWidthPx)
        };

        if (allowedImageDirectories is { Count: > 0 }) {
            for (var i = 0; i < allowedImageDirectories.Count; i++) {
                var directory = allowedImageDirectories[i];
                if (string.IsNullOrWhiteSpace(directory)) {
                    continue;
                }

                if (!options.AllowedImageDirectories.Contains(directory)) {
                    options.AllowedImageDirectories.Add(directory);
                }
            }
        }

        return options;
    }

    public static bool PreservesGroupedDefinitionLikeParagraphs() =>
        PreservesGroupedDefinitionLikeParagraphsLazy.Value;

    public static string DescribeWordMarkdownContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownToWordOptions).Assembly,
            "OfficeIMO.Word.Markdown",
            MinimumWordMarkdownVersion,
            "transcript markdown-to-word conversion");
    }

    private static int NormalizeDocxVisualMaxWidthPx(int? value) {
        if (!value.HasValue) {
            return DefaultDocxVisualMaxWidthPx;
        }

        var normalized = value.Value;
        if (normalized < MinDocxVisualMaxWidthPx) {
            return MinDocxVisualMaxWidthPx;
        }

        if (normalized > MaxDocxVisualMaxWidthPx) {
            return MaxDocxVisualMaxWidthPx;
        }

        return normalized;
    }

    private static bool DetectGroupedDefinitionLikeParagraphSupport() {
        try {
            const string sampleMarkdown = """
                # Transcript

                Status: healthy
                Impact: none
                """;

            using var document = sampleMarkdown.LoadFromMarkdown(new MarkdownToWordOptions {
                PreferNarrativeSingleLineDefinitions = true
            });

            var bodyParagraphs = new List<string>();
            foreach (var paragraph in document.Paragraphs) {
                var text = paragraph.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Transcript", StringComparison.Ordinal)) {
                    continue;
                }

                bodyParagraphs.Add(text);
            }

            return bodyParagraphs.Contains("Status: healthy", StringComparer.Ordinal)
                   && bodyParagraphs.Contains("Impact: none", StringComparer.Ordinal);
        } catch {
            return false;
        }
    }

    private static string DescribeAssemblyContract(
        Assembly? assembly,
        string componentName,
        Version minimumVersion,
        string feature) {
        if (assembly is null) {
            return $"{componentName} expected>={minimumVersion} feature={feature} loaded=unavailable status=missing";
        }

        var assemblyName = assembly.GetName();
        var loadedVersion = assemblyName.Version;
        var meetsMinimum = loadedVersion is not null && loadedVersion >= minimumVersion;
        var status = meetsMinimum ? "ok" : "older-than-expected";
        return $"{componentName} expected>={minimumVersion} feature={feature} loaded={FormatVersion(loadedVersion)} status={status} path={GetAssemblyPath(assembly)}";
    }

    private static string FormatVersion(Version? version) {
        return version?.ToString() ?? "unknown";
    }

    private static string GetAssemblyPath(Assembly assembly) {
        try {
            var location = assembly.Location ?? string.Empty;
            return string.IsNullOrWhiteSpace(location)
                ? "(dynamic)"
                : Path.GetFullPath(location);
        } catch {
            return "(dynamic)";
        }
    }
}
