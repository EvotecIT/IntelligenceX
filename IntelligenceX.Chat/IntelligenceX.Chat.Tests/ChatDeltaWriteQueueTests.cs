using System.Collections.Generic;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards ordered, single-channel assistant streaming at the service boundary.
/// </summary>
public sealed class ChatDeltaWriteQueueTests {
    /// <summary>Provider callbacks may enqueue quickly, but writes retain their exact order.</summary>
    [Fact]
    public async Task CompleteAsync_DrainsDeltasInProviderOrder() {
        var written = new List<string>();
        var queue = new ChatDeltaWriteQueue(async delta => {
            await Task.Yield();
            written.Add(delta);
        });

        Assert.True(queue.TryEnqueue("first"));
        Assert.True(queue.TryEnqueue(" "));
        Assert.True(queue.TryEnqueue("second"));

        await queue.CompleteAsync();

        Assert.Equal(new[] { "first", " ", "second" }, written);
        Assert.False(queue.TryEnqueue("late"));
    }

    /// <summary>Only visible response phases may publish assistant deltas.</summary>
    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, false)]
    public void ShouldPublishAssistantDelta_HidesBufferedAndReviewOnlyOutput(
        bool bufferDraft,
        int reviewDepth,
        bool expected) {
        Assert.Equal(expected, ChatServiceSession.ShouldPublishAssistantDelta(bufferDraft, reviewDepth));
    }
}
