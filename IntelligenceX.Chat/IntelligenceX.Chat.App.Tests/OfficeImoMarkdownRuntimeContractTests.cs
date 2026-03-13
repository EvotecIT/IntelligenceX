using System;
using System.IO;
using System.Reflection;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the explicit OfficeIMO markdown runtime contract used by package mode and diagnostics.
/// </summary>
public sealed class OfficeImoMarkdownRuntimeContractTests {
    /// <summary>
    /// Verifies renderer diagnostics advertise the current minimum supported package version.
    /// </summary>
    [Fact]
    public void DescribeMarkdownRendererContract_ReportsMinimumPublishedVersion() {
        var description = InvokeContractMethod("DescribeMarkdownRendererContract");

        Assert.Contains("OfficeIMO.MarkdownRenderer", description, StringComparison.Ordinal);
        Assert.Contains("expected>=0.1.9", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies markdown diagnostics advertise the preset-capable package floor.
    /// </summary>
    [Fact]
    public void DescribeMarkdownContract_ReportsNormalizationPresetMinimumVersion() {
        var description = InvokeContractMethod("DescribeMarkdownContract");

        Assert.Contains("OfficeIMO.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=0.5.12", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies DOCX runtime diagnostics advertise the current minimum supported package version.
    /// </summary>
    [Fact]
    public void DescribeWordMarkdownContract_ReportsMinimumPublishedVersion() {
        var description = InvokeContractMethod("DescribeWordMarkdownContract");

        Assert.Contains("OfficeIMO.Word.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=1.0.6", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies renderer option capability probing only enables network support when the runtime exposes it.
    /// </summary>
    [Fact]
    public void TryEnableOptionalRendererNetworkSupport_ReturnsExpectedCapabilityState() {
        var contractType = typeof(OfficeImoMarkdownRuntimeContract);
        var method = contractType!.GetMethod(
            "TryEnableOptionalRendererNetworkSupport",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();

        var enabled = (bool)(method!.Invoke(null, [options]) ?? false);
        var property = options.GetType().GetProperty("Network", BindingFlags.Instance | BindingFlags.Public);
        var networkOptions = property?.GetValue(options);
        var enabledProperty = networkOptions?.GetType().GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public);

        if (enabledProperty?.PropertyType != typeof(bool)) {
            Assert.False(enabled);
            return;
        }

        Assert.True(enabled);
        Assert.True((bool)(enabledProperty.GetValue(networkOptions) ?? false));
    }

    /// <summary>
    /// Verifies the central runtime contract creates the same chat-safe renderer defaults used by the app shell.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_EnablesExpectedVisualDefaults() {
        var contractType = typeof(OfficeImoMarkdownRuntimeContract);
        var method = contractType!.GetMethod(
            "CreateTranscriptRendererOptions",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        var options = Assert.IsType<MarkdownRendererOptions>(method!.Invoke(null, null));

        Assert.True(options.Mermaid.Enabled);
        Assert.True(options.Chart.Enabled);
        Assert.False(options.Math.Enabled);
        Assert.False(options.EnableCodeCopyButtons);
        Assert.False(options.EnableTableCopyButtons);
    }

    /// <summary>
    /// Verifies package-mode version pins match the published OfficeIMO packages required by the app contract.
    /// </summary>
    [Fact]
    public void DirectoryBuildProps_PinsCurrentPublishedOfficeImoPackageVersions() {
        var propsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Directory.Build.props"));
        var props = File.ReadAllText(propsPath);

        Assert.Contains(">0.5.12<", props, StringComparison.Ordinal);
        Assert.Contains(">0.1.9<", props, StringComparison.Ordinal);
        Assert.Contains(">0.6.12<", props, StringComparison.Ordinal);
        Assert.Contains(">1.0.6<", props, StringComparison.Ordinal);
    }

    private static string InvokeContractMethod(string methodName) {
        var contractType = typeof(OfficeImoMarkdownRuntimeContract);
        var method = contractType!.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return (string)(method!.Invoke(null, null) ?? string.Empty);
    }
}

