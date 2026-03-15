using System;
using System.IO;
using System.Reflection;
using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.App;

internal static class OfficeImoAssemblyContractDiagnostics {
    private static readonly Version MinimumMarkdownRendererVersion = new(0, 2, 0);
    private static readonly Version MinimumMarkdownVersion = new(0, 6, 0);
    private static readonly Version MinimumWordMarkdownVersion = new(1, 0, 7);

    public static string DescribeMarkdownRendererContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownRenderer).Assembly,
            "OfficeIMO.MarkdownRenderer",
            MinimumMarkdownRendererVersion,
            "explicit transcript presets + preprocessor pipeline");
    }

    public static string DescribeMarkdownContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownInputNormalizer).Assembly,
            "OfficeIMO.Markdown",
            MinimumMarkdownVersion,
            "transcript normalization + streaming preview");
    }

    public static string DescribeWordMarkdownContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownToWordOptions).Assembly,
            "OfficeIMO.Word.Markdown",
            MinimumWordMarkdownVersion,
            "transcript markdown-to-word conversion");
    }

    private static string DescribeAssemblyContract(
        Assembly assembly,
        string componentName,
        Version minimumVersion,
        string feature) {
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
