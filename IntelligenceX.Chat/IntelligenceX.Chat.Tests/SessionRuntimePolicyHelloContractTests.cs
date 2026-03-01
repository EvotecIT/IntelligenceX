using System.Text.Json;
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
            MissingRoutingContractTools = 0,
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
            Array.Empty<string>(),
            Array.Empty<string>(),
            runtimePolicy,
            routingCatalog);

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
        Assert.True(runtime.GetProperty("requireAuthenticationRuntime").GetBoolean());
        Assert.True(runtime.GetProperty("authenticationRuntimeConfigured").GetBoolean());
        Assert.True(runtime.GetProperty("requireSuccessfulSmtpProbeForSend").GetBoolean());
        Assert.Equal(600, runtime.GetProperty("smtpProbeMaxAgeSeconds").GetInt32());
        Assert.Equal("C:\\profiles\\run-as.json", runtime.GetProperty("runAsProfilePath").GetString());
        Assert.Equal("C:\\profiles\\auth.json", runtime.GetProperty("authenticationProfilePath").GetString());

        var serializedRoutingCatalog = serializedPolicy.GetProperty("routingCatalog");
        Assert.True(serializedRoutingCatalog.GetProperty("isHealthy").GetBoolean());
        Assert.Equal(12, serializedRoutingCatalog.GetProperty("totalTools").GetInt32());
        Assert.Equal(2, serializedRoutingCatalog.GetProperty("familyActions").GetArrayLength());
        Assert.Equal(
            "act_domain_scope_ad",
            serializedRoutingCatalog
                .GetProperty("familyActions")[0]
                .GetProperty("actionId")
                .GetString());
    }

    [Fact]
    public void BuildSessionPolicy_NormalizesMaxToolRoundsToAtLeastOne() {
        var policy = ChatServiceSession.BuildSessionPolicy(
            new ServiceOptions {
                MaxToolRounds = ChatRequestOptionLimits.MinToolRounds - 1
            },
            Array.Empty<ToolPackAvailabilityInfo>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
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
            Array.Empty<string>(),
            Array.Empty<string>(),
            new ToolRuntimePolicyDiagnostics {
                WriteGovernanceMode = ToolWriteGovernanceMode.Enforced,
                RequireWriteGovernanceRuntime = false,
                WriteGovernanceRuntimeConfigured = false,
                RequireWriteAuditSinkForWriteOperations = false,
                WriteAuditSinkMode = ToolWriteAuditSinkMode.None,
                WriteAuditSinkConfigured = false,
                AuthenticationPreset = ToolAuthenticationRuntimePreset.Default,
                RequireAuthenticationRuntime = false,
                AuthenticationRuntimeConfigured = false,
                RequireSuccessfulSmtpProbeForSend = false,
                SmtpProbeMaxAgeSeconds = 0
            });

        Assert.Equal(ChatRequestOptionLimits.MaxToolRounds, policy.MaxToolRounds);
    }
}
