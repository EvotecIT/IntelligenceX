using System;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo ResolveMaxReviewPassesMethod =
        typeof(ChatServiceSession).GetMethod("ResolveMaxReviewPasses", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveMaxReviewPasses not found.");
    private static readonly MethodInfo ResolveModelHeartbeatSecondsMethod =
        typeof(ChatServiceSession).GetMethod("ResolveModelHeartbeatSeconds", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveModelHeartbeatSeconds not found.");
    private static readonly MethodInfo ShouldAttemptResponseQualityReviewMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptResponseQualityReview", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptResponseQualityReview not found.");
    private static readonly MethodInfo BuildResponseQualityReviewPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildResponseQualityReviewPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildResponseQualityReviewPrompt not found.");
    private static readonly MethodInfo TryReadProactiveModeFromRequestTextMethod =
        typeof(ChatServiceSession).GetMethod("TryReadProactiveModeFromRequestText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryReadProactiveModeFromRequestText not found.");
    private static readonly MethodInfo ShouldAttemptProactiveFollowUpReviewMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptProactiveFollowUpReview", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptProactiveFollowUpReview not found.");
    private static readonly MethodInfo BuildProactiveFollowUpReviewPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildProactiveFollowUpReviewPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildProactiveFollowUpReviewPrompt not found.");

    [Fact]
    public void ResolveMaxReviewPasses_DefaultsToSafeValueWhenUnset() {
        var result = ResolveMaxReviewPassesMethod.Invoke(null, new object?[] { null });

        Assert.Equal(1, Assert.IsType<int>(result));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 3)]
    public void ResolveMaxReviewPasses_ClampsToSupportedRange(int requested, int expected) {
        var options = new ChatRequestOptions {
            MaxReviewPasses = requested
        };

        var result = ResolveMaxReviewPassesMethod.Invoke(null, new object?[] { options });

        Assert.Equal(expected, Assert.IsType<int>(result));
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(120, 60)]
    public void ResolveModelHeartbeatSeconds_RespectsBounds(int? requested, int expected) {
        ChatRequestOptions? options = requested is null
            ? null
            : new ChatRequestOptions {
                ModelHeartbeatSeconds = requested.Value
            };

        var result = ResolveModelHeartbeatSecondsMethod.Invoke(null, new object?[] { options });

        Assert.Equal(expected, Assert.IsType<int>(result));
    }

    [Theory]
    [InlineData("show latest failed logons", "ok", false, false, 0, 1, true)]
    [InlineData("show latest failed logons", "Findings: 4625 events are highest on DC01 between 02:00 and 05:00 UTC. Top source hosts are APP-17 and APP-22. Service account svc-backup is responsible for most failures and appears to have an outdated secret rotation. I already correlated event IDs 4625 and 4740, and lockouts are isolated to one OU. Recommended next step is to rotate the secret and re-run the report with a 6-hour window.", false, true, 0, 1, false)]
    [InlineData("show latest failed logons", "ok", true, false, 0, 1, false)]
    [InlineData("show latest failed logons", "ok", false, false, 1, 1, false)]
    public void ResponseQualityReviewEval_ScenariosMatchExpectedDecision(
        string userRequest,
        string assistantDraft,
        bool executionContractApplies,
        bool hasToolActivity,
        int reviewPassesUsed,
        int maxReviewPasses,
        bool expected) {
        var result = ShouldAttemptResponseQualityReviewMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, executionContractApplies, hasToolActivity, reviewPassesUsed, maxReviewPasses });

        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void BuildResponseQualityReviewPrompt_EmitsStableMarkerAndPassMetadata() {
        var result = BuildResponseQualityReviewPromptMethod.Invoke(
            null,
            new object?[] { "run diagnostics on dc01", "ok", false, 1, 2 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:response-review:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Review pass 1/2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tool activity this turn: none.", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled: true", true, true)]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled: false", true, false)]
    [InlineData("no marker", false, false)]
    public void TryReadProactiveModeFromRequestText_ParsesStructuredMarker(string requestText, bool expectedRead, bool expectedEnabled) {
        var args = new object?[] { requestText, null };
        var result = TryReadProactiveModeFromRequestTextMethod.Invoke(null, args);

        Assert.Equal(expectedRead, Assert.IsType<bool>(result));
        Assert.Equal(expectedEnabled, Assert.IsType<bool>(args[1]));
    }

    [Theory]
    [InlineData(true, true, false, "Findings summary without actions.", true)]
    [InlineData(false, true, false, "Findings summary without actions.", false)]
    [InlineData(true, false, false, "Findings summary without actions.", false)]
    [InlineData(true, true, true, "Findings summary without actions.", false)]
    [InlineData(true, true, false, "[Action]\nix:action:v1\nid: act_001\nreply: /act act_001", false)]
    public void ShouldAttemptProactiveFollowUpReview_RespectsToolActivityAndActionBlocks(
        bool proactiveModeEnabled,
        bool hasToolActivity,
        bool proactiveFollowUpUsed,
        string assistantDraft,
        bool expected) {
        var result = ShouldAttemptProactiveFollowUpReviewMethod.Invoke(
            null,
            new object?[] { proactiveModeEnabled, hasToolActivity, proactiveFollowUpUsed, assistantDraft });

        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_EmitsStableMarkerAndSections() {
        var result = BuildProactiveFollowUpReviewPromptMethod.Invoke(
            null,
            new object?[] { "analyze failed logons", "Current findings..." });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:proactive-followup:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Potential issues to verify", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recommended next fixes", text, StringComparison.OrdinalIgnoreCase);
    }
}
