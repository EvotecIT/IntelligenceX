using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies native transcript follow behavior without requiring a WinUI dispatcher.
/// </summary>
public sealed class NativeTranscriptScrollBehaviorTests {
    /// <summary>
    /// Leaves horizontal tilt-wheel input available to wide nested evidence controls.
    /// </summary>
    [Theory]
    [InlineData(false, 120, true)]
    [InlineData(false, 0, false)]
    [InlineData(true, 120, false)]
    public void ShouldHandleWheel_OnlyAcceptsVerticalMotion(
        bool isHorizontal,
        int wheelDelta,
        bool expected) {
        Assert.Equal(expected, NativeTranscriptScrollBehavior.ShouldHandleWheel(isHorizontal, wheelDelta));
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

    /// <summary>
    /// Keeps a following viewport pinned to the exact end as small extents are added.
    /// </summary>
    [Theory]
    [InlineData(1000, 1000, false)]
    [InlineData(999.5, 1000, false)]
    [InlineData(999.4, 1000, true)]
    [InlineData(0, -10, false)]
    public void RequiresFollowUpdate_UsesExactEndTolerance(
        double currentOffset,
        double scrollableHeight,
        bool expected) {
        Assert.Equal(
            expected,
            NativeTranscriptScrollBehavior.RequiresFollowUpdate(currentOffset, scrollableHeight));
    }

    /// <summary>
    /// Converts wheel motion into a smooth bounded transcript offset independent of sidebar focus.
    /// </summary>
    [Theory]
    [InlineData(500, 1000, 120, 428)]
    [InlineData(500, 1000, -120, 572)]
    [InlineData(20, 1000, 120, 0)]
    [InlineData(980, 1000, -120, 1000)]
    [InlineData(500, -10, -120, 0)]
    public void CalculateWheelTarget_ClampsAnimatedMovement(
        double current,
        double scrollableHeight,
        int wheelDelta,
        double expected) {
        Assert.Equal(
            expected,
            NativeTranscriptScrollBehavior.CalculateWheelTarget(current, scrollableHeight, wheelDelta));
    }

}
