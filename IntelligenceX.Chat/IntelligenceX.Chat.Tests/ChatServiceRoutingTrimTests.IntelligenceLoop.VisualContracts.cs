using System;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredVisualOverrideToDisableVisuals() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: false
            If needed, use `visnetwork`.
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredVisualOverrideToEnableVisuals() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            Keep response compact unless a visual helps.
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ParsesStructuredVisualOverrideWithInlineComment() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true # include graph only when evidence is complex
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }
}
