using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies native transcript wheel scrolling without requiring a WinUI dispatcher.
/// </summary>
public sealed class NativeTranscriptScrollBehaviorTests {
    /// <summary>
    /// Ensures wheel direction, step size, and boundaries remain deterministic.
    /// </summary>
    [Theory]
    [InlineData(500, 1000, 120, 428)]
    [InlineData(500, 1000, -120, 572)]
    [InlineData(20, 1000, 120, 0)]
    [InlineData(980, 1000, -120, 1000)]
    [InlineData(100, -10, -120, 0)]
    public void CalculateTargetOffset_UsesWheelDirectionAndClampsToTranscript(
        double current,
        double scrollableHeight,
        int wheelDelta,
        double expected) {
        var target = NativeTranscriptScrollBehavior.CalculateTargetOffset(current, scrollableHeight, wheelDelta);

        Assert.Equal(expected, target);
    }

    /// <summary>
    /// Keeps streamed output following only while the viewport remains near the transcript end.
    /// </summary>
    [Theory]
    [InlineData(1000, 1000, true)]
    [InlineData(976, 1000, true)]
    [InlineData(975, 1000, false)]
    [InlineData(100, -10, true)]
    public void IsAtEnd_UsesBoundedFollowTolerance(double current, double scrollableHeight, bool expected) {
        Assert.Equal(expected, NativeTranscriptScrollBehavior.IsAtEnd(current, scrollableHeight));
    }
}
