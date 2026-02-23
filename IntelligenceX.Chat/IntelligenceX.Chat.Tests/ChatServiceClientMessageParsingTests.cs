using System;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Client;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards client-side message parsing compatibility fallback behavior.
/// </summary>
public sealed class ChatServiceClientMessageParsingTests {
    /// <summary>
    /// Ensures chat_result payloads keep response text even when timeline timestamps are offsetless.
    /// </summary>
    [Fact]
    public void TryDeserializeMessageLine_RecoversChatResultWhenTimelineTimestampIsOffsetless() {
        const string line = """
            {
              "type":"chat_result",
              "kind":"response",
              "requestId":"req_parse_compat",
              "threadId":"thread_parse_compat",
              "text":"Recovered response text",
              "turnTimelineEvents":[
                {
                  "status":"phase_plan",
                  "atUtc":"2026-02-23T11:12:13"
                }
              ]
            }
            """;

        var parsed = ChatServiceClient.TryDeserializeMessageLine(line);

        var result = Assert.IsType<ChatResultMessage>(parsed);
        Assert.Equal("Recovered response text", result.Text);
        Assert.Null(result.TurnTimelineEvents);
    }

    /// <summary>
    /// Ensures valid timeline payloads continue parsing normally.
    /// </summary>
    [Fact]
    public void TryDeserializeMessageLine_PreservesTimelineWhenTimestampIsUtc() {
        const string line = """
            {
              "type":"chat_result",
              "kind":"response",
              "requestId":"req_parse_utc",
              "threadId":"thread_parse_utc",
              "text":"Response text",
              "turnTimelineEvents":[
                {
                  "status":"phase_plan",
                  "atUtc":"2026-02-23T11:12:13Z"
                }
              ]
            }
            """;

        var parsed = ChatServiceClient.TryDeserializeMessageLine(line);

        var result = Assert.IsType<ChatResultMessage>(parsed);
        var timeline = Assert.Single(result.TurnTimelineEvents!);
        Assert.Equal(DateTimeKind.Utc, timeline.AtUtc.Kind);
    }

    /// <summary>
    /// Ensures non-chat_result invalid payloads are rejected.
    /// </summary>
    [Fact]
    public void TryDeserializeMessageLine_ReturnsNullForInvalidNonChatResultPayload() {
        const string line = """
            {
              "type":"chat_status",
              "kind":"event",
              "requestId":"req_parse_status",
              "threadId":"thread_parse_status",
              "status":"phase_plan",
              "durationMs":"not-a-number"
            }
            """;

        var parsed = ChatServiceClient.TryDeserializeMessageLine(line);

        Assert.Null(parsed);
    }
}
