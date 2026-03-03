using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceOutputLifecycleDuplicateFinalTests {
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
