using System;
using System.Linq;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards wire-contract tokens and shared bounds used across host/service/app.
/// </summary>
public sealed class ChatContractsProtocolStabilityTests {
    [Fact]
    public void ChatStatusCodes_ExposeStableWireTokens() {
        Assert.Equal("thinking", ChatStatusCodes.Thinking);
        Assert.Equal("model_selected", ChatStatusCodes.ModelSelected);
        Assert.Equal("routing", ChatStatusCodes.Routing);
        Assert.Equal("routing_meta", ChatStatusCodes.RoutingMeta);
        Assert.Equal("routing_tool", ChatStatusCodes.RoutingTool);
        Assert.Equal("tool_call", ChatStatusCodes.ToolCall);
        Assert.Equal("tool_running", ChatStatusCodes.ToolRunning);
        Assert.Equal("tool_heartbeat", ChatStatusCodes.ToolHeartbeat);
        Assert.Equal("tool_completed", ChatStatusCodes.ToolCompleted);
        Assert.Equal("tool_canceled", ChatStatusCodes.ToolCanceled);
        Assert.Equal("tool_recovered", ChatStatusCodes.ToolRecovered);
        Assert.Equal("tool_parallel_mode", ChatStatusCodes.ToolParallelMode);
        Assert.Equal("tool_parallel_forced", ChatStatusCodes.ToolParallelForced);
        Assert.Equal("tool_parallel_safety_off", ChatStatusCodes.ToolParallelSafetyOff);
        Assert.Equal("tool_batch_started", ChatStatusCodes.ToolBatchStarted);
        Assert.Equal("tool_batch_progress", ChatStatusCodes.ToolBatchProgress);
        Assert.Equal("tool_batch_heartbeat", ChatStatusCodes.ToolBatchHeartbeat);
        Assert.Equal("tool_batch_recovering", ChatStatusCodes.ToolBatchRecovering);
        Assert.Equal("tool_batch_recovered", ChatStatusCodes.ToolBatchRecovered);
        Assert.Equal("tool_batch_completed", ChatStatusCodes.ToolBatchCompleted);
        Assert.Equal("tool_round_started", ChatStatusCodes.ToolRoundStarted);
        Assert.Equal("tool_round_completed", ChatStatusCodes.ToolRoundCompleted);
        Assert.Equal("tool_replay_compacted", ChatStatusCodes.ToolReplayCompacted);
        Assert.Equal("tool_round_limit_reached", ChatStatusCodes.ToolRoundLimitReached);
        Assert.Equal("tool_round_cap_applied", ChatStatusCodes.ToolRoundCapApplied);
        Assert.Equal("review_passes_clamped", ChatStatusCodes.ReviewPassesClamped);
        Assert.Equal("model_heartbeat_clamped", ChatStatusCodes.ModelHeartbeatClamped);
        Assert.Equal("phase_plan", ChatStatusCodes.PhasePlan);
        Assert.Equal("phase_execute", ChatStatusCodes.PhaseExecute);
        Assert.Equal("phase_review", ChatStatusCodes.PhaseReview);
        Assert.Equal("phase_heartbeat", ChatStatusCodes.PhaseHeartbeat);
        Assert.Equal("no_result_watchdog_triggered", ChatStatusCodes.NoResultWatchdogTriggered);
    }

    [Fact]
    public void ChatStatusCodes_AreUniqueAndExplicitlyEnumerated() {
        var values = typeof(ChatStatusCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(32, values.Length);
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ChatRequestOptionLimits_ExposeStableSafetyBounds() {
        Assert.Equal(1, ChatRequestOptionLimits.MinToolRounds);
        Assert.Equal(256, ChatRequestOptionLimits.MaxToolRounds);
        Assert.Equal(24, ChatRequestOptionLimits.DefaultToolRounds);
        Assert.Equal(0, ChatRequestOptionLimits.MinCandidateTools);
        Assert.Equal(256, ChatRequestOptionLimits.MaxCandidateTools);
        Assert.Equal(0, ChatRequestOptionLimits.MinTimeoutSeconds);
        Assert.Equal(3600, ChatRequestOptionLimits.MaxTimeoutSeconds);
        Assert.Equal(1, ChatRequestOptionLimits.MinPositiveTimeoutSeconds);
        Assert.Equal(1, ChatRequestOptionLimits.DefaultReviewPasses);
        Assert.Equal(3, ChatRequestOptionLimits.MaxReviewPasses);
        Assert.Equal(8, ChatRequestOptionLimits.DefaultModelHeartbeatSeconds);
        Assert.Equal(60, ChatRequestOptionLimits.MaxModelHeartbeatSeconds);
    }
}
