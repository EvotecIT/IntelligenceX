using System;
using System.IO;
using System.Reflection;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Owns the explicit OfficeIMO Word-markdown contract used by DOCX transcript export.
/// </summary>
internal static class OfficeImoWordMarkdownRuntimeContract {
    private static readonly Version MinimumWordMarkdownVersion = new(1, 0, 7);
    private static readonly Lazy<bool> PreservesGroupedDefinitionLikeParagraphsLazy =
        new(MarkdownToWordCapabilities.PreservesNarrativeSingleLineDefinitionsAsSeparateParagraphs);

    public static MarkdownToWordOptions CreateTranscriptMarkdownToWordOptions(
        IReadOnlyList<string>? allowedImageDirectories,
        int? docxVisualMaxWidthPx) =>
        MarkdownToWordPresets.CreateIntelligenceXTranscript(
            allowedImageDirectories,
            docxVisualMaxWidthPx);

    public static bool PreservesGroupedDefinitionLikeParagraphs() =>
        PreservesGroupedDefinitionLikeParagraphsLazy.Value;

    public static string DescribeWordMarkdownContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownToWordOptions).Assembly,
            "OfficeIMO.Word.Markdown",
            MinimumWordMarkdownVersion,
            "transcript markdown-to-word conversion");
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
