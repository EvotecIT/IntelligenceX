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
    private static readonly Version MinimumWordMarkdownVersion = new(1, 0, 7);
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
        ApplyReaderOptionsIfSupported(options);

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

    private static void ApplyReaderOptionsIfSupported(MarkdownToWordOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        try {
            var readerOptions = CreateReaderOptionsIfAvailable();
            if (readerOptions == null) {
                return;
            }

            var readerOptionsProperty = options.GetType().GetProperty(
                "ReaderOptions",
                BindingFlags.Instance | BindingFlags.Public);
            if (readerOptionsProperty?.CanWrite != true || !readerOptionsProperty.PropertyType.IsInstanceOfType(readerOptions)) {
                return;
            }

            readerOptionsProperty.SetValue(options, readerOptions);
        } catch {
            // Package mode may load an older OfficeIMO.Word.Markdown build. Fall back to the baseline option set.
        }
    }

    private static object? CreateReaderOptionsIfAvailable() {
        var readerOptionsType = Type.GetType("OfficeIMO.Markdown.MarkdownReaderOptions, OfficeIMO.Markdown", throwOnError: false);
        if (readerOptionsType == null) {
            return null;
        }

        object? readerOptions = null;

        var profileFactory = readerOptionsType.GetMethod(
            "CreateOfficeIMOProfile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        if (profileFactory != null) {
            readerOptions = profileFactory.Invoke(null, null);
        }

        readerOptions ??= Activator.CreateInstance(readerOptionsType);
        if (readerOptions == null) {
            return null;
        }

        TrySetBooleanProperty(readerOptions, "PreferNarrativeSingleLineDefinitions", true);
        TrySetBooleanProperty(readerOptions, "Callouts", true);
        TrySetBooleanProperty(readerOptions, "DefinitionLists", true);
        return readerOptions;
    }

    private static void TrySetBooleanProperty(object target, string propertyName, bool value) {
        if (target == null || string.IsNullOrWhiteSpace(propertyName)) {
            return;
        }

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite != true || property.PropertyType != typeof(bool)) {
            return;
        }

        property.SetValue(target, value);
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
