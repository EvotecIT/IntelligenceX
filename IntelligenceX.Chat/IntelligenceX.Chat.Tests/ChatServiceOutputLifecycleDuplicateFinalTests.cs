using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceOutputLifecycleDuplicateFinalTests {
    [Fact]
    public void NormalizeFinalResultTextForProtocol_RewritesSelfClaimedRefreshPhrases() {
        var normalized = ChatServiceSession.NormalizeFinalResultTextForProtocol(
            "I verified live tooling. I refreshed the check and can report fresh results.");

        Assert.DoesNotContain("I refreshed", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fresh results", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("I reran the check", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current results", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeFinalResultTextForProtocol_DoesNotRewritePolicyStatements() {
        var normalized = ChatServiceSession.NormalizeFinalResultTextForProtocol(
            "If no live tools run in a turn, I will not say I refreshed anything or claim fresh results.");

        Assert.Contains("I refreshed", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh results", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeFinalResultTextForProtocol_DoesNotRewriteQuotedUserText() {
        var normalized = ChatServiceSession.NormalizeFinalResultTextForProtocol(
            "You wrote: 'I refreshed the check and can share fresh results' but no tools ran in this turn.");

        Assert.Contains("'I refreshed the check and can share fresh results'", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldSuppressDuplicateFinalResultForRequest_ReturnsTrue_ForRepeatedNonEmptyFinalOnSameRequestAndThread() {
        var suppress = ChatServiceSession.ShouldSuppressDuplicateFinalResultForRequest(
            previousRequestId: "req-1",
            previousThreadId: "thread-1",
            previousText: "First final result",
            requestId: "req-1",
            threadId: "thread-1",
            text: "Second final result variation");

        Assert.True(suppress);
    }

    [Fact]
    public void ShouldSuppressDuplicateFinalResultForRequest_ReturnsFalse_ForEmptyToNonEmptyRecoveryOnSameRequestAndThread() {
        var suppress = ChatServiceSession.ShouldSuppressDuplicateFinalResultForRequest(
            previousRequestId: "req-1",
            previousThreadId: "thread-1",
            previousText: "   ",
            requestId: "req-1",
            threadId: "thread-1",
            text: "Recovered final result");

        Assert.False(suppress);
    }

    [Fact]
    public void ShouldSuppressDuplicateFinalResultForRequest_ReturnsFalse_WhenRequestOrThreadChanges() {
        var suppress = ChatServiceSession.ShouldSuppressDuplicateFinalResultForRequest(
            previousRequestId: "req-1",
            previousThreadId: "thread-1",
            previousText: "First final result",
            requestId: "req-2",
            threadId: "thread-1",
            text: "Second final result");

        Assert.False(suppress);
    }

    [Fact]
    public void ShouldSuppressDuplicateFinalResultForRequest_ReturnsFalse_WhenRequestOrThreadIsMissing() {
        var suppressMissingRequest = ChatServiceSession.ShouldSuppressDuplicateFinalResultForRequest(
            previousRequestId: "req-1",
            previousThreadId: "thread-1",
            previousText: "First final result",
            requestId: "",
            threadId: "thread-1",
            text: "Second final result");
        var suppressMissingThread = ChatServiceSession.ShouldSuppressDuplicateFinalResultForRequest(
            previousRequestId: "req-1",
            previousThreadId: "thread-1",
            previousText: "First final result",
            requestId: "req-1",
            threadId: "",
            text: "Second final result");

        Assert.False(suppressMissingRequest);
        Assert.False(suppressMissingThread);
    }
}
