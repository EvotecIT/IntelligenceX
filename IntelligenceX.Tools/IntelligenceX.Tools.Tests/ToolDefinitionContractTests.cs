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
using IntelligenceX.Tools.Common.CrossPack;
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
        registry.RegisterActiveDirectoryLifecyclePack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterTestimoXPack(new TestimoXToolOptions());
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());
        registry.RegisterReviewerSetupPack(new ReviewerSetupToolOptions());

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
        registry.RegisterActiveDirectoryLifecyclePack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions { Enabled = true });
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions { Enabled = true });
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());
        registry.RegisterReviewerSetupPack(new ReviewerSetupToolOptions());

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
        var required = new[] {
            "system_info",
            "ad_search",
            "ad_object_resolve",
            "ad_handoff_prepare",
            "ad_user_lifecycle",
            "ad_computer_lifecycle",
            "ad_group_lifecycle",
            "powershell_run",
            "email_smtp_send"
        };
        Assert.NotEmpty(required);

        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterActiveDirectoryLifecyclePack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in required) {
            Assert.True(definitionsByName.TryGetValue(toolName, out var definition), $"Missing required high-priority tool '{toolName}'.");
            Assert.Contains("routing:explicit", definition!.Tags, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("scope:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("operation:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("entity:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(definition.Tags, static tag => tag.StartsWith("risk:", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(definition.Tags, static tag => ToolSelectionHintTags.IsControlTag(tag));

            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.False(string.IsNullOrWhiteSpace(routing.Scope));
            Assert.False(string.IsNullOrWhiteSpace(routing.Operation));
            Assert.False(string.IsNullOrWhiteSpace(routing.Entity));
            Assert.False(string.IsNullOrWhiteSpace(routing.Risk));

            if (definition.WriteGovernance?.IsWriteCapable == true) {
                Assert.Contains("risk:high", definition.Tags, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(ToolRoutingTaxonomy.RiskHigh, routing.Risk, ignoreCase: true);
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
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        var canonical = Assert.Single(
            registry.GetDefinitions(),
            static definition => string.Equals(definition.Name, "system_info", StringComparison.OrdinalIgnoreCase));

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

        AssertRoutingRole(definitionsByName, "ad_pack_info", ToolRoutingTaxonomy.RolePackInfo);
        AssertRoutingRole(definitionsByName, "ad_environment_discover", ToolRoutingTaxonomy.RoleEnvironmentDiscover);
        AssertRoutingRole(definitionsByName, "ad_scope_discovery", ToolRoutingTaxonomy.RoleEnvironmentDiscover);
        AssertRoutingRole(definitionsByName, "ad_forest_discover", ToolRoutingTaxonomy.RoleEnvironmentDiscover);
        AssertRoutingRole(definitionsByName, "ad_domain_info", ToolRoutingTaxonomy.RoleDiagnostic);
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
        AssertRoutingRole(definitionsByName, "ad_monitoring_probe_run", ToolRoutingTaxonomy.RoleOperational);
        AssertRoutingRole(definitionsByName, "ad_object_get", ToolRoutingTaxonomy.RoleOperational);
        AssertRoutingRole(definitionsByName, "ad_handoff_prepare", ToolRoutingTaxonomy.RoleOperational);

        var handoffPrepare = Assert.IsType<ToolHandoffContract>(definitionsByName["ad_handoff_prepare"].Handoff);
        Assert.Contains(
            handoffPrepare.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "target_arguments/ad_object_resolve/identities", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "identities", StringComparison.OrdinalIgnoreCase)
                                && binding.IsRequired));
        Assert.Contains(
            handoffPrepare.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "target_arguments/ad_scope_discovery/domain_name", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "domain_name", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired)
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "target_arguments/ad_scope_discovery/include_domain_controllers", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "include_domain_controllers", StringComparison.OrdinalIgnoreCase)
                                && !binding.IsRequired));

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
    public void ActiveDirectoryMonitoringTools_ShouldExposeDeclaredFallbackRoutingMetadata() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertFallbackRouting(
            definitionsByName["ad_monitoring_dashboard_state_get"],
            requiresSelection: true,
            selectionKeys: new[] { "monitoring_directory" },
            hintKeys: new[] { "monitoring_directory" });
        AssertFallbackRouting(
            definitionsByName["ad_monitoring_metrics_get"],
            requiresSelection: true,
            selectionKeys: new[] { "monitoring_directory" },
            hintKeys: new[] { "monitoring_directory" });
        AssertFallbackRouting(
            definitionsByName["ad_monitoring_diagnostics_get"],
            requiresSelection: true,
            selectionKeys: new[] { "monitoring_directory" },
            hintKeys: new[] { "monitoring_directory", "include_slow_probes", "max_slow_probes" });
        AssertFallbackRouting(
            definitionsByName["ad_monitoring_service_heartbeat_get"],
            requiresSelection: true,
            selectionKeys: new[] { "monitoring_directory" },
            hintKeys: new[] { "monitoring_directory" });
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
        AssertRoutingRole(definitionsByName, "testimox_dashboard_autogenerate_status_get", ToolRoutingTaxonomy.RoleDiagnostic);
        AssertRoutingRole(definitionsByName, "testimox_availability_rollup_status_get", ToolRoutingTaxonomy.RoleDiagnostic);
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

        var dashboardStatusHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["testimox_dashboard_autogenerate_status_get"].Handoff);
        Assert.Contains(
            dashboardStatusHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "snapshot_path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            dashboardStatusHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "report_path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)));

        var rollupStatusHandoff = Assert.IsType<ToolHandoffContract>(definitionsByName["testimox_availability_rollup_status_get"].Handoff);
        Assert.Contains(
            rollupStatusHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && route.Bindings.Any(static binding =>
                                string.Equals(binding.SourceField, "snapshot_path", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(binding.TargetArgument, "path", StringComparison.OrdinalIgnoreCase)));

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
        AssertFallbackRouting(runsList, requiresSelection: false, selectionKeys: Array.Empty<string>(), hintKeys: new[] { "store_directory", "run_id_contains", "completed_only" });
        Assert.Contains("summary", runSummary.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(runSummary, requiresSelection: true, selectionKeys: new[] { "store_directory", "run_id" }, hintKeys: new[] { "store_directory", "run_id", "scope_group", "rule_name_contains", "scope_id_contains" });
        Assert.Contains("catalog", baselinesList.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(baselinesList, requiresSelection: false, selectionKeys: Array.Empty<string>(), hintKeys: new[] { "search_text", "vendor_ids", "product_ids", "version_wildcard", "baseline_ids", "id_patterns" });
        Assert.Contains("compare", baselineCompare.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(baselineCompare, requiresSelection: false, selectionKeys: Array.Empty<string>(), hintKeys: new[] { "product_id", "vendor_ids", "version_wildcard", "latest_only", "only_diff", "search_text" });
        Assert.Contains("profiles", profilesList.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("inventory", ruleInventory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("provenance", sourceQuery.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(sourceQuery, requiresSelection: true, selectionKeys: new[] { "search_text", "rule_names", "rule_name_patterns", "categories", "tags", "source_types", "rule_origin", "migration_states" }, hintKeys: new[] { "search_text", "rule_origin", "rule_names", "rule_name_patterns", "categories", "tags", "source_types", "migration_states", "profile" });
        Assert.Contains("crosswalk", baselineCrosswalk.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(baselineCrosswalk, requiresSelection: false, selectionKeys: Array.Empty<string>(), hintKeys: new[] { "search_text", "rule_origin", "categories", "tags", "source_types", "profile", "rule_names", "rule_name_patterns" });
        AssertFallbackRouting(ruleInventory, requiresSelection: false, selectionKeys: Array.Empty<string>(), hintKeys: new[] { "search_text", "rule_origin", "categories", "tags", "source_types", "migration_states", "profile" });
        AssertFallbackRouting(rulesList, requiresSelection: false, selectionKeys: Array.Empty<string>(), hintKeys: new[] { "search_text", "rule_origin", "categories", "tags", "source_types" });
        AssertFallbackRouting(rulesRun, requiresSelection: true, selectionKeys: new[] { "search_text", "rule_names", "rule_name_patterns", "categories", "tags", "source_types", "rule_origin" }, hintKeys: new[] { "search_text", "rule_origin", "rule_names", "rule_name_patterns", "categories", "tags", "source_types" });
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
        Assert.Contains("testimox_dashboard_autogenerate_status_get", names);
        Assert.Contains("testimox_availability_rollup_status_get", names);
        Assert.Contains("testimox_probe_index_status", names);
        Assert.Contains("testimox_maintenance_window_history", names);
        Assert.Contains("testimox_report_data_snapshot_get", names);
        Assert.Contains("testimox_report_snapshot_get", names);
        Assert.Contains("testimox_history_query", names);
        Assert.Contains("testimox_report_job_history", names);

        var definitionsByName = definitions.ToDictionary(static d => d.Name, StringComparer.OrdinalIgnoreCase);
        var monitoringDiagnostics = Assert.IsType<ToolDefinition>(definitionsByName["testimox_analytics_diagnostics_get"]);
        var dashboardStatus = Assert.IsType<ToolDefinition>(definitionsByName["testimox_dashboard_autogenerate_status_get"]);
        var rollupStatus = Assert.IsType<ToolDefinition>(definitionsByName["testimox_availability_rollup_status_get"]);
        var probeIndexStatus = Assert.IsType<ToolDefinition>(definitionsByName["testimox_probe_index_status"]);
        var maintenanceWindowHistory = Assert.IsType<ToolDefinition>(definitionsByName["testimox_maintenance_window_history"]);
        var reportDataSnapshot = Assert.IsType<ToolDefinition>(definitionsByName["testimox_report_data_snapshot_get"]);
        var reportSnapshot = Assert.IsType<ToolDefinition>(definitionsByName["testimox_report_snapshot_get"]);
        var historyQuery = Assert.IsType<ToolDefinition>(definitionsByName["testimox_history_query"]);
        var reportJobHistory = Assert.IsType<ToolDefinition>(definitionsByName["testimox_report_job_history"]);

        Assert.Contains("diagnostics", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("snapshot", monitoringDiagnostics.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(monitoringDiagnostics, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory", "include_slow_probes", "max_slow_probes" });
        Assert.Contains("dashboard", dashboardStatus.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("status", dashboardStatus.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(dashboardStatus, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory" });
        Assert.Contains("availability", rollupStatus.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rollup", rollupStatus.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(rollupStatus, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory" });
        Assert.Contains("index", probeIndexStatus.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("status", probeIndexStatus.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(probeIndexStatus, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory", "probe_names", "since_utc", "probe_name_contains", "statuses" });
        Assert.Contains("maintenance", maintenanceWindowHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("history", maintenanceWindowHistory.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(maintenanceWindowHistory, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory", "start_utc", "end_utc", "definition_key", "name_contains", "reason_contains", "probe_name_pattern_contains", "target_pattern_contains" });
        Assert.Contains("snapshot", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("data", reportDataSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(reportDataSnapshot, requiresSelection: true, selectionKeys: new[] { "history_directory", "report_key" }, hintKeys: new[] { "history_directory", "report_key", "include_payload", "max_chars" });
        Assert.Contains("snapshot", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("html", reportSnapshot.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(reportSnapshot, requiresSelection: true, selectionKeys: new[] { "history_directory", "report_key" }, hintKeys: new[] { "history_directory", "report_key", "include_html", "max_chars" });
        Assert.Contains("availability", historyQuery.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rollup", historyQuery.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(historyQuery, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory", "bucket_kind", "start_utc", "end_utc", "root_probe_names", "probe_name_contains" });
        Assert.Contains("monitoring", reportJobHistory.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reporting", reportJobHistory.Tags, StringComparer.OrdinalIgnoreCase);
        AssertFallbackRouting(reportJobHistory, requiresSelection: true, selectionKeys: new[] { "history_directory" }, hintKeys: new[] { "history_directory", "job_key", "report_key", "since_utc", "statuses" });
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
    public void RegisteredPackInfoTools_ShouldNotGetImplicitSetupOrRecoveryContracts() {
        var registry = new ToolRegistry();
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());
        registry.RegisterDnsClientXPack(new DnsClientXToolOptions());
        registry.RegisterDomainDetectivePack(new DomainDetectiveToolOptions());
        registry.RegisterOfficeImoPack(new OfficeImoToolOptions());
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var packInfoDefinitions = registry.GetDefinitions()
            .Where(static definition => definition.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(packInfoDefinitions);
        foreach (var definition in packInfoDefinitions) {
            Assert.Null(definition.Setup);
            Assert.Null(definition.Recovery);
        }
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
                },
                followUpKind: ToolHandoffFollowUpKinds.Investigation,
                followUpPriority: ToolHandoffFollowUpPriorities.High)
        });

        Assert.True(handoff.IsHandoffAware);
        var route = Assert.Single(handoff.OutboundRoutes);
        Assert.Equal("filesystem", route.TargetPackId);
        Assert.Equal("fs_read", route.TargetToolName);
        Assert.Equal("Inspect the raw file.", route.Reason);
        Assert.Equal(ToolHandoffFollowUpKinds.Investigation, route.FollowUpKind);
        Assert.Equal(ToolHandoffFollowUpPriorities.High, route.FollowUpPriority);
        Assert.Equal(2, route.Bindings.Count);
        Assert.Equal("files[].path", route.Bindings[0].SourceField);
        Assert.Equal("path", route.Bindings[0].TargetArgument);
        Assert.True(route.Bindings[0].IsRequired);
        Assert.Equal("meta/source_hash", route.Bindings[1].SourceField);
        Assert.Equal("expected_hash", route.Bindings[1].TargetArgument);
        Assert.False(route.Bindings[1].IsRequired);
        Assert.Equal("trim", route.Bindings[1].TransformId);

        var adRoutes = ActiveDirectoryEntityHandoffCatalog.CreateEntityHandoffRoutes(
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

        var remoteHostRoutes = RemoteHostFollowUpCatalog.CreateSystemAndEventLogTargetRoutes(
            sourceField: "rows[].target",
            systemReason: "Inspect the host.",
            eventLogReason: "Inspect host logs.",
            isRequired: false);
        Assert.Equal(SystemRemoteHostFollowUpCatalog.ComputerTargetRouteDescriptors.Length + EventLogRemoteHostFollowUpCatalog.MachineTargetRouteDescriptors.Length, remoteHostRoutes.Length);
        Assert.Equal("system", remoteHostRoutes[0].TargetPackId);
        Assert.Equal("system_info", remoteHostRoutes[0].TargetToolName);
        Assert.Equal("rows[].target", Assert.Single(remoteHostRoutes[0].Bindings).SourceField);
        Assert.Equal("computer_name", Assert.Single(remoteHostRoutes[0].Bindings).TargetArgument);
        Assert.False(Assert.Single(remoteHostRoutes[0].Bindings).IsRequired);
        var eventLogRoute = Assert.Single(
            remoteHostRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("eventlog_live_stats", eventLogRoute.TargetToolName);
        Assert.Equal("rows[].target", Assert.Single(eventLogRoute.Bindings).SourceField);
        Assert.Equal("machine_name", Assert.Single(eventLogRoute.Bindings).TargetArgument);
        Assert.False(Assert.Single(eventLogRoute.Bindings).IsRequired);

        var channelDiscoveryRoutes = RemoteHostFollowUpCatalog.CreateSystemAndEventLogChannelDiscoveryRoutes(
            sourceFields: new[] { "context/domain_controller", "domain_controllers/0/value" },
            systemReason: "Inspect discovered domain controllers.",
            eventLogReason: "Discover remote channels for the same hosts.",
            isRequired: false);
        Assert.Equal(
            SystemRemoteHostFollowUpCatalog.ComputerTargetRouteDescriptors.Length + EventLogRemoteHostFollowUpCatalog.ChannelDiscoveryRouteDescriptors.Length,
            channelDiscoveryRoutes.Length);
        var channelDiscoveryRoute = Assert.Single(
            channelDiscoveryRoutes,
            static route => string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, channelDiscoveryRoute.Bindings.Count);
        Assert.All(channelDiscoveryRoute.Bindings, static binding => Assert.Equal("machine_name", binding.TargetArgument));
        Assert.Equal("context/domain_controller", channelDiscoveryRoute.Bindings[0].SourceField);
        Assert.Equal("domain_controllers/0/value", channelDiscoveryRoute.Bindings[1].SourceField);

        var adAndSystemRoutes = ActiveDirectoryEntityHandoffCatalog.CreateEntityAndSelectedSystemRoutes(
            entityHandoffSourceField: "meta/entity_handoff",
            entityHandoffReason: "Normalize identities.",
            scopeDiscoverySourceField: "meta/entity_handoff/computer_candidates/0/value",
            scopeDiscoveryReason: "Discover AD scope.",
            systemSourceFields: new[] { "meta/entity_handoff/computer_candidates/0/value" },
            systemRouteSelections: new (string TargetToolName, string? ReasonOverride)[] {
                ("system_info", "Inspect host context.")
            },
            scopeDiscoveryIsRequired: false,
            systemRoutesAreRequired: false);
        Assert.Equal(3, adAndSystemRoutes.Length);
        Assert.Contains(
            adAndSystemRoutes,
            static route => string.Equals(route.TargetToolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            adAndSystemRoutes,
            static route => string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            adAndSystemRoutes,
            static route => string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));

        var analyticsDiagnosticsRoutes = TestimoXAnalyticsFollowUpCatalog.CreateDiagnosticsArtifactRoutes(
            snapshotPathSourceField: "snapshot_path",
            targetSourceField: "slow_probes[].target",
            targetRoutesAreRequired: false);
        Assert.Contains(
            analyticsDiagnosticsRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            analyticsDiagnosticsRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            analyticsDiagnosticsRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase));

        var analyticsReportRoutes = TestimoXAnalyticsFollowUpCatalog.CreateReportJobHistoryArtifactRoutes(
            historyDirectorySourceField: "history_directory",
            reportKeySourceField: "jobs[].report_key",
            reportPathSourceField: "jobs[].report_path");
        Assert.Equal(3, analyticsReportRoutes.Length);
        Assert.Contains(
            analyticsReportRoutes,
            static route => string.Equals(route.TargetToolName, "testimox_report_data_snapshot_get", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            analyticsReportRoutes,
            static route => string.Equals(route.TargetToolName, "testimox_report_snapshot_get", StringComparison.OrdinalIgnoreCase));
        var analyticsReportFileRoute = Assert.Single(
            analyticsReportRoutes,
            static route => string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase));
        Assert.False(Assert.Single(analyticsReportFileRoute.Bindings).IsRequired);

        var analyticsHistoryRoutes = TestimoXAnalyticsFollowUpCatalog.CreateHistoryTargetRoutes(
            targetSourceField: "rows[].target",
            targetRoutesAreRequired: false);
        Assert.Contains(
            analyticsHistoryRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            analyticsHistoryRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase));

        var testimoXScopeRoutes = TestimoXScopeFollowUpCatalog.CreateScopeAndHostFollowUpRoutes(
            domainSourceField: "include_domains/0",
            domainControllerSourceField: "include_domain_controllers/0",
            adReason: "Promote explicit scope into AD discovery.",
            systemReason: "Promote explicit scope into remote host inspection.",
            hostRoutesAreRequired: false,
            adRouteIsRequired: false);
        Assert.Contains(
            testimoXScopeRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            testimoXScopeRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_metrics_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            testimoXScopeRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));

        var dnsDomainRoutes = DomainInvestigationFollowUpCatalog.CreateDnsQueryToDomainSummaryRoutes(
            domainSourceField: "query/name",
            dnsEndpointSourceField: "query/endpoint");
        var dnsDomainRoute = Assert.Single(dnsDomainRoutes);
        Assert.Equal("domaindetective", dnsDomainRoute.TargetPackId, ignoreCase: true);
        Assert.Equal("domaindetective_domain_summary", dnsDomainRoute.TargetToolName, ignoreCase: true);
        Assert.Equal(2, dnsDomainRoute.Bindings.Count);

        var dnsPingRoutes = DomainInvestigationFollowUpCatalog.CreateDnsPingToNetworkProbeRoutes(
            hostSourceField: "probed_targets/0",
            timeoutSourceField: "timeout_ms");
        var dnsPingRoute = Assert.Single(dnsPingRoutes);
        Assert.Equal("domaindetective_network_probe", dnsPingRoute.TargetToolName, ignoreCase: true);
        Assert.Equal(2, dnsPingRoute.Bindings.Count);

        var domainSummaryRoutes = DomainInvestigationFollowUpCatalog.CreateDomainSummaryToActiveDirectoryRoutes(
            domainSourceField: "domain");
        Assert.Equal(2, domainSummaryRoutes.Length);
        Assert.Contains(
            domainSummaryRoutes,
            static route => string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            domainSummaryRoutes,
            static route => string.Equals(route.TargetToolName, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase));

        var networkProbeRoutes = DomainInvestigationFollowUpCatalog.CreateNetworkProbeToActiveDirectoryRoutes(
            hostSourceField: "host");
        var networkProbeRoute = Assert.Single(networkProbeRoutes);
        Assert.Equal("active_directory", networkProbeRoute.TargetPackId, ignoreCase: true);
        Assert.Equal("ad_scope_discovery", networkProbeRoute.TargetToolName, ignoreCase: true);

        var localFileRoutes = LocalFileInspectionFollowUpCatalog.CreateFilesystemReadRoutes(
            pathSourceField: "entries[].path",
            reason: "Inspect local files.",
            isRequired: false);
        var localFileRoute = Assert.Single(localFileRoutes);
        Assert.Equal("filesystem", localFileRoute.TargetPackId, ignoreCase: true);
        Assert.Equal("fs_read", localFileRoute.TargetToolName, ignoreCase: true);
        Assert.Equal("entries[].path", Assert.Single(localFileRoute.Bindings).SourceField);
        Assert.False(Assert.Single(localFileRoute.Bindings).IsRequired);

        Assert.Equal(
            new[] { "named_events", "machine_name", "path" },
            ToolContractDefaults.MergeDistinctStrings(
                new[] { "named_events", "machine_name" },
                new[] { "machine_name", "path", " " }));

        var systemHostContextRoutes = SystemHostContextFollowUpCatalog.CreateHostContextRoutes(
            sourceFields: new[] { "meta/computer_name", "computer_name" });
        Assert.Equal(2, systemHostContextRoutes.Length);
        Assert.Contains(
            systemHostContextRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            systemHostContextRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));

        var adPreparedRoutes = ActiveDirectoryFollowUpCatalog.CreatePreparedIdentityRoutes();
        Assert.Equal(2, adPreparedRoutes.Length);
        Assert.Contains(
            adPreparedRoutes,
            static route => string.Equals(route.TargetToolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            adPreparedRoutes,
            static route => string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));

        var evtxArtifactRoutes = EventLogArtifactFollowUpCatalog.CreateEvtxPathRoutes();
        Assert.Equal(3, evtxArtifactRoutes.Length);
        Assert.Contains(
            evtxArtifactRoutes,
            static route => string.Equals(route.TargetToolName, "eventlog_evtx_query", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            evtxArtifactRoutes,
            static route => string.Equals(route.TargetToolName, "eventlog_evtx_security_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            evtxArtifactRoutes,
            static route => string.Equals(route.TargetToolName, "eventlog_evtx_stats", StringComparison.OrdinalIgnoreCase));

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

        var adSetup = ActiveDirectoryContractCatalog.CreateDirectoryContextSetup();
        Assert.Equal("ad_environment_discover", adSetup.SetupToolName);
        Assert.Equal(2, adSetup.Requirements.Count);
        Assert.Equal("ad_directory_context", adSetup.Requirements[0].RequirementId);
        Assert.Equal("ad_ldap_connectivity", adSetup.Requirements[1].RequirementId);

        var adRecovery = ActiveDirectoryContractCatalog.CreateStandardRecovery();
        Assert.True(adRecovery.SupportsTransientRetry);
        Assert.Equal(new[] { "ad_environment_discover" }, adRecovery.RecoveryToolNames);
        Assert.Null(ActiveDirectoryContractCatalog.CreateSetup("ad_pack_info"));
        Assert.Null(ActiveDirectoryContractCatalog.CreateRecovery("ad_pack_info"));
        var adPreparedHandoff = ActiveDirectoryContractCatalog.CreateHandoff("ad_handoff_prepare");
        Assert.NotNull(adPreparedHandoff);
        Assert.Contains(
            adPreparedHandoff!.OutboundRoutes,
            static route => string.Equals(route.TargetToolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { "monitoring_directory" },
            ActiveDirectoryRoutingCatalog.ResolveFallbackSelectionKeys("ad_monitoring_metrics_get", explicitKeys: null));
        Assert.Equal(
            new[] { "monitoring_directory", "include_slow_probes", "max_slow_probes" },
            ActiveDirectoryRoutingCatalog.ResolveFallbackHintKeys("ad_monitoring_diagnostics_get", explicitKeys: null));

        var eventLogSetup = EventLogContractCatalog.CreateNamedEventQuerySetup();
        Assert.Equal("eventlog_named_events_catalog", eventLogSetup.SetupToolName);
        Assert.Equal(2, eventLogSetup.Requirements.Count);
        Assert.Contains("named_events", eventLogSetup.SetupHintKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("machine_name", eventLogSetup.SetupHintKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            eventLogSetup.SetupToolName,
            EventLogContractCatalog.CreateSetup("eventlog_named_events_query")!.SetupToolName);
        Assert.Equal(
            "eventlog_channels_list",
            EventLogContractCatalog.CreateSetup("eventlog_live_query")!.SetupToolName);
        Assert.Null(EventLogContractCatalog.CreateSetup("eventlog_pack_info"));

        var eventLogQueryHandoff = EventLogContractCatalog.CreateHandoff("eventlog_timeline_query");
        Assert.NotNull(eventLogQueryHandoff);
        Assert.Contains(
            eventLogQueryHandoff!.OutboundRoutes,
            static route => string.Equals(route.TargetToolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase));
        Assert.Null(EventLogContractCatalog.CreateHandoff("eventlog_pack_info"));

        var lifecycleHandoff = ActiveDirectoryLifecycleContractCatalog.CreateHandoff("ad_user_lifecycle");
        Assert.NotNull(lifecycleHandoff);
        var verificationRoute = Assert.Single(
            lifecycleHandoff!.OutboundRoutes,
            static route => string.Equals(route.TargetToolName, "ad_object_get", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ToolHandoffFollowUpKinds.Verification, verificationRoute.FollowUpKind);
        Assert.Equal(ToolHandoffFollowUpPriorities.Critical, verificationRoute.FollowUpPriority);
        var normalizationRoute = Assert.Single(
            lifecycleHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetToolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ToolHandoffFollowUpKinds.Normalization, normalizationRoute.FollowUpKind);
        Assert.Equal(ToolHandoffFollowUpPriorities.Normal, normalizationRoute.FollowUpPriority);

        var eventLogRecovery = EventLogContractCatalog.CreateRecovery("eventlog_timeline_query");
        Assert.True(eventLogRecovery!.SupportsTransientRetry);
        Assert.Equal(new[] { "eventlog_channels_list" }, eventLogRecovery.RecoveryToolNames);
        Assert.Null(EventLogContractCatalog.CreateRecovery("eventlog_pack_info"));

        var systemSetup = SystemContractCatalog.CreateRemoteHostAccessSetup();
        Assert.Equal("system_info", systemSetup.SetupToolName);
        Assert.Equal("system_host_access", Assert.Single(systemSetup.Requirements).RequirementId);
        Assert.Null(SystemContractCatalog.CreateSetup("system_pack_info"));

        var systemRecovery = SystemContractCatalog.CreateRecovery(supportsAlternateEngines: true);
        Assert.True(systemRecovery.SupportsAlternateEngines);
        Assert.Equal(new[] { "cim", "wmi" }, systemRecovery.AlternateEngineIds);
        Assert.Null(SystemContractCatalog.CreateRecovery("system_pack_info", parameters: null));
        Assert.Null(SystemContractCatalog.CreateHandoff("system_pack_info", parameters: null));

        var emailSearchDefinition = ApplyInternalToolContract(
            typeof(EmailPackInfoTool),
            "IntelligenceX.Tools.Email.EmailPackContractCatalog",
            new ToolDefinition(
                name: "email_imap_search",
                description: "Email IMAP search",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var emailSearchSetup = Assert.IsType<ToolSetupContract>(emailSearchDefinition.Setup);
        Assert.Equal("email_pack_info", emailSearchSetup.SetupToolName);
        Assert.Equal("email_account_authentication", Assert.Single(emailSearchSetup.Requirements).RequirementId);
        var emailSearchRecovery = Assert.IsType<ToolRecoveryContract>(emailSearchDefinition.Recovery);
        Assert.True(emailSearchRecovery.SupportsTransientRetry);
        var emailPackInfoDefinition = ApplyInternalToolContract(
            typeof(EmailPackInfoTool),
            "IntelligenceX.Tools.Email.EmailPackContractCatalog",
            new ToolDefinition(
                name: "email_pack_info",
                description: "Email pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(emailPackInfoDefinition.Setup);
        Assert.Null(emailPackInfoDefinition.Recovery);

        var emailSendDefinition = ApplyInternalToolContract(
            typeof(EmailPackInfoTool),
            "IntelligenceX.Tools.Email.EmailPackContractCatalog",
            new ToolDefinition(
                name: "email_smtp_send",
                description: "Email SMTP send",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var emailSendRecovery = Assert.IsType<ToolRecoveryContract>(emailSendDefinition.Recovery);
        Assert.False(emailSendRecovery.SupportsTransientRetry);

        var powerShellRunDefinition = ApplyInternalToolContract(
            typeof(PowerShellPackInfoTool),
            "IntelligenceX.Tools.PowerShell.PowerShellPackContractCatalog",
            new ToolDefinition(
                name: "powershell_run",
                description: "Run PowerShell",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var powerShellSetup = Assert.IsType<ToolSetupContract>(powerShellRunDefinition.Setup);
        Assert.Equal("powershell_environment_discover", powerShellSetup.SetupToolName);
        Assert.Equal("powershell_host_connectivity", Assert.Single(powerShellSetup.Requirements).RequirementId);
        var powerShellRecovery = Assert.IsType<ToolRecoveryContract>(powerShellRunDefinition.Recovery);
        Assert.False(powerShellRecovery.SupportsTransientRetry);
        Assert.Equal(new[] { "powershell_environment_discover" }, powerShellRecovery.RecoveryToolNames);
        var powerShellPackInfoDefinition = ApplyInternalToolContract(
            typeof(PowerShellPackInfoTool),
            "IntelligenceX.Tools.PowerShell.PowerShellPackContractCatalog",
            new ToolDefinition(
                name: "powershell_pack_info",
                description: "PowerShell pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(powerShellPackInfoDefinition.Setup);
        Assert.Null(powerShellPackInfoDefinition.Recovery);

        var fileSystemListDefinition = ApplyInternalToolContract(
            typeof(FileSystemPackInfoTool),
            "IntelligenceX.Tools.FileSystem.FileSystemPackContractCatalog",
            new ToolDefinition(
                name: "fs_list",
                description: "FileSystem list",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var fileSystemSetup = Assert.IsType<ToolSetupContract>(fileSystemListDefinition.Setup);
        Assert.Equal("fs_list", fileSystemSetup.SetupToolName);
        Assert.Equal("filesystem_path_access", Assert.Single(fileSystemSetup.Requirements).RequirementId);
        var fileSystemRecovery = Assert.IsType<ToolRecoveryContract>(fileSystemListDefinition.Recovery);
        Assert.True(fileSystemRecovery.SupportsTransientRetry);
        var fileSystemPackInfoDefinition = ApplyInternalToolContract(
            typeof(FileSystemPackInfoTool),
            "IntelligenceX.Tools.FileSystem.FileSystemPackContractCatalog",
            new ToolDefinition(
                name: "fs_pack_info",
                description: "FileSystem pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(fileSystemPackInfoDefinition.Setup);
        Assert.Null(fileSystemPackInfoDefinition.Handoff);
        Assert.Null(fileSystemPackInfoDefinition.Recovery);

        var officeReadDefinition = ApplyInternalToolContract(
            typeof(OfficeImoPackInfoTool),
            "IntelligenceX.Tools.OfficeIMO.OfficeImoPackContractCatalog",
            new ToolDefinition(
                name: "officeimo_read",
                description: "OfficeIMO read",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var officeSetup = Assert.IsType<ToolSetupContract>(officeReadDefinition.Setup);
        Assert.Equal("officeimo_pack_info", officeSetup.SetupToolName);
        Assert.Equal("officeimo_path_access", Assert.Single(officeSetup.Requirements).RequirementId);
        var officeRecovery = Assert.IsType<ToolRecoveryContract>(officeReadDefinition.Recovery);
        Assert.True(officeRecovery.SupportsTransientRetry);
        var officePackInfoDefinition = ApplyInternalToolContract(
            typeof(OfficeImoPackInfoTool),
            "IntelligenceX.Tools.OfficeIMO.OfficeImoPackContractCatalog",
            new ToolDefinition(
                name: "officeimo_pack_info",
                description: "OfficeIMO pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(officePackInfoDefinition.Setup);
        Assert.Null(officePackInfoDefinition.Handoff);
        Assert.Null(officePackInfoDefinition.Recovery);
    }

    [Fact]
    public void ToolContractDefaults_ShouldPreserveDeclaredContractsAndProjectExecutionTraits() {
        var declaredSetup = new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = "declared_setup"
        };
        var declaredHandoff = new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "filesystem",
                    targetToolName: "fs_read",
                    reason: "Inspect the artifact.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("path", "path")
                    })
            }
        };
        var declaredRecovery = new ToolRecoveryContract {
            IsRecoveryAware = true
        };
        var declaredExecution = new ToolExecutionContract {
            IsExecutionAware = true,
            ExecutionScope = ToolExecutionScopes.LocalOrRemote
        };
        var declaredDefinition = new ToolDefinition(
            name: "custom_declared",
            setup: declaredSetup,
            handoff: declaredHandoff,
            recovery: declaredRecovery,
            execution: declaredExecution);
        var routing = new ToolRoutingContract {
            IsRoutingAware = true,
            Role = "inspect"
        };

        Assert.Same(
            declaredSetup,
            ToolContractDefaults.ResolveSetupContract(
                declaredDefinition,
                static _ => ToolContractDefaults.CreateHintOnlySetup(new[] { "host" })));
        Assert.Same(
            declaredHandoff,
            ToolContractDefaults.ResolveHandoffContract(
                declaredDefinition,
                static _ => ToolContractDefaults.CreateHandoff(new[] {
                    ToolContractDefaults.CreateRoute(
                        targetPackId: "filesystem",
                        targetToolName: "fs_read",
                        reason: "Inspect the artifact.",
                        bindings: new[] {
                            ToolContractDefaults.CreateBinding("path", "path")
                        })
                })));
        Assert.Same(
            declaredRecovery,
            ToolContractDefaults.ResolveRecoveryContract(
                declaredDefinition,
                static _ => ToolContractDefaults.CreateNoRetryRecovery()));
        Assert.Same(
            declaredExecution,
            ToolContractDefaults.ResolveExecutionContractFromTraits(declaredDefinition, routing));

        var projectedExecution = ToolContractDefaults.ResolveExecutionContractFromTraits(
            new ToolDefinition(name: "system_info"),
            routing);
        Assert.NotNull(projectedExecution);
        Assert.True(projectedExecution!.IsExecutionAware);

        Assert.Null(
            ToolContractDefaults.ResolveExecutionContractFromTraits(
                new ToolDefinition(name: "system_pack_info"),
                new ToolRoutingContract {
                    IsRoutingAware = true,
                    Role = ToolRoutingTaxonomy.RolePackInfo
                }));
    }

    [Fact]
    public void ToolContractDefaults_ShouldCreateExplicitRoutingContractWithPreservedOverrides() {
        var existing = new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = "custom_contract",
            DomainSignalTokens = new[] { "custom_signal" },
            FallbackSelectionKeys = new[] { "ignored_selection" },
            FallbackHintKeys = new[] { "ignored_hint" }
        };

        var routing = ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "system",
            role: "inspect",
            domainIntentFamily: "ops",
            domainIntentActionId: "inspect_host",
            defaultSignalTokens: new[] { "default_signal" },
            requiresSelectionForFallback: true,
            fallbackSelectionKeys: new[] { "computer_name" },
            fallbackHintKeys: new[] { "timeout_ms" });

        Assert.True(routing.IsRoutingAware);
        Assert.Equal("custom_contract", routing.RoutingContractId);
        Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource);
        Assert.Equal("system", routing.PackId);
        Assert.Equal("inspect", routing.Role);
        Assert.Equal("ops", routing.DomainIntentFamily);
        Assert.Equal("inspect_host", routing.DomainIntentActionId);
        Assert.Equal(new[] { "custom_signal" }, routing.DomainSignalTokens);
        Assert.True(routing.RequiresSelectionForFallback);
        Assert.Equal(new[] { "computer_name" }, routing.FallbackSelectionKeys);
        Assert.Equal(new[] { "timeout_ms" }, routing.FallbackHintKeys);
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
    public void EventLogRemoteHostFollowUpCatalog_ShouldReferenceRegisteredEventLogTools() {
        var registry = new ToolRegistry();
        registry.RegisterEventLogPack(new EventLogToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in EventLogRemoteHostFollowUpCatalog.MachineTargetRouteDescriptors) {
            Assert.Contains(descriptor.TargetToolName, names);
        }

        foreach (var descriptor in EventLogRemoteHostFollowUpCatalog.ChannelDiscoveryRouteDescriptors) {
            Assert.Contains(descriptor.TargetToolName, names);
        }
    }

    [Fact]
    public void SystemRemoteHostFollowUpCatalog_ShouldBuildSelectedRoutesFromDeclaredCatalog() {
        var routes = SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
            sourceFields: new[] { "meta/entity_handoff/computer_candidates/0/value" },
            routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                ("system_info", "Inspect correlated host context."),
                ("system_metrics_summary", "Inspect correlated host runtime telemetry.")
            },
            isRequired: false);

        Assert.Equal(2, routes.Length);
        Assert.Equal("system_info", routes[0].TargetToolName);
        Assert.Equal("Inspect correlated host context.", routes[0].Reason);
        Assert.Equal("system_metrics_summary", routes[1].TargetToolName);
        Assert.Equal("Inspect correlated host runtime telemetry.", routes[1].Reason);
        Assert.Equal("meta/entity_handoff/computer_candidates/0/value", Assert.Single(routes[0].Bindings).SourceField);
        Assert.Equal("computer_name", Assert.Single(routes[0].Bindings).TargetArgument);
    }

    [Fact]
    public void EventLogRemoteHostFollowUpCatalog_ShouldBuildChannelDiscoveryRoutes() {
        var routes = EventLogRemoteHostFollowUpCatalog.CreateChannelDiscoveryRoutes(
            sourceFields: new[] { "context/domain_controller", "domain_controllers/0/value" },
            primaryReasonOverride: "Inspect related event channels.",
            isRequired: false);

        var route = Assert.Single(routes);
        Assert.Equal("eventlog", route.TargetPackId);
        Assert.Equal("eventlog_channels_list", route.TargetToolName);
        Assert.Equal("Inspect related event channels.", route.Reason);
        Assert.Equal(2, route.Bindings.Count);
        Assert.Equal("context/domain_controller", route.Bindings[0].SourceField);
        Assert.Equal("machine_name", route.Bindings[0].TargetArgument);
        Assert.Equal("domain_controllers/0/value", route.Bindings[1].SourceField);
        Assert.Equal("machine_name", route.Bindings[1].TargetArgument);
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
    [InlineData(typeof(AdPackInfoTool), "IntelligenceX.Tools.ADPlayground.ActiveDirectoryPackContractCatalog", "ad_domain_info", "diagnostic")]
    [InlineData(typeof(FileSystemPackInfoTool), "IntelligenceX.Tools.FileSystem.FileSystemPackContractCatalog", "fs_list", "operational")]
    [InlineData(typeof(EmailPackInfoTool), "IntelligenceX.Tools.Email.EmailPackContractCatalog", "email_smtp_probe", "operational")]
    [InlineData(typeof(DnsClientXPackInfoTool), "IntelligenceX.Tools.DnsClientX.DnsClientXPackContractCatalog", "dnsclientx_query", "diagnostic")]
    [InlineData(typeof(DomainDetectivePackInfoTool), "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog", "domaindetective_checks_catalog", "operational")]
    [InlineData(typeof(OfficeImoPackInfoTool), "IntelligenceX.Tools.OfficeIMO.OfficeImoPackContractCatalog", "officeimo_read", "diagnostic")]
    [InlineData(typeof(PowerShellPackInfoTool), "IntelligenceX.Tools.PowerShell.PowerShellPackContractCatalog", "powershell_run", "diagnostic")]
    [InlineData(typeof(TestimoXPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXPackContractCatalog", "testimox_rules_list", "operational")]
    [InlineData(typeof(TestimoXAnalyticsPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog", "testimox_report_snapshot_get", "operational")]
    public void InternalPackContractCatalogs_ShouldPreferExplicitRoleOverPackFallback(
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
    [InlineData(typeof(AdPackInfoTool), "IntelligenceX.Tools.ADPlayground.ActiveDirectoryPackContractCatalog", "ad_unclassified_probe")]
    [InlineData(typeof(FileSystemPackInfoTool), "IntelligenceX.Tools.FileSystem.FileSystemPackContractCatalog", "fs_unclassified_probe")]
    [InlineData(typeof(EmailPackInfoTool), "IntelligenceX.Tools.Email.EmailPackContractCatalog", "email_unclassified_probe")]
    [InlineData(typeof(DnsClientXPackInfoTool), "IntelligenceX.Tools.DnsClientX.DnsClientXPackContractCatalog", "dnsclientx_unclassified_probe")]
    [InlineData(typeof(DomainDetectivePackInfoTool), "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog", "domaindetective_unclassified_probe")]
    [InlineData(typeof(OfficeImoPackInfoTool), "IntelligenceX.Tools.OfficeIMO.OfficeImoPackContractCatalog", "officeimo_unclassified_probe")]
    [InlineData(typeof(PowerShellPackInfoTool), "IntelligenceX.Tools.PowerShell.PowerShellPackContractCatalog", "powershell_unclassified_probe")]
    [InlineData(typeof(TestimoXPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXPackContractCatalog", "testimox_unclassified_probe")]
    [InlineData(typeof(TestimoXAnalyticsPackInfoTool), "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog", "testimox_analytics_unclassified_probe")]
    public void InternalPackContractCatalogs_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole(
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
    public void SystemPackContractCatalog_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole() {
        var definition = new ToolDefinition(
            name: "system_unclassified_probe",
            description: "System unclassified probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<InvalidOperationException>(() => SystemPackContractCatalog.Apply(definition));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system_unclassified_probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventLogPackContractCatalog_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole() {
        var definition = new ToolDefinition(
            name: "eventlog_unclassified_probe",
            description: "EventLog unclassified probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<InvalidOperationException>(() => EventLogPackContractCatalog.Apply(definition));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_unclassified_probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventLogRoutingCatalog_ShouldResolveDeclaredRolesForKnownTools() {
        var role = EventLogRoutingCatalog.ResolveRole("eventlog_named_events_query", explicitRole: null);
        Assert.Equal(ToolRoutingTaxonomy.RoleResolver, role, ignoreCase: true);
    }

    [Fact]
    public void DnsClientXContracts_ShouldApplyDeclaredRoleForKnownTools() {
        var updated = ApplyInternalToolContract(
            typeof(DnsClientXPackInfoTool),
            "IntelligenceX.Tools.DnsClientX.DnsClientXPackContractCatalog",
            new ToolDefinition(
                name: "dnsclientx_query",
                description: "DNS query",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);

        Assert.Equal(ToolRoutingTaxonomy.RoleResolver, routing.Role, ignoreCase: true);
    }

    [Fact]
    public void DomainDetectiveContracts_ShouldApplyDeclaredRoleForKnownTools() {
        var updated = ApplyInternalToolContract(
            typeof(DomainDetectivePackInfoTool),
            "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog",
            new ToolDefinition(
                name: "domaindetective_domain_summary",
                description: "Domain summary",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);

        Assert.Equal(ToolRoutingTaxonomy.RoleOperational, routing.Role, ignoreCase: true);
    }

    [Fact]
    public void ActiveDirectoryPackContractCatalog_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole() {
        var definition = new ToolDefinition(
            name: "ad_unclassified_probe",
            description: "AD unclassified probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<InvalidOperationException>(() => ActiveDirectoryPackContractCatalog.Apply(definition));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_unclassified_probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveDirectoryLifecyclePackContractCatalog_ShouldRejectUnclassifiedToolNamesWithoutExplicitRole() {
        var definition = new ToolDefinition(
            name: "ad_lifecycle_unclassified_probe",
            description: "AD lifecycle unclassified probe",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var ex = Assert.Throws<InvalidOperationException>(() => ActiveDirectoryLifecyclePackContractCatalog.Apply(definition));
        Assert.Contains("must declare an explicit routing role", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_lifecycle_unclassified_probe", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemRoutingCatalog_ShouldApplyExplicitSelectionMetadataForSystemInfo() {
        var definition = new ToolDefinition(
            name: "system_info",
            description: "System information",
            parameters: ToolSchema.Object().NoAdditionalProperties());

        var updated = SystemRoutingCatalog.ApplySelectionMetadata(definition);
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);

        Assert.Equal("host", routing.Scope, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.OperationRead, routing.Operation, ignoreCase: true);
        Assert.Equal("host", routing.Entity, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RiskLow, routing.Risk, ignoreCase: true);
        Assert.Contains("inventory", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("baseline", updated.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmailRoutingCatalog_ShouldApplyExplicitSelectionMetadataForSmtpSend() {
        var updated = ApplyInternalToolContract(
            typeof(EmailPackInfoTool),
            "IntelligenceX.Tools.Email.EmailPackContractCatalog",
            new ToolDefinition(
                name: "email_smtp_send",
                description: "SMTP send",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);

        Assert.Equal("message", routing.Scope, ignoreCase: true);
        Assert.Equal("write", routing.Operation, ignoreCase: true);
        Assert.Equal("message", routing.Entity, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RiskHigh, routing.Risk, ignoreCase: true);
        Assert.Contains("smtp", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("send", updated.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveDirectoryLifecycleRoutingCatalog_ShouldApplyExplicitSelectionMetadataForUserLifecycle() {
        var updated = ApplyInternalToolContract(
            typeof(AdLifecyclePackInfoTool),
            "IntelligenceX.Tools.ADPlayground.ActiveDirectoryLifecyclePackContractCatalog",
            new ToolDefinition(
                name: "ad_user_lifecycle",
                description: "AD user lifecycle",
                parameters: ToolSchema.Object(
                        ("operation", ToolSchema.String().Enum("create", "disable")),
                        ("identity", ToolSchema.String()),
                        ("apply", ToolSchema.Boolean()))
                    .NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);
        var setup = Assert.IsType<ToolSetupContract>(updated.Setup);
        var recovery = Assert.IsType<ToolRecoveryContract>(updated.Recovery);

        Assert.Equal("identity", routing.Scope, ignoreCase: true);
        Assert.Equal("write", routing.Operation, ignoreCase: true);
        Assert.Equal("user", routing.Entity, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RiskHigh, routing.Risk, ignoreCase: true);
        Assert.Equal("active_directory_lifecycle", routing.PackId, ignoreCase: true);
        Assert.Contains("joiner", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("offboarding", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ad_environment_discover", setup.SetupToolName, ignoreCase: true);
        Assert.False(recovery.SupportsTransientRetry);
    }

    [Fact]
    public void ActiveDirectoryLifecycleRoutingCatalog_ShouldApplyExplicitSelectionMetadataForComputerLifecycle() {
        var updated = ApplyInternalToolContract(
            typeof(AdLifecyclePackInfoTool),
            "IntelligenceX.Tools.ADPlayground.ActiveDirectoryLifecyclePackContractCatalog",
            new ToolDefinition(
                name: "ad_computer_lifecycle",
                description: "AD computer lifecycle",
                parameters: ToolSchema.Object(
                        ("operation", ToolSchema.String().Enum("create", "disable")),
                        ("identity", ToolSchema.String()),
                        ("apply", ToolSchema.Boolean()))
                    .NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);
        var setup = Assert.IsType<ToolSetupContract>(updated.Setup);
        var recovery = Assert.IsType<ToolRecoveryContract>(updated.Recovery);

        Assert.Equal("host", routing.Scope, ignoreCase: true);
        Assert.Equal("write", routing.Operation, ignoreCase: true);
        Assert.Equal("computer", routing.Entity, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RiskHigh, routing.Risk, ignoreCase: true);
        Assert.Equal("active_directory_lifecycle", routing.PackId, ignoreCase: true);
        Assert.Contains("decommissioning", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("host_account", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ad_environment_discover", setup.SetupToolName, ignoreCase: true);
        Assert.False(recovery.SupportsTransientRetry);
    }

    [Fact]
    public void ActiveDirectoryLifecycleRoutingCatalog_ShouldApplyExplicitSelectionMetadataForGroupLifecycle() {
        var updated = ApplyInternalToolContract(
            typeof(AdLifecyclePackInfoTool),
            "IntelligenceX.Tools.ADPlayground.ActiveDirectoryLifecyclePackContractCatalog",
            new ToolDefinition(
                name: "ad_group_lifecycle",
                description: "AD group lifecycle",
                parameters: ToolSchema.Object(
                        ("operation", ToolSchema.String().Enum("create", "update")),
                        ("identity", ToolSchema.String()),
                        ("apply", ToolSchema.Boolean()))
                    .NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);
        var setup = Assert.IsType<ToolSetupContract>(updated.Setup);
        var recovery = Assert.IsType<ToolRecoveryContract>(updated.Recovery);

        Assert.Equal("identity", routing.Scope, ignoreCase: true);
        Assert.Equal("write", routing.Operation, ignoreCase: true);
        Assert.Equal("group", routing.Entity, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RiskHigh, routing.Risk, ignoreCase: true);
        Assert.Equal("active_directory_lifecycle", routing.PackId, ignoreCase: true);
        Assert.Contains("membership", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("group_account", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ad_environment_discover", setup.SetupToolName, ignoreCase: true);
        Assert.False(recovery.SupportsTransientRetry);
    }

    [Fact]
    public void PowerShellRoutingCatalog_ShouldApplyExplicitSelectionMetadataForRun() {
        var updated = ApplyInternalToolContract(
            typeof(PowerShellPackInfoTool),
            "IntelligenceX.Tools.PowerShell.PowerShellPackContractCatalog",
            new ToolDefinition(
                name: "powershell_run",
                description: "Run PowerShell",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
                }));
        var routing = Assert.IsType<ToolRoutingContract>(updated.Routing);

        Assert.Equal("host", routing.Scope, ignoreCase: true);
        Assert.Equal("execute_write", routing.Operation, ignoreCase: true);
        Assert.Equal("command", routing.Entity, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RiskHigh, routing.Risk, ignoreCase: true);
        Assert.Contains("execution", updated.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("mutating", updated.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileSystemContractCatalog_ShouldApplyDeclaredHandoffsForListAndSearch() {
        var listDefinition = ApplyInternalToolContract(
            typeof(FileSystemPackInfoTool),
            "IntelligenceX.Tools.FileSystem.FileSystemPackContractCatalog",
            new ToolDefinition(
                name: "fs_list",
                description: "File list",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var listHandoff = Assert.IsType<ToolHandoffContract>(listDefinition.Handoff);
        Assert.Contains(
            listHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && string.Equals(route.Bindings[0].SourceField, "entries[].path", StringComparison.OrdinalIgnoreCase));

        var searchDefinition = ApplyInternalToolContract(
            typeof(FileSystemPackInfoTool),
            "IntelligenceX.Tools.FileSystem.FileSystemPackContractCatalog",
            new ToolDefinition(
                name: "fs_search",
                description: "File search",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var searchHandoff = Assert.IsType<ToolHandoffContract>(searchDefinition.Handoff);
        Assert.Contains(
            searchHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && string.Equals(route.Bindings[0].SourceField, "matches[].path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OfficeImoContractCatalog_ShouldApplyDeclaredFilesystemHandoff() {
        var updated = ApplyInternalToolContract(
            typeof(OfficeImoPackInfoTool),
            "IntelligenceX.Tools.OfficeIMO.OfficeImoPackContractCatalog",
            new ToolDefinition(
                name: "officeimo_read",
                description: "Office read",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var handoff = Assert.IsType<ToolHandoffContract>(updated.Handoff);

        Assert.Contains(
            handoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && string.Equals(route.Bindings[0].SourceField, "files[]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DnsClientXContractCatalog_ShouldApplyDeclaredFallbackRoutingAndHandoffs() {
        var queryDefinition = ApplyInternalToolContract(
            typeof(DnsClientXPackInfoTool),
            "IntelligenceX.Tools.DnsClientX.DnsClientXPackContractCatalog",
            new ToolDefinition(
                name: "dnsclientx_query",
                description: "DNS query",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var queryHandoff = Assert.IsType<ToolHandoffContract>(queryDefinition.Handoff);
        var queryRecovery = Assert.IsType<ToolRecoveryContract>(queryDefinition.Recovery);

        Assert.Contains(
            queryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "domaindetective", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2);
        Assert.Equal(new[] { "timeout", "query_failed", "transport_unavailable" }, queryRecovery.RetryableErrorCodes);
        var dnsPackInfoDefinition = ApplyInternalToolContract(
            typeof(DnsClientXPackInfoTool),
            "IntelligenceX.Tools.DnsClientX.DnsClientXPackContractCatalog",
            new ToolDefinition(
                name: "dnsclientx_pack_info",
                description: "DnsClientX pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(dnsPackInfoDefinition.Setup);
        Assert.Null(dnsPackInfoDefinition.Handoff);
        Assert.Null(dnsPackInfoDefinition.Recovery);

        var pingDefinition = ApplyInternalToolContract(
            typeof(DnsClientXPackInfoTool),
            "IntelligenceX.Tools.DnsClientX.DnsClientXPackContractCatalog",
            new ToolDefinition(
                name: "dnsclientx_ping",
                description: "DNS ping",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var pingHandoff = Assert.IsType<ToolHandoffContract>(pingDefinition.Handoff);
        var pingRecovery = Assert.IsType<ToolRecoveryContract>(pingDefinition.Recovery);

        AssertFallbackRouting(
            pingDefinition,
            requiresSelection: true,
            selectionKeys: new[] { "target", "targets" },
            hintKeys: new[] { "target", "targets", "timeout_ms", "max_targets", "dont_fragment", "buffer_size" });
        Assert.Contains(
            pingHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "domaindetective", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 2);
        Assert.False(pingRecovery.SupportsTransientRetry);
    }

    [Fact]
    public void DomainDetectiveContractCatalog_ShouldApplyDeclaredSetupRecoveryAndHandoffs() {
        var checksCatalog = ApplyInternalToolContract(
            typeof(DomainDetectivePackInfoTool),
            "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog",
            new ToolDefinition(
                name: "domaindetective_checks_catalog",
                description: "Checks catalog",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var checksSetup = Assert.IsType<ToolSetupContract>(checksCatalog.Setup);
        var checksRecovery = Assert.IsType<ToolRecoveryContract>(checksCatalog.Recovery);

        Assert.Equal("domaindetective_checks_catalog", checksSetup.SetupToolName, ignoreCase: true);
        Assert.Equal("public_dns_connectivity", Assert.Single(checksSetup.Requirements).RequirementId, ignoreCase: true);
        Assert.False(checksRecovery.SupportsTransientRetry);

        var domainSummary = ApplyInternalToolContract(
            typeof(DomainDetectivePackInfoTool),
            "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog",
            new ToolDefinition(
                name: "domaindetective_domain_summary",
                description: "Domain summary",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var domainSummaryHandoff = Assert.IsType<ToolHandoffContract>(domainSummary.Handoff);
        var domainSummaryRecovery = Assert.IsType<ToolRecoveryContract>(domainSummary.Recovery);

        Assert.Contains(
            domainSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            domainSummaryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "timeout", "query_failed", "transport_unavailable" }, domainSummaryRecovery.RetryableErrorCodes);
        Assert.Equal(new[] { "domaindetective_checks_catalog" }, domainSummaryRecovery.RecoveryToolNames);

        var networkProbe = ApplyInternalToolContract(
            typeof(DomainDetectivePackInfoTool),
            "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog",
            new ToolDefinition(
                name: "domaindetective_network_probe",
                description: "Network probe",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var networkProbeHandoff = Assert.IsType<ToolHandoffContract>(networkProbe.Handoff);
        var networkProbeRecovery = Assert.IsType<ToolRecoveryContract>(networkProbe.Recovery);

        Assert.Contains(
            networkProbeHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
                            && route.Bindings.Count == 1
                            && string.Equals(route.Bindings[0].TargetArgument, "domain_controller", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "probe_failed", "timeout", "transport_unavailable" }, networkProbeRecovery.RetryableErrorCodes);
        Assert.Equal(new[] { "domaindetective_checks_catalog" }, networkProbeRecovery.RecoveryToolNames);
        var domainDetectivePackInfo = ApplyInternalToolContract(
            typeof(DomainDetectivePackInfoTool),
            "IntelligenceX.Tools.DomainDetective.DomainDetectivePackContractCatalog",
            new ToolDefinition(
                name: "domaindetective_pack_info",
                description: "DomainDetective pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(domainDetectivePackInfo.Setup);
        Assert.Null(domainDetectivePackInfo.Handoff);
        Assert.Null(domainDetectivePackInfo.Recovery);
    }

    [Fact]
    public void TestimoXContractCatalog_ShouldApplyDeclaredSetupRecoveryAndHandoffs() {
        var rulesList = ApplyInternalToolContract(
            typeof(TestimoXPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXPackContractCatalog",
            new ToolDefinition(
                name: "testimox_rules_list",
                description: "Rules list",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var rulesListSetup = Assert.IsType<ToolSetupContract>(rulesList.Setup);
        var rulesListRecovery = Assert.IsType<ToolRecoveryContract>(rulesList.Recovery);

        Assert.Equal("testimox_rules_list", rulesListSetup.SetupToolName, ignoreCase: true);
        Assert.Equal("testimox_rules_catalog", Assert.Single(rulesListSetup.Requirements).RequirementId, ignoreCase: true);
        Assert.False(rulesListRecovery.SupportsTransientRetry);
        Assert.Equal(new[] { "testimox_rules_list" }, rulesListRecovery.RecoveryToolNames);

        var rulesRun = ApplyInternalToolContract(
            typeof(TestimoXPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXPackContractCatalog",
            new ToolDefinition(
                name: "testimox_rules_run",
                description: "Rules run",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var rulesRunHandoff = Assert.IsType<ToolHandoffContract>(rulesRun.Handoff);
        var rulesRunRecovery = Assert.IsType<ToolRecoveryContract>(rulesRun.Recovery);
        var rulesRunRouting = Assert.IsType<ToolRoutingContract>(rulesRun.Routing);

        Assert.Equal("security_posture", rulesRunRouting.DomainIntentFamily, ignoreCase: true);
        Assert.Equal("act_domain_scope_security_posture", rulesRunRouting.DomainIntentActionId, ignoreCase: true);
        Assert.True(rulesRunRecovery.SupportsTransientRetry);
        Assert.Equal(new[] { "execution_failed", "timeout", "transport_unavailable" }, rulesRunRecovery.RetryableErrorCodes);
        Assert.Contains(
            rulesRunHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            rulesRunHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            rulesRunHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TestimoXAnalyticsContractCatalog_ShouldApplyDeclaredSetupRecoveryAndHandoffs() {
        var diagnostics = ApplyInternalToolContract(
            typeof(TestimoXAnalyticsPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog",
            new ToolDefinition(
                name: "testimox_analytics_diagnostics_get",
                description: "Analytics diagnostics",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var diagnosticsSetup = Assert.IsType<ToolSetupContract>(diagnostics.Setup);
        var diagnosticsRecovery = Assert.IsType<ToolRecoveryContract>(diagnostics.Recovery);
        var diagnosticsHandoff = Assert.IsType<ToolHandoffContract>(diagnostics.Handoff);
        var diagnosticsRouting = Assert.IsType<ToolRoutingContract>(diagnostics.Routing);

        Assert.Equal("monitoring_artifacts", diagnosticsRouting.DomainIntentFamily, ignoreCase: true);
        Assert.Equal("act_domain_scope_monitoring_artifacts", diagnosticsRouting.DomainIntentActionId, ignoreCase: true);
        Assert.Contains("history_directory", diagnosticsSetup.SetupHintKeys, StringComparer.OrdinalIgnoreCase);
        Assert.False(diagnosticsRecovery.SupportsTransientRetry);
        Assert.Contains(
            diagnosticsHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            diagnosticsHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));

        var dashboardStatus = ApplyInternalToolContract(
            typeof(TestimoXAnalyticsPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog",
            new ToolDefinition(
                name: "testimox_dashboard_autogenerate_status_get",
                description: "Dashboard status",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var dashboardStatusHandoff = Assert.IsType<ToolHandoffContract>(dashboardStatus.Handoff);
        Assert.Contains(
            dashboardStatusHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase));

        var rollupStatus = ApplyInternalToolContract(
            typeof(TestimoXAnalyticsPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog",
            new ToolDefinition(
                name: "testimox_availability_rollup_status_get",
                description: "Rollup status",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var rollupStatusHandoff = Assert.IsType<ToolHandoffContract>(rollupStatus.Handoff);
        Assert.Contains(
            rollupStatusHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "filesystem", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "fs_read", StringComparison.OrdinalIgnoreCase));

        var reportJobHistory = ApplyInternalToolContract(
            typeof(TestimoXAnalyticsPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog",
            new ToolDefinition(
                name: "testimox_report_job_history",
                description: "Report job history",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var reportJobHistoryHandoff = Assert.IsType<ToolHandoffContract>(reportJobHistory.Handoff);

        Assert.Contains(
            reportJobHistoryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "testimox_analytics", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "testimox_report_data_snapshot_get", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            reportJobHistoryHandoff.OutboundRoutes,
            static route => string.Equals(route.TargetPackId, "testimox_analytics", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "testimox_report_snapshot_get", StringComparison.OrdinalIgnoreCase));
        var analyticsPackInfo = ApplyInternalToolContract(
            typeof(TestimoXAnalyticsPackInfoTool),
            "IntelligenceX.Tools.TestimoX.TestimoXAnalyticsPackContractCatalog",
            new ToolDefinition(
                name: "testimox_analytics_pack_info",
                description: "TestimoX analytics pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        Assert.Null(analyticsPackInfo.Setup);
        Assert.Null(analyticsPackInfo.Handoff);
        Assert.Null(analyticsPackInfo.Recovery);
    }

    [Fact]
    public void ReviewerSetupContractCatalog_ShouldKeepGuidanceToolsWithoutDefaultSetupHandoffRecovery() {
        var packInfo = ApplyInternalToolContract(
            typeof(ReviewerSetupPackInfoTool),
            "IntelligenceX.Tools.ReviewerSetup.ReviewerSetupPackContractCatalog",
            new ToolDefinition(
                name: "reviewer_setup_pack_info",
                description: "Reviewer setup pack info",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var packInfoRouting = Assert.IsType<ToolRoutingContract>(packInfo.Routing);

        Assert.Equal("reviewer_setup", packInfoRouting.PackId, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, packInfoRouting.Role, ignoreCase: true);
        Assert.Contains("reviewer", packInfoRouting.DomainSignalTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Null(packInfo.Setup);
        Assert.Null(packInfo.Handoff);
        Assert.Null(packInfo.Recovery);

        var contractVerify = ApplyInternalToolContract(
            typeof(ReviewerSetupPackInfoTool),
            "IntelligenceX.Tools.ReviewerSetup.ReviewerSetupPackContractCatalog",
            new ToolDefinition(
                name: "reviewer_setup_contract_verify",
                description: "Reviewer setup contract verify",
                parameters: ToolSchema.Object().NoAdditionalProperties()));
        var contractVerifyRouting = Assert.IsType<ToolRoutingContract>(contractVerify.Routing);

        Assert.Equal("reviewer_setup", contractVerifyRouting.PackId, ignoreCase: true);
        Assert.Equal(ToolRoutingTaxonomy.RoleDiagnostic, contractVerifyRouting.Role, ignoreCase: true);
        Assert.Contains("contract", contractVerifyRouting.DomainSignalTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Null(contractVerify.Setup);
        Assert.Null(contractVerify.Handoff);
        Assert.Null(contractVerify.Recovery);
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

    private static void AssertFallbackRouting(
        ToolDefinition definition,
        bool requiresSelection,
        IReadOnlyList<string> selectionKeys,
        IReadOnlyList<string> hintKeys) {
        var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
        Assert.Equal(requiresSelection, routing.RequiresSelectionForFallback);
        Assert.Equal(selectionKeys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase), routing.FallbackSelectionKeys);
        Assert.Equal(hintKeys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase), routing.FallbackHintKeys);
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

        var parameters = applyMethod!.GetParameters();
        Assert.Single(parameters);

        if (parameters[0].ParameterType == typeof(ToolDefinition)) {
            return Assert.IsType<ToolDefinition>(applyMethod.Invoke(null, new object[] { definition }));
        }

        var appliedTool = Assert.IsAssignableFrom<ITool>(applyMethod.Invoke(null, new object[] { new StubTool(definition) }));
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
