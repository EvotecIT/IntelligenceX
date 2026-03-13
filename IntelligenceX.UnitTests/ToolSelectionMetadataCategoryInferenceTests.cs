using System;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.UnitTests.TestDoubles;
using Xunit;

namespace IntelligenceX.UnitTests {
    public sealed class ToolSelectionMetadataCategoryInferenceTests {

        [Fact]
        public void Enrich_ShouldPreferNamePrefixOverRuntimeTypeNamespace() {
            var definition = new ToolDefinition(
                name: "ad_custom_probe",
                description: "Probe",
                parameters: null);

            var enriched = ToolSelectionMetadata.Enrich(definition, ToolSelectionMetadataNamespaceTypes.SystemDecoratorType);

            Assert.Equal("active_directory", enriched.Category);
            Assert.Contains("active_directory", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Enrich_ShouldFallbackToRuntimeTypeNamespace_WhenNamePrefixIsMissing() {
            var definition = new ToolDefinition(
                name: "customprobe",
                description: "Probe",
                parameters: null);

            var enriched = ToolSelectionMetadata.Enrich(definition, ToolSelectionMetadataNamespaceTypes.EventLogDecoratorType);

            Assert.Equal("eventlog", enriched.Category);
            Assert.Contains("eventlog", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Enrich_ShouldInferDnsCategory_FromDnsAndDomainDetectivePrefixes() {
            var dnsDefinition = new ToolDefinition(
                name: "dnsclientx_query",
                description: "Query DNS",
                parameters: null);
            var domainDetectiveDefinition = new ToolDefinition(
                name: "domaindetective_domain_summary",
                description: "Domain posture",
                parameters: null);

            var dnsEnriched = ToolSelectionMetadata.Enrich(dnsDefinition, toolType: null);
            var domainDetectiveEnriched = ToolSelectionMetadata.Enrich(domainDetectiveDefinition, toolType: null);

            Assert.Equal("dns", dnsEnriched.Category);
            Assert.Equal("dns", domainDetectiveEnriched.Category);
            Assert.Contains("dns", dnsEnriched.Tags, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dns", domainDetectiveEnriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveRouting_ShouldInferDnsAndProbeSemantics_WithoutExplicitOverrides() {
            Assert.False(ToolSelectionMetadata.HasExplicitOverride("dnsclientx_ping"));
            Assert.False(ToolSelectionMetadata.HasExplicitOverride("domaindetective_network_probe"));

            var summary = new ToolDefinition(
                name: "domaindetective_domain_summary",
                description: "Domain summary",
                parameters: null);
            var probe = new ToolDefinition(
                name: "domaindetective_network_probe",
                description: "Network probe",
                parameters: BuildSingleStringPropertySchema("host"));
            var ping = new ToolDefinition(
                name: "dnsclientx_ping",
                description: "Ping probe",
                parameters: BuildSingleStringPropertySchema("target"));

            var summaryRouting = ToolSelectionMetadata.ResolveRouting(summary);
            var probeRouting = ToolSelectionMetadata.ResolveRouting(probe);
            var pingRouting = ToolSelectionMetadata.ResolveRouting(ping);

            Assert.Equal("domain", summaryRouting.Scope);
            Assert.Equal("dns", summaryRouting.Entity);
            Assert.False(summaryRouting.IsExplicit);

            Assert.Equal("host", probeRouting.Scope);
            Assert.Equal("probe", probeRouting.Operation);
            Assert.Equal("host", probeRouting.Entity);
            Assert.False(probeRouting.IsExplicit);

            Assert.Equal("host", pingRouting.Scope);
            Assert.Equal("probe", pingRouting.Operation);
            Assert.Equal("host", pingRouting.Entity);
            Assert.False(pingRouting.IsExplicit);
        }

        [Fact]
        public void TryResolveDomainIntentFamily_ShouldResolveFromExplicitRoutingContract() {
            var adDefinition = new ToolDefinition(
                name: "active-directory-scope-discovery",
                description: "AD scope",
                parameters: null,
                category: "active_directory",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                });
            var dnsDefinition = new ToolDefinition(
                name: "dns-client-x-query",
                description: "DNS query",
                parameters: null,
                category: "dns",
                tags: new[] { "domain_family:public_domain" });

            var adResolved = ToolSelectionMetadata.TryResolveDomainIntentFamily(adDefinition, out var adFamily);
            var dnsResolved = ToolSelectionMetadata.TryResolveDomainIntentFamily(dnsDefinition, out var dnsFamily);

            Assert.True(adResolved);
            Assert.True(dnsResolved);
            Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, adFamily);
            Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyPublic, dnsFamily);
        }

        [Fact]
        public void TryResolveDomainIntentFamily_ShouldRequireExplicitMetadata_WhenOnlyCategoryIsPresent() {
            var eventLogDefinition = new ToolDefinition(
                name: "eventlog_live_query",
                description: "Event log",
                parameters: null,
                category: "eventlog");

            var resolved = ToolSelectionMetadata.TryResolveDomainIntentFamily(eventLogDefinition, out var family);

            Assert.False(resolved);
            Assert.Equal(string.Empty, family);
        }

        [Fact]
        public void TryResolveDomainIntentFamily_ShouldPreferExplicitDomainFamilyTagOverride() {
            var definition = new ToolDefinition(
                name: "dnsclientx_query",
                description: "DNS query",
                parameters: null,
                tags: new[] { "domain_family:ad_domain" });

            var resolved = ToolSelectionMetadata.TryResolveDomainIntentFamily(definition, out var family);

            Assert.True(resolved);
            Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, family);
        }

        [Fact]
        public void TryNormalizeDomainIntentFamily_ShouldAllowCustomNormalizedFamilies() {
            var resolved = ToolSelectionMetadata.TryNormalizeDomainIntentFamily("corp_internal", out var family);

            Assert.True(resolved);
            Assert.Equal("corp_internal", family);
        }

        [Fact]
        public void GetDefaultDomainIntentActionId_ShouldDeriveActionIdForCustomFamilies() {
            var actionId = ToolSelectionMetadata.GetDefaultDomainIntentActionId("corp_internal");

            Assert.Equal("act_domain_scope_corp_internal", actionId);
        }

        [Theory]
        [InlineData("active-directory-scope-discovery")]
        [InlineData("adplayground-domain-controllers")]
        [InlineData("dns-client-x-query")]
        [InlineData("domain-detective-domain-summary")]
        public void TryResolveDomainIntentFamily_ShouldNotInferFromNamePrefixes(string toolName) {
            var resolved = ToolSelectionMetadata.TryResolveDomainIntentFamily(
                toolName: toolName,
                category: null,
                tags: null,
                out var family);

            Assert.False(resolved);
            Assert.Equal(string.Empty, family);
        }

        [Fact]
        public void TryResolvePackId_ShouldResolveFromExplicitPackTagForCustomToolName() {
            var definition = new ToolDefinition(
                name: "custom_scope_probe",
                description: "Custom scope probe",
                parameters: null,
                tags: new[] { "pack:dnsclientx" });

            var resolved = ToolSelectionMetadata.TryResolvePackId(definition, out var packId);

            Assert.True(resolved);
            Assert.Equal("dnsclientx", packId);
        }

        [Theory]
        [InlineData("computerx_inventory_snapshot")]
        [InlineData("domain_detective_domain_summary")]
        [InlineData("dns_client_x_query")]
        [InlineData("testimo_x_rules_list")]
        public void TryResolvePackId_ShouldNotInferFromMetadataFriendlyNamePatterns(string toolName) {
            var resolved = ToolSelectionMetadata.TryResolvePackId(
                toolName: toolName,
                category: null,
                tags: null,
                out var packId);

            Assert.False(resolved);
            Assert.Equal(string.Empty, packId);
        }

        [Fact]
        public void TryResolveDomainIntentFamily_ShouldRequireExplicitMetadataForUncategorizedDomainLikeTool() {
            var unresolved = ToolSelectionMetadata.TryResolveDomainIntentFamily(
                toolName: "directory_scope_probe",
                category: "general",
                tags: null,
                out _);
            var resolvedWithTag = ToolSelectionMetadata.TryResolveDomainIntentFamily(
                toolName: "directory_scope_probe",
                category: "general",
                tags: new[] { "domain_family:ad_domain" },
                out var family);

            Assert.False(unresolved);
            Assert.True(resolvedWithTag);
            Assert.Equal("ad_domain", family);
        }

        [Fact]
        public void Enrich_ShouldAddPackTag_WhenPackCanBeResolvedFromMetadata() {
            var definition = new ToolDefinition(
                name: "computerx_inventory_snapshot",
                description: "ComputerX inventory",
                parameters: null,
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                });

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            Assert.Contains("pack:system", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetNormalizedPackAliases_ShouldReturnCanonicalAliasSet_ForActiveDirectoryPack() {
            var aliases = ToolSelectionMetadata.GetNormalizedPackAliases("active_directory");

            Assert.Contains("activedirectory", aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("ad", aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("adplayground", aliases, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetPackSearchTokens_ShouldIncludeDisplayAliases_ForDomainDetectivePack() {
            var tokens = ToolSelectionMetadata.GetPackSearchTokens("domaindetective");

            Assert.Contains("domaindetective", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("domain_detective", tokens, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetNormalizedPackAliases_ShouldIncludeEventViewerXAlias_ForEventLogPack() {
            var aliases = ToolSelectionMetadata.GetNormalizedPackAliases("eventlog");

            Assert.Contains("eventlog", aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("eventviewerx", aliases, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetPackSearchTokens_ShouldIncludeDisplayAliases_ForEventLogPack() {
            var tokens = ToolSelectionMetadata.GetPackSearchTokens("eventlog");

            Assert.Contains("eventlog", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("eventviewerx", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("eventviewer_x", tokens, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void IsKnownCompoundPackRoutingCompact_ShouldRecognizeKnownCompactsOnly() {
            Assert.True(ToolSelectionMetadata.IsKnownCompoundPackRoutingCompact("domaindetective"));
            Assert.True(ToolSelectionMetadata.IsKnownCompoundPackRoutingCompact("dnsclientx"));
            Assert.True(ToolSelectionMetadata.IsKnownCompoundPackRoutingCompact("eventviewerx"));
            Assert.False(ToolSelectionMetadata.IsKnownCompoundPackRoutingCompact("custompack"));
        }

        [Fact]
        public void GetDefaultDomainSignalTokens_ShouldReturnFamilySpecificSignals() {
            var adSignals = ToolSelectionMetadata.GetDefaultDomainSignalTokens(ToolSelectionMetadata.DomainIntentFamilyAd);
            var publicSignals = ToolSelectionMetadata.GetDefaultDomainSignalTokens(ToolSelectionMetadata.DomainIntentFamilyPublic);

            Assert.Contains("ldap", adSignals, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("replication", adSignals, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dns", publicSignals, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dmarc", publicSignals, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetDomainSignalTokens_ShouldParseAndNormalizeDomainSignalTags() {
            var tokens = ToolSelectionMetadata.GetDomainSignalTokens(
                new ToolDefinition(
                    name: "custom_signal_probe",
                    description: "Probe",
                    parameters: null,
                    tags: new[] {
                        "domain_signal: LDAP ",
                        "domain_signals: replication, mta-sts , dnssec, ldap"
                    }));

            Assert.Contains("ldap", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("replication", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("mta_sts", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dnssec", tokens, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(4, tokens.Count);
        }

        [Fact]
        public void GetFallbackHintKeys_ShouldResolveFromToolOwnedTags_ForTestimoTools() {
            var listEnriched = ToolSelectionMetadata.Enrich(
                new ToolDefinition(
                    name: "testimox_rules_list",
                    description: "Rules list",
                    parameters: null,
                    tags: new[] { "fallback_hint_keys:search_text,rule_origin,categories,tags,source_types" }),
                toolType: null);
            var runEnriched = ToolSelectionMetadata.Enrich(
                new ToolDefinition(
                    name: "testimox_rules_run",
                    description: "Rules run",
                    parameters: null,
                    tags: new[] { "fallback_hint_keys:search_text,rule_origin,rule_names,rule_name_patterns,categories,tags,source_types" }),
                toolType: null);
            var listKeys = ToolSelectionMetadata.GetFallbackHintKeys(listEnriched);
            var runKeys = ToolSelectionMetadata.GetFallbackHintKeys(runEnriched);

            Assert.Contains("search_text", listKeys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("rule_origin", listKeys, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("rule_names", listKeys, StringComparer.OrdinalIgnoreCase);

            Assert.Contains("rule_names", runKeys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("rule_name_patterns", runKeys, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetFallbackHintKeys_ShouldPreferTagOverrides_AndDeduplicate() {
            var keys = ToolSelectionMetadata.GetFallbackHintKeys(
                new ToolDefinition(
                    name: "custom_tool",
                    description: "Custom tool",
                    parameters: null,
                    tags: new[] {
                        "fallback_hint_key: target",
                        "fallback_hint_keys: name, target , domain"
                    }));

            Assert.Equal(3, keys.Count);
            Assert.Contains("target", keys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("name", keys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("domain", keys, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void RequiresSelectionForFallback_ShouldNotTreatRulesListAsSelectionRequired() {
            var listEnriched = ToolSelectionMetadata.Enrich(
                new ToolDefinition(
                    name: "testimox_rules_list",
                    description: "Rules list",
                    parameters: null,
                    tags: new[] { "fallback_hint_keys:search_text,rule_origin,categories,tags,source_types" }),
                toolType: null);
            var runEnriched = ToolSelectionMetadata.Enrich(
                new ToolDefinition(
                    name: "testimox_rules_run",
                    description: "Rules run",
                    parameters: null,
                    tags: new[] {
                        "fallback:requires_selection",
                        "fallback_selection_keys:search_text,rule_names,rule_name_patterns,categories,tags,source_types,rule_origin",
                        "fallback_hint_keys:search_text,rule_origin,rule_names,rule_name_patterns,categories,tags,source_types"
                    }),
                toolType: null);
            var untaggedDefinition = ToolSelectionMetadata.Enrich(
                new ToolDefinition(
                    name: "testimox_rules_run",
                    description: "Rules run",
                    parameters: null),
                toolType: null);
            var listRequired = ToolSelectionMetadata.RequiresSelectionForFallback(listEnriched);
            var runRequired = ToolSelectionMetadata.RequiresSelectionForFallback(runEnriched);
            var untaggedRequired = ToolSelectionMetadata.RequiresSelectionForFallback(untaggedDefinition);

            Assert.False(listRequired);
            Assert.True(runRequired);
            Assert.False(untaggedRequired);
        }

        [Fact]
        public void Enrich_ShouldPopulateRoutingContract_FromMetadataTagsAndDefaults() {
            var definition = new ToolDefinition(
                name: "custom_domain_probe",
                description: "Domain probe",
                parameters: BuildSingleStringPropertySchema("domain_name"),
                category: "dns",
                tags: new[] {
                    "pack:dnsclientx",
                    "domain_family:public_domain",
                    "domain_signal: DMARC ",
                    "fallback:requires_selection",
                    "fallback_selection_keys:domain_name,target",
                    "fallback_hint_keys:domain_name"
                });

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            var routing = Assert.IsType<ToolRoutingContract>(enriched.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingContract.DefaultContractId, routing.RoutingContractId);
            Assert.Equal(ToolRoutingTaxonomy.SourceInferred, routing.RoutingSource, ignoreCase: true);
            Assert.Equal("dnsclientx", routing.PackId, ignoreCase: true);
            Assert.Equal(ToolRoutingTaxonomy.RoleOperational, routing.Role, ignoreCase: true);
            Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyPublic, routing.DomainIntentFamily);
            Assert.Equal(ToolSelectionMetadata.DomainIntentActionIdPublic, routing.DomainIntentActionId);
            Assert.Contains("dmarc", routing.DomainSignalTokens, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dns", routing.DomainSignalTokens, StringComparer.OrdinalIgnoreCase);
            Assert.True(routing.RequiresSelectionForFallback);
            Assert.Contains("domain_name", routing.FallbackSelectionKeys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("target", routing.FallbackSelectionKeys, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("domain_name", routing.FallbackHintKeys, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoutingAwareFallbackHelpers_ShouldPreferRoutingContractValuesOverTags() {
            var definition = new ToolDefinition(
                name: "custom_fallback_tool",
                description: "Fallback probe",
                parameters: BuildSingleStringPropertySchema("domain_name"),
                tags: new[] {
                    "fallback_selection_keys:tag_key",
                    "fallback_hint_keys:tag_hint"
                },
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RequiresSelectionForFallback = true,
                    FallbackSelectionKeys = new[] { "routing_key" },
                    FallbackHintKeys = new[] { "routing_hint" }
                });

            Assert.True(ToolSelectionMetadata.RequiresSelectionForFallback(definition));
            var selectionKeys = ToolSelectionMetadata.GetFallbackSelectionKeys(definition);
            var hintKeys = ToolSelectionMetadata.GetFallbackHintKeys(definition);

            Assert.Single(selectionKeys);
            Assert.Single(hintKeys);
            Assert.Equal("routing_key", selectionKeys[0], ignoreCase: true);
            Assert.Equal("routing_hint", hintKeys[0], ignoreCase: true);
        }

        [Fact]
        public void TryResolveDomainIntentActionId_ShouldPreferRoutingContractActionId() {
            var definition = new ToolDefinition(
                name: "custom_ad_pack_info",
                description: "Custom AD pack",
                parameters: null,
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_custom"
                });

            var resolved = ToolSelectionMetadata.TryResolveDomainIntentActionId(definition, out var actionId);

            Assert.True(resolved);
            Assert.Equal("act_domain_scope_ad_custom", actionId);
        }

        [Fact]
        public void Enrich_ShouldPreserveExplicitRoutingSource_WhenProvidedInRoutingContract() {
            var definition = new ToolDefinition(
                name: "custom_explicit_probe",
                description: "Explicit probe",
                parameters: null,
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                });

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            var routing = Assert.IsType<ToolRoutingContract>(enriched.Routing);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.Equal("system", routing.PackId, ignoreCase: true);
        }

        [Fact]
        public void Enrich_ShouldInferPackInfoRole_ForPackInfoToolName() {
            var definition = new ToolDefinition(
                name: "custom_pack_info",
                description: "Pack info",
                parameters: null);

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            var routing = Assert.IsType<ToolRoutingContract>(enriched.Routing);
            Assert.Equal(ToolRoutingTaxonomy.RolePackInfo, routing.Role, ignoreCase: true);
        }

        [Fact]
        public void Enrich_ShouldInferEnvironmentDiscoverRole_ForEnvironmentDiscoverToolName() {
            var definition = new ToolDefinition(
                name: "custom_environment_discover",
                description: "Environment discover",
                parameters: null);

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            var routing = Assert.IsType<ToolRoutingContract>(enriched.Routing);
            Assert.Equal(ToolRoutingTaxonomy.RoleEnvironmentDiscover, routing.Role, ignoreCase: true);
        }

        [Fact]
        public void Enrich_ShouldTagDomainAndForestScopeArguments_AsTargetScope() {
            var definition = new ToolDefinition(
                name: "ad_scope_probe",
                description: "AD scope probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add(
                        "properties",
                        new JsonObject()
                            .Add("domain_name", new JsonObject().Add("type", "string"))
                            .Add("forest_name", new JsonObject().Add("type", "string")))
                    .Add("additionalProperties", false),
                category: "active_directory");

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            Assert.Contains("target_scope", enriched.Tags, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("scope:domain", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Enrich_ShouldPreserveSetupHandoffAndRecoveryContracts() {
            var definition = new ToolDefinition(
                name: "custom_contract_preserve_tool",
                description: "Contract preservation probe",
                parameters: null,
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupHintKeys = new[] { "environment_name" }
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "dnsclientx",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "domain_name",
                                    TargetArgument = "target"
                                }
                            }
                        }
                    }
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1
                });

            var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);

            Assert.NotNull(enriched.Setup);
            Assert.NotNull(enriched.Handoff);
            Assert.NotNull(enriched.Recovery);
            Assert.Equal("ix.tool-setup.v1", enriched.Setup!.SetupContractId, ignoreCase: true);
            Assert.Equal("ix.tool-handoff.v1", enriched.Handoff!.HandoffContractId, ignoreCase: true);
            Assert.Equal("ix.tool-recovery.v1", enriched.Recovery!.RecoveryContractId, ignoreCase: true);
        }

        private static JsonObject BuildSingleStringPropertySchema(string propertyName) {
            return new JsonObject()
                .Add("type", "object")
                .Add(
                    "properties",
                    new JsonObject().Add(
                        propertyName,
                        new JsonObject().Add("type", "string")))
                .Add("additionalProperties", false);
        }
    }
}
