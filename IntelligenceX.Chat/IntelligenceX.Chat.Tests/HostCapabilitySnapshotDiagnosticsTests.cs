using System;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using HostProgram = IntelligenceX.Chat.Host.Program;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostCapabilitySnapshotDiagnosticsTests {
    [Fact]
    public void BuildHostCapabilitySnapshot_FormatsBootstrapReadinessForConsoleDiagnostics() {
        var definitions = CreateDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var routingCatalog = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
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
        var pluginAvailability = new[] {
            new ToolPluginAvailabilityInfo {
                Id = "ops_bundle",
                Name = "Ops Bundle",
                Origin = "folder",
                SourceKind = "closed_source",
                DefaultEnabled = true,
                Enabled = true,
                PackIds = new[] { "active_directory", "eventlog", "system" },
                SkillIds = new[] { "ad-ops", "event-triage" }
            }
        };

        SessionCapabilitySnapshotDto snapshot = HostProgram.BuildHostCapabilitySnapshot(
            allowedRootCount: 2,
            toolDefinitions: definitions,
            packAvailability: packAvailability,
            pluginAvailability: pluginAvailability,
            routingCatalogDiagnostics: routingCatalog,
            orchestrationCatalog: orchestrationCatalog);
        var summary = HostProgram.FormatCapabilitySnapshotSummary(snapshot);
        var highlights = HostProgram.BuildCapabilitySnapshotHighlights(snapshot);

        Assert.Equal(2, snapshot.AllowedRootCount);
        Assert.Equal("remote_capable", snapshot.RemoteReachabilityMode);
        Assert.Equal(1, snapshot.EnabledPluginCount);
        Assert.Contains("ops_bundle", snapshot.EnabledPluginIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad-ops", snapshot.Skills, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("remote_reachability=remote_capable", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy remote-capable 2, cross-pack 2", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(highlights, static line => line.Contains("enabled packs: active_directory, eventlog, system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(highlights, static line => line.Contains("skills: ad-ops, event-triage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(highlights, static line => line.Contains("cross-pack targets: system", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildToolsInspectionLines_ExposeCapabilityRoutingAndPackAutonomyForLightweightHostInspection() {
        var definitions = CreateDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var routingCatalog = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
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
        var pluginAvailability = new[] {
            new ToolPluginAvailabilityInfo {
                Id = "ops_bundle",
                Name = "Ops Bundle",
                Origin = "folder",
                SourceKind = "closed_source",
                DefaultEnabled = true,
                Enabled = true,
                PackIds = new[] { "active_directory", "eventlog", "system" },
                SkillIds = new[] { "ad-ops", "event-triage" }
            }
        };

        var lines = HostProgram.BuildToolsInspectionLines(
            allowedRootCount: 2,
            toolDefinitions: definitions,
            packAvailability: packAvailability,
            pluginAvailability: pluginAvailability,
            routingCatalogDiagnostics: routingCatalog,
            orchestrationCatalog: orchestrationCatalog,
            showToolIds: true);

        Assert.Contains(lines, static line => line.Contains("Capability snapshot:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("[capability] remote-capable packs: eventlog, system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("Routing catalog:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("[routing]", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("Pack readiness:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("Active Directory [active_directory]: tools=1, remote-capable=0, setup-aware=0, handoff-aware=1, recovery-aware=0, cross-pack=1, targets=system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("Event Log [eventlog]: tools=1, remote-capable=1, setup-aware=1, handoff-aware=1, recovery-aware=1, cross-pack=1, targets=system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("Eventlog / Timeline Query (eventlog_timeline_query): Query event timeline from a host. [pack=eventlog, role=operational, scope=local_or_remote, remote_args=machine_name, setup=eventlog_channels_list, handoff=system/system_metrics_summary, recovery=eventlog_channels_list]", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, static line => line.Contains("System / Metrics Summary (system_metrics_summary): Summarize local system metrics. [pack=system, role=operational, scope=local_or_remote, remote_args=computer_name]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildHostToolsExportMessage_ProducesMachineReadableToolListPayload() {
        var definitions = CreateDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var routingCatalog = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
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
        var pluginAvailability = new[] {
            new ToolPluginAvailabilityInfo {
                Id = "ops_bundle",
                Name = "Ops Bundle",
                Origin = "folder",
                SourceKind = "closed_source",
                DefaultEnabled = true,
                Enabled = true,
                PackIds = new[] { "active_directory", "eventlog", "system" },
                SkillIds = new[] { "ad-ops", "event-triage" }
            }
        };

        var message = HostProgram.BuildHostToolsExportMessage(
            allowedRootCount: 2,
            toolDefinitions: definitions,
            packAvailability: packAvailability,
            pluginAvailability: pluginAvailability,
            routingCatalogDiagnostics: routingCatalog,
            orchestrationCatalog: orchestrationCatalog);
        var json = JsonSerializer.Serialize(message, ChatServiceJsonContext.Default.ToolListMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ToolListMessage);

        Assert.NotNull(parsed);
        Assert.Equal(ChatServiceMessageKind.Response, parsed!.Kind);
        Assert.Equal("host.toolsjson", parsed.RequestId);
        Assert.Equal(3, parsed.Tools.Length);
        Assert.Equal(3, parsed.Packs.Length);
        Assert.NotNull(parsed.RoutingCatalog);
        Assert.NotNull(parsed.CapabilitySnapshot);
        Assert.Equal("remote_capable", parsed.CapabilitySnapshot!.RemoteReachabilityMode);
        Assert.Equal(2, parsed.CapabilitySnapshot.Autonomy!.RemoteCapableToolCount);
        Assert.Equal(2, parsed.RoutingCatalog!.CrossPackHandoffTools);

        var eventLogTool = Assert.Single(parsed.Tools, static item =>
            string.Equals(item.Name, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("eventlog", eventLogTool.PackId);
        Assert.True(eventLogTool.SupportsRemoteHostTargeting);
        Assert.Contains("machine_name", eventLogTool.RemoteHostArguments, StringComparer.OrdinalIgnoreCase);
        Assert.True(eventLogTool.IsSetupAware);
        Assert.Equal("eventlog_channels_list", eventLogTool.SetupToolName);
        Assert.Contains("system", eventLogTool.HandoffTargetPackIds, StringComparer.OrdinalIgnoreCase);

        var eventLogPack = Assert.Single(parsed.Packs, static item =>
            string.Equals(item.Id, "eventlog", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(eventLogPack.AutonomySummary);
        Assert.Equal(1, eventLogPack.AutonomySummary!.RemoteCapableTools);
        Assert.Equal(1, eventLogPack.AutonomySummary.CrossPackHandoffTools);
        Assert.Contains("system", eventLogPack.AutonomySummary.CrossPackTargetPacks, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUnavailablePackAvailabilityWarnings_DeduplicatesAliasPackIdsWithCanonicalLabels() {
        var warnings = HostProgram.BuildUnavailablePackAvailabilityWarnings(new[] {
            new ToolPackAvailabilityInfo {
                Id = "ADPlayground",
                Name = "Active Directory",
                SourceKind = "closed_source",
                Enabled = false,
                DisabledReason = "Missing required module."
            },
            new ToolPackAvailabilityInfo {
                Id = "active_directory",
                Name = "Active Directory",
                SourceKind = "closed_source",
                Enabled = false,
                DisabledReason = "Missing required module."
            },
            new ToolPackAvailabilityInfo {
                Id = "ComputerX",
                Name = "System",
                SourceKind = "closed_source",
                Enabled = false,
                DisabledReason = "Remote endpoint unavailable."
            }
        });

        Assert.Equal(2, warnings.Count);
        Assert.Contains("Active Directory: Missing required module.", warnings, StringComparer.Ordinal);
        Assert.Contains("System: Remote endpoint unavailable.", warnings, StringComparer.Ordinal);
    }

    [Fact]
    public void FormatPackWarningForConsole_NormalizesStructuredToolHealthAliasPackIds() {
        const string warning = "[tool health][open_source][ADPlayground] ad_pack_info failed (smoke_not_configured): Select a domain before running the startup probe.";
        var formatted = HostProgram.FormatPackWarningForConsole(
            warning,
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "active_directory",
                    Name = "Active Directory",
                    SourceKind = "closed_source",
                    Enabled = true
                }
            });

        Assert.Equal(
            "Active Directory (Open): startup smoke check is not configured: Select a domain before running the startup probe.",
            formatted);
    }

    [Fact]
    public void FormatPackWarningForConsole_FormatsStructuredBootstrapWarnings() {
        const string warning = "[pack warning] [startup] tooling bootstrap timings total=1.8s policy=50ms options=20ms packs=1.6s registry=120ms tools=200 packsLoaded=14 packsDisabled=2 pluginRoots=3.";

        var formatted = HostProgram.FormatPackWarningForConsole(warning);

        Assert.Equal(
            "Starting runtime... tool bootstrap finished (1.8s), finalizing runtime connection",
            formatted);
    }

    [Fact]
    public void FormatPackWarningForConsole_FormatsBootstrapCacheHitWarnings() {
        const string warning = "[startup] tooling bootstrap cache hit elapsed=42ms tools=187 packsLoaded=10.";

        var formatted = HostProgram.FormatPackWarningForConsole(warning);

        Assert.Equal(
            "Starting runtime... reused tooling bootstrap cache (42ms), finalizing runtime connection",
            formatted);
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
