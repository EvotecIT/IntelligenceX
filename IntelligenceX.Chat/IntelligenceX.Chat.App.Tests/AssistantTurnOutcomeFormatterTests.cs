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
    /// Ensures tool-round-limit failures render actionable guidance instead of raw internal errors.
    /// </summary>
    [Fact]
    public void Format_RendersToolRoundLimitGuidance() {
        var text = AssistantTurnOutcomeFormatter.Format(
            AssistantTurnOutcome.ToolRoundLimit("Chat failed: Tool runner exceeded max rounds (3)."));

        Assert.Contains("tool safety limit", text);
        Assert.Contains("max rounds: 3", text);
        Assert.Contains("preferred next step", text);
    }

    /// <summary>
    /// Ensures usage-limit outcomes name the affected account when that context is available.
    /// </summary>
    [Fact]
    public void Format_RendersUsageLimitWithAccountLabel() {
        var text = AssistantTurnOutcomeFormatter.Format(
            AssistantTurnOutcome.UsageLimit(
                "Retry in about 15 minute.",
                "ChatGPT (przemyslaw.klys+openai@evotec.pl)"));

        Assert.Contains("ChatGPT usage limit reached for ChatGPT (przemyslaw.klys+openai@evotec.pl).", text);
        Assert.Contains("retry in about 15 minute", text, StringComparison.OrdinalIgnoreCase);
    }
}
