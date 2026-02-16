using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolHealthContractTests {
    [Fact]
    public void CheckToolHealthRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"check_tool_health",
              "requestId":"req_1",
              "toolTimeoutSeconds":4,
              "sourceKinds":["closedSource","openSource"],
              "packIds":["ad","testimox"]
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<CheckToolHealthRequest>(parsed);

        Assert.Equal("req_1", request.RequestId);
        Assert.Equal(4, request.ToolTimeoutSeconds);
        Assert.NotNull(request.SourceKinds);
        Assert.Equal(2, request.SourceKinds!.Length);
        Assert.Contains(ToolPackSourceKind.ClosedSource, request.SourceKinds);
        Assert.Contains(ToolPackSourceKind.OpenSource, request.SourceKinds);
        Assert.NotNull(request.PackIds);
        Assert.Equal("ad", request.PackIds![0]);
    }

    [Fact]
    public void ToolHealthMessage_RoundTripsProbeMetadata() {
        var message = new ToolHealthMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_2",
            OkCount = 2,
            FailedCount = 1,
            Probes = new[] {
                new ToolHealthProbeDto {
                    ToolName = "eventlog_pack_info",
                    PackId = "eventlog",
                    PackName = "Event Log",
                    SourceKind = ToolPackSourceKind.Builtin,
                    Ok = true,
                    DurationMs = 12
                },
                new ToolHealthProbeDto {
                    ToolName = "ad_pack_info",
                    PackId = "ad",
                    PackName = "ADPlayground",
                    SourceKind = ToolPackSourceKind.ClosedSource,
                    Ok = false,
                    ErrorCode = "provider_unavailable",
                    Error = "Domain controller not reachable.",
                    DurationMs = 53
                }
            }
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(message, ChatServiceJsonContext.Default.ChatServiceMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<ToolHealthMessage>(parsed);

        Assert.Equal(2, typed.OkCount);
        Assert.Equal(1, typed.FailedCount);
        Assert.Equal(2, typed.Probes.Length);
        Assert.Equal("ad_pack_info", typed.Probes[1].ToolName);
        Assert.Equal(ToolPackSourceKind.ClosedSource, typed.Probes[1].SourceKind);
        Assert.Equal("provider_unavailable", typed.Probes[1].ErrorCode);
    }
}
