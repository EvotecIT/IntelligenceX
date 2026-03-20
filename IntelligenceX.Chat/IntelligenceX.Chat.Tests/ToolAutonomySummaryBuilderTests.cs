using System;
using System.Linq;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolAutonomySummaryBuilderTests {
    [Fact]
    public void BuildCapabilityAutonomySummary_ProjectsRemoteAndCrossPackCounts() {
        var definitions = CreateDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var packAvailability = new[] {
            new ToolPackAvailabilityInfo {
                Id = "active_directory",
                Name = "Active Directory",
                SourceKind = "closed_source",
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "eventlog",
                Name = "Event Log",
                SourceKind = "closed_source",
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "system",
                Name = "System",
                SourceKind = "closed_source",
                Enabled = true
            }
        };

        var packSummary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary("active_directory", orchestrationCatalog);
        Assert.NotNull(packSummary);
        Assert.Equal(1, packSummary!.LocalCapableTools);
        Assert.Contains("ad_domain_monitor", packSummary.LocalCapableToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, packSummary!.EnvironmentDiscoverTools);
        Assert.Equal(1, packSummary.TargetScopedTools);
        Assert.Contains("ad_domain_monitor", packSummary.EnvironmentDiscoverToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, packSummary!.CrossPackHandoffTools);
        Assert.Contains("ad_domain_monitor", packSummary.CrossPackHandoffToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", packSummary.CrossPackTargetPacks, StringComparer.OrdinalIgnoreCase);

        var capabilitySummary = ToolAutonomySummaryBuilder.BuildCapabilityAutonomySummary(packAvailability, orchestrationCatalog);
        Assert.NotNull(capabilitySummary);
        Assert.Equal(3, capabilitySummary!.LocalCapableToolCount);
        Assert.Equal(2, capabilitySummary!.RemoteCapableToolCount);
        Assert.Equal(3, capabilitySummary.TargetScopedToolCount);
        Assert.Equal(2, capabilitySummary.RemoteHostTargetingToolCount);
        Assert.Equal(1, capabilitySummary.SetupAwareToolCount);
        Assert.Equal(1, capabilitySummary.EnvironmentDiscoverToolCount);
        Assert.Equal(2, capabilitySummary.HandoffAwareToolCount);
        Assert.Equal(1, capabilitySummary.RecoveryAwareToolCount);
        Assert.Equal(0, capabilitySummary.WriteCapableToolCount);
        Assert.Equal(0, capabilitySummary.GovernedWriteToolCount);
        Assert.Equal(0, capabilitySummary.AuthenticationRequiredToolCount);
        Assert.Equal(0, capabilitySummary.ProbeCapableToolCount);
        Assert.Equal(2, capabilitySummary.CrossPackHandoffToolCount);
        Assert.Contains("active_directory", capabilitySummary.LocalCapablePackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog", capabilitySummary.LocalCapablePackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", capabilitySummary.LocalCapablePackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog", capabilitySummary.RemoteCapablePackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", capabilitySummary.RemoteCapablePackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("active_directory", capabilitySummary.TargetScopedPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog", capabilitySummary.RemoteHostTargetingPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("active_directory", capabilitySummary.EnvironmentDiscoverPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("active_directory", capabilitySummary.CrossPackReadyPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("eventlog", capabilitySummary.CrossPackReadyPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", capabilitySummary.CrossPackTargetPackIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPackAutonomySummary_KeepsExactCounts_WhenPreviewNamesAreTrimmed() {
        var definitions = Enumerable.Range(1, 20)
            .Select(index => new ToolDefinition(
                $"system_metrics_summary_{index}",
                "Summarize remote system metrics.",
                ToolSchema.Object(("computer_name", ToolSchema.String("Optional host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                }))
            .ToArray();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);

        var summary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary("system", orchestrationCatalog, maxItems: 5);

        Assert.NotNull(summary);
        Assert.Equal(20, summary!.LocalCapableTools);
        Assert.Equal(5, summary.LocalCapableToolNames.Length);
        Assert.Equal(20, summary!.RemoteCapableTools);
        Assert.Equal(5, summary.RemoteCapableToolNames.Length);
        Assert.Equal(20, summary.RemoteHostTargetingTools);
        Assert.Equal(5, summary.RemoteHostTargetingToolNames.Length);
    }

    [Fact]
    public void BuildPackAutonomySummary_DoesNotCountLocalOnlyHostLikeSchemasAsRemoteCapable() {
        var definitions = new[] {
            new ToolDefinition(
                "system_local_trace_query",
                "Inspect local traces only.",
                ToolSchema.Object(("machine_name", ToolSchema.String("Host label."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);

        var summary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary("system", orchestrationCatalog);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.LocalCapableTools);
        Assert.Equal(new[] { "system_local_trace_query" }, summary.LocalCapableToolNames);
        Assert.Equal(0, summary!.RemoteCapableTools);
        Assert.Empty(summary.RemoteCapableToolNames);
    }

    [Fact]
    public void BuildPackAutonomySummary_TracksGovernedWriteToolsSeparatelyFromGeneralWrites() {
        var definitions = new[] {
            new ToolDefinition(
                "ad_user_create",
                "Create a governed AD user.",
                ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true,
                    RequiresGovernanceAuthorization = true,
                    GovernanceContractId = "ix:governance:v1"
                }),
            new ToolDefinition(
                "ad_user_preview",
                "Preview a lifecycle change.",
                ToolSchema.Object(("identity", ToolSchema.String("Identity."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true,
                    RequiresGovernanceAuthorization = false,
                    GovernanceContractId = string.Empty
                })
        };
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);

        var summary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary("active_directory", orchestrationCatalog);

        Assert.NotNull(summary);
        Assert.Equal(2, summary!.WriteCapableTools);
        Assert.Equal(1, summary.GovernedWriteTools);
        Assert.Equal(new[] { "ad_user_create" }, summary.GovernedWriteToolNames);

        var capabilitySummary = ToolAutonomySummaryBuilder.BuildCapabilityAutonomySummary(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "active_directory",
                    Name = "Active Directory",
                    SourceKind = "closed_source",
                    Enabled = true
                }
            },
            orchestrationCatalog);

        Assert.NotNull(capabilitySummary);
        Assert.Equal(2, capabilitySummary!.WriteCapableToolCount);
        Assert.Equal(1, capabilitySummary.GovernedWriteToolCount);
        Assert.Equal(new[] { "active_directory" }, capabilitySummary.LocalCapablePackIds);
        Assert.Equal(new[] { "active_directory" }, capabilitySummary.WriteCapablePackIds);
        Assert.Equal(new[] { "active_directory" }, capabilitySummary.GovernedWritePackIds);
    }

    private static ToolDefinition[] CreateDefinitions() {
        return new[] {
            new ToolDefinition(
                "eventlog_timeline_query",
                "Query event timeline from a host.",
                ToolSchema.Object(
                        ("machine_name", ToolSchema.String("Remote machine.")),
                        ("channel", ToolSchema.String("Channel.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_channels_list"
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_metrics_summary",
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "machine_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    RecoveryToolNames = new[] { "eventlog_channels_list" }
                }),
            new ToolDefinition(
                "ad_domain_monitor",
                "Inspect domain controller health.",
                ToolSchema.Object(
                        ("domain_name", ToolSchema.String("Domain.")))
                    .Required("domain_name")
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "active_directory",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
                    DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
                    DomainIntentActionId = "act_domain_monitor"
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_info",
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "domain_name",
                                    TargetArgument = "search_base_dn"
                                }
                            }
                        }
                    }
                }),
            new ToolDefinition(
                "system_metrics_summary",
                "Summarize local system metrics.",
                ToolSchema.Object(
                        ("computer_name", ToolSchema.String("Optional host.")))
                    .NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };
    }
}
