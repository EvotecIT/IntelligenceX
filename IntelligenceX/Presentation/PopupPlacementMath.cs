using System;

namespace IntelligenceX.Presentation;

/// <summary>
/// Helper methods for converting monitor pixel coordinates into device-independent popup placement.
/// </summary>
public static class PopupPlacementMath {
    private const double DefaultDpi = 96d;
    private const double DefaultMargin = 8d;
    private const double CursorHorizontalOffset = 18d;
    private const double CursorVerticalOffset = 12d;

    /// <summary>
    /// Converts monitor work-area bounds from device pixels into device-independent units.
    /// </summary>
    public static PopupBounds ConvertPixelBoundsToDips(
        double leftPixels,
        double topPixels,
        double rightPixels,
        double bottomPixels,
        double dpiX,
        double dpiY) {
        return new PopupBounds(
            ScalePixelsToDips(leftPixels, dpiX),
            ScalePixelsToDips(topPixels, dpiY),
            ScalePixelsToDips(rightPixels, dpiX),
            ScalePixelsToDips(bottomPixels, dpiY));
    }

    /// <summary>
    /// Converts a monitor-relative point from device pixels into device-independent units.
    /// </summary>
    public static PopupPoint ConvertPixelsToDips(double xPixels, double yPixels, double dpiX, double dpiY) {
        return new PopupPoint(
            ScalePixelsToDips(xPixels, dpiX),
            ScalePixelsToDips(yPixels, dpiY));
    }

    /// <summary>
    /// Computes a popup placement near the cursor while clamping the result into the visible work area.
    /// </summary>
    public static PopupPlacement PlaceNearCursor(
        PopupBounds workArea,
        double popupWidth,
        double popupHeight,
        double cursorXDips,
        double cursorYDips) {
        var targetLeft = cursorXDips - popupWidth + CursorHorizontalOffset;
        var targetTop = cursorYDips - popupHeight - CursorVerticalOffset;

        var clampedLeft = Math.Max(
            workArea.Left + DefaultMargin,
            Math.Min(targetLeft, workArea.Right - popupWidth - DefaultMargin));
        var clampedTop = Math.Max(
            workArea.Top + DefaultMargin,
            Math.Min(targetTop, workArea.Bottom - popupHeight - DefaultMargin));

        return new PopupPlacement(clampedLeft, clampedTop);
    }

    private static double ScalePixelsToDips(double pixels, double dpi) {
        var safeDpi = dpi > 0d ? dpi : DefaultDpi;
        return pixels * DefaultDpi / safeDpi;
    }
}

/// <summary>
/// Device-independent bounds used during popup placement.
/// </summary>
public readonly record struct PopupBounds(double Left, double Top, double Right, double Bottom);

/// <summary>
/// Device-independent point used during popup placement.
/// </summary>
public readonly record struct PopupPoint(double X, double Y);

/// <summary>
/// Final popup placement in device-independent units.
/// </summary>
public readonly record struct PopupPlacement(double Left, double Top);
