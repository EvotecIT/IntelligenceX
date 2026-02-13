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
                StartupWarnings = new[] {
                    "[plugin] path_not_found path='C:\\plugins\\missing'",
                    "[plugin] init_failed plugin='ix.mail' error='dependency missing'"
                },
                PluginSearchPaths = new[] {
                    "C:\\Users\\user\\AppData\\Local\\IntelligenceX.Chat\\plugins",
                    "C:\\Support\\GitHub\\IntelligenceX\\plugins"
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
    }
}
