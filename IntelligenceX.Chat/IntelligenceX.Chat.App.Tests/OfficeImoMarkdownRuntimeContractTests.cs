using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.App;
using System.Xml.Linq;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the explicit OfficeIMO markdown runtime contract used by package mode, diagnostics, and transcript preprocessing.
/// </summary>
public sealed class OfficeImoMarkdownRuntimeContractTests {
    /// <summary>
    /// Verifies renderer diagnostics advertise the current minimum supported package version.
    /// </summary>
    [Fact]
    public void DescribeMarkdownRendererContract_ReportsMinimumPublishedVersion() {
        var description = InvokeContractMethod("DescribeMarkdownRendererContract");

        Assert.Contains("OfficeIMO.MarkdownRenderer", description, StringComparison.Ordinal);
        Assert.Contains("expected>=0.2.1", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies markdown diagnostics advertise the preset-capable package floor.
    /// </summary>
    [Fact]
    public void DescribeMarkdownContract_ReportsNormalizationPresetMinimumVersion() {
        var description = InvokeContractMethod("DescribeMarkdownContract");

        Assert.Contains("OfficeIMO.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=0.6.1", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies DOCX runtime diagnostics advertise the current minimum supported package version.
    /// </summary>
    [Fact]
    public void DescribeWordMarkdownContract_ReportsMinimumPublishedVersion() {
        var description = InvokeContractMethod("DescribeWordMarkdownContract");

        Assert.Contains("OfficeIMO.Word.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=1.0.8", description, StringComparison.Ordinal);
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
    /// Verifies the IX runtime contract builds on the explicit OfficeIMO transcript preset rather than re-composing chat behavior locally.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_ComposesOnTopOfExplicitOfficeImoTranscriptPreset() {
        var baseline = TryCreateExplicitOfficeImoTranscriptDesktopShell();
        if (baseline == null) {
            return;
        }

        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();

        Assert.Equal(baseline.HtmlOptions.Style, options.HtmlOptions.Style);
        Assert.Equal(baseline.HtmlOptions.CssScopeSelector, options.HtmlOptions.CssScopeSelector);
        Assert.Equal(baseline.EnableCodeCopyButtons, options.EnableCodeCopyButtons);
        Assert.Equal(baseline.EnableTableCopyButtons, options.EnableTableCopyButtons);
        Assert.Equal(baseline.MarkdownPreProcessors.Count, options.MarkdownPreProcessors.Count);
        Assert.Equal(baseline.Mermaid.Enabled, options.Mermaid.Enabled);
        Assert.Equal(baseline.Chart.Enabled, options.Chart.Enabled);
        Assert.Equal(baseline.Network.Enabled, options.Network.Enabled);
    }

    /// <summary>
    /// Verifies the repo still declares OfficeIMO package pins for package-mode adoption, even when local checkout mode is preferred for explicit transcript API work.
    /// </summary>
    [Fact]
    public void DirectoryBuildProps_PinsCurrentPublishedOfficeImoPackageVersions() {
        var propsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Directory.Build.props"));
        var props = LoadMsBuildProperties(propsPath);

        Assert.Equal("0.6.1", props["OfficeImoMarkdownNuGetVersion"]);
        Assert.Equal("0.2.1", props["OfficeImoMarkdownRendererNuGetVersion"]);
        Assert.Equal("0.6.14", props["OfficeImoExcelNuGetVersion"]);
        Assert.Equal("1.0.8", props["OfficeImoWordMarkdownNuGetVersion"]);
    }

    /// <summary>
    /// Verifies cached-evidence transport markers are stripped and legacy network payloads are upgraded through the shared IX adapter path.
    /// </summary>
    [Fact]
    public void ApplyTranscriptMarkdownPreProcessors_StripsCachedEvidenceMarkerAndUpgradesNetworkPayload() {
        if (!SupportsExplicitTranscriptPreProcessorBehavior()) {
            return;
        }

        const string markdown = """
ix:cached-tool-evidence:v1

```json
{
  "nodes": [
    { "id": "forest_ad.evotec.xyz", "label": "Forest: ad.evotec.xyz" }
  ],
  "edges": [
    { "source": "forest_ad.evotec.xyz", "target": "domain_ad.evotec.xyz", "label": "contains" }
  ]
}
```
""";

        var normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);

        Assert.DoesNotContain("cached-tool-evidence", normalized, System.StringComparison.Ordinal);
        Assert.Contains("```ix-network", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies cached-evidence visual upgrades are scoped to recognized IX payloads and leave ordinary JSON examples untouched.
    /// </summary>
    [Fact]
    public void ApplyTranscriptMarkdownPreProcessors_UpgradesCachedEvidenceChartAndDataViewPayloadsOnly() {
        if (!SupportsExplicitTranscriptPreProcessorBehavior()) {
            return;
        }

        const string markdown = """
ix:cached-tool-evidence:v1

```json
{ "type": "bar", "data": { "labels": [ "A" ], "datasets": [ { "label": "Count", "data": [ 1 ] } ] } }
```

```json
{ "kind": "ix_tool_dataview_v1", "rows": [ [ "Server", "Fails" ], [ "AD0", "0" ] ] }
```

Standalone example:

```json
{ "hello": "world" }
```
""";

        var normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);

        Assert.Contains("```ix-chart", normalized, System.StringComparison.Ordinal);
        Assert.Contains("```ix-dataview", normalized, System.StringComparison.Ordinal);
        Assert.Contains("```json\n{ \"hello\": \"world\" }\n```", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies legacy cached-evidence tool slug bullet headings are normalized through the shared OfficeIMO transcript adapter.
    /// </summary>
    [Fact]
    public void ApplyTranscriptMarkdownPreProcessors_PromotesLegacyToolHeadingBullets() {
        if (!SupportsExplicitTranscriptPreProcessorBehavior()) {
            return;
        }

        const string markdown = """
[Cached evidence fallback]

Recent evidence:
- eventlog_top_events: ### Top 30 recent events (preview)
""";

        var normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);

        Assert.Contains("### Top 30 recent events (preview)", normalized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("eventlog_top_events:", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies duplicate legacy tool slug headings are removed through the shared OfficeIMO transcript adapter.
    /// </summary>
    [Fact]
    public void ApplyTranscriptMarkdownPreProcessors_RemovesDuplicateLegacyToolSlugHeading() {
        if (!SupportsExplicitTranscriptPreProcessorBehavior()) {
            return;
        }

        const string markdown = """
[Cached evidence fallback]

#### ad_environment_discover
### Active Directory: Environment Discovery
""";

        var normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);

        Assert.DoesNotContain("#### ad_environment_discover", normalized, System.StringComparison.Ordinal);
        Assert.Contains("### Active Directory: Environment Discovery", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies plain legacy IX network payloads upgrade through the shared OfficeIMO transcript adapter even without a transport marker.
    /// </summary>
    [Fact]
    public void ApplyTranscriptMarkdownPreProcessors_UpgradesPlainLegacyJsonNetworkFenceWithoutTransportMarker() {
        if (!SupportsExplicitTranscriptPreProcessorBehavior()) {
            return;
        }

        const string markdown = """
```json
{"nodes":[{"id":"A","label":"Forest: ad.evotec.xyz"}],"edges":[{"source":"forest_ad.evotec.xyz","target":"domain_ad.evotec.xyz","label":"contains"}]}
```
""";

        var normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);

        Assert.Contains("```ix-network", normalized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("```json", normalized, System.StringComparison.Ordinal);
    }

    private static string InvokeContractMethod(string methodName) {
        var contractType = typeof(OfficeImoMarkdownRuntimeContract);
        var method = contractType!.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return (string)(method!.Invoke(null, null) ?? string.Empty);
    }

    private static MarkdownRendererOptions? TryCreateExplicitOfficeImoTranscriptDesktopShell() {
        var method = ResolveOptionalBaseHrefFactory("CreateIntelligenceXTranscriptDesktopShell");
        if (method == null) {
            return null;
        }

        var parameters = method.GetParameters();
        return method.Invoke(null, parameters.Length == 0 ? null : [null]) as MarkdownRendererOptions;
    }

    private static bool SupportsExplicitTranscriptPreProcessorBehavior() {
        return Type.GetType(
                   "OfficeIMO.MarkdownRenderer.MarkdownRendererIntelligenceXLegacyMigration, OfficeIMO.MarkdownRenderer",
                   throwOnError: false) != null
               && Type.GetType(
                   "OfficeIMO.Markdown.MarkdownTranscriptPreparation, OfficeIMO.Markdown",
                   throwOnError: false) != null;
    }

    private static System.Reflection.MethodInfo? ResolveOptionalBaseHrefFactory(string methodName) {
        var methods = typeof(MarkdownRendererPresets).GetMethods(BindingFlags.Public | BindingFlags.Static);
        for (var i = 0; i < methods.Length; i++) {
            var method = methods[i];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)
                || !typeof(MarkdownRendererOptions).IsAssignableFrom(method.ReturnType)) {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 0) {
                return method;
            }

            if (parameters.Length == 1
                && parameters[0].ParameterType == typeof(string)
                && parameters[0].IsOptional) {
                return method;
            }
        }

        return null;
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
