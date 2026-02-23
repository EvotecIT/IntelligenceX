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
}
