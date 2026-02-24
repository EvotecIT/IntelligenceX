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
    /// <summary>
    /// Ensures appending a delta marks state as active and keeps buffered preview content.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_MarksDeltaAndBuffersContent() {
        var state = new AssistantStreamingState();

        var preview = state.AppendDeltaAndNormalizePreview("hello");

        Assert.True(state.HasReceivedDelta());
        Assert.False(state.HasReceivedProvisionalDelta());
        Assert.True(state.HasBufferedContent());
        Assert.Contains("hello", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures reset clears every tracked streaming/provisional state flag.
    /// </summary>
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

    /// <summary>
    /// Ensures provisional fragments are tracked independently from regular chat deltas.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_TracksProvisionalFragmentsSeparately() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("delta-1");

        Assert.False(state.HasReceivedProvisionalDelta());

        state.AppendDeltaAndNormalizePreview("prov-1", fromProvisionalEvent: true);

        Assert.True(state.HasReceivedDelta());
        Assert.True(state.HasReceivedProvisionalDelta());
    }

    /// <summary>
    /// Ensures concurrent append/reset calls remain thread-safe and recover to a valid state.
    /// </summary>
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

    /// <summary>
    /// Ensures oversized streams are trimmed to the in-memory guardrail size.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_TrimsBufferWhenStreamGrowsTooLarge() {
        var state = new AssistantStreamingState();
        var oversizedChunk = new string('a', 80 * 1024);

        var preview = state.AppendDeltaAndNormalizePreview(oversizedChunk);

        Assert.InRange(state.BufferedLengthForTesting(), 1, 64 * 1024);
        Assert.False(string.IsNullOrEmpty(preview));
        Assert.True(state.HasBufferedContent());
    }

    /// <summary>
    /// Ensures empty deltas do not clear preview and return current normalized state.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_EmptyDeltaReturnsCurrentPreview() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("hello");

        var preview = state.AppendDeltaAndNormalizePreview(string.Empty);

        Assert.Contains("hello", preview, StringComparison.Ordinal);
        Assert.True(state.HasReceivedDelta());
    }

    /// <summary>
    /// Ensures null delta input is rejected with a defensive argument exception.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_NullDeltaThrowsArgumentNullException() {
        var state = new AssistantStreamingState();

        Assert.Throws<ArgumentNullException>(() => state.AppendDeltaAndNormalizePreview(null!));
    }

    /// <summary>
    /// Ensures snapshot-style chat delta fragments merge without duplicated prefixes.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_MergesChatDeltaSnapshotFragmentsWithoutPrefixDuplication() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("Thanks for");

        var preview = state.AppendDeltaAndNormalizePreview("Thanks for the nudge — I checked AD0.");

        Assert.Contains("Thanks for the nudge", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Thanks forThanks for", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures provisional snapshot fragments also merge without duplicated prefixes.
    /// </summary>
    [Fact]
    public void AppendDeltaAndNormalizePreview_MergesProvisionalSnapshotFragmentsWithoutPrefixDuplication() {
        var state = new AssistantStreamingState();
        state.AppendDeltaAndNormalizePreview("Done");

        var preview = state.AppendDeltaAndNormalizePreview("Done — and I hit an environment limit.", fromProvisionalEvent: true);

        Assert.Contains("Done — and I hit an environment limit.", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("DoneDone", preview, StringComparison.Ordinal);
    }
}
