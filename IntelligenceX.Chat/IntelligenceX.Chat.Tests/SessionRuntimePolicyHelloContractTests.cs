using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class SessionRuntimePolicyHelloContractTests {
    [Fact]
    public void HelloMessage_ShouldIncludeSerializedRuntimePolicyFromSessionBuild() {
        var runtimePolicy = new ToolRuntimePolicyDiagnostics {
            WriteGovernanceMode = ToolWriteGovernanceMode.Yolo,
            RequireWriteGovernanceRuntime = false,
            WriteGovernanceRuntimeConfigured = false,
            RequireWriteAuditSinkForWriteOperations = true,
            WriteAuditSinkMode = ToolWriteAuditSinkMode.FileAppendOnly,
            WriteAuditSinkConfigured = true,
            WriteAuditSinkPath = "C:\\audit\\events.jsonl",
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
            RequireExplicitRoutingMetadata = true,
            RequireAuthenticationRuntime = true,
            AuthenticationRuntimeConfigured = true,
            RequireSuccessfulSmtpProbeForSend = true,
            SmtpProbeMaxAgeSeconds = 600,
            RunAsProfilePath = "C:\\profiles\\run-as.json",
            AuthenticationProfilePath = "C:\\profiles\\auth.json"
        };
        var routingCatalog = new ToolRoutingCatalogDiagnostics {
            TotalTools = 12,
            RoutingAwareTools = 12,
            ExplicitRoutingTools = 11,
            InferredRoutingTools = 1,
            MissingRoutingContractTools = 0,
            MissingPackIdTools = 0,
            MissingRoleTools = 0,
            SetupAwareTools = 2,
            HandoffAwareTools = 1,
            RecoveryAwareTools = 3,
            RemoteCapableTools = 5,
            CrossPackHandoffTools = 2,
            DomainFamilyTools = 6,
            ExpectedDomainFamilyMissingTools = 0,
            DomainFamilyMissingActionTools = 0,
            ActionWithoutFamilyTools = 0,
            FamilyActionConflictFamilies = 0,
            FamilyActions = new[] {
                new ToolRoutingFamilyActionSummary {
                    Family = "ad_domain",
                    ActionId = "act_domain_scope_ad",
                    ToolCount = 3
                },
                new ToolRoutingFamilyActionSummary {
                    Family = "public_domain",
                    ActionId = "act_domain_scope_public",
                    ToolCount = 3
                }
            }
        };

        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions {
                ParallelTools = true,
                AllowMutatingParallelToolCalls = true
            },
            Array.Empty<ToolPackAvailabilityInfo>(),
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            runtimePolicy,
            routingCatalog,
            healthyToolNames: new[] { "ad_domain_lookup" },
            remoteReachabilityMode: "remote_capable");

        var hello = new HelloMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_runtime",
            Name = "IntelligenceX.Chat.Service",
            Version = "1.0.0",
            ProcessId = "1234",
            Policy = policy
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(hello, ChatServiceJsonContext.Default.ChatServiceMessage);
        using var doc = JsonDocument.Parse(json);
        var serializedPolicy = doc.RootElement.GetProperty("policy");
        Assert.True(serializedPolicy.GetProperty("parallelTools").GetBoolean());
        Assert.True(serializedPolicy.GetProperty("allowMutatingParallelToolCalls").GetBoolean());

        var runtime = serializedPolicy.GetProperty("runtimePolicy");

        Assert.Equal("yolo", runtime.GetProperty("writeGovernanceMode").GetString());
        Assert.False(runtime.GetProperty("requireWriteGovernanceRuntime").GetBoolean());
        Assert.False(runtime.GetProperty("writeGovernanceRuntimeConfigured").GetBoolean());
        Assert.True(runtime.GetProperty("requireWriteAuditSinkForWriteOperations").GetBoolean());
        Assert.Equal("file", runtime.GetProperty("writeAuditSinkMode").GetString());
        Assert.True(runtime.GetProperty("writeAuditSinkConfigured").GetBoolean());
        Assert.Equal("C:\\audit\\events.jsonl", runtime.GetProperty("writeAuditSinkPath").GetString());
        Assert.Equal("strict", runtime.GetProperty("authenticationRuntimePreset").GetString());
        Assert.True(runtime.GetProperty("requireExplicitRoutingMetadata").GetBoolean());
        Assert.True(runtime.GetProperty("requireAuthenticationRuntime").GetBoolean());
        Assert.True(runtime.GetProperty("authenticationRuntimeConfigured").GetBoolean());
        Assert.True(runtime.GetProperty("requireSuccessfulSmtpProbeForSend").GetBoolean());
        Assert.Equal(600, runtime.GetProperty("smtpProbeMaxAgeSeconds").GetInt32());
        Assert.Equal("C:\\profiles\\run-as.json", runtime.GetProperty("runAsProfilePath").GetString());
        Assert.Equal("C:\\profiles\\auth.json", runtime.GetProperty("authenticationProfilePath").GetString());

        var serializedRoutingCatalog = serializedPolicy.GetProperty("routingCatalog");
        Assert.True(serializedRoutingCatalog.GetProperty("isHealthy").GetBoolean());
        Assert.False(serializedRoutingCatalog.GetProperty("isExplicitRoutingReady").GetBoolean());
        Assert.Equal(12, serializedRoutingCatalog.GetProperty("totalTools").GetInt32());
        Assert.Equal(11, serializedRoutingCatalog.GetProperty("explicitRoutingTools").GetInt32());
        Assert.Equal(1, serializedRoutingCatalog.GetProperty("inferredRoutingTools").GetInt32());
        Assert.Equal(2, serializedRoutingCatalog.GetProperty("setupAwareTools").GetInt32());
        Assert.Equal(1, serializedRoutingCatalog.GetProperty("handoffAwareTools").GetInt32());
        Assert.Equal(3, serializedRoutingCatalog.GetProperty("recoveryAwareTools").GetInt32());
        Assert.Equal(5, serializedRoutingCatalog.GetProperty("remoteCapableTools").GetInt32());
        Assert.Equal(2, serializedRoutingCatalog.GetProperty("crossPackHandoffTools").GetInt32());
        Assert.Equal(2, serializedRoutingCatalog.GetProperty("familyActions").GetArrayLength());
        Assert.Equal(6, serializedRoutingCatalog.GetProperty("autonomyReadinessHighlights").GetArrayLength());
        Assert.Contains(
            "remote host-targeting",
            serializedRoutingCatalog.GetProperty("autonomyReadinessHighlights")[0].GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "act_domain_scope_ad",
            serializedRoutingCatalog
                .GetProperty("familyActions")[0]
                .GetProperty("actionId")
                .GetString());

        var capabilitySnapshot = serializedPolicy.GetProperty("capabilitySnapshot");
        Assert.True(capabilitySnapshot.GetProperty("toolingAvailable").GetBoolean());
        Assert.Equal(12, capabilitySnapshot.GetProperty("registeredTools").GetInt32());
        Assert.Equal(0, capabilitySnapshot.GetProperty("enabledPackCount").GetInt32());
        Assert.Equal(0, capabilitySnapshot.GetProperty("pluginCount").GetInt32());
        Assert.Equal(0, capabilitySnapshot.GetProperty("enabledPluginCount").GetInt32());
        Assert.Equal("remote_capable", capabilitySnapshot.GetProperty("remoteReachabilityMode").GetString());
        Assert.Equal(2, capabilitySnapshot.GetProperty("routingFamilies").GetArrayLength());
        Assert.Equal(2, capabilitySnapshot.GetProperty("familyActions").GetArrayLength());
        Assert.Equal(2, capabilitySnapshot.GetProperty("skills").GetArrayLength());
        Assert.Equal("ad_domain_lookup", capabilitySnapshot.GetProperty("healthyTools")[0].GetString());
    }

    [Fact]
    public void BuildSessionPolicy_NormalizesMaxToolRoundsToAtLeastOne() {
        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions {
                MaxToolRounds = ChatRequestOptionLimits.MinToolRounds - 1
            },
            Array.Empty<ToolPackAvailabilityInfo>(),
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            });

        Assert.Equal(ChatRequestOptionLimits.MinToolRounds, policy.MaxToolRounds);
    }

    [Fact]
    public void BuildSessionPolicy_ClampsMaxToolRoundsToSafetyLimit() {
        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions {
                MaxToolRounds = 500
            },
            Array.Empty<ToolPackAvailabilityInfo>(),
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            });

        Assert.Equal(ChatRequestOptionLimits.MaxToolRounds, policy.MaxToolRounds);
    }

    [Fact]
    public void BuildSessionPolicy_PrefersResolvedPluginSkillIdsInCapabilitySnapshot() {
        var skillIds = Enumerable.Range(1, 10)
            .Select(index => $"inventory-skill-{index:00}")
            .ToArray();
        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    SourceKind = "open_source",
                    Enabled = true
                }
            },
            new[] {
                new ToolPluginAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    Origin = "plugin_folder",
                    SourceKind = "open_source",
                    DefaultEnabled = true,
                    Enabled = true,
                    PackIds = new[] { "plugin-loader-test" },
                    SkillIds = skillIds
                }
            },
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 4,
                RoutingAwareTools = 4,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 1,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 4
                    }
                }
            });

        Assert.Equal(8, policy.CapabilitySnapshot!.Skills.Length);
        Assert.Equal(skillIds.Take(8).ToArray(), policy.CapabilitySnapshot.Skills);
        Assert.Equal(skillIds, policy.Plugins[0].SkillIds);
    }

    [Fact]
    public void BuildSessionPolicy_MergesConnectedRuntimeSkillsIntoCapabilitySnapshotBeforeRoutingFallback() {
        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    SourceKind = "open_source",
                    Enabled = true
                }
            },
            new[] {
                new ToolPluginAvailabilityInfo {
                    Id = "plugin-loader-test",
                    Name = "Plugin Loader Test",
                    Origin = "plugin_folder",
                    SourceKind = "open_source",
                    DefaultEnabled = true,
                    Enabled = true,
                    PackIds = new[] { "plugin-loader-test" },
                    SkillIds = new[] { "inventory-test", "network-recon" }
                }
            },
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 4,
                RoutingAwareTools = 4,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 1,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 4
                    }
                }
            },
            connectedRuntimeSkills: new[] { "repo-search", "task-runner" });

        Assert.Equal(new[] { "inventory-test", "network-recon", "repo-search", "task-runner" }, policy.CapabilitySnapshot!.Skills);
    }

    [Fact]
    public void BuildSessionPolicy_ProjectsPackAutonomySummaryFromOrchestrationCatalog() {
        var definitions = new[] {
            new ToolDefinition(
                name: "eventlog_timeline_query",
                description: "Query timeline on a local or remote host.",
                parameters: ToolSchema.Object(
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
                            TargetToolName = "system_info",
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
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 2,
                    RecoveryToolNames = new[] { "eventlog_channels_list" }
                }),
            new ToolDefinition(
                name: "eventlog_channels_list",
                description: "List channels.",
                parameters: ToolSchema.Object().NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                })
        };

        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "eventlog",
                    Name = "Event Viewer",
                    SourceKind = "open_source",
                    Enabled = true
                }
            },
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            },
            orchestrationCatalog: ToolOrchestrationCatalog.Build(definitions));

        var pack = Assert.Single(policy.Packs);
        var autonomySummary = Assert.IsType<ToolPackAutonomySummaryDto>(pack.AutonomySummary);

        Assert.Equal(2, autonomySummary.TotalTools);
        Assert.Equal(1, autonomySummary.RemoteCapableTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, autonomySummary.RemoteCapableToolNames);
        Assert.Equal(1, autonomySummary.TargetScopedTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, autonomySummary.TargetScopedToolNames);
        Assert.Equal(1, autonomySummary.RemoteHostTargetingTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, autonomySummary.RemoteHostTargetingToolNames);
        Assert.Equal(1, autonomySummary.SetupAwareTools);
        Assert.Equal(0, autonomySummary.EnvironmentDiscoverTools);
        Assert.Equal(1, autonomySummary.HandoffAwareTools);
        Assert.Equal(1, autonomySummary.RecoveryAwareTools);
        Assert.Equal(1, autonomySummary.AuthenticationRequiredTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, autonomySummary.AuthenticationRequiredToolNames);
        Assert.Equal(1, autonomySummary.ProbeCapableTools);
        Assert.Equal(new[] { "eventlog_timeline_query" }, autonomySummary.ProbeCapableToolNames);
        Assert.Equal(1, autonomySummary.CrossPackHandoffTools);
        Assert.Equal(new[] { "system" }, autonomySummary.CrossPackTargetPacks);

        var hello = new HelloMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_pack_autonomy",
            Name = "IntelligenceX.Chat.Service",
            Version = "1.0.0",
            ProcessId = "1234",
            Policy = policy
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(hello, ChatServiceJsonContext.Default.ChatServiceMessage);
        using var doc = JsonDocument.Parse(json);
        var serializedSummary = doc.RootElement
            .GetProperty("policy")
            .GetProperty("packs")[0]
            .GetProperty("autonomySummary");
        var serializedCapabilityAutonomy = doc.RootElement
            .GetProperty("policy")
            .GetProperty("capabilitySnapshot")
            .GetProperty("autonomy");

        Assert.Equal(2, serializedSummary.GetProperty("totalTools").GetInt32());
        Assert.Equal(1, serializedSummary.GetProperty("remoteCapableTools").GetInt32());
        Assert.Equal(1, serializedSummary.GetProperty("targetScopedTools").GetInt32());
        Assert.Equal(1, serializedSummary.GetProperty("remoteHostTargetingTools").GetInt32());
        Assert.Equal(1, serializedSummary.GetProperty("authenticationRequiredTools").GetInt32());
        Assert.Equal(1, serializedSummary.GetProperty("probeCapableTools").GetInt32());
        Assert.Equal("system", serializedSummary.GetProperty("crossPackTargetPacks")[0].GetString());
        Assert.Equal(1, serializedCapabilityAutonomy.GetProperty("remoteCapableToolCount").GetInt32());
        Assert.Equal(1, serializedCapabilityAutonomy.GetProperty("targetScopedToolCount").GetInt32());
        Assert.Equal(1, serializedCapabilityAutonomy.GetProperty("remoteHostTargetingToolCount").GetInt32());
        Assert.Equal(1, serializedCapabilityAutonomy.GetProperty("authenticationRequiredToolCount").GetInt32());
        Assert.Equal(1, serializedCapabilityAutonomy.GetProperty("probeCapableToolCount").GetInt32());
        Assert.Equal(1, serializedCapabilityAutonomy.GetProperty("crossPackHandoffToolCount").GetInt32());
        Assert.Equal("eventlog", serializedCapabilityAutonomy.GetProperty("remoteCapablePackIds")[0].GetString());
        Assert.Equal("eventlog", serializedCapabilityAutonomy.GetProperty("targetScopedPackIds")[0].GetString());
        Assert.Equal("eventlog", serializedCapabilityAutonomy.GetProperty("remoteHostTargetingPackIds")[0].GetString());
        Assert.Equal("eventlog", serializedCapabilityAutonomy.GetProperty("authenticationRequiredPackIds")[0].GetString());
        Assert.Equal("eventlog", serializedCapabilityAutonomy.GetProperty("probeCapablePackIds")[0].GetString());
    }

    [Fact]
    public void BuildSessionPolicy_UsesOrchestrationCatalogPackMetadataWhenPackAvailabilityMissing() {
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            Array.Empty<ToolDefinition>(),
            new IToolPack[] {
                new SyntheticPolicyPack(
                    id: "ops_inventory",
                    name: "Ops Inventory",
                    sourceKind: "closed_source",
                    engineId: "computerx",
                    category: "system",
                    capabilityTags: new[] { "host_inventory", "remote_analysis" })
            });

        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            Array.Empty<ToolPackAvailabilityInfo>(),
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            },
            orchestrationCatalog: orchestrationCatalog);

        var pack = Assert.Single(policy.Packs);
        Assert.Equal("ops_inventory", pack.Id);
        Assert.Equal("Ops Inventory", pack.Name);
        Assert.Equal(ToolPackSourceKind.ClosedSource, pack.SourceKind);
        Assert.Equal("system", pack.Category);
        Assert.Equal("computerx", pack.EngineId);
        Assert.Equal(new[] { "host_inventory", "remote_analysis" }, pack.CapabilityTags);
        Assert.True(pack.Enabled);

        var hello = new HelloMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_pack_metadata_fallback",
            Name = "IntelligenceX.Chat.Service",
            Version = "1.0.0",
            ProcessId = "1234",
            Policy = policy
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(hello, ChatServiceJsonContext.Default.ChatServiceMessage);
        using var doc = JsonDocument.Parse(json);
        var serializedPack = doc.RootElement
            .GetProperty("policy")
            .GetProperty("packs")[0];

        Assert.Equal("ops_inventory", serializedPack.GetProperty("id").GetString());
        Assert.Equal("Ops Inventory", serializedPack.GetProperty("name").GetString());
        Assert.Equal("ClosedSource", serializedPack.GetProperty("sourceKind").GetString());
        Assert.Equal("system", serializedPack.GetProperty("category").GetString());
        Assert.Equal("computerx", serializedPack.GetProperty("engineId").GetString());
        Assert.Equal(2, serializedPack.GetProperty("capabilityTags").GetArrayLength());
    }

    [Fact]
    public void BuildSessionPolicy_UsesPackSummaryFallbackForPluginsWhenAvailabilityMissing() {
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            Array.Empty<ToolDefinition>(),
            new IToolPack[] {
                new SyntheticPolicyPack(
                    id: "ops_inventory",
                    name: "Ops Inventory",
                    sourceKind: "closed_source",
                    engineId: "computerx",
                    category: "system",
                    capabilityTags: new[] { "host_inventory", "remote_analysis" })
            });

        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            Array.Empty<ToolPackAvailabilityInfo>(),
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            },
            orchestrationCatalog: orchestrationCatalog);

        var plugin = Assert.Single(policy.Plugins);
        Assert.Equal("ops_inventory", plugin.Id);
        Assert.Equal("Ops Inventory", plugin.Name);
        Assert.Equal("closed_source", plugin.Origin);
        Assert.Equal(ToolPackSourceKind.ClosedSource, plugin.SourceKind);
        Assert.True(plugin.Enabled);
        Assert.Equal(new[] { "ops_inventory" }, plugin.PackIds);

        var hello = new HelloMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_plugin_fallback",
            Name = "IntelligenceX.Chat.Service",
            Version = "1.0.0",
            ProcessId = "1234",
            Policy = policy
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(hello, ChatServiceJsonContext.Default.ChatServiceMessage);
        using var doc = JsonDocument.Parse(json);
        var serializedPlugin = doc.RootElement
            .GetProperty("policy")
            .GetProperty("plugins")[0];

        Assert.Equal("ops_inventory", serializedPlugin.GetProperty("id").GetString());
        Assert.Equal("Ops Inventory", serializedPlugin.GetProperty("name").GetString());
        Assert.Equal("closed_source", serializedPlugin.GetProperty("origin").GetString());
        Assert.Equal("ClosedSource", serializedPlugin.GetProperty("sourceKind").GetString());
        Assert.Equal("ops_inventory", serializedPlugin.GetProperty("packIds")[0].GetString());
    }

    [Fact]
    public void BuildSessionPolicy_BackfillsSparsePluginMetadataFromPackSummaries() {
        var orchestrationCatalog = ToolOrchestrationCatalog.Build(
            Array.Empty<ToolDefinition>(),
            new IToolPack[] {
                new SyntheticPolicyPack(
                    id: "ops_inventory",
                    name: "Ops Inventory",
                    sourceKind: "closed_source",
                    engineId: "computerx",
                    category: "system",
                    capabilityTags: new[] { "host_inventory", "remote_analysis" },
                    tier: ToolCapabilityTier.DangerousWrite)
            });

        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            Array.Empty<ToolPackAvailabilityInfo>(),
            new[] {
                new ToolPluginAvailabilityInfo {
                    Id = "ops_inventory",
                    Name = "",
                    Origin = "",
                    SourceKind = "",
                    DefaultEnabled = true,
                    Enabled = true,
                    IsDangerous = false,
                    PackIds = Array.Empty<string>(),
                    SkillIds = new[] { " REPORTING.CUSTOM ", "reporting.custom" }
                }
            },
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireExplicitRoutingMetadata = false,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            },
            orchestrationCatalog: orchestrationCatalog);

        var plugin = Assert.Single(policy.Plugins);
        Assert.Equal("Ops Inventory", plugin.Name);
        Assert.Equal("unknown", plugin.Origin);
        Assert.Equal(ToolPackSourceKind.ClosedSource, plugin.SourceKind);
        Assert.True(plugin.IsDangerous);
        Assert.Equal(new[] { "ops_inventory" }, plugin.PackIds);
        Assert.Equal(new[] { "reporting.custom" }, plugin.SkillIds);

        var hello = new HelloMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_plugin_backfill",
            Name = "IntelligenceX.Chat.Service",
            Version = "1.0.0",
            ProcessId = "1234",
            Policy = policy
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(hello, ChatServiceJsonContext.Default.ChatServiceMessage);
        using var doc = JsonDocument.Parse(json);
        var serializedPlugin = doc.RootElement
            .GetProperty("policy")
            .GetProperty("plugins")[0];

        Assert.Equal("Ops Inventory", serializedPlugin.GetProperty("name").GetString());
        Assert.Equal("unknown", serializedPlugin.GetProperty("origin").GetString());
        Assert.Equal("ClosedSource", serializedPlugin.GetProperty("sourceKind").GetString());
        Assert.True(serializedPlugin.GetProperty("isDangerous").GetBoolean());
        Assert.Equal("ops_inventory", serializedPlugin.GetProperty("packIds")[0].GetString());
        Assert.Equal("reporting.custom", serializedPlugin.GetProperty("skillIds")[0].GetString());
    }

    [Fact]
    public void BuildSessionPolicy_TreatsWriteCapableNonDangerousPackAsNonReadOnly() {
        var definitions = new[] {
            new ToolDefinition(
                name: "email_smtp_send",
                description: "Send SMTP mail.",
                parameters: ToolSchema.Object(
                        ("send", ToolSchema.Boolean("Apply send.")))
                    .WithWriteGovernanceDefaults(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "email",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue("send"))
        };

        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions(),
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "email",
                    Name = "Email",
                    SourceKind = "builtin",
                    Enabled = true,
                    IsDangerous = false,
                    Tier = ToolCapabilityTier.SensitiveRead
                }
            },
            Array.Empty<ToolPluginAvailabilityInfo>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = true,
                WriteGovernanceRuntimeConfigured = true,
                RequireWriteAuditSinkForWriteOperations = true,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.FileAppendOnly,
                WriteAuditSinkConfigured = true,
                WriteAuditSinkPath = "C:\\audit\\write.jsonl",
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
                RequireExplicitRoutingMetadata = true,
                RequireAuthenticationRuntime = true,
                AuthenticationRuntimeConfigured = true,
                RequireSuccessfulSmtpProbeForSend = true,
                SmtpProbeMaxAgeSeconds = 600
            },
            orchestrationCatalog: ToolOrchestrationCatalog.Build(definitions));

        Assert.False(policy.ReadOnly);
        Assert.True(policy.DangerousToolsEnabled);

        var pack = Assert.Single(policy.Packs);
        Assert.True(pack.IsDangerous);

        var snapshot = Assert.IsType<SessionCapabilitySnapshotDto>(policy.CapabilitySnapshot);
        Assert.True(snapshot.DangerousToolsEnabled);
        Assert.Equal(new[] { "email" }, snapshot.DangerousPackIds);
        Assert.NotNull(snapshot.Autonomy);
        Assert.Equal(new[] { "email" }, snapshot.Autonomy!.WriteCapablePackIds);
        Assert.Equal(new[] { "email" }, snapshot.Autonomy.GovernedWritePackIds);
    }

    private sealed class SyntheticPolicyPack : IToolPack {
        public SyntheticPolicyPack(
            string id,
            string name,
            string sourceKind,
            string engineId,
            string category,
            IReadOnlyList<string> capabilityTags,
            ToolCapabilityTier tier = ToolCapabilityTier.ReadOnly) {
            Descriptor = new ToolPackDescriptor {
                Id = id,
                Name = name,
                Tier = tier,
                SourceKind = sourceKind,
                EngineId = engineId,
                Category = category,
                CapabilityTags = capabilityTags
            };
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            _ = registry;
        }
    }
}
