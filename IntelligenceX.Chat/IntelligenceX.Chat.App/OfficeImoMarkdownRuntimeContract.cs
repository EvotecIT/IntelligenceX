using System;
using System.IO;
using System.Reflection;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralizes the OfficeIMO markdown runtime contract used by the desktop chat host.
/// </summary>
internal static class OfficeImoMarkdownRuntimeContract {
    private static readonly Version MinimumMarkdownRendererVersion = new(0, 1, 9);
    private static readonly Version MinimumMarkdownVersionForNormalizationPresets = new(0, 5, 12);
    private static readonly Version MinimumWordMarkdownVersion = new(1, 0, 6);
    private static readonly Lazy<PropertyInfo?> NetworkPropertyLazy = new(
        () => typeof(MarkdownRendererOptions).GetProperty("Network", BindingFlags.Instance | BindingFlags.Public));

    /// <summary>
    /// Creates transcript renderer options using the central OfficeIMO runtime contract.
    /// </summary>
    public static MarkdownRendererOptions CreateTranscriptRendererOptions() {
        // Preset factory returns a fresh options object per call; these mutations are call-local.
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        options.Mermaid.Enabled = true;
        options.Chart.Enabled = true;
        TryEnableOptionalRendererNetworkSupport(options);
        return options;
    }

    /// <summary>
    /// Enables optional vis-network support when the loaded renderer exposes it.
    /// </summary>
    /// <param name="options">Renderer options to mutate.</param>
    /// <returns><see langword="true"/> when the optional capability was enabled.</returns>
    public static bool TryEnableOptionalRendererNetworkSupport(MarkdownRendererOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        var networkProperty = NetworkPropertyLazy.Value;
        var networkOptions = networkProperty?.GetValue(options);
        var enabledProperty = networkOptions?.GetType().GetProperty(
            "Enabled",
            BindingFlags.Instance | BindingFlags.Public);
        if (enabledProperty?.CanWrite != true || enabledProperty.PropertyType != typeof(bool)) {
            return false;
        }

        enabledProperty.SetValue(networkOptions, true);
        return true;
    }

    /// <summary>
    /// Describes the loaded markdown renderer assembly against the minimum supported package contract.
    /// </summary>
    public static string DescribeMarkdownRendererContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownRenderer).Assembly,
            "OfficeIMO.MarkdownRenderer",
            MinimumMarkdownRendererVersion,
            "chat renderer presets + optional network support");
    }

    /// <summary>
    /// Describes the loaded markdown normalizer assembly against the minimum supported package contract.
    /// </summary>
    public static string DescribeMarkdownContract() {
        var markdownAssembly = Type.GetType(
            "OfficeIMO.Markdown.MarkdownInputNormalizer, OfficeIMO.Markdown",
            throwOnError: false)?.Assembly;
        return DescribeAssemblyContract(
            markdownAssembly,
            "OfficeIMO.Markdown",
            MinimumMarkdownVersionForNormalizationPresets,
            "input normalization presets");
    }

    /// <summary>
    /// Describes the loaded markdown-to-word assembly against the minimum supported package contract.
    /// </summary>
    public static string DescribeWordMarkdownContract() {
        var wordMarkdownAssembly = Type.GetType(
            "OfficeIMO.Word.Markdown.MarkdownToWordOptions, OfficeIMO.Word.Markdown",
            throwOnError: false)?.Assembly;
        return DescribeAssemblyContract(
            wordMarkdownAssembly,
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
