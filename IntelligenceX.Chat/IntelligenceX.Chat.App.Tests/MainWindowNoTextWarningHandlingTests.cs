using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards no-text warning handling so streamed assistant content is not overwritten.
/// </summary>
public sealed class MainWindowNoTextWarningHandlingTests {
    /// <summary>
    /// Ensures final no-text warning does not overwrite an already streamed assistant response.
    /// </summary>
    [Fact]
    public void ShouldPreserveStreamedAssistantDraftOnNoTextWarning_PreservesWhenDeltaWasReceived() {
        var preserve = MainWindow.ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
            activeTurnReceivedDelta: true,
            finalAssistantText: "[warning] No response text was produced by the runtime.",
            streamedAssistantText: "Sure, I can run that check now.",
            out var notice);

        Assert.True(preserve);
        Assert.Contains("partial streamed response", notice, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures preservation does not trigger when no streamed delta content was received.
    /// </summary>
    [Fact]
    public void ShouldPreserveStreamedAssistantDraftOnNoTextWarning_DoesNotPreserveWithoutDelta() {
        var preserve = MainWindow.ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
            activeTurnReceivedDelta: false,
            finalAssistantText: "[warning] No response text was produced by the runtime.",
            streamedAssistantText: "Sure, I can run that check now.",
            out _);

        Assert.False(preserve);
    }

    /// <summary>
    /// Ensures preservation logic is specific to no-text fallback warnings only.
    /// </summary>
    [Fact]
    public void ShouldPreserveStreamedAssistantDraftOnNoTextWarning_DoesNotPreserveForNormalFinalText() {
        var preserve = MainWindow.ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
            activeTurnReceivedDelta: true,
            finalAssistantText: "Replication is healthy across all DCs.",
            streamedAssistantText: "Sure, I can run that check now.",
            out _);

        Assert.False(preserve);
    }

    /// <summary>
    /// Ensures equivalent interim/final text does not duplicate the final assistant bubble.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterInterim_DoesNotAppendWhenFinalMatchesInterim() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterInterim(
            finalAssistantText: "  Running checks now.  ",
            interimAssistantText: "Running checks now.");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures meaningfully different final text is appended after interim output.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterInterim_AppendsWhenFinalDiffersFromInterim() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterInterim(
            finalAssistantText: "Running checks now. Found two unexpected reboots.",
            interimAssistantText: "Running checks now.");

        Assert.True(append);
    }

    /// <summary>
    /// Ensures whitespace/case/punctuation-only differences are treated as duplicates.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterInterim_DoesNotAppendForWhitespaceAndPunctuationOnlyDiffs() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterInterim(
            finalAssistantText: "  Running checks now!  ",
            interimAssistantText: "running checks now");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures materially different finalized text is appended after streamed draft content.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterStreamedDraft_AppendsWhenFinalDiffersFromStreamedDraft() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta: true,
            activeTurnInterimResultSeen: false,
            finalAssistantText: "Confirmed reboot evidence on AD0: 41 + 6008 at 2026-02-17T07:41Z.",
            streamedAssistantText: "Running checks now.");

        Assert.True(append);
    }

    /// <summary>
    /// Ensures near-duplicate final text does not create an extra assistant bubble after streaming.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterStreamedDraft_DoesNotAppendNearDuplicates() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta: true,
            activeTurnInterimResultSeen: false,
            finalAssistantText: "Running checks now!",
            streamedAssistantText: "running checks now");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures Unicode punctuation-only differences do not create duplicate final bubbles after streaming.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterStreamedDraft_DoesNotAppendForUnicodePunctuationOnlyDiffs() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta: true,
            activeTurnInterimResultSeen: false,
            finalAssistantText: "Running checks now！",
            streamedAssistantText: "running checks now");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures short suffix-only final updates are treated as near-duplicates after streamed drafts.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterStreamedDraft_DoesNotAppendWhenOnlyShortSuffixDiffers() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta: true,
            activeTurnInterimResultSeen: false,
            finalAssistantText: "Running checks now. Confirmed.",
            streamedAssistantText: "Running checks now.");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures long synthesized final summaries still append after streamed drafts.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterStreamedDraft_AppendsWhenSuffixExceedsNearDuplicateThreshold() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta: true,
            activeTurnInterimResultSeen: false,
            finalAssistantText: "Running checks now. Confirmed replication lag on AD0, AD1, and AD2.",
            streamedAssistantText: "Running checks now.");

        Assert.True(append);
    }

    /// <summary>
    /// Ensures streamed-draft append path is disabled once interim snapshots are already in play.
    /// </summary>
    [Fact]
    public void ShouldAppendFinalAssistantAfterStreamedDraft_DoesNotAppendWhenInterimAlreadySeen() {
        var append = MainWindow.ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta: true,
            activeTurnInterimResultSeen: true,
            finalAssistantText: "Confirmed reboot evidence on AD0.",
            streamedAssistantText: "Running checks now.");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures interim results are suppressed when a streaming draft already exists in the turn.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_DoesNotAppendWhenStreamingDraftExists() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: true,
            activeTurnBoundToConversation: true);

        Assert.False(append);
    }

    /// <summary>
    /// Ensures interim assistant output can be appended when no streaming draft exists.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_AppendsWhenNoStreamingDraftExists() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: false,
            activeTurnBoundToConversation: true);

        Assert.True(append);
    }

    /// <summary>
    /// Ensures reconnect/interim snapshots that only restate existing assistant text do not append duplicate bubbles.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_DoesNotAppendWhenInterimMatchesLatestAssistantText() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: false,
            activeTurnBoundToConversation: true,
            interimAssistantText: "Running checks now!",
            latestAssistantText: "running checks now");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures Unicode punctuation-only interim/latest diffs stay replace-only during reconnect snapshots.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_DoesNotAppendForUnicodePunctuationOnlyDiffs() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: false,
            activeTurnBoundToConversation: true,
            interimAssistantText: "Running checks now！",
            latestAssistantText: "running checks now");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures reconnect interim updates with short suffix-only differences do not append duplicate bubbles.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_DoesNotAppendWhenInterimOnlyAddsShortSuffix() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: false,
            activeTurnBoundToConversation: true,
            interimAssistantText: "Running checks now. Confirmed.",
            latestAssistantText: "Running checks now.");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures reconnect interim updates remain replace-only when latest text carries a short suffix.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_DoesNotAppendWhenLatestOnlyAddsShortSuffix() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: false,
            activeTurnBoundToConversation: true,
            interimAssistantText: "Running checks now.",
            latestAssistantText: "Running checks now. Confirmed.");

        Assert.False(append);
    }

    /// <summary>
    /// Ensures materially different interim snapshots are still appended when no streaming delta exists.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_AppendsWhenInterimDiffersFromLatestAssistantText() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: false,
            activeTurnBoundToConversation: true,
            interimAssistantText: "Running checks now. Found AD0 and AD1 evidence.",
            latestAssistantText: "Running checks now.");

        Assert.True(append);
    }

    /// <summary>
    /// Ensures streamed-turn interim snapshots stay replace-only even when interim text differs.
    /// </summary>
    [Fact]
    public void ShouldAppendInterimAssistantResult_DoesNotAppendWhenStreamingDraftExists_WithTextAwarePath() {
        var append = MainWindow.ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: true,
            activeTurnBoundToConversation: true,
            interimAssistantText: "Interim summary after reconnect.",
            latestAssistantText: "Initial streamed draft.");

        Assert.False(append);
    }
}
