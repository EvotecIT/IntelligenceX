using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies that chat request tool exposure options favor pack-level isolation when possible.
/// </summary>
public sealed class MainWindowToolExposureOptionsTests {
    /// <summary>
    /// Ensures fully disabled packs are emitted as pack-level disables and not redundant per-tool disables.
    /// </summary>
    [Fact]
    public void BuildToolExposureOverridesForRequest_CompressesFullyDisabledPackIntoDisabledPackIds() {
        var toolStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search_users"] = false,
            ["ad_search_groups"] = false,
            ["dnsclientx_query"] = true
        };
        var toolPackIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search_users"] = "active_directory",
            ["ad_search_groups"] = "active_directory",
            ["dnsclientx_query"] = "dnsclientx"
        };

        var result = MainWindow.BuildToolExposureOverridesForRequest(toolStates, toolPackIds);

        Assert.Empty(result.DisabledTools);
        Assert.Equal(new[] { "active_directory" }, result.DisabledPackIds);
    }

    /// <summary>
    /// Ensures partially disabled packs preserve explicit tool-level disables.
    /// </summary>
    [Fact]
    public void BuildToolExposureOverridesForRequest_KeepsDisabledToolWhenPackNotFullyDisabled() {
        var toolStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search_users"] = false,
            ["ad_search_groups"] = true
        };
        var toolPackIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search_users"] = "active_directory",
            ["ad_search_groups"] = "active_directory"
        };

        var result = MainWindow.BuildToolExposureOverridesForRequest(toolStates, toolPackIds);

        Assert.Equal(new[] { "ad_search_users" }, result.DisabledTools);
        Assert.Empty(result.DisabledPackIds);
    }

    /// <summary>
    /// Ensures tools without explicit pack metadata remain in tool-level disable output.
    /// </summary>
    [Fact]
    public void BuildToolExposureOverridesForRequest_KeepsDisabledToolWithoutExplicitPackMetadata() {
        var toolStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["custom_tool"] = false
        };
        var toolPackIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = MainWindow.BuildToolExposureOverridesForRequest(toolStates, toolPackIds);

        Assert.Equal(new[] { "custom_tool" }, result.DisabledTools);
        Assert.Empty(result.DisabledPackIds);
    }

    /// <summary>
    /// Ensures stable sorting and case-insensitive deduplication of pack-level disable output.
    /// </summary>
    [Fact]
    public void BuildToolExposureOverridesForRequest_SortsOutputsAndDeduplicatesPackIdsByComparer() {
        var toolStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["tool_b"] = false,
            ["tool_a"] = false
        };
        var toolPackIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["tool_a"] = "Pack_A",
            ["tool_b"] = "pack_a"
        };

        var result = MainWindow.BuildToolExposureOverridesForRequest(toolStates, toolPackIds);

        Assert.Empty(result.DisabledTools);
        var packId = Assert.Single(result.DisabledPackIds);
        Assert.Equal("pack_a", packId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures request-time pack compression uses canonical shared Chat pack ids for alias metadata.
    /// </summary>
    [Fact]
    public void BuildToolExposureOverridesForRequest_NormalizesAliasPackIdsToCanonicalContractIds() {
        var toolStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search_users"] = false,
            ["ad_search_groups"] = false,
            ["computer_inventory"] = false
        };
        var toolPackIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search_users"] = "ADPlayground",
            ["ad_search_groups"] = "Active Directory",
            ["computer_inventory"] = "ComputerX"
        };

        var result = MainWindow.BuildToolExposureOverridesForRequest(toolStates, toolPackIds);

        Assert.Empty(result.DisabledTools);
        Assert.Equal(new[] { "active_directory", "system" }, result.DisabledPackIds);
    }
}
