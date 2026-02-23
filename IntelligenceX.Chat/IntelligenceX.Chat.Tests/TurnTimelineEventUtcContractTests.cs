using System;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class TurnTimelineEventUtcContractTests {
    [Fact]
    public void TurnTimelineEventDto_NormalizesLocalKindToUtc() {
        var localInput = new DateTime(2026, 2, 23, 11, 12, 13, DateTimeKind.Local);

        var localEvent = new TurnTimelineEventDto {
            Status = "phase_plan",
            AtUtc = localInput
        };

        Assert.Equal(DateTimeKind.Utc, localEvent.AtUtc.Kind);
        Assert.Equal(localInput.ToUniversalTime(), localEvent.AtUtc);
    }

    [Fact]
    public void TurnTimelineEventDto_RejectsUnspecifiedDateTimeKind() {
        var unspecifiedInput = new DateTime(2026, 2, 23, 11, 12, 13, DateTimeKind.Unspecified);

        var ex = Assert.Throws<ArgumentException>(() => new TurnTimelineEventDto {
            Status = "phase_plan",
            AtUtc = unspecifiedInput
        });

        Assert.Contains("explicit UTC or local DateTimeKind", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatResultMessage_RejectsTimelineAtUtcWhenJsonTimestampHasNoOffset() {
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

        Assert.Throws<ArgumentException>(() =>
            JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage));
    }

    [Fact]
    public void ChatResultMessage_DeserializesTimelineAtUtcAsUtcWhenJsonTimestampUsesUtcOffset() {
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
                  "atUtc":"2026-02-23T11:12:13Z"
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
