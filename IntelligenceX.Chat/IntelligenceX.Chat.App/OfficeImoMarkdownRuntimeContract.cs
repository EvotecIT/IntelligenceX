using System;
using System.IO;
using System.Reflection;
using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralizes the OfficeIMO markdown runtime contract used by the desktop chat host.
/// </summary>
internal static class OfficeImoMarkdownRuntimeContract {
    private static readonly Version MinimumMarkdownRendererVersion = new(0, 1, 9);
    private static readonly Version MinimumMarkdownVersionForNormalizationPresets = new(0, 5, 12);

    /// <summary>
    /// Enables optional vis-network support when the loaded renderer exposes it.
    /// </summary>
    /// <param name="options">Renderer options to mutate.</param>
    /// <returns><see langword="true"/> when the optional capability was enabled.</returns>
    public static bool TryEnableOptionalRendererNetworkSupport(MarkdownRendererOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        var networkProperty = typeof(MarkdownRendererOptions).GetProperty(
            "Network",
            BindingFlags.Instance | BindingFlags.Public);
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
    /// Creates a strict minimal renderer preset and upgrades it to Markdig-compatible reader behavior when the loaded OfficeIMO runtime exposes that capability.
    /// </summary>
    /// <param name="options">Resolved renderer options.</param>
    /// <returns><see langword="true"/> when Markdig-compatible reader behavior was enabled through the runtime surface.</returns>
    public static bool TryCreateMarkdigCompatibleChatStrictMinimal(out MarkdownRendererOptions options) {
        if (TryInvokeRendererPreset("CreateChatStrictMinimalMarkdigCompatible", out options)) {
            return true;
        }

        options = MarkdownRendererPresets.CreateChatStrictMinimal();
        return TryApplyMarkdigCompatibleReaderOptions(options);
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

    private static bool TryInvokeRendererPreset(string methodName, out MarkdownRendererOptions options) {
        options = null!;
        var presetsType = typeof(MarkdownRendererPresets);
        var method = presetsType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(string)],
            modifiers: null);

        object? result;
        if (method is not null) {
            result = method.Invoke(null, [null]);
        } else {
            method = presetsType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method is null) {
                return false;
            }

            result = method.Invoke(null, null);
        }

        if (result is not MarkdownRendererOptions resolved) {
            return false;
        }

        options = resolved;
        return true;
    }

    private static bool TryApplyMarkdigCompatibleReaderOptions(MarkdownRendererOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        var createMethod = Type.GetType(
                "OfficeIMO.Markdown.MarkdownReaderOptions, OfficeIMO.Markdown",
                throwOnError: false)?
            .GetMethod(
                "CreateMarkdigCompatible",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

        if (createMethod?.Invoke(null, null) is not MarkdownReaderOptions readerOptions) {
            return false;
        }

        var current = options.ReaderOptions;
        CopyReaderOption(readerOptions, "HtmlBlocks", current.HtmlBlocks);
        CopyReaderOption(readerOptions, "InlineHtml", current.InlineHtml);
        CopyReaderOption(readerOptions, "DisallowFileUrls", current.DisallowFileUrls);
        CopyReaderOption(readerOptions, "AllowDataUrls", current.AllowDataUrls);
        CopyReaderOption(readerOptions, "AllowProtocolRelativeUrls", current.AllowProtocolRelativeUrls);
        CopyReaderOption(readerOptions, "RestrictUrlSchemes", current.RestrictUrlSchemes);
        CopyReaderOption(readerOptions, "AllowedUrlSchemes", current.AllowedUrlSchemes);

        options.ReaderOptions = readerOptions;
        return true;
    }

    private static void CopyReaderOption(object target, string propertyName, object? value) {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite != true) {
            return;
        }

        if (value is null) {
            if (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null) {
                return;
            }

            property.SetValue(target, null);
            return;
        }

        if (!property.PropertyType.IsInstanceOfType(value)) {
            return;
        }

        property.SetValue(target, value);
    }
}
