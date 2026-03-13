using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
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
        Assert.Contains("expected>=0.2.0", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies markdown diagnostics advertise the preset-capable package floor.
    /// </summary>
    [Fact]
    public void DescribeMarkdownContract_ReportsNormalizationPresetMinimumVersion() {
        var description = InvokeContractMethod("DescribeMarkdownContract");

        Assert.Contains("OfficeIMO.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=0.6.0", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies DOCX runtime diagnostics advertise the current minimum supported package version.
    /// </summary>
    [Fact]
    public void DescribeWordMarkdownContract_ReportsMinimumPublishedVersion() {
        var description = InvokeContractMethod("DescribeWordMarkdownContract");

        Assert.Contains("OfficeIMO.Word.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=1.0.7", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
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
        Assert.True(options.Network.Enabled);
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
        var props = LoadMsBuildProperties(propsPath);

        Assert.Equal("0.6.0", props["OfficeImoMarkdownNuGetVersion"]);
        Assert.Equal("0.2.0", props["OfficeImoMarkdownRendererNuGetVersion"]);
        Assert.Equal("0.6.13", props["OfficeImoExcelNuGetVersion"]);
        Assert.Equal("1.0.7", props["OfficeImoWordMarkdownNuGetVersion"]);
    }

    private static string InvokeContractMethod(string methodName) {
        var contractType = typeof(OfficeImoMarkdownRuntimeContract);
        var method = contractType!.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return (string)(method!.Invoke(null, null) ?? string.Empty);
    }

    private static IReadOnlyDictionary<string, string> LoadMsBuildProperties(string propsPath) {
        using var stream = File.OpenRead(propsPath);
        var document = XDocument.Load(stream);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var propertyGroup in document.Root?.Elements() ?? []) {
            if (!string.Equals(propertyGroup.Name.LocalName, "PropertyGroup", StringComparison.Ordinal)) {
                continue;
            }

            foreach (var property in propertyGroup.Elements()) {
                properties[property.Name.LocalName] = (property.Value ?? string.Empty).Trim();
            }
        }

        return properties;
    }
}

