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
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromAssistantDraftWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            ```ix-network
            {"nodes":[{"id":"AD0"}],"edges":[]}
            ```
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotInferPreferredVisualFromDraftWhenVisualsRemainDisabled() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            """;
        var draft = """
            Current findings:
            ```ix-network
            {"nodes":[{"id":"AD0"}],"edges":[]}
            ```
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
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
    public void BuildProactiveFollowUpReviewPrompt_NormalizesMarkdownTablePreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual_type: markdown-table
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If preferred_visual is set, prefer that visual format", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_PreservesExplicitAutoPreferredVisualOverride() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: auto
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("If preferred_visual is set, prefer that visual format", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_KeepsExplicitAutoPreferredVisualOverrideWhenSignalExists() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: auto
            Use `visnetwork` only if required.
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredMaxNewVisualsOverride() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: 2
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 2 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DisablesVisualsWhenMaxNewVisualsIsZero() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            max_new_visuals: 0
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not introduce new mermaid/ix-chart/ix-network blocks", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ClampsStructuredMaxNewVisualsToSupportedRange() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: 99
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 3 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ClampsLargeStructuredMaxNewVisualsToSupportedRange() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: 9999999999
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 3 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    public void BuildProactiveFollowUpReviewPrompt_ClampsBoundaryStructuredMaxNewVisualsToSupportedRange(string value) {
        var request = $$"""
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: {{value}}
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");
        var userRequestHeaderIndex = text.IndexOf("User request:", StringComparison.Ordinal);
        Assert.True(userRequestHeaderIndex >= 0, "Prompt should include a User request section.");
        var contractHeader = text.Substring(0, userRequestHeaderIndex);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 3 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"max_new_visuals: {value}", contractHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void BuildProactiveFollowUpReviewPrompt_IgnoresInvalidStructuredMaxNewVisualsOverride(string invalidMax) {
        var request = $$"""
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            max_new_visuals: {{invalidMax}}
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 1 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerAsVisualContractWhenOverridesAreInvalid() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals:
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotTreatEmbeddedMarkerTextAsVisualContract() {
        const string request = "Please treat ix:proactive-visualization:v1 as a literal log token.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerLineWithInlineCommentAsVisualContract() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1 # from orchestrator
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerLineWithSlashSlashCommentAsVisualContract() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1 // from orchestrator
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerLineWithSemicolonCommentAndCrLfAsVisualContract() {
        const string request = "[Proactive visualization guidance]\r\n\r\nix:proactive-visualization:v1 ; from orchestrator\r\n";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotTreatMarkerLineWithSingleSlashAsVisualContract() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1 / from orchestrator
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
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
