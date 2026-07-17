using System;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Shared follow-position calculation for the native transcript surface.
/// </summary>
internal static class NativeTranscriptScrollBehavior {
    private const double FollowEndTolerance = 24d;
    private const double FollowUpdateTolerance = 0.5d;
    private const double WheelPixelsPerNotch = 72d;
    private const double StandardWheelDelta = 120d;

    /// <summary>
    /// Returns whether a wheel event should navigate the vertical transcript.
    /// </summary>
    public static bool ShouldHandleWheel(bool isHorizontal, int wheelDelta) =>
        !isHorizontal && wheelDelta != 0;

    /// <summary>
    /// Returns whether the transcript viewport is close enough to the bottom to keep following streamed content.
    /// </summary>
    public static bool IsAtEnd(double currentOffset, double scrollableHeight) {
        var maximum = Math.Max(0, scrollableHeight);
        var current = Math.Clamp(currentOffset, 0, maximum);
        return maximum - current <= FollowEndTolerance;
    }

    /// <summary>
    /// Returns whether a following viewport still needs an exact move to the current end.
    /// </summary>
    public static bool RequiresFollowUpdate(double currentOffset, double scrollableHeight) {
        var maximum = Math.Max(0, scrollableHeight);
        var current = Math.Clamp(currentOffset, 0, maximum);
        return maximum - current > FollowUpdateTolerance;
    }

    /// <summary>
    /// Calculates a bounded transcript offset for a mouse-wheel delta.
    /// </summary>
    public static double CalculateWheelTarget(
        double currentOffset,
        double scrollableHeight,
        int wheelDelta) {
        var maximum = Math.Max(0, scrollableHeight);
        var current = Math.Clamp(currentOffset, 0, maximum);
        var movement = wheelDelta / StandardWheelDelta * WheelPixelsPerNotch;
        return Math.Clamp(current - movement, 0, maximum);
    }

}
