using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for transcript-driven conversation style guidance.
/// </summary>
public sealed class ConversationStyleGuidanceBuilderTests {
    /// <summary>
    /// Ensures compact recent user turns produce terse/direct guidance.
    /// </summary>
    [Fact]
    public void BuildRecentUserStyleLines_ReturnsTerseDirectGuidanceForCompactTurns() {
        var lines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Check AD0", DateTime.UtcNow, null),
            ("Assistant", "I checked AD0.", DateTime.UtcNow, null),
            ("User", "And the rest?", DateTime.UtcNow, null),
            ("User", "Keep it short.", DateTime.UtcNow, null)
        });

        Assert.Contains(lines, line => line.Contains("terse and direct", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("response shape compact", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Skip optional follow-up suggestions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("end cleanly after the answer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Avoid filler closers", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures longer recent user turns produce detailed/exploratory guidance.
    /// </summary>
    [Fact]
    public void BuildRecentUserStyleLines_ReturnsDetailedGuidanceForLongExploratoryTurns() {
        var lines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Can you walk through what you checked on replication, explain the assumptions you made, and tell me what you still need before you can validate the remaining domain controllers?", DateTime.UtcNow, null),
            ("Assistant", "I checked AD0 first.", DateTime.UtcNow, null),
            ("User", "Also include why you think that is the best next step, because I want the assistant to feel thoughtful rather than scripted.", DateTime.UtcNow, null)
        });

        Assert.Contains(lines, line => line.Contains("detailed and exploratory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("more explanation and rationale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("one or two concrete next-step suggestions", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures emphatic recent user turns produce energy-mirroring guidance without hostility.
    /// </summary>
    [Fact]
    public void BuildRecentUserStyleLines_ReturnsEnergyGuidanceForEmphaticTurns() {
        var lines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "CHECK THIS NOW!!", DateTime.UtcNow, null),
            ("Assistant", "Working on it.", DateTime.UtcNow, null),
            ("User", "And don't pad it.", DateTime.UtcNow, null)
        });

        Assert.Contains(lines, line => line.Contains("energy and directness", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures long assistant answers are treated as substantive for acknowledgement-style follow-ups.
    /// </summary>
    [Fact]
    public void HasRecentSubstantiveAssistantAnswer_ReturnsTrueForLongAssistantTurn() {
        var result = ConversationStyleGuidanceBuilder.HasRecentSubstantiveAssistantAnswer(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Check AD0 replication.", DateTime.UtcNow, null),
            ("Assistant", "I checked AD0 replication health, validated the partner set, confirmed two failing replication edges, and correlated one transport error with the same time window so the current blocker set is now concrete enough for a compact handoff.", DateTime.UtcNow, null)
        });

        Assert.True(result);
    }

    /// <summary>
    /// Ensures short assistant turns are not over-classified as substantive acknowledgements.
    /// </summary>
    [Fact]
    public void HasRecentSubstantiveAssistantAnswer_ReturnsFalseForShortAssistantTurn() {
        var result = ConversationStyleGuidanceBuilder.HasRecentSubstantiveAssistantAnswer(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Check AD0 replication.", DateTime.UtcNow, null),
            ("Assistant", "Looks fine.", DateTime.UtcNow, null)
        });

        Assert.False(result);
    }

    /// <summary>
    /// Ensures recent assistant questions are surfaced as a separate compact-reply signal.
    /// </summary>
    [Fact]
    public void HasRecentAssistantQuestion_ReturnsTrueWhenLatestAssistantTurnAsksQuestion() {
        var result = ConversationStyleGuidanceBuilder.HasRecentAssistantQuestion(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Check evotec.xyz.", DateTime.UtcNow, null),
            ("Assistant", "Do you mean internal AD health or the public DNS/mail side?", DateTime.UtcNow, null)
        });

        Assert.True(result);
    }

    /// <summary>
    /// Ensures declarative assistant turns are not mistaken for pending questions.
    /// </summary>
    [Fact]
    public void HasRecentAssistantQuestion_ReturnsFalseWhenLatestAssistantTurnIsDeclarative() {
        var result = ConversationStyleGuidanceBuilder.HasRecentAssistantQuestion(new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Check AD0 replication.", DateTime.UtcNow, null),
            ("Assistant", "AD0 has two failing partners.", DateTime.UtcNow, null)
        });

        Assert.False(result);
    }

    /// <summary>
    /// Ensures pending assistant questions are surfaced as continuation-state guidance.
    /// </summary>
    [Fact]
    public void BuildContinuationStateLines_ReturnsPendingQuestionGuidance() {
        var lines = ConversationStyleGuidanceBuilder.BuildContinuationStateLines(
            new (string Role, string Text, DateTime Time, string? Model)[] {
                ("User", "Check evotec.xyz.", DateTime.UtcNow, null),
                ("Assistant", "Do you mean internal AD health or the public DNS/mail side?", DateTime.UtcNow, null)
            },
            Array.Empty<AssistantPendingAction>());

        Assert.Contains(lines, line => line.Contains("pending question or clarification", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Pending assistant question:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures pending structured actions are surfaced as continuation-state guidance.
    /// </summary>
    [Fact]
    public void BuildContinuationStateLines_ReturnsPendingActionGuidance() {
        var lines = ConversationStyleGuidanceBuilder.BuildContinuationStateLines(
            new (string Role, string Text, DateTime Time, string? Model)[] {
                ("User", "Check evotec.xyz.", DateTime.UtcNow, null),
                ("Assistant", "I can check that in two ways.", DateTime.UtcNow, null)
            },
            new[] {
                new AssistantPendingAction("act_public", "Check public DNS/mail", "Check public DNS/mail for evotec.xyz", "/act act_public")
            });

        Assert.Contains(lines, line => line.Contains("structured follow-up actions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Check public DNS/mail", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures persisted assistant-question hints can restore continuation guidance even without a live question turn.
    /// </summary>
    [Fact]
    public void BuildContinuationStateLines_UsesPersistedAssistantQuestionHint() {
        var lines = ConversationStyleGuidanceBuilder.BuildContinuationStateLines(
            new (string Role, string Text, DateTime Time, string? Model)[] {
                ("User", "public", DateTime.UtcNow, null)
            },
            Array.Empty<AssistantPendingAction>(),
            persistedAssistantQuestionHint: "Do you mean internal AD health or the public DNS/mail side?");

        Assert.Contains(lines, line => line.Contains("pending question or clarification", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("public DNS/mail side", StringComparison.OrdinalIgnoreCase));
    }
}
