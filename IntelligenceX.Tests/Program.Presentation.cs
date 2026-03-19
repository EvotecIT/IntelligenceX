using IntelligenceX.Presentation;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestPopupPlacementMathConvertsPixelsToDips() {
        var point = PopupPlacementMath.ConvertPixelsToDips(1440d, 900d, 144d, 144d);
        AssertEqual(960d, point.X, "popup placement dip x");
        AssertEqual(600d, point.Y, "popup placement dip y");

        var bounds = PopupPlacementMath.ConvertPixelBoundsToDips(0d, 0d, 1920d, 1080d, 144d, 144d);
        AssertEqual(1280d, bounds.Right, "popup placement work area width in dips");
        AssertEqual(720d, bounds.Bottom, "popup placement work area height in dips");
    }

    private static void TestPopupPlacementMathClampsWithinWorkArea() {
        var placement = PopupPlacementMath.PlaceNearCursor(
            new PopupBounds(0d, 0d, 1280d, 720d),
            popupWidth: 400d,
            popupHeight: 500d,
            cursorXDips: 1260d,
            cursorYDips: 710d);

        AssertEqual(872d, placement.Left, "popup placement clamps right edge");
        AssertEqual(198d, placement.Top, "popup placement retains visible top");
    }
}
