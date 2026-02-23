using System;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class TurnTimelineEventUtcContractTests {
    [Fact]
    public void TurnTimelineEventDto_NormalizesLocalAndUnspecifiedKindsToUtc() {
        var localInput = new DateTime(2026, 2, 23, 11, 12, 13, DateTimeKind.Local);
        var unspecifiedInput = new DateTime(2026, 2, 23, 11, 12, 13, DateTimeKind.Unspecified);

        var localEvent = new TurnTimelineEventDto {
            Status = "phase_plan",
            AtUtc = localInput
        };
        var unspecifiedEvent = new TurnTimelineEventDto {
            Status = "phase_plan",
            AtUtc = unspecifiedInput
        };

        Assert.Equal(DateTimeKind.Utc, localEvent.AtUtc.Kind);
        Assert.Equal(localInput.ToUniversalTime(), localEvent.AtUtc);

        Assert.Equal(DateTimeKind.Utc, unspecifiedEvent.AtUtc.Kind);
        Assert.Equal(DateTime.SpecifyKind(unspecifiedInput, DateTimeKind.Utc), unspecifiedEvent.AtUtc);
    }

    [Fact]
    public void ChatResultMessage_DeserializesTimelineAtUtcAsUtcWhenJsonTimestampHasNoOffset() {
        const string json = """
            {
              "type":"chat_result",
              "kind":"response",
              "requestId":"req_utc_contract",
              "threadId":"thread_utc_contract",
              "text":"done",
              "turnTimelineEvents":[
                {
                  "status":"phase_plan",
                  "atUtc":"2026-02-23T11:12:13"
                }
              ]
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var result = Assert.IsType<ChatResultMessage>(parsed);
        var timelineEvent = Assert.Single(result.TurnTimelineEvents!);

        Assert.Equal(DateTimeKind.Utc, timelineEvent.AtUtc.Kind);
        Assert.Equal(new DateTime(2026, 2, 23, 11, 12, 13, DateTimeKind.Utc), timelineEvent.AtUtc);
    }
}

