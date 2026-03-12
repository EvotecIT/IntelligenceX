using System;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolRoutingCatalogDiagnosticsBuilderTests {
    [Fact]
    public void Build_HealthyCatalog_ReportsNoWarnings() {
        var diagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(new[] {
            CreateDefinition(
                name: "ad_replication_summary",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                }),
            CreateDefinition(
                name: "ad_dc_health",
                category: "active_directory",
                parameters: ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote host.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "machine_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                }),
            CreateDefinition(
                name: "dnsclientx_query",
                category: "dns",
                routing: new ToolRoutingContract {
                    PackId = "dnsclientx",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                    DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic
                })
        });

        Assert.True(diagnostics.IsHealthy);
        Assert.True(diagnostics.IsExplicitRoutingReady);
        Assert.Equal(3, diagnostics.TotalTools);
        Assert.Equal(3, diagnostics.RoutingAwareTools);
        Assert.Equal(3, diagnostics.ExplicitRoutingTools);
        Assert.Equal(0, diagnostics.InferredRoutingTools);
        Assert.Equal(0, diagnostics.MissingRoutingContractTools);
        Assert.Equal(0, diagnostics.MissingPackIdTools);
        Assert.Equal(0, diagnostics.MissingRoleTools);
        Assert.Equal(0, diagnostics.SetupAwareTools);
        Assert.Equal(1, diagnostics.HandoffAwareTools);
        Assert.Equal(0, diagnostics.RecoveryAwareTools);
        Assert.Equal(1, diagnostics.RemoteCapableTools);
        Assert.Equal(1, diagnostics.CrossPackHandoffTools);
        Assert.Equal(3, diagnostics.DomainFamilyTools);
        Assert.Equal(0, diagnostics.ExpectedDomainFamilyMissingTools);
        Assert.Equal(0, diagnostics.DomainFamilyMissingActionTools);
        Assert.Equal(0, diagnostics.ActionWithoutFamilyTools);
        Assert.Equal(0, diagnostics.FamilyActionConflictFamilies);

        var summary = ToolRoutingCatalogDiagnosticsBuilder.FormatSummary(diagnostics);
        Assert.Contains("tools=3", summary, StringComparison.Ordinal);
        Assert.Contains("routing_explicit=3", summary, StringComparison.Ordinal);
        Assert.Contains("routing_inferred=0", summary, StringComparison.Ordinal);
        Assert.Contains("remote_capable=1", summary, StringComparison.Ordinal);
        Assert.Contains("cross_pack_handoffs=1", summary, StringComparison.Ordinal);
        Assert.Contains("conflicts=0", summary, StringComparison.Ordinal);

        var familySummaries = ToolRoutingCatalogDiagnosticsBuilder.FormatFamilySummaries(diagnostics, maxItems: 8);
        Assert.Contains(familySummaries, static line => line.Contains("ad_domain", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(familySummaries, static line => line.Contains("public_domain", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ToolRoutingCatalogDiagnosticsBuilder.BuildWarnings(diagnostics));
        var readiness = ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(diagnostics, maxItems: 8);
        Assert.Contains(readiness, static line => line.Contains("remote host-targeting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("cross-pack continuation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("strict enforcement", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("fully populated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_DegradedCatalog_ReportsConflictsAndMissingMetadata() {
        var missingActionDefinition = CreateDefinition(
            name: "dnsclientx_probe",
            category: "dns",
            routing: new ToolRoutingContract {
                PackId = "dnsclientx",
                DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
                DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic
            });
        // Simulate accidental runtime mutation after registration/enrichment.
        missingActionDefinition.Routing!.DomainIntentActionId = string.Empty;

        var inferredRoutingDefinition = CreateDefinition(
            name: "system_contract_probe",
            category: "system",
            parameters: ToolSchema.Object(
                    ("machine_name", ToolSchema.String("Remote machine.")))
                .NoAdditionalProperties(),
            routing: new ToolRoutingContract {
                PackId = "system",
                Role = ToolRoutingTaxonomy.RoleOperational,
                RoutingSource = ToolRoutingTaxonomy.SourceInferred
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupHintKeys = new[] { "host" }
            },
            handoff: new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "dnsclientx",
                        TargetRole = ToolRoutingTaxonomy.RoleOperational,
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "host",
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

        inferredRoutingDefinition.Routing!.PackId = string.Empty;
        inferredRoutingDefinition.Routing.Role = string.Empty;

        var diagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(new[] {
            CreateDefinition(
                name: "ad_tool_a",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_a"
                }),
            CreateDefinition(
                name: "ad_tool_b",
                category: "active_directory",
                routing: new ToolRoutingContract {
                    PackId = "active_directory",
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_scope_ad_b"
                }),
            missingActionDefinition,
            CreateDefinition(
                name: "utility_action_only",
                category: "general",
                routing: new ToolRoutingContract {
                    DomainIntentActionId = "act_orphan_action"
                }),
            inferredRoutingDefinition,
            CreateDefinition(
                name: "dnsclientx_no_routing",
                category: "dns",
                routing: null)
        });

        Assert.False(diagnostics.IsHealthy);
        Assert.False(diagnostics.IsExplicitRoutingReady);
        Assert.Equal(6, diagnostics.TotalTools);
        Assert.Equal(5, diagnostics.RoutingAwareTools);
        Assert.Equal(4, diagnostics.ExplicitRoutingTools);
        Assert.Equal(1, diagnostics.InferredRoutingTools);
        Assert.Equal(1, diagnostics.MissingRoutingContractTools);
        Assert.Equal(2, diagnostics.MissingPackIdTools);
        Assert.Equal(1, diagnostics.MissingRoleTools);
        Assert.Equal(1, diagnostics.SetupAwareTools);
        Assert.Equal(1, diagnostics.HandoffAwareTools);
        Assert.Equal(1, diagnostics.RecoveryAwareTools);
        Assert.Equal(1, diagnostics.RemoteCapableTools);
        Assert.Equal(1, diagnostics.CrossPackHandoffTools);
        Assert.Equal(3, diagnostics.DomainFamilyTools);
        Assert.Equal(0, diagnostics.ExpectedDomainFamilyMissingTools);
        Assert.Equal(1, diagnostics.DomainFamilyMissingActionTools);
        Assert.Equal(1, diagnostics.ActionWithoutFamilyTools);
        Assert.Equal(1, diagnostics.FamilyActionConflictFamilies);

        var warnings = ToolRoutingCatalogDiagnosticsBuilder.BuildWarnings(diagnostics, maxWarnings: 12);
        Assert.Contains(warnings, static line => line.Contains("missing routing contracts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("inferred routing metadata", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("missing routing pack id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("missing routing role", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("miss action id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("action id without a domain intent family", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("multiple action ids", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static line => line.Contains("conflict ad_domain", StringComparison.OrdinalIgnoreCase));
        var readiness = ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(diagnostics, maxItems: 8);
        Assert.Contains(readiness, static line => line.Contains("remote host-targeting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("cross-pack continuation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("setup helpers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("recovery helpers", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(readiness, static line => line.Contains("inferred metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_DoesNotCountLocalOnlyExecutionContractToolAsRemoteCapable() {
        var diagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(new[] {
            CreateDefinition(
                name: "system_local_trace_query",
                category: "system",
                parameters: ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Host label.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                })
        });

        Assert.Equal(0, diagnostics.RemoteCapableTools);
        Assert.DoesNotContain(
            ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(diagnostics, maxItems: 8),
            static line => line.Contains("remote host-targeting", StringComparison.OrdinalIgnoreCase));
    }

    private static ToolDefinition CreateDefinition(
        string name,
        string? category,
        ToolRoutingContract? routing,
        JsonObject? parameters = null,
        ToolSetupContract? setup = null,
        ToolHandoffContract? handoff = null,
        ToolRecoveryContract? recovery = null,
        ToolExecutionContract? execution = null) {
        return new ToolDefinition(
            name: name,
            description: "test tool",
            category: category,
            parameters: parameters,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery,
            execution: execution);
    }
}
