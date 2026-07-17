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
}
