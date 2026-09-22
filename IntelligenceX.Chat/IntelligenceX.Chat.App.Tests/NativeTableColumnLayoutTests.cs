using System.Collections.Generic;
using IntelligenceX.Chat.App.Native;
using IntelligenceX.Chat.App.Native.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies shared table auto-fit and resize calculations without constructing WinUI controls.
/// </summary>
public sealed class NativeTableColumnLayoutTests {
    /// <summary>
    /// Ensures content measurement expands useful columns while preserving sane bounds.
    /// </summary>
    [Fact]
    public void MeasurePreferredWidths_UsesHeaderAndCellContent() {
        var table = new NativeTranscriptTable(
            new[] { "Id", "Detailed message" },
            new IReadOnlyList<string>[] {
                new[] { "1", "A much longer investigation result that should receive more room" }
            });

        var widths = NativeTableColumnLayout.MeasurePreferredWidths(table);

        Assert.Equal(2, widths.Count);
        Assert.True(widths[1] > widths[0]);
        Assert.All(widths, width => Assert.InRange(
            width,
            NativeTableColumnLayout.MinimumWidth,
            NativeTableColumnLayout.MaximumPreferredWidth));
    }

    /// <summary>
    /// Ensures narrow tables expand into the viewport instead of hugging the left edge.
    /// </summary>
    [Fact]
    public void FitToViewport_DistributesAvailableSpace() {
        var fitted = NativeTableColumnLayout.FitToViewport(new[] { 120d, 180d }, 800);

        Assert.Equal(800, fitted[0] + fitted[1], precision: 3);
        Assert.True(fitted[0] > 120);
        Assert.True(fitted[1] > 180);
    }

    /// <summary>
    /// Ensures drag resizing remains inside the supported interactive range.
    /// </summary>
    [Theory]
    [InlineData(200, 50, 250)]
    [InlineData(120, -100, NativeTableColumnLayout.MinimumWidth)]
    [InlineData(540, 100, NativeTableColumnLayout.MaximumExpandedWidth)]
    public void Resize_ClampsWidth(double current, double delta, double expected) =>
        Assert.Equal(expected, NativeTableColumnLayout.Resize(current, delta));
}
