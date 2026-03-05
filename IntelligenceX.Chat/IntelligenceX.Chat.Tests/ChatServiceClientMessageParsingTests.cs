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
    /// Ensures fallback recovery remains resilient when timeline property casing differs.
    /// </summary>
    [Fact]
    public void TryDeserializeMessageLine_RecoversChatResultWhenTimelinePropertyUsesAlternateCasing() {
        const string line = """
            {
              "type":"chat_result",
              "kind":"response",
              "requestId":"req_parse_compat_case",
              "threadId":"thread_parse_compat_case",
              "text":"Recovered response text",
              "TurnTimelineEvents":[
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

    /// <summary>
    /// Ensures fallback still runs when primary deserializer throws a non-JSON exception type.
    /// </summary>
    [Fact]
    public void TryDeserializeMessageLine_RecoversWhenPrimaryDeserializerThrowsInvalidOperationException() {
        const string line = """
            {
              "type":"chat_result",
              "kind":"response",
              "requestId":"req_parse_invalid_operation",
              "threadId":"thread_parse_invalid_operation",
              "text":"Recovered via fallback",
              "turnTimelineEvents":[
                {
                  "status":"phase_plan",
                  "atUtc":"2026-02-23T11:12:13"
                }
              ]
            }
            """;

        var parsed = ChatServiceClient.TryDeserializeMessageLine(
            line,
            _ => throw new InvalidOperationException("Injected parser failure for fallback test."));

        var result = Assert.IsType<ChatResultMessage>(parsed);
        Assert.Equal("Recovered via fallback", result.Text);
        Assert.Null(result.TurnTimelineEvents);
    }

    /// <summary>
    /// Ensures per-turn phase timing telemetry fields in chat_metrics events deserialize end-to-end.
    /// </summary>
    [Fact]
    public void TryDeserializeMessageLine_ParsesChatMetricsWithPhaseTimings() {
        const string line = """
            {
              "type":"chat_metrics",
              "kind":"event",
              "requestId":"req_metrics_phase_timings",
              "threadId":"thread_metrics_phase_timings",
              "startedAtUtc":"2026-03-02T11:12:13Z",
              "completedAtUtc":"2026-03-02T11:12:20Z",
              "durationMs":7000,
              "ensureThreadMs":120,
              "weightedSubsetSelectionMs":930,
              "resolveModelMs":410,
              "toolCallsCount":2,
              "toolRounds":1,
              "projectionFallbackCount":0,
              "autonomyTelemetry":{
                "autonomyDepth":1,
                "recoveryEvents":2,
                "completionRate":1.0
              },
              "outcome":"ok"
            }
            """;

        var parsed = ChatServiceClient.TryDeserializeMessageLine(line);

        var metrics = Assert.IsType<ChatMetricsMessage>(parsed);
        Assert.Equal(120, metrics.EnsureThreadMs);
        Assert.Equal(930, metrics.WeightedSubsetSelectionMs);
        Assert.Equal(410, metrics.ResolveModelMs);
        var autonomy = Assert.IsType<AutonomyTelemetryDto>(metrics.AutonomyTelemetry);
        Assert.Equal(1, autonomy.AutonomyDepth);
        Assert.Equal(2, autonomy.RecoveryEvents);
        Assert.Equal(1.0d, autonomy.CompletionRate);
    }
}
