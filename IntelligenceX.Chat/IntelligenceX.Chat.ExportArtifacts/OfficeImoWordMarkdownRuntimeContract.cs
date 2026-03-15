using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Owns the OfficeIMO Word-markdown contract used by DOCX transcript export.
/// Prefers the explicit IX OfficeIMO preset/capability surface when available and
/// falls back to the published package baseline when that surface has not shipped yet.
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
        return TryCreateExplicitTranscriptMarkdownToWordOptions(allowedImageDirectories, docxVisualMaxWidthPx)
               ?? CreateLegacyTranscriptMarkdownToWordOptions(allowedImageDirectories, docxVisualMaxWidthPx);
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

    private static MarkdownToWordOptions? TryCreateExplicitTranscriptMarkdownToWordOptions(
        IReadOnlyList<string>? allowedImageDirectories,
        int? docxVisualMaxWidthPx) {
        try {
            var presetsType = Type.GetType(
                "OfficeIMO.Word.Markdown.MarkdownToWordPresets, OfficeIMO.Word.Markdown",
                throwOnError: false);
            var createMethod = presetsType?.GetMethod(
                "CreateIntelligenceXTranscript",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(IReadOnlyList<string>), typeof(int?)],
                modifiers: null);
            if (createMethod == null || !typeof(MarkdownToWordOptions).IsAssignableFrom(createMethod.ReturnType)) {
                return null;
            }

            return createMethod.Invoke(null, [allowedImageDirectories, docxVisualMaxWidthPx]) as MarkdownToWordOptions;
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            return null;
        }
    }

    private static MarkdownToWordOptions CreateLegacyTranscriptMarkdownToWordOptions(
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
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
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
        TryAddDocumentTransform(readerOptions);
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

    private static void TryAddDocumentTransform(object target) {
        if (target == null) {
            return;
        }

        try {
            var property = target.GetType().GetProperty(
                "DocumentTransforms",
                BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(target) is not System.Collections.IList transforms) {
                return;
            }

            var transformType = Type.GetType(
                "OfficeIMO.Markdown.MarkdownJsonVisualCodeBlockTransform, OfficeIMO.Markdown",
                throwOnError: false);
            var modeType = Type.GetType(
                "OfficeIMO.Markdown.MarkdownVisualFenceLanguageMode, OfficeIMO.Markdown",
                throwOnError: false);
            if (transformType == null || modeType == null) {
                return;
            }

            var aliasMode = Enum.Parse(modeType, "IntelligenceXAliasFence", ignoreCase: false);
            for (var i = 0; i < transforms.Count; i++) {
                var existing = transforms[i];
                if (existing == null || !transformType.IsInstanceOfType(existing)) {
                    continue;
                }

                var modeProperty = transformType.GetProperty("LanguageMode", BindingFlags.Instance | BindingFlags.Public);
                if (Equals(modeProperty?.GetValue(existing), aliasMode)) {
                    return;
                }
            }

            var ctor = transformType.GetConstructor([modeType]);
            if (ctor == null) {
                return;
            }

            transforms.Add(ctor.Invoke([aliasMode]));
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            // Older package lines do not expose the document transform surface yet.
        }
    }

    private static bool DetectGroupedDefinitionLikeParagraphSupport() {
        try {
            var capabilitiesType = Type.GetType(
                "OfficeIMO.Word.Markdown.MarkdownToWordCapabilities, OfficeIMO.Word.Markdown",
                throwOnError: false);
            var method = capabilitiesType?.GetMethod(
                "PreservesNarrativeSingleLineDefinitionsAsSeparateParagraphs",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method?.ReturnType == typeof(bool)) {
                return (bool)(method.Invoke(null, null) ?? false);
            }
        } catch (Exception ex) when (IsCompatibilityFallbackException(ex)) {
            // Fall back to a direct behavioral probe below.
        }

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

    private static bool IsCompatibilityFallbackException(Exception exception) {
        var unwrapped = UnwrapInvocationException(exception);
        return unwrapped is FileNotFoundException
            or FileLoadException
            or BadImageFormatException
            or MissingMethodException
            or MissingMemberException
            or TypeLoadException
            or NotSupportedException
            or MemberAccessException
            or InvalidCastException;
    }

    private static Exception UnwrapInvocationException(Exception exception) {
        var current = exception;
        while (current is TargetInvocationException { InnerException: not null } invocationException) {
            current = invocationException.InnerException!;
        }

        return current;
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
