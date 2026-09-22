using System;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Deterministic zoom, fit, and pan calculations for native visual workspaces.
/// </summary>
internal static class NativeVisualViewportBehavior {
    public static float CalculateFitZoom(
        double viewportWidth,
        double viewportHeight,
        double contentWidth,
        double contentHeight,
        float minimumZoom,
        float maximumZoom,
        double inset = 24) {
        if (contentWidth <= 0 || contentHeight <= 0) return minimumZoom;
        var widthRatio = Math.Max(0.01, (Math.Max(0, viewportWidth) - inset) / contentWidth);
        var heightRatio = Math.Max(0.01, (Math.Max(0, viewportHeight) - inset) / contentHeight);
        return Math.Clamp((float)Math.Min(widthRatio, heightRatio), minimumZoom, maximumZoom);
    }

    public static float CalculateWheelZoom(float currentZoom, int wheelDelta, float minimumZoom, float maximumZoom) {
        var requested = wheelDelta > 0 ? currentZoom * 1.12f : currentZoom / 1.12f;
        return Math.Clamp(requested, minimumZoom, maximumZoom);
    }

    public static double CalculatePanOffset(double startOffset, double pointerDelta, double scrollableExtent) =>
        Math.Clamp(startOffset - pointerDelta, 0, Math.Max(0, scrollableExtent));
}
