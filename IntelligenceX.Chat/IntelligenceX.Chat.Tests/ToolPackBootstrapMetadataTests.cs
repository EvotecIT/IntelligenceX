using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolPackBootstrapMetadataTests {
    [Fact]
    public void CreateRuntimeBootstrapOptions_MapsSettingsAndRuntimePolicyContext() {
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
            RunAsProfilePath = "C:/temp/runas.json",
            AuthenticationProfilePath = "C:/temp/auth.json"
        });

        var options = ToolPackBootstrap.CreateRuntimeBootstrapOptions(
            new TestPackRuntimeSettings {
                AllowedRoots = new[] { "C:/allowed-a", "C:/allowed-b" },
                AdDomainController = "dc.contoso.local",
                AdDefaultSearchBaseDn = "DC=contoso,DC=local",
                AdMaxResults = 2222,
                EnablePowerShellPack = true,
                PowerShellAllowWrite = true,
                EnableTestimoXPack = false,
                EnableOfficeImoPack = false,
                EnableDnsClientXPack = false,
                EnableDomainDetectivePack = false,
                EnableDefaultPluginPaths = false,
                PluginPaths = new[] { "C:/plugins/a", "C:/plugins/b" }
            },
            runtimePolicyContext);

        Assert.Equal(new[] { "C:/allowed-a", "C:/allowed-b" }, options.AllowedRoots);
        Assert.Equal("dc.contoso.local", options.AdDomainController);
        Assert.Equal("DC=contoso,DC=local", options.AdDefaultSearchBaseDn);
        Assert.Equal(2222, options.AdMaxResults);
        Assert.True(options.EnablePowerShellPack);
        Assert.True(options.PowerShellAllowWrite);
        Assert.False(options.EnableTestimoXPack);
        Assert.False(options.EnableOfficeImoPack);
        Assert.False(options.EnableDnsClientXPack);
        Assert.False(options.EnableDomainDetectivePack);
        Assert.False(options.EnableDefaultPluginPaths);
        Assert.Equal(new[] { "C:/plugins/a", "C:/plugins/b" }, options.PluginPaths);
        Assert.Same(runtimePolicyContext.AuthenticationProbeStore, options.AuthenticationProbeStore);
        Assert.True(options.RequireSuccessfulSmtpProbeForSend);
        Assert.Equal(600, options.SmtpProbeMaxAgeSeconds);
        Assert.Equal(runtimePolicyContext.Options.RunAsProfilePath, options.RunAsProfilePath);
        Assert.Equal(runtimePolicyContext.Options.AuthenticationProfilePath, options.AuthenticationProfilePath);
    }

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
    [InlineData("ad", "active_directory")]
    [InlineData("adplayground", "active_directory")]
    [InlineData("reviewer_setup", "reviewer_setup")]
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
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsDisabledReason_WhenPackDisabledByConfiguration() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
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

        Assert.DoesNotContain(result.Packs, static pack => string.Equals(pack.Descriptor.Id, "officeimo", StringComparison.OrdinalIgnoreCase));

        var officeImo = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
        Assert.False(officeImo.Enabled);
        Assert.Equal("Disabled by runtime configuration.", officeImo.DisabledReason);
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsEnabled_WhenPackLoaded() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            EnableFileSystemPack = false,
            EnableSystemPack = false,
            EnableActiveDirectoryPack = false,
            EnablePowerShellPack = false,
            EnableTestimoXPack = false,
            EnableEmailPack = false,
            EnableReviewerSetupPack = false,
            EnableDefaultPluginPaths = false
        });

        var officeImo = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "officeimo", StringComparison.OrdinalIgnoreCase));
        Assert.True(officeImo.Enabled);
        Assert.True(string.IsNullOrWhiteSpace(officeImo.DisabledReason));
    }

    [Fact]
    public void CreateDefaultReadOnlyPacksWithAvailability_ReportsDisabledReason_ForDnsOpenSourcePacksWhenDisabledByConfiguration() {
        var result = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(new ToolPackBootstrapOptions {
            EnableFileSystemPack = false,
            EnableSystemPack = false,
            EnableActiveDirectoryPack = false,
            EnablePowerShellPack = false,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false,
            EnableDnsClientXPack = false,
            EnableDomainDetectivePack = false,
            EnableEmailPack = false,
            EnableReviewerSetupPack = false,
            EnableDefaultPluginPaths = false
        });

        var dnsClientX = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "dnsclientx", StringComparison.OrdinalIgnoreCase));
        Assert.False(dnsClientX.Enabled);
        Assert.Equal("Disabled by runtime configuration.", dnsClientX.DisabledReason);
        Assert.Equal("open_source", dnsClientX.SourceKind);

        var domainDetective = Assert.Single(result.PackAvailability, static pack =>
            string.Equals(pack.Id, "domaindetective", StringComparison.OrdinalIgnoreCase));
        Assert.False(domainDetective.Enabled);
        Assert.Equal("Disabled by runtime configuration.", domainDetective.DisabledReason);
        Assert.Equal("open_source", domainDetective.SourceKind);
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
        Assert.Equal("reviewer_setup", reviewerPackId);
    }

    [Fact]
    public void RegisterAll_Throws_WhenDistinctPackIdsCollideAfterNormalization() {
        var packs = new IToolPack[] {
            new TestPack("event-log", "EventLog A"),
            new TestPack("event_log", "EventLog B")
        };
        var registry = new ToolRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() => ToolPackBootstrap.RegisterAll(registry, packs));

        Assert.Contains("both normalize to 'eventlog'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatePackMetadataIndexes_Throws_WhenDescriptorIdsCollideAfterNormalization() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var descriptors = new[] {
            new ToolPackDescriptor { Id = "event-log", Name = "EventLog A", Tier = ToolCapabilityTier.ReadOnly, SourceKind = "open_source" },
            new ToolPackDescriptor { Id = "event_log", Name = "EventLog B", Tier = ToolCapabilityTier.ReadOnly, SourceKind = "open_source" }
        };
        var method = typeof(ChatServiceSession).GetMethod("UpdatePackMetadataIndexes", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var ex = Assert.Throws<TargetInvocationException>(() => method!.Invoke(session, new object[] { descriptors }));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);

        Assert.Contains("both normalize to 'eventlog'", inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestPackRuntimeSettings : IToolPackRuntimeSettings {
        public IReadOnlyList<string> AllowedRoots { get; init; } = Array.Empty<string>();
        public string? AdDomainController { get; init; }
        public string? AdDefaultSearchBaseDn { get; init; }
        public int AdMaxResults { get; init; } = 1000;
        public bool EnablePowerShellPack { get; init; }
        public bool PowerShellAllowWrite { get; init; }
        public bool EnableTestimoXPack { get; init; } = true;
        public bool EnableOfficeImoPack { get; init; } = true;
        public bool EnableDnsClientXPack { get; init; } = true;
        public bool EnableDomainDetectivePack { get; init; } = true;
        public bool EnableDefaultPluginPaths { get; init; } = true;
        public IReadOnlyList<string> PluginPaths { get; init; } = Array.Empty<string>();
    }

    private sealed class TestPack : IToolPack {
        public TestPack(string id, string name) {
            Descriptor = new ToolPackDescriptor {
                Id = id,
                Name = name,
                Tier = ToolCapabilityTier.ReadOnly,
                SourceKind = "open_source"
            };
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            _ = registry;
        }
    }
}
