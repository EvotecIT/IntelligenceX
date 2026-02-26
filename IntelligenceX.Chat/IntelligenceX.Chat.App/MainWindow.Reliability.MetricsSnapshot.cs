using System;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private static string MapOutcomeToMetricsToken(AssistantTurnOutcome outcome) {
        return outcome.Kind switch {
            AssistantTurnOutcomeKind.Canceled => "canceled",
            AssistantTurnOutcomeKind.Disconnected => "disconnected",
            AssistantTurnOutcomeKind.UsageLimit => "usage_limit",
            AssistantTurnOutcomeKind.ToolRoundLimit => "tool_round_limit",
            AssistantTurnOutcomeKind.Error => "error",
            _ => "error"
        };
    }

    private static string? MapErrorCodeToMetricsToken(AssistantTurnOutcome outcome) {
        return outcome.Kind switch {
            AssistantTurnOutcomeKind.Canceled => "canceled",
            AssistantTurnOutcomeKind.Disconnected => "disconnected",
            AssistantTurnOutcomeKind.UsageLimit => "usage_limit",
            AssistantTurnOutcomeKind.ToolRoundLimit => "tool_round_limit",
            _ => null
        };
    }

    private static TurnMetricsSnapshot BuildTurnMetricsSnapshotFromCompletion(
        TurnLatencyCompletion completion,
        string outcome,
        string? errorCode,
        long? ttftMs,
        long? promptTokens,
        long? completionTokens,
        long? totalTokens,
        long? cachedPromptTokens,
        long? reasoningTokens,
        string? model,
        string? requestedModel,
        string? transport,
        string? endpointHost,
        int toolCallsCount = 0,
        int toolRounds = 0,
        int projectionFallbackCount = 0) {
        return new TurnMetricsSnapshot(
            RequestId: completion.RequestId,
            CompletedUtc: completion.CompletedUtc,
            DurationMs: Math.Max(0L, completion.DurationMs),
            TtftMs: ttftMs,
            QueueWaitMs: completion.QueueWaitMs,
            AuthProbeMs: completion.AuthProbeMs,
            ConnectMs: completion.ConnectMs,
            DispatchToFirstStatusMs: completion.DispatchToFirstStatusMs,
            DispatchToModelSelectedMs: completion.DispatchToModelSelectedMs,
            DispatchToFirstToolRunningMs: completion.DispatchToFirstToolRunningMs,
            DispatchToFirstDeltaMs: completion.DispatchToFirstDeltaMs,
            DispatchToLastDeltaMs: completion.DispatchToLastDeltaMs,
            StreamDurationMs: completion.StreamDurationMs,
            ToolCallsCount: Math.Max(0, toolCallsCount),
            ToolRounds: Math.Max(0, toolRounds),
            ProjectionFallbackCount: Math.Max(0, projectionFallbackCount),
            Outcome: string.IsNullOrWhiteSpace(outcome) ? "unknown" : outcome.Trim(),
            ErrorCode: string.IsNullOrWhiteSpace(errorCode) ? null : errorCode.Trim(),
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens,
            CachedPromptTokens: cachedPromptTokens,
            ReasoningTokens: reasoningTokens,
            AutonomyCounters: Array.Empty<TurnCounterMetricDto>(),
            Model: string.IsNullOrWhiteSpace(model) ? null : model.Trim(),
            RequestedModel: string.IsNullOrWhiteSpace(requestedModel) ? null : requestedModel.Trim(),
            Transport: string.IsNullOrWhiteSpace(transport) ? null : transport.Trim(),
            EndpointHost: string.IsNullOrWhiteSpace(endpointHost) ? null : endpointHost.Trim());
    }
}
