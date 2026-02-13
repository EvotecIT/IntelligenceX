using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for assistant turn outcome rendering.
/// </summary>
public sealed class AssistantTurnOutcomeFormatterTests {
    /// <summary>
    /// Ensures canceled outcome renders stable transcript text.
    /// </summary>
    [Fact]
    public void Format_RendersCanceledMessage() {
        var text = AssistantTurnOutcomeFormatter.Format(AssistantTurnOutcome.Canceled());
        Assert.Equal("[canceled] Turn canceled.", text);
    }

    /// <summary>
    /// Ensures disconnected outcome renders stable transcript text.
    /// </summary>
    [Fact]
    public void Format_RendersDisconnectedMessage() {
        var text = AssistantTurnOutcomeFormatter.Format(AssistantTurnOutcome.Disconnected());
        Assert.Equal("[error] Disconnected.", text);
    }

    /// <summary>
    /// Ensures explicit error detail is preserved.
    /// </summary>
    [Fact]
    public void Format_RendersErrorMessageWithDetail() {
        var text = AssistantTurnOutcomeFormatter.Format(AssistantTurnOutcome.Error("boom"));
        Assert.Equal("[error] boom", text);
    }

    /// <summary>
    /// Ensures missing error detail uses fallback text.
    /// </summary>
    [Fact]
    public void Format_UsesFallbackForEmptyErrorDetail() {
        var text = AssistantTurnOutcomeFormatter.Format(AssistantTurnOutcome.Error(" "));
        Assert.Equal("[error] Unknown error.", text);
    }

    /// <summary>
    /// Ensures usage-limit failures are formatted as a dedicated limit outcome.
    /// </summary>
    [Fact]
    public void Format_RendersUsageLimitOutcome() {
        var text = AssistantTurnOutcomeFormatter.Format(
            AssistantTurnOutcome.UsageLimit("ChatGPT usage limit reached. Try again in about 60 minutes."));
        Assert.Equal("[limit] ChatGPT usage limit reached. Try again in about 60 minutes.", text);
    }

    /// <summary>
    /// Ensures tool-round-limit failures render actionable guidance instead of raw internal errors.
    /// </summary>
    [Fact]
    public void Format_RendersToolRoundLimitGuidance() {
        var text = AssistantTurnOutcomeFormatter.Format(
            AssistantTurnOutcome.ToolRoundLimit("Chat failed: Tool runner exceeded max rounds (3)."));

        Assert.StartsWith("[warning] Tool safety limit reached.", text);
        Assert.Contains("tool safety limit", text);
        Assert.Contains("max rounds: 3", text);
        Assert.Contains("continue", text);
    }
}
