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
        Assert.Equal(1, packSummary!.CrossPackHandoffTools);
        Assert.Contains("ad_domain_monitor", packSummary.CrossPackHandoffToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", packSummary.CrossPackTargetPacks, StringComparer.OrdinalIgnoreCase);

        var capabilitySummary = ToolAutonomySummaryBuilder.BuildCapabilityAutonomySummary(packAvailability, orchestrationCatalog);
        Assert.NotNull(capabilitySummary);
        Assert.Equal(2, capabilitySummary!.RemoteCapableToolCount);
        Assert.Equal(1, capabilitySummary.SetupAwareToolCount);
        Assert.Equal(2, capabilitySummary.HandoffAwareToolCount);
        Assert.Equal(1, capabilitySummary.RecoveryAwareToolCount);
        Assert.Equal(2, capabilitySummary.CrossPackHandoffToolCount);
        Assert.Contains("eventlog", capabilitySummary.RemoteCapablePackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", capabilitySummary.RemoteCapablePackIds, StringComparer.OrdinalIgnoreCase);
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
        Assert.Equal(20, summary!.RemoteCapableTools);
        Assert.Equal(5, summary.RemoteCapableToolNames.Length);
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
        Assert.Equal(0, summary!.RemoteCapableTools);
        Assert.Empty(summary.RemoteCapableToolNames);
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
