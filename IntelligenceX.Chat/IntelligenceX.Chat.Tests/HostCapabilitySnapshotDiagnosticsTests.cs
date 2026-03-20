using System;
using System.Collections.Generic;
using System.Linq;
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
                EngineId = "adplayground",
                CapabilityTags = new[] { "directory", "remote_analysis" },
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "eventlog",
                Name = "Event Log",
                SourceKind = "closed_source",
                EngineId = "eventviewerx",
                CapabilityTags = new[] { "event_logs", "evtx" },
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "system",
                Name = "System",
                SourceKind = "closed_source",
                EngineId = "computerx",
                CapabilityTags = new[] { "host_inventory", "storage" },
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
        Assert.Contains("adplayground", snapshot.EnabledPackEngineIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("computerx", snapshot.EnabledPackEngineIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("directory", snapshot.EnabledCapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("host_inventory", snapshot.EnabledCapabilityTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad-ops", snapshot.Skills, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("remote_reachability=remote_capable", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("autonomy remote-capable 2, cross-pack 2", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("governed-write 1", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, snapshot.Autonomy!.LocalCapableToolCount);
        Assert.Equal(1, snapshot.Autonomy.GovernedWriteToolCount);
        Assert.Contains(highlights, static line => line.Contains("enabled packs: active_directory, eventlog, system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(highlights, static line => line.Contains("local-capable packs: active_directory, eventlog, system", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            highlights,
            static line => line.Contains("enabled engines:", StringComparison.OrdinalIgnoreCase)
                && line.Contains("adplayground", StringComparison.OrdinalIgnoreCase)
                && line.Contains("computerx", StringComparison.OrdinalIgnoreCase)
                && line.Contains("eventviewerx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            highlights,
            static line => line.Contains("enabled capability tags:", StringComparison.OrdinalIgnoreCase)
                && line.Contains("directory", StringComparison.OrdinalIgnoreCase)
                && line.Contains("host_inventory", StringComparison.OrdinalIgnoreCase)
                && line.Contains("remote_analysis", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(highlights, static line => line.Contains("skills: ad-ops, event-triage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(highlights, static line => line.Contains("governed-write packs: active_directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(highlights, static line => line.Contains("cross-pack targets: system", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildHostCapabilitySnapshot_TracksWriteCapableNonDangerousPackVisibility() {
        var definitions = CreateDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var routingCatalog = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
        var packAvailability = new[] {
            new ToolPackAvailabilityInfo {
                Id = "active_directory",
                Name = "Active Directory",
                Tier = ToolCapabilityTier.SensitiveRead,
                IsDangerous = false,
                SourceKind = "closed_source",
                Enabled = true
            }
        };

        var snapshot = HostProgram.BuildHostCapabilitySnapshot(
            allowedRootCount: 0,
            toolDefinitions: definitions,
            packAvailability: packAvailability,
            pluginAvailability: Array.Empty<ToolPluginAvailabilityInfo>(),
            routingCatalogDiagnostics: routingCatalog,
            orchestrationCatalog: orchestrationCatalog);

        Assert.True(snapshot.DangerousToolsEnabled);
        Assert.Equal(new[] { "active_directory" }, snapshot.DangerousPackIds);
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
                EngineId = "adplayground",
                CapabilityTags = new[] { "directory", "remote_analysis" },
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "eventlog",
                Name = "Event Log",
                SourceKind = "closed_source",
                EngineId = "eventviewerx",
                CapabilityTags = new[] { "event_logs", "evtx" },
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "system",
                Name = "System",
                SourceKind = "closed_source",
                EngineId = "computerx",
                CapabilityTags = new[] { "host_inventory", "storage" },
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
        var activeDirectoryPackLine = Assert.Single(lines, static line => line.Contains("Active Directory [active_directory]:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("target-scoped=1", activeDirectoryPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("write-capable=1", activeDirectoryPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross-pack=1", activeDirectoryPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("targets=system", activeDirectoryPackLine, StringComparison.OrdinalIgnoreCase);

        var eventLogPackLine = Assert.Single(lines, static line => line.Contains("Event Log [eventlog]:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("remote-capable=1", eventLogPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target-scoped=1", eventLogPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote-targeting=1", eventLogPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auth-required=1", eventLogPackLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probe-capable=1", eventLogPackLine, StringComparison.OrdinalIgnoreCase);

        var eventLogToolLine = Assert.Single(lines, static line => line.Contains("Eventlog / Timeline Query (eventlog_timeline_query):", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("pack=eventlog", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope=local_or_remote", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote_args=machine_name", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_scope=channel/machine_name", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auth=ix.auth.runtime.v1", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probe=eventlog_channels_list", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setup=eventlog_channels_list", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handoff=system/system_metrics_summary", eventLogToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovery=eventlog_channels_list", eventLogToolLine, StringComparison.OrdinalIgnoreCase);

        var activeDirectoryToolLine = Assert.Single(lines, static line => line.Contains("(ad_domain_monitor):", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("pack=active_directory", activeDirectoryToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope=local_only", activeDirectoryToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_scope=domain_name", activeDirectoryToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("write=mutating", activeDirectoryToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("handoff=system/system_info", activeDirectoryToolLine, StringComparison.OrdinalIgnoreCase);

        var systemToolLine = Assert.Single(lines, static line => line.Contains("(system_metrics_summary):", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("pack=system", systemToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope=local_or_remote", systemToolLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote_args=computer_name", systemToolLine, StringComparison.OrdinalIgnoreCase);
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
                EngineId = "adplayground",
                CapabilityTags = new[] { "directory", "remote_analysis" },
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "eventlog",
                Name = "Event Log",
                SourceKind = "closed_source",
                EngineId = "eventviewerx",
                CapabilityTags = new[] { "event_logs", "evtx" },
                Enabled = true
            },
            new ToolPackAvailabilityInfo {
                Id = "system",
                Name = "System",
                SourceKind = "closed_source",
                EngineId = "computerx",
                CapabilityTags = new[] { "host_inventory", "storage" },
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
        Assert.True(eventLogTool.SupportsLocalExecution);
        Assert.True(eventLogTool.SupportsRemoteExecution);
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
    public void BuildHostToolsExportMessage_ProjectsWriteCapableNonDangerousPackAsDangerous() {
        var definitions = CreateDefinitions();
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(definitions);
        var routingCatalog = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
        var packAvailability = new[] {
            new ToolPackAvailabilityInfo {
                Id = "active_directory",
                Name = "Active Directory",
                Tier = ToolCapabilityTier.SensitiveRead,
                IsDangerous = false,
                SourceKind = "closed_source",
                Enabled = true
            }
        };

        var message = HostProgram.BuildHostToolsExportMessage(
            allowedRootCount: 0,
            toolDefinitions: definitions,
            packAvailability: packAvailability,
            pluginAvailability: Array.Empty<ToolPluginAvailabilityInfo>(),
            routingCatalogDiagnostics: routingCatalog,
            orchestrationCatalog: orchestrationCatalog);

        var activeDirectoryPack = Assert.Single(message.Packs, static pack =>
            string.Equals(pack.Id, "active_directory", StringComparison.OrdinalIgnoreCase));
        Assert.True(activeDirectoryPack.IsDangerous);
        Assert.NotNull(activeDirectoryPack.AutonomySummary);
        Assert.Equal(1, activeDirectoryPack.AutonomySummary!.WriteCapableTools);
    }

    [Fact]
    public void BuildHostToolsExportMessage_PreservesRepresentativeMetadataFromOrchestrationCatalog() {
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
        var parsed = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(message, ChatServiceJsonContext.Default.ToolListMessage),
            ChatServiceJsonContext.Default.ToolListMessage);

        Assert.NotNull(parsed);
        var toolsByName = parsed!.Tools.ToDictionary(static item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var toolName in new[] { "ad_domain_monitor", "eventlog_timeline_query", "system_metrics_summary" }) {
            Assert.True(orchestrationCatalog.TryGetEntry(toolName, out var entry));
            AssertToolDtoMatchesOrchestration(toolsByName[toolName], entry!);
        }

        var packsById = parsed.Packs.ToDictionary(static item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var packId in new[] { "active_directory", "eventlog", "system" }) {
            var expected = ToolAutonomySummaryBuilder.BuildPackAutonomySummary(packId, orchestrationCatalog);
            Assert.NotNull(expected);
            AssertPackSummaryMatchesOrchestration(packsById[packId], expected!);
        }
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

    private static void AssertToolDtoMatchesOrchestration(ToolDefinitionDto dto, ToolOrchestrationCatalogEntry entry) {
        Assert.Equal(entry.PackId, dto.PackId);
        Assert.Equal(entry.IsPackInfoTool, dto.IsPackInfoTool);
        Assert.Equal(entry.IsEnvironmentDiscoverTool, dto.IsEnvironmentDiscoverTool);
        Assert.Equal(entry.IsWriteCapable, dto.IsWriteCapable);
        Assert.Equal(entry.RequiresWriteGovernance, dto.RequiresWriteGovernance);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.WriteGovernanceContractId) ? null : entry.WriteGovernanceContractId, dto.WriteGovernanceContractId);
        Assert.Equal(entry.RequiresAuthentication, dto.RequiresAuthentication);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.AuthenticationContractId) ? null : entry.AuthenticationContractId, dto.AuthenticationContractId);
        Assert.Equal(entry.AuthenticationArguments, dto.AuthenticationArguments);
        Assert.Equal(entry.SupportsConnectivityProbe, dto.SupportsConnectivityProbe);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.ProbeToolName) ? null : entry.ProbeToolName, dto.ProbeToolName);
        Assert.Equal(entry.ExecutionScope, dto.ExecutionScope);
        Assert.Equal(entry.SupportsTargetScoping, dto.SupportsTargetScoping);
        Assert.Equal(entry.TargetScopeArguments, dto.TargetScopeArguments);
        Assert.Equal(entry.SupportsRemoteHostTargeting, dto.SupportsRemoteHostTargeting);
        Assert.Equal(entry.RemoteHostArguments, dto.RemoteHostArguments);
        Assert.Equal(entry.RepresentativeExamples, dto.RepresentativeExamples);
        Assert.Equal(entry.IsSetupAware, dto.IsSetupAware);
        Assert.Equal(string.IsNullOrWhiteSpace(entry.SetupToolName) ? null : entry.SetupToolName, dto.SetupToolName);
        Assert.Equal(entry.IsHandoffAware, dto.IsHandoffAware);
        Assert.Equal(
            entry.HandoffEdges
                .Select(static edge => edge.TargetPackId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            dto.HandoffTargetPackIds);
        Assert.Equal(
            entry.HandoffEdges
                .Select(static edge => edge.TargetToolName)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            dto.HandoffTargetToolNames);
        Assert.Equal(entry.IsRecoveryAware, dto.IsRecoveryAware);
        Assert.Equal(entry.SupportsTransientRetry, dto.SupportsTransientRetry);
        Assert.Equal(entry.MaxRetryAttempts, dto.MaxRetryAttempts);
        Assert.Equal(entry.RecoveryToolNames, dto.RecoveryToolNames);
    }

    private static void AssertPackSummaryMatchesOrchestration(
        ToolPackInfoDto pack,
        ToolPackAutonomySummaryDto expected) {
        var summary = Assert.IsType<ToolPackAutonomySummaryDto>(pack.AutonomySummary);
        Assert.Equal(expected.TotalTools, summary.TotalTools);
        Assert.Equal(expected.RemoteCapableTools, summary.RemoteCapableTools);
        Assert.Equal(expected.RemoteCapableToolNames, summary.RemoteCapableToolNames);
        Assert.Equal(expected.SetupAwareTools, summary.SetupAwareTools);
        Assert.Equal(expected.EnvironmentDiscoverTools, summary.EnvironmentDiscoverTools);
        Assert.Equal(expected.SetupAwareToolNames, summary.SetupAwareToolNames);
        Assert.Equal(expected.EnvironmentDiscoverToolNames, summary.EnvironmentDiscoverToolNames);
        Assert.Equal(expected.HandoffAwareTools, summary.HandoffAwareTools);
        Assert.Equal(expected.HandoffAwareToolNames, summary.HandoffAwareToolNames);
        Assert.Equal(expected.RecoveryAwareTools, summary.RecoveryAwareTools);
        Assert.Equal(expected.RecoveryAwareToolNames, summary.RecoveryAwareToolNames);
        Assert.Equal(expected.CrossPackHandoffTools, summary.CrossPackHandoffTools);
        Assert.Equal(expected.CrossPackHandoffToolNames, summary.CrossPackHandoffToolNames);
        Assert.Equal(expected.CrossPackTargetPacks, summary.CrossPackTargetPacks);
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
                authentication: new ToolAuthenticationContract {
                    IsAuthenticationAware = true,
                    RequiresAuthentication = true,
                    AuthenticationContractId = "ix.auth.runtime.v1",
                    Mode = ToolAuthenticationMode.ProfileReference,
                    ProfileIdArgumentName = "profile_id",
                    SupportsConnectivityProbe = true,
                    ProbeToolName = "eventlog_channels_list",
                    ProbeIdArgumentName = "probe_id"
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
                execution: new ToolExecutionContract {
                    IsExecutionAware = true,
                    ExecutionScope = ToolExecutionScopes.LocalOnly,
                    TargetScopeArguments = new[] { "domain_name" }
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
                },
                writeGovernance: new ToolWriteGovernanceContract {
                    IsWriteCapable = true
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
