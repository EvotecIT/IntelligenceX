using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards assistant turn lifecycle transitions so late streaming events cannot mutate finalized turns.
/// </summary>
public sealed class MainWindowAssistantTurnLifecycleTests {
    /// <summary>
    /// Ensures draft mutations are blocked once a turn is finalized or failed.
    /// </summary>
    [Fact]
    public void CanApplyAssistantTurnDraftUpdate_DeniesTerminalStages() {
        Assert.False(MainWindow.CanApplyAssistantTurnDraftUpdate(MainWindow.AssistantTurnLifecycleStage.Finalized));
        Assert.False(MainWindow.CanApplyAssistantTurnDraftUpdate(MainWindow.AssistantTurnLifecycleStage.Failed));
    }

    /// <summary>
    /// Ensures tool-related statuses promote lifecycle state from draft to tool phase.
    /// </summary>
    [Fact]
    public void PromoteAssistantTurnLifecycleForStatus_PromotesToolStageForToolStatuses() {
        var stage = MainWindow.PromoteAssistantTurnLifecycleForStatus(
            MainWindow.AssistantTurnLifecycleStage.Draft,
            ChatStatusCodes.ToolRunning);

        Assert.Equal(MainWindow.AssistantTurnLifecycleStage.Tool, stage);
    }

    /// <summary>
    /// Ensures terminal lifecycle stages remain immutable under late status updates.
    /// </summary>
    [Fact]
    public void PromoteAssistantTurnLifecycleForStatus_LeavesTerminalStagesUntouched() {
        var stage = MainWindow.PromoteAssistantTurnLifecycleForStatus(
            MainWindow.AssistantTurnLifecycleStage.Finalized,
            ChatStatusCodes.ToolRunning);

        Assert.Equal(MainWindow.AssistantTurnLifecycleStage.Finalized, stage);
    }

    /// <summary>
    /// Ensures terminal lifecycle transitions are idempotent and never regress from finalized to failed.
    /// </summary>
    [Fact]
    public void ResolveAssistantTurnLifecycleTerminalTransition_IsIdempotentAfterFinalization() {
        var first = MainWindow.ResolveAssistantTurnLifecycleTerminalTransition(
            MainWindow.AssistantTurnLifecycleStage.Tool,
            succeeded: true);
        var second = MainWindow.ResolveAssistantTurnLifecycleTerminalTransition(
            first,
            succeeded: false);

        Assert.Equal(MainWindow.AssistantTurnLifecycleStage.Finalized, first);
        Assert.Equal(MainWindow.AssistantTurnLifecycleStage.Finalized, second);
    }

    /// <summary>
    /// Ensures tool statuses promote assistant bubbles into the tool-activity channel.
    /// </summary>
    [Fact]
    public void ResolveAssistantBubbleChannelForStatus_PromotesToolActivityChannel() {
        var channel = MainWindow.ResolveAssistantBubbleChannelForStatus(
            AssistantBubbleChannelKind.DraftThinking,
            ChatStatusCodes.ToolBatchProgress);

        Assert.Equal(AssistantBubbleChannelKind.ToolActivity, channel);
    }
}
