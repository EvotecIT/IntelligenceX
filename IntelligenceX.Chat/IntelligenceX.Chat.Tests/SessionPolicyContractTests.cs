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
                        SourceKind = ToolPackSourceKind.ClosedSource
                    }
                },
                StartupWarnings = new[] {
                    "[plugin] path_not_found path='C:\\plugins\\missing'",
                    "[plugin] init_failed plugin='ix.mail' error='dependency missing'"
                },
                PluginSearchPaths = new[] {
                    "C:\\Users\\user\\AppData\\Local\\IntelligenceX.Chat\\plugins",
                    "C:\\Support\\GitHub\\IntelligenceX\\plugins"
                },
                RoutingCatalog = new SessionRoutingCatalogDiagnosticsDto {
                    TotalTools = 8,
                    RoutingAwareTools = 8,
                    MissingRoutingContractTools = 0,
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
        Assert.False(policy.AllowMutatingParallelToolCalls);
        Assert.Single(policy.Packs);
        Assert.False(policy.Packs[0].Enabled);
        Assert.Equal("License expired on 2026-03-31.", policy.Packs[0].DisabledReason);
        var routingCatalog = Assert.IsType<SessionRoutingCatalogDiagnosticsDto>(policy.RoutingCatalog);
        Assert.True(routingCatalog.IsHealthy);
        Assert.Equal(8, routingCatalog.TotalTools);
        Assert.Equal(2, routingCatalog.FamilyActions.Length);
        Assert.Equal("ad_domain", routingCatalog.FamilyActions[0].Family);
    }
}
