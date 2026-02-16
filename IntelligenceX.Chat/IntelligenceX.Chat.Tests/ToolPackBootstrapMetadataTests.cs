using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolPackBootstrapMetadataTests {
    [Fact]
    public void NormalizeSourceKind_Throws_WhenSourceKindMissing() {
        Assert.Throws<ArgumentException>(() => ToolPackBootstrap.NormalizeSourceKind(sourceKind: null, descriptorId: "system"));
    }

    [Theory]
    [InlineData("open_source", "open_source")]
    [InlineData("public", "open_source")]
    [InlineData("closed_source", "closed_source")]
    [InlineData("private", "closed_source")]
    [InlineData("builtin", "builtin")]
    public void NormalizeSourceKind_NormalizesKnownValues(string input, string expected) {
        var normalized = ToolPackBootstrap.NormalizeSourceKind(input);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("system", "system")]
    [InlineData("ad", "ad")]
    [InlineData("reviewer_setup", "reviewersetup")]
    [InlineData("event-log", "eventlog")]
    [InlineData("file system", "filesystem")]
    public void NormalizePackId_UsesCanonicalShape(string input, string expected) {
        var normalized = ToolPackBootstrap.NormalizePackId(input);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_IncludesOfficeImoPack_ByDefault() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableFileSystemPack = false,
            EnableSystemPack = false,
            EnableActiveDirectoryPack = false,
            EnablePowerShellPack = false,
            EnableTestimoXPack = false,
            EnableEmailPack = false,
            EnableReviewerSetupPack = false,
            EnableDefaultPluginPaths = false
        });

        Assert.Contains(packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacks_RespectsDisableOfficeImoPack() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableFileSystemPack = false,
            EnableSystemPack = false,
            EnableActiveDirectoryPack = false,
            EnablePowerShellPack = false,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false,
            EnableEmailPack = false,
            EnableReviewerSetupPack = false,
            EnableDefaultPluginPaths = false
        });

        Assert.DoesNotContain(packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegisterAll_AssignsPackIds_ForRegisteredTools() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            EnableFileSystemPack = false,
            EnableSystemPack = false,
            EnableActiveDirectoryPack = false,
            EnablePowerShellPack = false,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false,
            EnableEmailPack = false,
            EnableDefaultPluginPaths = false
        });
        var registry = new ToolRegistry();
        var toolPackIdsByToolName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ToolPackBootstrap.RegisterAll(registry, packs, toolPackIdsByToolName);

        Assert.True(toolPackIdsByToolName.TryGetValue("eventlog_pack_info", out var eventLogPackId));
        Assert.Equal("eventlog", eventLogPackId);

        Assert.True(toolPackIdsByToolName.TryGetValue("reviewer_setup_pack_info", out var reviewerPackId));
        Assert.Equal("reviewersetup", reviewerPackId);
    }
}
