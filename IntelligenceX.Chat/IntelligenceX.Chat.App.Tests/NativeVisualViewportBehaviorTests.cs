using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies diagram fit, zoom, and pan calculations without a WinUI dispatcher.
/// </summary>
public sealed class NativeVisualViewportBehaviorTests {
    /// <summary>
    /// Ensures fit mode considers both dimensions and clamps supported zoom.
    /// </summary>
    [Theory]
    [InlineData(1224, 724, 1200, 700, 1)]
    [InlineData(624, 374, 1200, 700, 0.5)]
    [InlineData(120, 80, 1200, 700, 0.2)]
    public void CalculateFitZoom_FitsAndClamps(
        double viewportWidth,
        double viewportHeight,
        double contentWidth,
        double contentHeight,
        float expected) =>
        Assert.Equal(expected, NativeVisualViewportBehavior.CalculateFitZoom(
            viewportWidth,
            viewportHeight,
            contentWidth,
            contentHeight,
            minimumZoom: 0.2f,
            maximumZoom: 5f), precision: 3);

    /// <summary>
    /// Ensures panning follows drag direction and stays inside scroll bounds.
    /// </summary>
    [Theory]
    [InlineData(300, 50, 1000, 250)]
    [InlineData(10, 50, 1000, 0)]
    [InlineData(980, -50, 1000, 1000)]
    public void CalculatePanOffset_Clamps(double start, double delta, double extent, double expected) =>
        Assert.Equal(expected, NativeVisualViewportBehavior.CalculatePanOffset(start, delta, extent));
}
