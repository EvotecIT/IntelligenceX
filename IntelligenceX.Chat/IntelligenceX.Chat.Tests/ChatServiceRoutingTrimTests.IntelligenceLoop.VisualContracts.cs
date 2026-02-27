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

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredPreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: visnetwork
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If preferred_visual is set, prefer that visual format", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ParsesPreferredVisualTypeWithInlineComment() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual_type: chart # keep compact
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-chart", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_IgnoresUnknownStructuredPreferredVisual() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            preferred_visual: radar
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
    }
}
