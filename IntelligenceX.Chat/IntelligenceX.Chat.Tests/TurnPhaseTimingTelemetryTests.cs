using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class TurnPhaseTimingTelemetryTests {
    [Fact]
    public void BuildTurnPhaseTimings_ReturnsNullWhenTimelineMissing() {
        Assert.Null(ChatServiceSession.BuildTurnPhaseTimings(null));
        Assert.Null(ChatServiceSession.BuildTurnPhaseTimings(Array.Empty<TurnTimelineEventDto>()));
    }

    [Fact]
    public void BuildTurnPhaseTimings_AggregatesCanonicalDurationsAndHeartbeatAttribution() {
        var baselineUtc = new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc);
        var timeline = new List<TurnTimelineEventDto> {
            BuildEvent(ChatStatusCodes.Accepted, baselineUtc.AddMilliseconds(0)),
            BuildEvent(ChatStatusCodes.TurnQueued, baselineUtc.AddMilliseconds(100)),
            BuildEvent(ChatStatusCodes.ExecutionLaneWaiting, baselineUtc.AddMilliseconds(350)),
            BuildEvent(ChatStatusCodes.ContextReady, baselineUtc.AddMilliseconds(700)),
            BuildEvent(ChatStatusCodes.PhasePlan, baselineUtc.AddMilliseconds(900)),
            BuildEvent(ChatStatusCodes.PhaseHeartbeat, baselineUtc.AddMilliseconds(1200)),
            BuildEvent(ChatStatusCodes.PhaseExecute, baselineUtc.AddMilliseconds(1500)),
            BuildEvent(ChatStatusCodes.ToolRunning, baselineUtc.AddMilliseconds(2100)),
            BuildEvent(ChatStatusCodes.PhaseReview, baselineUtc.AddMilliseconds(2800)),
            BuildEvent(ChatStatusCodes.Timeout, baselineUtc.AddMilliseconds(3400))
        };

        var phaseTimings = ChatServiceSession.BuildTurnPhaseTimings(timeline);

        Assert.NotNull(phaseTimings);
        var byPhase = new Dictionary<string, TurnPhaseTimingDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var timing in phaseTimings!) {
            byPhase[timing.Phase] = timing;
        }

        AssertPhase(byPhase, "accepted", expectedDurationMs: 100, expectedEventCount: 1);
        AssertPhase(byPhase, "queued", expectedDurationMs: 250, expectedEventCount: 1);
        AssertPhase(byPhase, "lane_wait", expectedDurationMs: 350, expectedEventCount: 1);
        AssertPhase(byPhase, "context_ready", expectedDurationMs: 200, expectedEventCount: 1);
        AssertPhase(byPhase, "model_plan", expectedDurationMs: 600, expectedEventCount: 2);
        AssertPhase(byPhase, "tool_execute", expectedDurationMs: 1300, expectedEventCount: 2);
        AssertPhase(byPhase, "review", expectedDurationMs: 600, expectedEventCount: 1);
        AssertPhase(byPhase, "timeout", expectedDurationMs: 0, expectedEventCount: 1);
        Assert.DoesNotContain(byPhase.Keys, phase => string.Equals(phase, "done", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(byPhase.Keys, phase => string.Equals(phase, "error", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertPhase(
        IReadOnlyDictionary<string, TurnPhaseTimingDto> byPhase,
        string phase,
        long expectedDurationMs,
        int expectedEventCount) {
        var timing = Assert.IsType<TurnPhaseTimingDto>(Assert.Contains(phase, byPhase));
        Assert.Equal(expectedDurationMs, timing.DurationMs);
        Assert.Equal(expectedEventCount, timing.EventCount);
    }

    private static TurnTimelineEventDto BuildEvent(string status, DateTime atUtc) {
        return new TurnTimelineEventDto {
            Status = status,
            AtUtc = atUtc
        };
    }
}
