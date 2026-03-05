using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
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
        Assert.Equal(2, serializedRoutingCatalog.GetProperty("familyActions").GetArrayLength());
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
            });

        Assert.Equal(new[] { "inventory-test", "network-recon" }, policy.CapabilitySnapshot!.Skills);
        Assert.Equal(new[] { "inventory-test", "network-recon" }, policy.Plugins[0].SkillIds);
    }
}
