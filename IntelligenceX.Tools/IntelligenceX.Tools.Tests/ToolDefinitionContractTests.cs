using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using IntelligenceX.Tools.OfficeIMO;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.ReviewerSetup;
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
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());
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
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions { Enabled = true });
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

    [Theory]
    [InlineData("ad", "active_directory")]
    [InlineData("adplayground", "active_directory")]
    [InlineData("event-logs", "eventlog")]
    [InlineData("eventviewerx", "eventlog")]
    [InlineData("testimoxpack", "testimox")]
    [InlineData("reviewer setup", "reviewer_setup")]
    [InlineData("my-pack", "my_pack")]
    public void NormalizePackId_ShouldCanonicalizeKnownAliases_AndCompactUnknownIds(string input, string expected) {
        var normalized = ToolSelectionMetadata.NormalizePackId(input);
        Assert.Equal(expected, normalized);
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
    public void Enrich_ShouldNotBackfillRoutingPackOrFamily_WhenRoutingSourceIsExplicit() {
        var definition = new ToolDefinition(
            name: "ad_custom_probe",
            description: "Probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            category: "active_directory",
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = string.Empty,
                Role = ToolRoutingTaxonomy.RoleOperational,
                DomainIntentFamily = string.Empty,
                DomainIntentActionId = string.Empty
            });

        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);
        var routing = Assert.IsType<ToolRoutingContract>(enriched.Routing);

        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
        Assert.Equal(string.Empty, routing.PackId);
        Assert.Equal(string.Empty, routing.DomainIntentFamily);
        Assert.Equal(string.Empty, routing.DomainIntentActionId);
    }

    [Fact]
    public void Enrich_ShouldInferRoutingPackAndFamilyFromTags_WhenRoutingSourceIsInferred() {
        var definition = new ToolDefinition(
            name: "ad_custom_probe",
            description: "Probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            category: "active_directory",
            tags: new[] { "pack:active_directory", "domain_family:ad_domain" });

        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);
        var routing = Assert.IsType<ToolRoutingContract>(enriched.Routing);

        Assert.Equal("active_directory", routing.PackId, ignoreCase: true);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, routing.DomainIntentFamily);
        Assert.Equal(ToolSelectionMetadata.DomainIntentActionIdAd, routing.DomainIntentActionId);
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
                tags: new[] { "scope:host", "operation:read", "routing:explicit", "pack:custom", "custom" })));

        registry.Register(new StubTool(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe v2",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                tags: new[] { "scope:domain", "operation:query", "routing:explicit", "pack:custom", "custom2" })),
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
        Assert.Contains("system_metrics_summary", names);
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
    public void FileSystemPack_ShouldExposeSemanticRoutingRoles_AndLocalReadHandoffs() {
        var registry = new ToolRegistry();
        registry.RegisterFileSystemPack(new FileSystemToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRoutingRole(definitionsByName, "fs_list", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "fs_search", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "fs_read", ToolRoutingTaxonomy.RoleOperational);

        var fsListHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["fs_list"].Handoff);
        Assert.Contains(
            fsListHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "entries[].path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));

        var fsSearchHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["fs_search"].Handoff);
        Assert.Contains(
            fsSearchHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "matches[].path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
    }

    [Fact]
    public void EventLogPack_ShouldExposeEvtxArtifactAndAdHandoffs() {
        var registry = new ToolRegistry();
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRoutingRole(definitionsByName, "eventlog_evtx_find", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "eventlog_evtx_security_summary", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "eventlog_evtx_query", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "eventlog_evtx_stats", ToolRoutingTaxonomy.RoleDiagnostic);

        var evtxFindHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["eventlog_evtx_find"].Handoff);
        Assert.Contains(
            evtxFindHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_evtx_query", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "files[].path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
        Assert.Contains(
            evtxFindHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_evtx_security_summary", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "files[].path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
        Assert.Contains(
            evtxFindHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_evtx_stats", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "files[].path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));

        var evtxSecuritySummaryHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["eventlog_evtx_security_summary"].Handoff);
        Assert.Contains(
            evtxSecuritySummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "meta/entity_handoff", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "entity_handoff", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
        Assert.Contains(
            evtxSecuritySummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "meta/entity_handoff/computer_candidates/0/value", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "domain_controller", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));

        var evtxQueryRecovery = Assert.IsType<ToolRecoveryContract>(definitionsByName["eventlog_evtx_query"].Recovery);
        Assert.Contains("eventlog_channels_list", evtxQueryRecovery.RecoveryToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("eventlog_channel_list", evtxQueryRecovery.RecoveryToolNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventLogPack_DefaultSetupAndRecoveryHelpers_ShouldResolveToRegisteredTools() {
        var registry = new ToolRegistry();
        registry.RegisterEventLogPack(new EventLogToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitionsByName.Values.Where(static definition =>
                     definition.Name.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase))) {
            if (definition.Setup is ToolSetupContract setup && !string.IsNullOrWhiteSpace(setup.SetupToolName)) {
                Assert.True(
                    definitionsByName.ContainsKey(setup.SetupToolName),
                    $"EventLog setup helper '{setup.SetupToolName}' for '{definition.Name}' is not a registered tool.");
                Assert.DoesNotContain("eventlog_channel_list", setup.SetupToolName, StringComparison.OrdinalIgnoreCase);
            }

            if (definition.Recovery is not ToolRecoveryContract recovery) {
                continue;
            }

            foreach (var recoveryToolName in recovery.RecoveryToolNames.Where(static name => !string.IsNullOrWhiteSpace(name))) {
                Assert.True(
                    definitionsByName.ContainsKey(recoveryToolName),
                    $"EventLog recovery helper '{recoveryToolName}' for '{definition.Name}' is not a registered tool.");
                Assert.DoesNotContain("eventlog_channel_list", recoveryToolName, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void OfficeImoPack_ShouldExposeLocalSourceReadHandoff() {
        var registry = new ToolRegistry();
        registry.RegisterOfficeImoPack(new OfficeImoToolOptions());
        registry.RegisterFileSystemPack(new FileSystemToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRoutingRole(definitionsByName, "officeimo_read", ToolRoutingTaxonomy.RoleOperational);

        var officeReadHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["officeimo_read"].Handoff);
        Assert.Contains(
            officeReadHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "files[]", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
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
    public void ActiveDirectoryPack_ShouldExposeSemanticRoutingRoles_ForRepresentativeTools() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterSystemPack(new SystemToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRoutingRole(definitionsByName, "ad_search", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "ad_object_resolve", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "ad_ldap_query", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "ad_spn_search", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "ad_domain_statistics", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "ad_spn_stats", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "ad_password_policy", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "ad_groups_list", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "ad_search_facets", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "ad_trust", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "ad_object_get", ToolRoutingTaxonomy.RoleOperational);
        AssertRoutingRole(definitionsByName, "ad_handoff_prepare", ToolRoutingTaxonomy.RoleOperational);

        var environmentDiscoverHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["ad_environment_discover"].Handoff);
        Assert.Contains(
            environmentDiscoverHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "context/domain_controller", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "computer_name", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired)
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "domain_controllers/0/value", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "computer_name", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));
        Assert.Contains(
            environmentDiscoverHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_metrics_summary", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "context/domain_controller", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "computer_name", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired)
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "domain_controllers/0/value", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "computer_name", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));
    }

    [Fact]
    public void TestimoXPacks_ShouldExposeSemanticRoutingRoles_ForRepresentativeTools() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions { Enabled = true });
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions { Enabled = true });

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRoutingRole(definitionsByName, "testimox_rules_list", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "testimox_source_query", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "testimox_baseline_crosswalk", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "testimox_rules_run", ToolRoutingTaxonomy.RoleOperational);
        AssertRoutingIntent(definitionsByName, "testimox_run_summary", "security_posture", "act_domain_scope_security_posture");
        AssertRoutingRole(definitionsByName, "testimox_analytics_diagnostics_get", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "testimox_history_query", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "testimox_report_job_history", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "testimox_report_data_snapshot_get", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingRole(definitionsByName, "testimox_report_snapshot_get", ToolRoutingTaxonomy.RoleResolver);
        AssertRoutingIntent(definitionsByName, "testimox_history_query", "monitoring_artifacts", "act_domain_scope_monitoring_artifacts");

        var runSummaryHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["testimox_run_summary"].Handoff);
        Assert.Contains(
            runSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            runSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            runSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase));

        var analyticsDiagnosticsHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["testimox_analytics_diagnostics_get"].Handoff);
        Assert.Contains(
            analyticsDiagnosticsHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "snapshot_path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            analyticsDiagnosticsHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "slow_probes[].target", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "computer_name", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            analyticsDiagnosticsHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "slow_probes[].target", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "machine_name", StringComparison.OrdinalIgnoreCase)));

        var historyQueryHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["testimox_history_query"].Handoff);
        Assert.Contains(
            historyQueryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            historyQueryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase));

        var reportJobHistoryHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["testimox_report_job_history"].Handoff);
        Assert.Contains(
            reportJobHistoryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "testimox_analytics", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "testimox_report_data_snapshot_get", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "history_directory", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "history_directory", StringComparison.OrdinalIgnoreCase))
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "jobs[].report_key", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "report_key", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            reportJobHistoryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "testimox_analytics", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "testimox_report_snapshot_get", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "history_directory", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "history_directory", StringComparison.OrdinalIgnoreCase))
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "jobs[].report_key", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "report_key", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            reportJobHistoryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "jobs[].report_path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)));
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
    public void TestimoXPack_ShouldExposeProfileInventoryCrosswalkCatalogAndRunTools() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions {
            Enabled = true
        });

        var definitions = registry.GetDefinitions();
        var names = new HashSet<string>(
            definitions.Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("testimox_pack_info", names);
        Assert.Contains("testimox_runs_list", names);
        Assert.Contains("testimox_run_summary", names);
        Assert.Contains("testimox_baselines_list", names);
        Assert.Contains("testimox_baseline_compare", names);
        Assert.Contains("testimox_profiles_list", names);
        Assert.Contains("testimox_rule_inventory", names);
        Assert.Contains("testimox_source_query", names);
        Assert.Contains("testimox_baseline_crosswalk", names);
        Assert.Contains("testimox_rules_list", names);
        Assert.Contains("testimox_rules_run", names);

        var definitionsByName = definitions.ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);
        var runsList = Assert.IsType<ToolDefinition>(definitionsByName["testimox_runs_list"]);
        var runSummary = Assert.IsType<ToolDefinition>(definitionsByName["testimox_run_summary"]);
        var baselinesList = Assert.IsType<ToolDefinition>(definitionsByName["testimox_baselines_list"]);
        var baselineCompare = Assert.IsType<ToolDefinition>(definitionsByName["testimox_baseline_compare"]);
        var profilesList = Assert.IsType<ToolDefinition>(definitionsByName["testimox_profiles_list"]);
        var ruleInventory = Assert.IsType<ToolDefinition>(definitionsByName["testimox_rule_inventory"]);
        var sourceQuery = Assert.IsType<ToolDefinition>(definitionsByName["testimox_source_query"]);
        var baselineCrosswalk = Assert.IsType<ToolDefinition>(definitionsByName["testimox_baseline_crosswalk"]);
        var rulesList = Assert.IsType<ToolDefinition>(definitionsByName["testimox_rules_list"]);
        var rulesRun = Assert.IsType<ToolDefinition>(definitionsByName["testimox_rules_run"]);

        Assert.Contains("history", runsList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("store", runsList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:store_directory,run_id_contains,completed_only", runsList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("summary", runSummary.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback:requires_selection", runSummary.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_selection_keys:store_directory,run_id", runSummary.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:store_directory,run_id,scope_group,rule_name_contains,scope_id_contains", runSummary.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("catalog", baselinesList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:search_text,vendor_ids,product_ids,version_wildcard,baseline_ids,id_patterns", baselinesList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("compare", baselineCompare.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:product_id,vendor_ids,version_wildcard,latest_only,only_diff,search_text", baselineCompare.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("profiles", profilesList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("inventory", ruleInventory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("provenance", sourceQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback:requires_selection", sourceQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_selection_keys:search_text,rule_names,rule_name_patterns,categories,tags,source_types,rule_origin,migration_states", sourceQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:search_text,rule_origin,rule_names,rule_name_patterns,categories,tags,source_types,migration_states,profile", sourceQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("crosswalk", baselineCrosswalk.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:search_text,rule_origin,categories,tags,source_types,profile,rule_names,rule_name_patterns", baselineCrosswalk.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:search_text,rule_origin,categories,tags,source_types,migration_states,profile", ruleInventory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:search_text,rule_origin,categories,tags,source_types", rulesList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback:requires_selection", rulesRun.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_selection_keys:search_text,rule_names,rule_name_patterns,categories,tags,source_types,rule_origin", rulesRun.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:search_text,rule_origin,rule_names,rule_name_patterns,categories,tags,source_types", rulesRun.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestimoXAnalyticsPack_ShouldExposeHistoryAndSnapshotTools() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions {
            Enabled = true
        });

        var definitions = registry.GetDefinitions();
        var names = new HashSet<string>(
            definitions.Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("testimox_analytics_pack_info", names);
        Assert.Contains("testimox_analytics_diagnostics_get", names);
        Assert.Contains("testimox_probe_index_status", names);
        Assert.Contains("testimox_maintenance_window_history", names);
        Assert.Contains("testimox_report_data_snapshot_get", names);
        Assert.Contains("testimox_report_snapshot_get", names);
        Assert.Contains("testimox_history_query", names);
        Assert.Contains("testimox_report_job_history", names);

        var definitionsByName = definitions.ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);
        var monitoringDiagnostics = Assert.IsType<ToolDefinition>(definitionsByName["testimox_analytics_diagnostics_get"]);
        var probeIndexStatus = Assert.IsType<ToolDefinition>(definitionsByName["testimox_probe_index_status"]);
        var maintenanceWindowHistory = Assert.IsType<ToolDefinition>(definitionsByName["testimox_maintenance_window_history"]);
        var reportDataSnapshot = Assert.IsType<ToolDefinition>(definitionsByName["testimox_report_data_snapshot_get"]);
        var reportSnapshot = Assert.IsType<ToolDefinition>(definitionsByName["testimox_report_snapshot_get"]);
        var historyQuery = Assert.IsType<ToolDefinition>(definitionsByName["testimox_history_query"]);
        var reportJobHistory = Assert.IsType<ToolDefinition>(definitionsByName["testimox_report_job_history"]);

        Assert.Contains("diagnostics", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("snapshot", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback:requires_selection", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_selection_keys:history_directory", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,include_slow_probes,max_slow_probes", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("index", probeIndexStatus.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("status", probeIndexStatus.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,probe_names,since_utc,probe_name_contains,statuses", probeIndexStatus.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("maintenance", maintenanceWindowHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("history", maintenanceWindowHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,start_utc,end_utc,definition_key,name_contains,reason_contains,probe_name_pattern_contains,target_pattern_contains", maintenanceWindowHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("snapshot", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("data", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback:requires_selection", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_selection_keys:history_directory,report_key", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,report_key,include_payload,max_chars", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("snapshot", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("html", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback:requires_selection", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_selection_keys:history_directory,report_key", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,report_key,include_html,max_chars", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("availability", historyQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rollup", historyQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,bucket_kind,start_utc,end_utc,root_probe_names,probe_name_contains", historyQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("monitoring", reportJobHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reporting", reportJobHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("fallback_hint_keys:history_directory,job_key,report_key,since_utc,statuses", reportJobHistory.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenSourceDnsAndDomainPacks_ShouldExposePackInfoAndCoreTools() {
        var registry = new ToolRegistry();
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());

        var definitions = registry.GetDefinitions();
        var names = new HashSet<string>(
            definitions.Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("dnsclientx_pack_info", names);
        Assert.Contains("dnsclientx_query", names);
        Assert.Contains("dnsclientx_ping", names);
        Assert.Contains("domaindetective_pack_info", names);
        Assert.Contains("domaindetective_checks_catalog", names);
        Assert.Contains("domaindetective_domain_summary", names);
        Assert.Contains("domaindetective_network_probe", names);

        var definitionsByName = definitions.ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);
        var dnsPackInfo = Assert.IsType<ToolDefinition>(definitionsByName["dnsclientx_pack_info"]);
        var domainDetectivePackInfo = Assert.IsType<ToolDefinition>(definitionsByName["domaindetective_pack_info"]);
        var dnsQuery = Assert.IsType<ToolDefinition>(definitionsByName["dnsclientx_query"]);
        var dnsPing = Assert.IsType<ToolDefinition>(definitionsByName["dnsclientx_ping"]);
        var domainSummary = Assert.IsType<ToolDefinition>(definitionsByName["domaindetective_domain_summary"]);
        var networkProbe = Assert.IsType<ToolDefinition>(definitionsByName["domaindetective_network_probe"]);

        Assert.Contains("domain_family:public_domain", dnsPackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_signals:dns,mx,spf,dmarc,dkim,ns,dnssec,caa,whois,mta_sts,bimi,dnsclientx,dns_client_x", dnsPackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_family:public_domain", domainDetectivePackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_signals:dns,mx,spf,dmarc,dkim,ns,dnssec,caa,whois,mta_sts,bimi,domaindetective,domain_detective", domainDetectivePackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("resolver", dnsQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reachability", dnsPing.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_posture", domainSummary.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reachability", networkProbe.Tags, StringComparer.OrdinalIgnoreCase);

        var dnsQueryHandoff = Assert.IsType<ToolHandoffContract>(dnsQuery.Handoff);
        Assert.Contains(
            dnsQueryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "domaindetective", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "query/name", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "domain", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired)
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "query/endpoint", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "dns_endpoint", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));

        var dnsPingHandoff = Assert.IsType<ToolHandoffContract>(dnsPing.Handoff);
        Assert.Contains(
            dnsPingHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "domaindetective", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "probed_targets/0", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "host", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired)
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "timeout_ms", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "timeout_ms", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));

        var domainSummaryHandoff = Assert.IsType<ToolHandoffContract>(domainSummary.Handoff);
        Assert.Contains(
            domainSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "domain", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "domain_name", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
        Assert.Contains(
            domainSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "domain", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "forest_name", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));

        var networkProbeHandoff = Assert.IsType<ToolHandoffContract>(networkProbe.Handoff);
        Assert.Contains(
            networkProbeHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "host", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "domain_controller", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
    }

    [Fact]
    public void CoreDomainPacks_ShouldExposeDomainIntentSignalsViaPackInfoTags() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);
        var adPackInfo = Assert.IsType<ToolDefinition>(definitionsByName["ad_pack_info"]);
        var eventLogPackInfo = Assert.IsType<ToolDefinition>(definitionsByName["eventlog_pack_info"]);

        Assert.Contains("domain_family:ad_domain", adPackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_signals:dc,ldap,gpo,kerberos,replication,sysvol,netlogon,ntds,forest,trust,active_directory,adplayground", adPackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_family:ad_domain", eventLogPackInfo.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("domain_signals:eventlog,eventviewerx,security,kerberos,gpo,ad_domain,dc", eventLogPackInfo.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorePackInfoTools_ShouldExposeRoutingContractsForDomainIntentFamilies() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);

        var adPackInfo = Assert.IsType<ToolDefinition>(definitionsByName["ad_pack_info"]);
        var eventLogPackInfo = Assert.IsType<ToolDefinition>(definitionsByName["eventlog_pack_info"]);
        var dnsPackInfo = Assert.IsType<ToolDefinition>(definitionsByName["dnsclientx_pack_info"]);
        var domainDetectivePackInfo = Assert.IsType<ToolDefinition>(definitionsByName["domaindetective_pack_info"]);

        AssertRoutingContract(
            adPackInfo,
            expectedPackId: "active_directory",
            expectedFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            expectedActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
            expectedSignalToken: "replication");
        AssertRoutingContract(
            eventLogPackInfo,
            expectedPackId: "eventlog",
            expectedFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            expectedActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
            expectedSignalToken: "eventlog");
        AssertRoutingContract(
            dnsPackInfo,
            expectedPackId: "dnsclientx",
            expectedFamily: ToolSelectionMetadata.DomainIntentFamilyPublic,
            expectedActionId: ToolSelectionMetadata.DomainIntentActionIdPublic,
            expectedSignalToken: "dns");
        AssertRoutingContract(
            domainDetectivePackInfo,
            expectedPackId: "domaindetective",
            expectedFamily: ToolSelectionMetadata.DomainIntentFamilyPublic,
            expectedActionId: ToolSelectionMetadata.DomainIntentActionIdPublic,
            expectedSignalToken: "dmarc");
    }

    [Fact]
    public void PackInfoToolDefinitions_ShouldDeclareExplicitPackInfoRoutingMetadataBeforeRegistration() {
        var definitions = new ToolDefinition[] {
            new AdPackInfoTool(new ActiveDirectoryToolOptions()).Definition,
            new EventLogPackInfoTool(new EventLogToolOptions()).Definition,
            new DnsClientXPackInfoTool(new DnsClientXToolOptions()).Definition,
            new DomainDetectivePackInfoTool(new DomainDetectiveToolOptions()).Definition,
            new SystemPackInfoTool(new SystemToolOptions()).Definition,
            new FileSystemPackInfoTool(new FileSystemToolOptions()).Definition,
            new EmailPackInfoTool(new EmailToolOptions()).Definition,
            new PowerShellPackInfoTool(new PowerShellToolOptions()).Definition,
            new OfficeImoPackInfoTool(new OfficeImoToolOptions()).Definition,
            new TestimoXPackInfoTool(new TestimoXToolOptions()).Definition,
            new TestimoXAnalyticsPackInfoTool(new TestimoXToolOptions()).Definition,
            new ReviewerSetupPackInfoTool(new ReviewerSetupToolOptions()).Definition
        };

        foreach (var definition in definitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, routing.Role, ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(routing.PackId));
        }
    }

    [Fact]
    public void CreatePackInfoDefinition_ShouldNormalizePackIdAndDeclareExplicitPackInfoRouting() {
        var definition = ToolPackDefinitionFactory.CreatePackInfoDefinition(
            toolName: "sample_pack_info",
            description: "Sample pack info",
            packId: "adplayground",
            category: "active_directory",
            tags: new[] { "pack:active_directory" },
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
            domainSignalTokens: new[] { "dc" });

        var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
        Assert.Equal("active_directory", routing.PackId, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, routing.Role, ignoreCase: true);
        Assert.Equal("object", definition.Parameters!.GetString("type"));
        Assert.False(definition.Parameters.GetBoolean("additionalProperties", defaultValue: true));
    }

    [Fact]
    public void CreateEnvironmentDiscoverDefinition_ShouldNormalizePackIdAndDeclareExplicitEnvironmentDiscoverRouting() {
        var definition = ToolPackDefinitionFactory.CreateEnvironmentDiscoverDefinition(
            toolName: "sample_environment_discover",
            description: "Sample environment discovery",
            parameters: ToolSchema.Object(("domain_controller", ToolSchema.String())).NoAdditionalProperties(),
            packId: "adplayground",
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
            domainSignalTokens: new[] { "dc" });

        var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
        Assert.Equal("active_directory", routing.PackId, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RoleEnvironmentDiscover, routing.Role, ignoreCase: true);
        Assert.Equal("object", definition.Parameters!.GetString("type"));
        Assert.False(definition.Parameters.GetBoolean("additionalProperties", defaultValue: true));
    }

    [Fact]
    public void EnvironmentDiscoverToolDefinitions_ShouldDeclareExplicitEnvironmentDiscoverRoutingMetadataBeforeRegistration() {
        var definitions = new ToolDefinition[] {
            new AdEnvironmentDiscoverTool(new ActiveDirectoryToolOptions()).Definition,
            new PowerShellEnvironmentDiscoverTool(new PowerShellToolOptions()).Definition
        };

        foreach (var definition in definitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.Equal(ToolRoutingTaxonomy.RoleEnvironmentDiscover, routing.Role, ignoreCase: true);
            Assert.False(string.IsNullOrWhiteSpace(routing.PackId));
        }
    }

    [Fact]
    public void ToolContractDefaults_ShouldPreserveExplicitContracts_AndSkipPackInfoFallbacks() {
        var explicitSetup = new ToolSetupContract {
            IsSetupAware = true,
            SetupHintKeys = new[] { "path" }
        };
        var explicitRecovery = new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 1,
            RetryableErrorCodes = new[] { "timeout" }
        };
        var explicitDefinition = new ToolDefinition(
            name: "sample_probe",
            description: "Sample probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            setup: explicitSetup,
            recovery: explicitRecovery);

        var preservedSetup = ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            explicitDefinition,
            ToolRoutingTaxonomy.RoleOperational,
            () => ToolContractDefaults.CreateHintOnlySetup(new[] { "fallback" }));
        var preservedRecovery = ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            explicitDefinition,
            ToolRoutingTaxonomy.RoleOperational,
            () => ToolContractDefaults.CreateNoRetryRecovery());

        Assert.Same(explicitSetup, preservedSetup);
        Assert.Same(explicitRecovery, preservedRecovery);

        var packInfoDefinition = new ToolDefinition(
            name: "sample_pack_info",
            description: "Sample pack info",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var packInfoSetup = ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            packInfoDefinition,
            ToolRoutingTaxonomy.RolePackInfo,
            () => throw new InvalidOperationException("Pack info setup fallback should not run."));
        var packInfoRecovery = ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            packInfoDefinition,
            ToolRoutingTaxonomy.RolePackInfo,
            () => throw new InvalidOperationException("Pack info recovery fallback should not run."));

        Assert.Null(packInfoSetup);
        Assert.Null(packInfoRecovery);
    }

    [Fact]
    public void ToolContractDefaults_ShouldCreateStandardSetupAndRecoveryContracts() {
        var setup = ToolContractDefaults.CreateSetup(
            setupToolName: "system_info",
            requirements: new[] {
                ToolContractDefaults.CreateRequirement(
                    requirementId: "system_host_access",
                    requirementKind: ToolSetupRequirementKinds.Connectivity,
                    hintKeys: new[] { "machine_name" }),
                ToolContractDefaults.CreateRequirement(
                    requirementId: "system_permissions",
                    requirementKind: ToolSetupRequirementKinds.Capability,
                    hintKeys: new[] { "computer_name", "machine_name" })
            },
            setupHintKeys: new[] { "computer_name", "machine_name" });
        var recovery = ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: true,
            maxRetryAttempts: 1,
            retryableErrorCodes: new[] { "timeout", "query_failed" },
            recoveryToolNames: new[] { "system_info" },
            supportsAlternateEngines: true,
            alternateEngineIds: new[] { "cim", "wmi" });

        Assert.True(setup.IsSetupAware);
        Assert.Equal("system_info", setup.SetupToolName);
        Assert.Equal(2, setup.Requirements.Count);
        Assert.Equal("system_host_access", setup.Requirements[0].RequirementId);
        Assert.Equal(ToolSetupRequirementKinds.Connectivity, setup.Requirements[0].Kind);
        Assert.Equal(new[] { "machine_name" }, setup.Requirements[0].HintKeys);
        Assert.Equal("system_permissions", setup.Requirements[1].RequirementId);
        Assert.Equal(ToolSetupRequirementKinds.Capability, setup.Requirements[1].Kind);
        Assert.Equal(new[] { "computer_name", "machine_name" }, setup.SetupHintKeys);

        Assert.True(recovery.IsRecoveryAware);
        Assert.True(recovery.SupportsTransientRetry);
        Assert.Equal(1, recovery.MaxRetryAttempts);
        Assert.Equal(new[] { "timeout", "query_failed" }, recovery.RetryableErrorCodes);
        Assert.Equal(new[] { "system_info" }, recovery.RecoveryToolNames);
        Assert.True(recovery.SupportsAlternateEngines);
        Assert.Equal(new[] { "cim", "wmi" }, recovery.AlternateEngineIds);

        var handoff = ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "filesystem",
                targetToolName: "fs_read",
                reason: "Inspect the raw file.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("files[].path", "path"),
                    ToolContractDefaults.CreateBinding("meta/source_hash", "expected_hash", isRequired: false, transformId: "trim")
                })
        });

        Assert.True(handoff.IsHandoffAware);
        var route = Assert.Single(handoff.OutboundRoutes);
        Assert.Equal("filesystem", route.TargetPackId);
        Assert.Equal("fs_read", route.TargetToolName);
        Assert.Equal("Inspect the raw file.", route.Reason);
        Assert.Equal(2, route.Bindings.Count);
        Assert.Equal("files[].path", route.Bindings[0].SourceField);
        Assert.Equal("path", route.Bindings[0].TargetArgument);
        Assert.True(route.Bindings[0].IsRequired);
        Assert.Equal("meta/source_hash", route.Bindings[1].SourceField);
        Assert.Equal("expected_hash", route.Bindings[1].TargetArgument);
        Assert.False(route.Bindings[1].IsRequired);
        Assert.Equal("trim", route.Bindings[1].TransformId);

        var adRoutes = ToolContractDefaults.CreateActiveDirectoryEntityHandoffRoutes(
            entityHandoffSourceField: "meta/entity_handoff",
            entityHandoffReason: "Normalize identities.",
            scopeDiscoverySourceField: "meta/entity_handoff/computer_candidates/0/value",
            scopeDiscoveryReason: "Discover AD scope.",
            scopeDiscoveryIsRequired: false);
        Assert.Equal(2, adRoutes.Length);
        Assert.Equal("active_directory", adRoutes[0].TargetPackId);
        Assert.Equal("ad_handoff_prepare", adRoutes[0].TargetToolName);
        Assert.Equal("meta/entity_handoff", Assert.Single(adRoutes[0].Bindings).SourceField);
        Assert.Equal("entity_handoff", Assert.Single(adRoutes[0].Bindings).TargetArgument);
        Assert.True(Assert.Single(adRoutes[0].Bindings).IsRequired);
        Assert.Equal("ad_scope_discovery", adRoutes[1].TargetToolName);
        Assert.Equal("meta/entity_handoff/computer_candidates/0/value", Assert.Single(adRoutes[1].Bindings).SourceField);
        Assert.Equal("domain_controller", Assert.Single(adRoutes[1].Bindings).TargetArgument);
        Assert.False(Assert.Single(adRoutes[1].Bindings).IsRequired);

        var remoteHostRoutes = ToolContractDefaults.CreateRemoteHostFollowUpRoutes(
            sourceField: "rows[].target",
            systemReason: "Inspect the host.",
            eventLogReason: "Inspect host logs.",
            isRequired: false);
        Assert.Equal(2, remoteHostRoutes.Length);
        Assert.Equal("system", remoteHostRoutes[0].TargetPackId);
        Assert.Equal("system_info", remoteHostRoutes[0].TargetToolName);
        Assert.Equal("rows[].target", Assert.Single(remoteHostRoutes[0].Bindings).SourceField);
        Assert.Equal("computer_name", Assert.Single(remoteHostRoutes[0].Bindings).TargetArgument);
        Assert.False(Assert.Single(remoteHostRoutes[0].Bindings).IsRequired);
        Assert.Equal("eventlog", remoteHostRoutes[1].TargetPackId);
        Assert.Equal("eventlog_live_stats", remoteHostRoutes[1].TargetToolName);
        Assert.Equal("rows[].target", Assert.Single(remoteHostRoutes[1].Bindings).SourceField);
        Assert.Equal("machine_name", Assert.Single(remoteHostRoutes[1].Bindings).TargetArgument);
        Assert.False(Assert.Single(remoteHostRoutes[1].Bindings).IsRequired);

        var sharedTargetRoute = ToolContractDefaults.CreateSharedTargetRoute(
            targetPackId: "system",
            targetToolName: "system_info",
            reason: "Inspect with fallback sources.",
            targetArgument: "computer_name",
            sourceFields: new[] { "primary/value", "fallback/value" },
            isRequired: false);
        Assert.Equal("system", sharedTargetRoute.TargetPackId);
        Assert.Equal("system_info", sharedTargetRoute.TargetToolName);
        Assert.Equal(2, sharedTargetRoute.Bindings.Count);
        Assert.Equal("primary/value", sharedTargetRoute.Bindings[0].SourceField);
        Assert.Equal("computer_name", sharedTargetRoute.Bindings[0].TargetArgument);
        Assert.False(sharedTargetRoute.Bindings[0].IsRequired);
        Assert.Equal("fallback/value", sharedTargetRoute.Bindings[1].SourceField);
        Assert.Equal("computer_name", sharedTargetRoute.Bindings[1].TargetArgument);
        Assert.False(sharedTargetRoute.Bindings[1].IsRequired);

        var sharedTargetRoutes = ToolContractDefaults.CreateSharedTargetRoutes(
            targetPackId: "system",
            targetArgument: "computer_name",
            sourceFields: new[] { "primary/value", "fallback/value" },
            routeDescriptors: new[] {
                ("system_info", "Inspect the host."),
                ("system_metrics_summary", "Inspect runtime telemetry.")
            },
            isRequired: false);
        Assert.Equal(2, sharedTargetRoutes.Length);
        Assert.Equal("system_info", sharedTargetRoutes[0].TargetToolName);
        Assert.Equal("system_metrics_summary", sharedTargetRoutes[1].TargetToolName);
        Assert.Equal(2, sharedTargetRoutes[0].Bindings.Count);
        Assert.Equal("primary/value", sharedTargetRoutes[0].Bindings[0].SourceField);
        Assert.Equal("fallback/value", sharedTargetRoutes[0].Bindings[1].SourceField);

        var systemCatalogRoutes = SystemRemoteHostFollowUpCatalog.CreateComputerTargetRoutes(
            sourceFields: new[] { "context/domain_controller", "domain_controllers/0/value" },
            primaryReasonOverride: "Inspect discovered domain controllers.",
            isRequired: false);
        Assert.NotEmpty(systemCatalogRoutes);
        Assert.Equal("system_info", systemCatalogRoutes[0].TargetToolName);
        Assert.Equal("Inspect discovered domain controllers.", systemCatalogRoutes[0].Reason);
        Assert.Equal("system_metrics_summary", systemCatalogRoutes[2].TargetToolName);
        Assert.Equal("context/domain_controller", systemCatalogRoutes[0].Bindings[0].SourceField);
        Assert.Equal("domain_controllers/0/value", systemCatalogRoutes[0].Bindings[1].SourceField);
    }

    [Fact]
    public void SystemRemoteHostFollowUpCatalog_ShouldReferenceRegisteredSystemTools() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in SystemRemoteHostFollowUpCatalog.ComputerTargetRouteDescriptors) {
            Assert.Contains(descriptor.TargetToolName, names);
        }
    }

    [Fact]
    public void ToolRoutingRoleResolver_ShouldPreferExplicitRoleAndValidateFallbacks() {
        var explicitRole = ToolRoutingRoleResolver.ResolveExplicitOrFallback(
            explicitRole: ToolRoutingTaxonomy.RoleResolver,
            fallbackRole: ToolRoutingTaxonomy.RoleDiagnostic,
            packDisplayName: "Synthetic");
        Assert.Equal(ToolRoutingTaxonomy.RoleResolver, explicitRole, ignoreCase: true);

        var fallbackRole = ToolRoutingRoleResolver.ResolveExplicitOrFallback(
            explicitRole: null,
            fallbackRole: ToolRoutingTaxonomy.RoleDiagnostic,
            packDisplayName: "Synthetic");
        Assert.Equal(ToolRoutingTaxonomy.RoleDiagnostic, fallbackRole, ignoreCase: true);

        var ex = Assert.Throws<InvalidOperationException>(() => ToolRoutingRoleResolver.ResolveExplicitOrFallback(
            explicitRole: "not_a_role",
            fallbackRole: ToolRoutingTaxonomy.RoleDiagnostic,
            packDisplayName: "Synthetic"));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(AdPackInfoTool), "IntelligenceX.Tools.ADPlayground.ActiveDirectoryToolContracts", "ad_domain_info", "diagnostic")]
    [InlineData(typeof(FileSystemPackInfoTool), "IntelligenceX.Tools.FileSystem.FileSystemToolContracts", "fs_list", "operational")]
    [InlineData(typeof(EmailPackInfoTool), "IntelligenceX.Tools.Email.EmailToolContracts", "email_smtp_probe", "operational")]
    [InlineData(typeof(DnsClientXPackInfoTool), "IntelligenceX.Tools.DnsClientX.DnsClientXToolContracts", "dnsclientx_query", "diagnostic")]
    [InlineData(typeof(DomainDetectivePackInfoTool), "IntelligenceX.Tools.DomainDetective.DomainDetectiveToolContracts", "domaindetective_checks_catalog", "operational")]
    [InlineData(typeof(OfficeImoPackInfoTool), "IntelligenceX.Tools.OfficeIMO.OfficeImoToolContracts", "officeimo_read", "diagnostic")]
    [InlineData(typeof(PowerShellPackInfoTool), "IntelligenceX.Tools.PowerShell.PowerShellToolContracts", "powershell_run", "diagnostic")]
    [InlineData(typeof(TestimoXPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXToolContracts", "testimox_rules_list", "operational")]
    [InlineData(typeof(TestimoXAnalyticsPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsToolContracts", "testimox_report_snapshot_get", "operational")]
    public void InternalToolContracts_ShouldPreferExplicitRoleOverPackFallback(
        Type assemblyMarkerType,
        string contractTypeName,
        string toolName,
        string explicitRole) {
        var definition = new ToolDefinition(
            name: toolName,
            description: "Explicit role override probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "custom_probe",
                Role = explicitRole
            });

        var appliedDefinition = ApplyInternalToolContract(assemblyMarkerType, contractTypeName, definition);
        var routing = Assert.IsType<ToolRoutingContract>(appliedDefinition.Routing);
        Assert.Equal(explicitRole, routing.Role, ignoreCase: true);
    }

    [Fact]
    public void ToolRoutingRoleResolver_ShouldRequireDeclaredRoleWhenExplicitRoleMissing() {
        var ex = Assert.Throws<InvalidOperationException>(() => ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: null,
            toolName: "unclassified_probe",
            declaredRolesByToolName: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            packDisplayName: "Synthetic"));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(AdPackInfoTool), "IntelligenceX.Tools.ADPlayground.ActiveDirectoryToolContracts", "ad_unclassified_probe")]
    [InlineData(typeof(FileSystemPackInfoTool), "IntelligenceX.Tools.FileSystem.FileSystemToolContracts", "fs_unclassified_probe")]
    [InlineData(typeof(EmailPackInfoTool), "IntelligenceX.Tools.Email.EmailToolContracts", "email_unclassified_probe")]
    [InlineData(typeof(DnsClientXPackInfoTool), "IntelligenceX.Tools.DnsClientX.DnsClientXToolContracts", "dnsclientx_unclassified_probe")]
    [InlineData(typeof(DomainDetectivePackInfoTool), "IntelligenceX.Tools.DomainDetective.DomainDetectiveToolContracts", "domaindetective_unclassified_probe")]
    [InlineData(typeof(OfficeImoPackInfoTool), "IntelligenceX.Tools.OfficeIMO.OfficeImoToolContracts", "officeimo_unclassified_probe")]
    [InlineData(typeof(PowerShellPackInfoTool), "IntelligenceX.Tools.PowerShell.PowerShellToolContracts", "powershell_unclassified_probe")]
    [InlineData(typeof(TestimoXPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXToolContracts", "testimox_unclassified_probe")]
    [InlineData(typeof(TestimoXAnalyticsPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsToolContracts", "testimox_analytics_unclassified_probe")]
    public void InternalToolContracts_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole(
        Type assemblyMarkerType,
        string contractTypeName,
        string toolName) {
        var definition = new ToolDefinition(
            name: toolName,
            description: "Unclassified role probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<TargetInvocationException>(() => ApplyInternalToolContract(assemblyMarkerType, contractTypeName, definition));
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("must declare an explicit routing role", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(toolName, inner.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldValidateMutatedRoutingContractBeforeAddingTool() {
        var routing = new ToolRoutingContract {
            IsRoutingAware = true,
            PackId = "active_directory",
            DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
            DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
        };

        var definition = new ToolDefinition(
            name: "custom_routing_probe",
            description: "Routing probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: routing);

        routing.RequiresSelectionForFallback = true;
        routing.FallbackSelectionKeys = Array.Empty<string>();

        var registry = new ToolRegistry();
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("FallbackSelectionKeys", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldRejectRoutingMetadataOptOut() {
        var definition = new ToolDefinition(
            name: "custom_routing_opt_out",
            description: "Routing opt-out probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = false
            });

        var registry = new ToolRegistry();
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("IsRoutingAware=false", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldRequireRoutingPackId_WhenRequireExplicitRoutingMetadataEnabled() {
        var definition = new ToolDefinition(
            name: "custom_strict_packid_probe",
            description: "Strict routing metadata pack-id probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = string.Empty,
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains("Routing.PackId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldRequirePackInfoRoleConsistency_WhenRequireExplicitRoutingMetadataEnabled() {
        var definition = new ToolDefinition(
            name: "custom_pack_info",
            description: "Strict routing metadata pack-info role probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            tags: new[] { "pack_info" },
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains($"Routing.Role='{ToolRoutingTaxonomy.RolePackInfo}'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldRequireEnvironmentDiscoverRoleConsistency_WhenRequireExplicitRoutingMetadataEnabled() {
        var definition = new ToolDefinition(
            name: "custom_environment_discover",
            description: "Strict routing metadata environment-discover role probe",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational
            });

        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = true
        };
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(definition)));
        Assert.Contains($"Routing.Role='{ToolRoutingTaxonomy.RoleEnvironmentDiscover}'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemToolContracts_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole() {
        var definition = new ToolDefinition(
            name: "system_unclassified_probe",
            description: "System unclassified probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<InvalidOperationException>(() => SystemToolContracts.Apply(new StubTool(definition)));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system_unclassified_probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventLogToolContracts_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole() {
        var definition = new ToolDefinition(
            name: "eventlog_unclassified_probe",
            description: "EventLog unclassified probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<InvalidOperationException>(() => EventLogToolContracts.Apply(new StubTool(definition)));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_unclassified_probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ShouldRejectConflictingDomainIntentActionIdsForSameFamily() {
        var firstDefinition = new ToolDefinition(
            name: "custom_ad_pack_a",
            description: "AD pack A",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                PackId = "active_directory",
                DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                DomainIntentActionId = "act_domain_scope_ad_custom_a"
            });

        var secondDefinition = new ToolDefinition(
            name: "custom_ad_pack_b",
            description: "AD pack B",
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                PackId = "active_directory",
                DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                DomainIntentActionId = "act_domain_scope_ad_custom_b"
            });

        var registry = new ToolRegistry();
        registry.Register(new StubTool(firstDefinition));
        var ex = Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTool(secondDefinition)));
        Assert.Contains("DomainIntentActionId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRoutingContract(
        ToolDefinition definition,
        string expectedPackId,
        string expectedFamily,
        string expectedActionId,
        string expectedSignalToken) {
        var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
        Assert.True(routing.IsRoutingAware);
        Assert.Equal(ToolRoutingContract.DefaultContractId, routing.RoutingContractId);
        Assert.Equal(expectedPackId, routing.PackId, ignoreCase: true);
        Assert.Equal(expectedFamily, routing.DomainIntentFamily);
        Assert.Equal(expectedActionId, routing.DomainIntentActionId);
        Assert.Contains(expectedSignalToken, routing.DomainSignalTokens, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertRoutingRole(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        string toolName,
        string expectedRole) {
        Assert.True(definitionsByName.TryGetValue(toolName, out var definition), $"Missing tool '{toolName}'.");
        var routing = Assert.IsType<ToolRoutingContract>(definition!.Routing);
        Assert.Equal(expectedRole, routing.Role, ignoreCase: true);
    }

    private static void AssertRoutingIntent(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        string toolName,
        string expectedFamily,
        string expectedActionId) {
        Assert.True(definitionsByName.TryGetValue(toolName, out var definition), $"Missing tool '{toolName}'.");
        var routing = Assert.IsType<ToolRoutingContract>(definition!.Routing);
        Assert.Equal(expectedFamily, routing.DomainIntentFamily);
        Assert.Equal(expectedActionId, routing.DomainIntentActionId);
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private static ToolDefinition ApplyInternalToolContract(Type assemblyMarkerType, string contractTypeName, ToolDefinition definition) {
        var contractType = assemblyMarkerType.Assembly.GetType(contractTypeName, throwOnError: true)!;
        var applyMethod = contractType.GetMethod(
            "Apply",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(applyMethod);

        var appliedTool = Assert.IsAssignableFrom<ITool>(applyMethod!.Invoke(null, new object[] { new StubTool(definition) }));
        return appliedTool.Definition;
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
