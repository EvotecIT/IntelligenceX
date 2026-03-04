using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ApplyToolExposureOverrides_UsesEnabledAllowListWhenProvided() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query", "filesystem_read");

        var selected = InvokeApplyToolExposure(defs, enabledTools: new[] { "dnsclientx_query" });
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_AppliesDisabledListAfterEnabledList() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var selected = InvokeApplyToolExposure(
            defs,
            enabledTools: new[] { "ad_search", "dnsclientx_query" },
            disabledTools: new[] { "ad_search" });
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_IgnoresUnknownAndWhitespaceEntries() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var selected = InvokeApplyToolExposure(
            defs,
            enabledTools: new[] { "  ", "unknown_tool", " dnsclientx_query " },
            disabledTools: new[] { " " });
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_ReturnsOriginalSetWhenNoOverridesProvided() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var selected = InvokeApplyToolExposure(defs);
        Assert.Equal(2, selected.Count);
        Assert.Equal(new[] { "ad_search", "dnsclientx_query" }, selected.Select(static d => d.Name), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_ExplicitEmptyEnabledTools_DisablesAllTools() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var selected = InvokeApplyToolExposure(defs, enabledTools: Array.Empty<string>());
        Assert.Empty(selected);
    }

    [Fact]
    public void ApplyToolExposureOverrides_WhitespaceOnlyEnabledTools_DisablesAllTools() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var selected = InvokeApplyToolExposure(defs, enabledTools: new[] { "  ", "\t" });
        Assert.Empty(selected);
    }

    [Fact]
    public void ApplyToolExposureOverrides_MatchesEnabledToolsCaseInsensitivelyAndDeduplicatesEntries() {
        var defs = BuildToolDefinitions("dnsclientx_query", "ad_search");

        var selected = InvokeApplyToolExposure(
            defs,
            enabledTools: new[] { "DNSCLIENTX_QUERY", "dnsclientx_query", " DNSCLIENTX_QUERY " });
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_UsesEnabledPackAllowListWhenProvided() {
        var defs = BuildToolDefinitionsWithPacks(
            ("ad_search", "active-directory"),
            ("dnsclientx_query", "dnsclientx"),
            ("filesystem_read", "filesystem"));

        var selected = InvokeApplyToolExposure(defs, enabledPackIds: new[] { "dnsclientx" });
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_AppliesDisabledPackAfterEnabledPackAllowList() {
        var defs = BuildToolDefinitionsWithPacks(
            ("ad_search", "active-directory"),
            ("dnsclientx_query", "dnsclientx"));

        var selected = InvokeApplyToolExposure(
            defs,
            enabledPackIds: new[] { "active_directory", "dnsclientx" },
            disabledPackIds: new[] { "active-directory" });
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_ExplicitEmptyEnabledPackIds_DisablesAllTools() {
        var defs = BuildToolDefinitionsWithPacks(
            ("ad_search", "active-directory"),
            ("dnsclientx_query", "dnsclientx"));

        var selected = InvokeApplyToolExposure(defs, enabledPackIds: Array.Empty<string>());
        Assert.Empty(selected);
    }

    [Fact]
    public void ApplyToolExposureOverrides_IntersectsEnabledToolsAndEnabledPackIds() {
        var defs = BuildToolDefinitionsWithPacks(
            ("ad_search", "active-directory"),
            ("dnsclientx_query", "dnsclientx"));

        var selected = InvokeApplyToolExposure(
            defs,
            enabledTools: new[] { "ad_search", "dnsclientx_query" },
            enabledPackIds: new[] { "active_directory" });
        Assert.Single(selected);
        Assert.Equal("ad_search", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ToolDefinition> BuildToolDefinitions(params string[] names) {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new List<ToolDefinition>(names.Length);
        for (var i = 0; i < names.Length; i++) {
            tools.Add(new ToolDefinition(names[i], $"{names[i]} description", schema));
        }

        return tools;
    }

    private static IReadOnlyList<ToolDefinition> BuildToolDefinitionsWithPacks(params (string Name, string PackId)[] items) {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new List<ToolDefinition>(items.Length);
        for (var i = 0; i < items.Length; i++) {
            var item = items[i];
            tools.Add(new ToolDefinition(
                item.Name,
                $"{item.Name} description",
                schema,
                routing: new ToolRoutingContract {
                    PackId = item.PackId
                }));
        }

        return tools;
    }

    private static IReadOnlyList<ToolDefinition> InvokeApplyToolExposure(
        IReadOnlyList<ToolDefinition> definitions,
        string[]? enabledTools = null,
        string[]? disabledTools = null,
        string[]? enabledPackIds = null,
        string[]? disabledPackIds = null) {
        var catalog = ToolOrchestrationCatalog.Build(definitions);
        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { definitions, enabledTools, disabledTools, enabledPackIds, disabledPackIds, catalog });

        return Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
    }
}
