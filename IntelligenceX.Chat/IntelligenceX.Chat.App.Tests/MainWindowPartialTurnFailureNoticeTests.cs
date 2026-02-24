using System;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Validates partial-turn failure notices keep actionable reason details when available.
/// </summary>
public sealed class MainWindowPartialTurnFailureNoticeTests {
    /// <summary>
    /// Ensures error-code suffixes (for example chat_failed) are surfaced in partial response notices.
    /// </summary>
    [Fact]
    public void BuildPartialTurnFailureNoticeText_IncludesErrorCodeAndDetailWhenPresent() {
        var outcome = AssistantTurnOutcome.Error(
            "Chat failed: No tool call found for custom tool call output with call_id host_next_action_abc. (chat_failed)");

        var notice = MainWindow.BuildPartialTurnFailureNoticeText(outcome);

        Assert.Contains("chat_failed", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No tool call found", notice, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures empty error details still produce a stable generic partial-turn notice.
    /// </summary>
    [Fact]
    public void BuildPartialTurnFailureNoticeText_FallsBackToGenericTextWhenDetailMissing() {
        var notice = MainWindow.BuildPartialTurnFailureNoticeText(AssistantTurnOutcome.Error(" "));

        Assert.Equal("Partial response shown above. The turn ended before completion.", notice);
    }
}
