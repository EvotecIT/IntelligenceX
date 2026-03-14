using System;
using System.IO;
using System.Reflection;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralizes the OfficeIMO markdown runtime contract used by the desktop chat host.
/// </summary>
internal static class OfficeImoMarkdownRuntimeContract {
    private static readonly Version MinimumMarkdownRendererVersion = new(0, 2, 0);
    private static readonly Version MinimumMarkdownVersionForNormalizationPresets = new(0, 6, 0);
    private static readonly Version MinimumWordMarkdownVersion = new(1, 0, 7);
    private static readonly Lazy<PropertyInfo?> NetworkPropertyLazy = new(
        () => typeof(MarkdownRendererOptions).GetProperty("Network", BindingFlags.Instance | BindingFlags.Public));

    /// <summary>
    /// Creates transcript renderer options using the central OfficeIMO runtime contract.
    /// </summary>
    public static MarkdownRendererOptions CreateTranscriptRendererOptions() {
        var options = MarkdownRendererPresets.CreateStrictMinimal();
        MarkdownRendererPresets.ApplyChatPresentation(options, enableCopyButtons: false);
        MarkdownRendererIntelligenceXAdapter.Apply(options);
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
    /// Applies the IntelligenceX markdown pre-processor chain without rendering HTML.
    /// This keeps the transcript normalizer aligned with the shared OfficeIMO IX adapter.
    /// </summary>
    public static string ApplyTranscriptMarkdownPreProcessors(string markdown) {
        if (string.IsNullOrEmpty(markdown)) {
            return markdown;
        }

        var options = new MarkdownRendererOptions();
        MarkdownRendererIntelligenceXAdapter.Apply(options);
        var value = markdown;
        var processors = options.MarkdownPreProcessors;
        for (var i = 0; i < processors.Count; i++) {
            var processor = processors[i];
            if (processor == null) {
                continue;
            }

            value = processor(value, options) ?? value;
        }

        return value;
    }

    /// <summary>
    /// Describes the loaded markdown renderer assembly against the minimum supported package contract.
    /// </summary>
    public static string DescribeMarkdownRendererContract() {
        return DescribeAssemblyContract(
            typeof(MarkdownRenderer).Assembly,
            "OfficeIMO.MarkdownRenderer",
            MinimumMarkdownRendererVersion,
            "generic presets + composed chat presentation + IntelligenceX aliases + network support");
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
