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
        Assert.True(state.HasBufferedContent());
        Assert.Contains("hello", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_ClearsBufferedContentAndDeltaFlag() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("partial");

        state.Reset();

        Assert.False(state.HasReceivedDelta());
        Assert.False(state.HasBufferedContent());
        Assert.Equal(string.Empty, state.SnapshotNormalizedPreview());
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
}
