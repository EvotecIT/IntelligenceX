using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class SessionPolicyContractTests {
    [Fact]
    public void HelloMessage_RoundTripsPolicyDiagnostics() {
        var hello = new HelloMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_1",
            Name = "IntelligenceX.Chat.Service",
            Version = "1.0.0",
            ProcessId = "1234",
            Policy = new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 3,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto {
                        Id = "testimox",
                        Name = "TestimoX",
                        Description = "Test diagnostics",
                        Tier = CapabilityTier.SensitiveRead,
                        Enabled = false,
                        DisabledReason = "License expired on 2026-03-31.",
                        IsDangerous = false,
                        SourceKind = ToolPackSourceKind.ClosedSource,
                        Category = "testimox",
                        EngineId = "testimox",
                        Aliases = new[] { "testimo", "testimoxpack" },
                        CapabilityTags = new[] { "reporting", "remote_analysis" },
                        SearchTokens = new[] { "testimox", "testimo_x", "reporting" },
                        AutonomySummary = new ToolPackAutonomySummaryDto {
                            TotalTools = 3,
                            RemoteCapableTools = 1,
                            RemoteCapableToolNames = new[] { "testimox_rules_run" },
                            TargetScopedTools = 2,
                            TargetScopedToolNames = new[] { "testimox_pack_info", "testimox_rules_run" },
                            RemoteHostTargetingTools = 1,
                            RemoteHostTargetingToolNames = new[] { "testimox_rules_run" },
                            SetupAwareTools = 1,
                            EnvironmentDiscoverTools = 1,
                            SetupAwareToolNames = new[] { "testimox_pack_info" },
                            EnvironmentDiscoverToolNames = new[] { "testimox_pack_info" },
                            HandoffAwareTools = 2,
                            HandoffAwareToolNames = new[] { "testimox_rules_run", "testimox_run_summary" },
                            RecoveryAwareTools = 1,
                            RecoveryAwareToolNames = new[] { "testimox_rules_run" },
                            WriteCapableTools = 1,
                            WriteCapableToolNames = new[] { "testimox_rules_run" },
                            AuthenticationRequiredTools = 1,
                            AuthenticationRequiredToolNames = new[] { "testimox_rules_run" },
                            ProbeCapableTools = 1,
                            ProbeCapableToolNames = new[] { "testimox_rules_run" },
                            CrossPackHandoffTools = 2,
                            CrossPackHandoffToolNames = new[] { "testimox_rules_run", "testimox_run_summary" },
                            CrossPackTargetPacks = new[] { "active_directory", "eventlog", "system" }
                        }
                    }
                },
                Plugins = new[] {
                    new PluginInfoDto {
                        Id = "ix-testimox",
                        Name = "TestimoX Plugin",
                        Version = "1.2.3",
                        Origin = "plugin_folder",
                        SourceKind = ToolPackSourceKind.ClosedSource,
                        DefaultEnabled = false,
                        Enabled = false,
                        DisabledReason = "License expired on 2026-03-31.",
                        IsDangerous = false,
                        PackIds = new[] { "testimox" },
                        RootPath = "C:\\plugins\\testimox",
                        SkillDirectories = new[] { "C:\\plugins\\testimox\\skills" },
                        SkillIds = new[] { "testimox.health", "testimox.permissions" }
                    }
                },
                StartupWarnings = new[] {
                    "[plugin] path_not_found path='C:\\plugins\\missing'",
                    "[plugin] init_failed plugin='ix.mail' error='dependency missing'"
                },
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto {
                    TotalMs = 4120,
                    RuntimePolicyMs = 35,
                    BootstrapOptionsMs = 14,
                    PackLoadMs = 3988,
                    PackRegisterMs = 52,
                    RegistryFinalizeMs = 31,
                    RegistryMs = 83,
                    Tools = 142,
                    PacksLoaded = 10,
                    PacksDisabled = 2,
                    PluginRoots = 2,
                    SlowPackCount = 2,
                    SlowPackTopCount = 2,
                    PackProgressProcessed = 12,
                    PackProgressTotal = 12,
                    SlowPluginCount = 3,
                    SlowPluginTopCount = 3,
                    PluginProgressProcessed = 5,
                    PluginProgressTotal = 5,
                    Phases = new[] {
                        new SessionStartupBootstrapPhaseTelemetryDto {
                            Id = StartupBootstrapContracts.PhaseRuntimePolicyId,
                            Label = StartupBootstrapContracts.PhaseRuntimePolicyLabel,
                            DurationMs = 35,
                            Order = 1
                        },
                        new SessionStartupBootstrapPhaseTelemetryDto {
                            Id = StartupBootstrapContracts.PhaseDescriptorDiscoveryId,
                            Label = StartupBootstrapContracts.PhaseDescriptorDiscoveryLabel,
                            DurationMs = 3988,
                            Order = 2
                        }
                    },
                    SlowestPhaseId = StartupBootstrapContracts.PhaseDescriptorDiscoveryId,
                    SlowestPhaseLabel = StartupBootstrapContracts.PhaseDescriptorDiscoveryLabel,
                    SlowestPhaseMs = 3988
                },
                PluginSearchPaths = new[] {
                    "C:\\Users\\user\\AppData\\Local\\IntelligenceX.Chat\\plugins",
                    "C:\\Support\\GitHub\\IntelligenceX\\plugins"
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 8,
                    EnabledPackCount = 0,
                    PluginCount = 1,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    EnabledPackIds = System.Array.Empty<string>(),
                    EnabledPluginIds = System.Array.Empty<string>(),
                    DangerousToolsEnabled = true,
                    DangerousPackIds = new[] { "active_directory" },
                    RoutingFamilies = new[] { "ad_domain", "public_domain" },
                    FamilyActions = new[] {
                        new SessionRoutingFamilyActionSummaryDto {
                            Family = "ad_domain",
                            ActionId = "act_domain_scope_ad",
                            ToolCount = 2
                        }
                    },
                    Skills = new[] { "ad_domain.act_domain_scope_ad" },
                    RepresentativeExamples = new[] { "discover AD scope before querying remote evidence" },
                    CrossPackTargetPackDisplayNames = new[] { "System", "Event Log" },
                    HealthyTools = new[] { "unit_test_tool" },
                    RemoteReachabilityMode = "remote_capable",
                    Autonomy = new SessionCapabilityAutonomySummaryDto {
                        RemoteCapableToolCount = 2,
                        TargetScopedToolCount = 3,
                        RemoteHostTargetingToolCount = 1,
                        SetupAwareToolCount = 1,
                        EnvironmentDiscoverToolCount = 1,
                        HandoffAwareToolCount = 2,
                        RecoveryAwareToolCount = 1,
                        WriteCapableToolCount = 1,
                        AuthenticationRequiredToolCount = 1,
                        ProbeCapableToolCount = 1,
                        CrossPackHandoffToolCount = 1,
                        RemoteCapablePackIds = new[] { "testimox" },
                        TargetScopedPackIds = new[] { "testimox" },
                        RemoteHostTargetingPackIds = new[] { "testimox" },
                        EnvironmentDiscoverPackIds = new[] { "testimox" },
                        WriteCapablePackIds = new[] { "testimox" },
                        AuthenticationRequiredPackIds = new[] { "testimox" },
                        ProbeCapablePackIds = new[] { "testimox" },
                        CrossPackReadyPackIds = new[] { "testimox" },
                        CrossPackTargetPackIds = new[] { "system", "eventlog" }
                    }
                },
                RoutingCatalog = new SessionRoutingCatalogDiagnosticsDto {
                    TotalTools = 8,
                    RoutingAwareTools = 8,
                    MissingRoutingContractTools = 0,
                    RemoteCapableTools = 2,
                    CrossPackHandoffTools = 1,
                    DomainFamilyTools = 4,
                    ExpectedDomainFamilyMissingTools = 0,
                    DomainFamilyMissingActionTools = 0,
                    ActionWithoutFamilyTools = 0,
                    FamilyActionConflictFamilies = 0,
                    IsHealthy = true,
                    FamilyActions = new[] {
                        new SessionRoutingFamilyActionSummaryDto {
                            Family = "ad_domain",
                            ActionId = "act_domain_scope_ad",
                            ToolCount = 2
                        },
                        new SessionRoutingFamilyActionSummaryDto {
                            Family = "public_domain",
                            ActionId = "act_domain_scope_public",
                            ToolCount = 2
                        }
                    },
                    AutonomyReadinessHighlights = new[] {
                        "remote host-targeting is ready for 2 tool(s).",
                        "cross-pack continuation is ready for 1 tool(s)."
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(hello, ChatServiceJsonContext.Default.ChatServiceMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var roundTrip = Assert.IsType<HelloMessage>(parsed);
        var policy = Assert.IsType<SessionPolicyDto>(roundTrip.Policy);

        Assert.Equal(2, policy.StartupWarnings.Length);
        Assert.Equal("[plugin] path_not_found path='C:\\plugins\\missing'", policy.StartupWarnings[0]);
        Assert.Equal(2, policy.PluginSearchPaths.Length);
        Assert.Equal("C:\\Support\\GitHub\\IntelligenceX\\plugins", policy.PluginSearchPaths[1]);
        var capabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(policy.CapabilitySnapshot);
        Assert.True(capabilitySnapshot.ToolingAvailable);
        Assert.Equal(8, capabilitySnapshot.RegisteredTools);
        Assert.Empty(capabilitySnapshot.EnabledPackIds);
        Assert.Empty(capabilitySnapshot.EnabledPluginIds);
        Assert.True(capabilitySnapshot.DangerousToolsEnabled);
        Assert.Equal(new[] { "active_directory" }, capabilitySnapshot.DangerousPackIds);
        Assert.Equal("discover AD scope before querying remote evidence", Assert.Single(capabilitySnapshot.RepresentativeExamples));
        Assert.Equal(new[] { "System", "Event Log" }, capabilitySnapshot.CrossPackTargetPackDisplayNames);
        Assert.Equal("unit_test_tool", capabilitySnapshot.HealthyTools[0]);
        var autonomy = Assert.IsType<SessionCapabilityAutonomySummaryDto>(capabilitySnapshot.Autonomy);
        Assert.Equal(2, autonomy.RemoteCapableToolCount);
        Assert.Equal(3, autonomy.TargetScopedToolCount);
        Assert.Equal(1, autonomy.RemoteHostTargetingToolCount);
        Assert.Equal(1, autonomy.EnvironmentDiscoverToolCount);
        Assert.Equal(1, autonomy.WriteCapableToolCount);
        Assert.Equal(1, autonomy.AuthenticationRequiredToolCount);
        Assert.Equal(1, autonomy.ProbeCapableToolCount);
        Assert.Equal(new[] { "system", "eventlog" }, autonomy.CrossPackTargetPackIds);
        var startupBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(policy.StartupBootstrap);
        Assert.Equal(4120, startupBootstrap.TotalMs);
        Assert.Equal(3988, startupBootstrap.PackLoadMs);
        Assert.Equal(52, startupBootstrap.PackRegisterMs);
        Assert.Equal(31, startupBootstrap.RegistryFinalizeMs);
        Assert.Equal(2, startupBootstrap.SlowPackCount);
        Assert.Equal(12, startupBootstrap.PackProgressProcessed);
        Assert.Equal(12, startupBootstrap.PackProgressTotal);
        Assert.Equal(5, startupBootstrap.PluginProgressProcessed);
        Assert.Equal(5, startupBootstrap.PluginProgressTotal);
        Assert.Equal(2, startupBootstrap.Phases.Length);
        Assert.Equal(StartupBootstrapContracts.PhaseDescriptorDiscoveryId, startupBootstrap.SlowestPhaseId);
        Assert.Equal(3988, startupBootstrap.SlowestPhaseMs);
        Assert.False(policy.AllowMutatingParallelToolCalls);
        Assert.Single(policy.Packs);
        Assert.False(policy.Packs[0].Enabled);
        Assert.Equal("License expired on 2026-03-31.", policy.Packs[0].DisabledReason);
        Assert.Equal("testimox", policy.Packs[0].Category);
        Assert.Equal("testimox", policy.Packs[0].EngineId);
        Assert.Equal(new[] { "testimo", "testimoxpack" }, policy.Packs[0].Aliases);
        Assert.Equal(new[] { "reporting", "remote_analysis" }, policy.Packs[0].CapabilityTags);
        Assert.Equal(new[] { "testimox", "testimo_x", "reporting" }, policy.Packs[0].SearchTokens);
        var autonomySummary = Assert.IsType<ToolPackAutonomySummaryDto>(policy.Packs[0].AutonomySummary);
        Assert.Equal(3, autonomySummary.TotalTools);
        Assert.Equal(2, autonomySummary.TargetScopedTools);
        Assert.Equal(1, autonomySummary.RemoteHostTargetingTools);
        Assert.Equal(1, autonomySummary.EnvironmentDiscoverTools);
        Assert.Equal(1, autonomySummary.WriteCapableTools);
        Assert.Equal(1, autonomySummary.AuthenticationRequiredTools);
        Assert.Equal(1, autonomySummary.ProbeCapableTools);
        Assert.Equal(new[] { "active_directory", "eventlog", "system" }, autonomySummary.CrossPackTargetPacks);
        Assert.Single(policy.Plugins);
        Assert.Equal("ix-testimox", policy.Plugins[0].Id);
        Assert.Equal("C:\\plugins\\testimox\\skills", Assert.Single(policy.Plugins[0].SkillDirectories));
        Assert.Equal(new[] { "testimox.health", "testimox.permissions" }, policy.Plugins[0].SkillIds);
        var routingCatalog = Assert.IsType<SessionRoutingCatalogDiagnosticsDto>(policy.RoutingCatalog);
        Assert.True(routingCatalog.IsHealthy);
        Assert.Equal(8, routingCatalog.TotalTools);
        Assert.Equal(2, routingCatalog.RemoteCapableTools);
        Assert.Equal(1, routingCatalog.CrossPackHandoffTools);
        Assert.Equal(2, routingCatalog.FamilyActions.Length);
        Assert.Equal("ad_domain", routingCatalog.FamilyActions[0].Family);
        Assert.Equal("remote host-targeting is ready for 2 tool(s).", routingCatalog.AutonomyReadinessHighlights[0]);
    }
}
