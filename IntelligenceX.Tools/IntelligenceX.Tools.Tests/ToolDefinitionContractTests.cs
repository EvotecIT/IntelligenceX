using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.DnsClientX;
using IntelligenceX.Tools.DomainDetective;
using IntelligenceX.Tools.Email;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolDefinitionContractTests {
    [Fact]
    public void RegisteredPacks_ShouldExposeUniqueToolsWithClosedObjectSchemas() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterTestimoXPack(new TestimoXToolOptions());
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());

        var definitions = registry.GetDefinitions();
        Assert.NotEmpty(definitions);

        var duplicateNames = definitions
            .GroupBy(static d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToArray();
        Assert.Empty(duplicateNames);

        foreach (var definition in definitions) {
            Assert.NotNull(definition.Parameters);

            var schema = definition.Parameters!;
            Assert.Equal("object", schema.GetString("type"));
            Assert.NotNull(schema.GetObject("properties"));
            Assert.False(schema.GetBoolean("additionalProperties", defaultValue: true));
        }
    }

    [Fact]
    public void RegisteredTools_ShouldExposeCategoryAndSelectionTags() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions { Enabled = true });
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());

        var definitions = registry.GetDefinitions();
        Assert.NotEmpty(definitions);

        foreach (var definition in definitions) {
            Assert.False(string.IsNullOrWhiteSpace(definition.Category));
            Assert.Contains(definition.Category!, definition.Tags, StringComparer.OrdinalIgnoreCase);

            if (definition.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase)) {
                Assert.Contains("pack_info", definition.Tags, StringComparer.OrdinalIgnoreCase);
            }
        }

        var writeCapable = definitions.Where(static d => d.WriteGovernance?.IsWriteCapable == true).ToArray();
        Assert.NotEmpty(writeCapable);
        foreach (var definition in writeCapable) {
            Assert.Contains("write", definition.Tags, StringComparer.OrdinalIgnoreCase);
        }

        var authAware = definitions.Where(static d => d.Authentication?.IsAuthenticationAware == true).ToArray();
        Assert.NotEmpty(authAware);
        foreach (var definition in authAware) {
            Assert.Contains("auth", definition.Tags, StringComparer.OrdinalIgnoreCase);
            if (definition.Authentication!.RequiresAuthentication) {
                Assert.Contains("auth_required", definition.Tags, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void HighPriorityTools_ShouldKeepExplicitSelectionMetadataOverrides() {
        var required = ToolSelectionMetadata.GetRequiredExplicitOverrideToolNames();
        Assert.NotEmpty(required);

        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in required) {
            Assert.True(definitionsByName.TryGetValue(toolName, out var definition), $"Missing required high-priority tool '{toolName}'.");
            Assert.True(ToolSelectionMetadata.HasExplicitOverride(toolName), $"Expected explicit override for '{toolName}'.");
            Assert.Contains("routing:explicit", definition!.Tags, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("scope:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("operation:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("entity:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("risk:", StringComparison.OrdinalIgnoreCase));

            if (definition.WriteGovernance?.IsWriteCapable == true) {
                Assert.Contains("risk:high", definition.Tags, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Enrich_ShouldNotMutateToolNameOrCanonicalName() {
        var aliasDefinition = new ToolDefinition(
            name: "system_info_alias",
            description: "Alias",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            aliasOf: "system_info");

        var enriched = ToolSelectionMetadata.Enrich(aliasDefinition, toolType: null);

        Assert.Equal("system_info_alias", enriched.Name);
        Assert.Equal("system_info", enriched.AliasOf);
        Assert.Equal(aliasDefinition.CanonicalName, enriched.CanonicalName);
    }

    [Fact]
    public void Enrich_ShouldBeIdempotentAfterNormalization() {
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String())).NoAdditionalProperties(),
            category: "General",
            tags: new[] { "TagB", "tagA", "TAGA" });

        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);
        var enrichedAgain = ToolSelectionMetadata.Enrich(enriched, toolType: null);

        Assert.Same(enriched, enrichedAgain);
        Assert.Equal(enriched.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), enriched.Tags);
    }

    [Fact]
    public void Enrich_ShouldIgnoreConflictingInputTaxonomyTags() {
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            tags: new[] {
                "scope:domain",
                "operation:search",
                "entity:user",
                "risk:high",
                "routing:explicit"
            });

        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

        Assert.Contains("scope:general", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(enriched.Tags, "scope:");
        AssertSingleTaxonomyTag(enriched.Tags, "operation:");
        AssertSingleTaxonomyTag(enriched.Tags, "entity:");
        AssertSingleTaxonomyTag(enriched.Tags, "risk:");
        AssertSingleTaxonomyTag(enriched.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldMergeAndSortEnrichedTagsDeterministically() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "system_info",
                description: "System info",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "system", "inventory" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "host_info_alias",
            tags: new[] { "host", "HOST" });

        Assert.Equal(alias.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), alias.Tags);
        Assert.Equal(
            alias.Tags.Count,
            alias.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("host", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldPreferAliasTaxonomyTagOverrides() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: ToolSchema.Object().NoAdditionalProperties()),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] {
                "scope:domain",
                "operation:search",
                "entity:user",
                "risk:high",
                "routing:explicit"
            });

        Assert.Contains("scope:domain", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:user", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("operation:probe", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldProduceStableTaxonomyTagOrderAcrossOverrideInputOrdering() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "zeta", "alpha" }),
            toolType: null);

        var aliasA = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias_a",
            tags: new[] {
                "routing:explicit",
                "risk:high",
                "scope:domain",
                "operation:search"
            });

        var aliasB = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias_b",
            tags: new[] {
                "operation:search",
                "scope:domain",
                "risk:high",
                "routing:explicit"
            });

        Assert.True(aliasA.Tags.SequenceEqual(aliasB.Tags, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(aliasA.Tags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase), aliasA.Tags);
        Assert.Contains("scope:domain", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", aliasA.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(aliasA.Tags, "scope:");
        AssertSingleTaxonomyTag(aliasA.Tags, "operation:");
        AssertSingleTaxonomyTag(aliasA.Tags, "entity:");
        AssertSingleTaxonomyTag(aliasA.Tags, "risk:");
        AssertSingleTaxonomyTag(aliasA.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldRetainCanonicalTaxonomy_WhenAliasDoesNotOverrideKeys() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "zeta", "alpha" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "host", "HOST", "inventory" });

        Assert.Contains("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldIgnoreMalformedTaxonomyOverrideTags() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "zeta", "alpha" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "risk:", "routing:", "scope:   ", "operation:search" });

        Assert.DoesNotContain("risk:", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("routing:", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:general", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "scope:");
        AssertSingleTaxonomyTag(alias.Tags, "operation:");
        AssertSingleTaxonomyTag(alias.Tags, "entity:");
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void CreateAliasDefinition_ShouldApplyCaseInsensitiveTaxonomyOverrides() {
        var canonical = ToolSelectionMetadata.Enrich(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "alpha" }),
            toolType: null);

        var alias = canonical.CreateAliasDefinition(
            aliasName: "custom_probe_alias",
            tags: new[] { "RISK:LOW", "risk:high", "ROUTING:EXPLICIT", "routing:explicit" });

        Assert.Contains("risk:high", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("risk:low", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", alias.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("routing:inferred", alias.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(alias.Tags, "risk:");
        AssertSingleTaxonomyTag(alias.Tags, "routing:");
    }

    [Fact]
    public void Register_WithReplaceExisting_ShouldPreserveSingleTaxonomyTagPerKey() {
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe v1",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "scope:host", "operation:read", "routing:explicit", "custom" })));

        registry.Register(new StubTool(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe v2",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "scope:domain", "operation:query", "routing:explicit", "custom2" })),
            replaceExisting: true);

        Assert.True(registry.TryGetDefinition("custom_probe", out var definition));
        Assert.NotNull(definition);
        AssertSingleTaxonomyTag(definition!.Tags, "scope:");
        AssertSingleTaxonomyTag(definition.Tags, "operation:");
        AssertSingleTaxonomyTag(definition.Tags, "entity:");
        AssertSingleTaxonomyTag(definition.Tags, "risk:");
        AssertSingleTaxonomyTag(definition.Tags, "routing:");
        Assert.Contains("scope:general", definition.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", definition.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", definition.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorePackToolNames_ShouldRemainStable() {
        var registry = new ToolRegistry();
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        var expected = new[] {
            "ad_pack_info",
            "ad_environment_discover",
            "ad_scope_discovery",
            "ad_handoff_prepare",
            "ad_object_resolve",
            "ad_stale_accounts",
            "ad_spn_stats",
            "eventlog_pack_info",
            "eventlog_named_events_catalog",
            "eventlog_named_events_query",
            "eventlog_timeline_explain",
            "eventlog_timeline_query",
            "eventlog_top_events",
            "eventlog_evtx_security_summary",
            "eventlog_evtx_query",
            "eventlog_evtx_stats"
        };

        foreach (var name in expected) {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void SystemPack_ShouldExposeFirewallToolNames() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("system_pack_info", names);
        Assert.Contains("system_firewall_rules", names);
        Assert.Contains("system_firewall_profiles", names);
        Assert.Contains("system_security_options", names);
        Assert.Contains("system_logical_disks_list", names);
        Assert.Contains("system_disks_list", names);
        Assert.Contains("system_devices_summary", names);
        Assert.Contains("system_hardware_identity", names);
        Assert.Contains("system_hardware_summary", names);
        Assert.Contains("system_features_list", names);
    }

    [Fact]
    public void FileSystemPack_ShouldExposePackInfoTool() {
        var registry = new ToolRegistry();
        registry.RegisterFileSystemPack(new FileSystemToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("fs_pack_info", names);
    }

    [Fact]
    public void PowerShellPack_ShouldExposeRuntimeToolNames() {
        var registry = new ToolRegistry();
        registry.RegisterPowerShellPack(new PowerShellToolOptions {
            Enabled = true
        });

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("powershell_pack_info", names);
        Assert.Contains("powershell_environment_discover", names);
        Assert.Contains("powershell_hosts", names);
        Assert.Contains("powershell_run", names);
    }

    [Fact]
    public void WriteCapableTools_ShouldDeclareGovernanceContracts() {
        var registry = new ToolRegistry();
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(definitionsByName.TryGetValue("powershell_run", out var powershellRun));
        Assert.NotNull(powershellRun.WriteGovernance);
        Assert.True(powershellRun.WriteGovernance!.IsWriteCapable);
        Assert.True(powershellRun.WriteGovernance.RequiresGovernanceAuthorization);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, powershellRun.WriteGovernance.GovernanceContractId);

        Assert.True(definitionsByName.TryGetValue("email_smtp_send", out var smtpSend));
        Assert.NotNull(smtpSend.WriteGovernance);
        Assert.True(smtpSend.WriteGovernance!.IsWriteCapable);
        Assert.True(smtpSend.WriteGovernance.RequiresGovernanceAuthorization);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, smtpSend.WriteGovernance.GovernanceContractId);
        Assert.NotNull(smtpSend.Authentication);
        Assert.True(smtpSend.Authentication!.IsAuthenticationAware);
        Assert.True(smtpSend.Authentication.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, smtpSend.Authentication.AuthenticationContractId);
        Assert.Equal(ToolAuthenticationMode.HostManaged, smtpSend.Authentication.Mode);
        Assert.True(smtpSend.Authentication.SupportsConnectivityProbe);
        Assert.Equal("email_smtp_probe", smtpSend.Authentication.ProbeToolName);
        Assert.Contains(ToolAuthenticationArgumentNames.ProbeId, smtpSend.Authentication.GetSchemaArgumentNames());
        var smtpSendProperties = smtpSend.Parameters?.GetObject("properties");
        Assert.NotNull(smtpSendProperties);
        Assert.NotNull(smtpSendProperties!.GetObject(ToolAuthenticationArgumentNames.ProbeId));

        Assert.True(definitionsByName.TryGetValue("email_smtp_probe", out var smtpProbe));
        Assert.Null(smtpProbe.WriteGovernance);
        Assert.NotNull(smtpProbe.Authentication);
        Assert.True(smtpProbe.Authentication!.IsAuthenticationAware);
        Assert.True(smtpProbe.Authentication.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, smtpProbe.Authentication.AuthenticationContractId);
        Assert.Equal(ToolAuthenticationMode.HostManaged, smtpProbe.Authentication.Mode);
    }

    [Fact]
    public void WriteCapableTools_ShouldExposeCanonicalGovernanceMetadataArguments() {
        var registry = new ToolRegistry();
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());

        var writeCapableDefinitions = registry.GetDefinitions()
            .Where(static definition => definition.WriteGovernance?.IsWriteCapable == true)
            .ToArray();
        Assert.NotEmpty(writeCapableDefinitions);

        foreach (var definition in writeCapableDefinitions) {
            var properties = definition.Parameters?.GetObject("properties");
            Assert.NotNull(properties);
            Assert.NotNull(properties!.GetObject(ToolWriteGovernanceArgumentNames.OperationId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ExecutionId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ActorId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ChangeReason));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackPlanId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackProviderId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.AuditCorrelationId));
        }
    }

    [Fact]
    public void TestimoXPack_ShouldExposeRuleCatalogAndRunTools() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions {
            Enabled = true
        });

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("testimox_pack_info", names);
        Assert.Contains("testimox_rules_list", names);
        Assert.Contains("testimox_rules_run", names);
    }

    [Fact]
    public void OpenSourceDnsAndDomainPacks_ShouldExposePackInfoAndCoreTools() {
        var registry = new ToolRegistry();
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("dnsclientx_pack_info", names);
        Assert.Contains("dnsclientx_query", names);
        Assert.Contains("dnsclientx_ping", names);
        Assert.Contains("domaindetective_pack_info", names);
        Assert.Contains("domaindetective_checks_catalog", names);
        Assert.Contains("domaindetective_domain_summary", names);
        Assert.Contains("domaindetective_network_probe", names);
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("{}");
        }
    }
}
