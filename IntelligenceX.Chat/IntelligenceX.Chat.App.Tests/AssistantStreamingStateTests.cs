using System;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards thread-safe streamed assistant draft state used by chat_delta/provisional paths.
/// </summary>
public sealed class AssistantStreamingStateTests {
    [Fact]
    public void AppendDeltaAndNormalizePreview_MarksDeltaAndBuffersContent() {
        var state = new AssistantStreamingState();

        var preview = state.AppendDeltaAndNormalizePreview("hello");

        Assert.True(state.HasReceivedDelta());
        Assert.False(state.HasReceivedProvisionalDelta());
        Assert.True(state.HasBufferedContent());
        Assert.Contains("hello", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_ClearsBufferedContentAndDeltaFlag() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("partial", fromProvisionalEvent: true);

        state.Reset();

        Assert.False(state.HasReceivedDelta());
        Assert.False(state.HasReceivedProvisionalDelta());
        Assert.False(state.HasBufferedContent());
        Assert.Equal(string.Empty, state.SnapshotNormalizedPreview());
    }

    [Fact]
    public void AppendDeltaAndNormalizePreview_TracksProvisionalFragmentsSeparately() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("delta-1");

        Assert.False(state.HasReceivedProvisionalDelta());

        state.AppendDeltaAndNormalizePreview("prov-1", fromProvisionalEvent: true);

        Assert.True(state.HasReceivedDelta());
        Assert.True(state.HasReceivedProvisionalDelta());
    }

    [Fact]
    public async Task AppendAndReset_AreThreadSafeAcrossConcurrentCallers() {
        var state = new AssistantStreamingState();
        var appendTasks = Enumerable.Range(0, 24)
            .Select(worker => Task.Run(() => {
                for (var i = 0; i < 20; i++) {
                    state.AppendDeltaAndNormalizePreview($"chunk-{worker}-{i} ");
                }
            }))
            .ToArray();
        var resetTask = Task.Run(() => {
            for (var i = 0; i < 12; i++) {
                state.Reset();
            }
        });

        await Task.WhenAll(appendTasks.Append(resetTask));

        state.AppendDeltaAndNormalizePreview("tail");
        Assert.True(state.HasReceivedDelta());
        Assert.True(state.HasBufferedContent());
        Assert.Contains("tail", state.SnapshotNormalizedPreview(), StringComparison.Ordinal);
    }

    [Fact]
    public void AppendDeltaAndNormalizePreview_TrimsBufferWhenStreamGrowsTooLarge() {
        var state = new AssistantStreamingState();
        var oversizedChunk = new string('a', 80 * 1024);

        var preview = state.AppendDeltaAndNormalizePreview(oversizedChunk);

        Assert.InRange(state.BufferedLengthForTesting(), 1, 64 * 1024);
        Assert.False(string.IsNullOrEmpty(preview));
        Assert.True(state.HasBufferedContent());
    }

    [Fact]
    public void AppendDeltaAndNormalizePreview_EmptyDeltaReturnsCurrentPreview() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("hello");

        var preview = state.AppendDeltaAndNormalizePreview(string.Empty);

        Assert.Contains("hello", preview, StringComparison.Ordinal);
        Assert.True(state.HasReceivedDelta());
    }

    [Fact]
    public void AppendDeltaAndNormalizePreview_NullDeltaThrowsArgumentNullException() {
        var state = new AssistantStreamingState();

        Assert.Throws<ArgumentNullException>(() => state.AppendDeltaAndNormalizePreview(null!));
    }
}
