using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ApplyToolExposureOverrides_UsesEnabledAllowListWhenProvided() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query", "filesystem_read");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, new[] { "dnsclientx_query" }, null });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_AppliesDisabledListAfterEnabledList() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, new[] { "ad_search", "dnsclientx_query" }, new[] { "ad_search" } });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_IgnoresUnknownAndWhitespaceEntries() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, new[] { "  ", "unknown_tool", " dnsclientx_query " }, new[] { " " } });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_ReturnsOriginalSetWhenNoOverridesProvided() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, null, null });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Equal(2, selected.Count);
        Assert.Equal(new[] { "ad_search", "dnsclientx_query" }, selected.Select(static d => d.Name), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyToolExposureOverrides_ExplicitEmptyEnabledTools_DisablesAllTools() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, Array.Empty<string>(), null });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Empty(selected);
    }

    [Fact]
    public void ApplyToolExposureOverrides_WhitespaceOnlyEnabledTools_DisablesAllTools() {
        var defs = BuildToolDefinitions("ad_search", "dnsclientx_query");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, new[] { "  ", "\t" }, null });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Empty(selected);
    }

    [Fact]
    public void ApplyToolExposureOverrides_MatchesEnabledToolsCaseInsensitivelyAndDeduplicatesEntries() {
        var defs = BuildToolDefinitions("dnsclientx_query", "ad_search");

        var result = ApplyToolExposureOverridesMethod.Invoke(
            null,
            new object?[] { defs, new[] { "DNSCLIENTX_QUERY", "dnsclientx_query", " DNSCLIENTX_QUERY " }, null });

        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);
        Assert.Single(selected);
        Assert.Equal("dnsclientx_query", selected[0].Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ToolDefinition> BuildToolDefinitions(params string[] names) {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new List<ToolDefinition>(names.Length);
        for (var i = 0; i < names.Length; i++) {
            tools.Add(new ToolDefinition(names[i], $"{names[i]} description", schema));
        }

        return tools;
    }
}
