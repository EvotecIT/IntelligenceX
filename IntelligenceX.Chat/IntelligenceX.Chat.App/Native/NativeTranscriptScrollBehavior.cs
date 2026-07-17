using System;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Shared wheel-to-offset calculation for the native transcript surface.
/// </summary>
internal static class NativeTranscriptScrollBehavior {
    private const double StandardWheelDelta = 120d;
    private const double PixelsPerNotch = 72d;
    private const double FollowEndTolerance = 24d;

    public static double CalculateTargetOffset(double currentOffset, double scrollableHeight, int wheelDelta) {
        var maximum = Math.Max(0, scrollableHeight);
        var current = Math.Clamp(currentOffset, 0, maximum);
        var change = wheelDelta / StandardWheelDelta * PixelsPerNotch;
        return Math.Clamp(current - change, 0, maximum);
    }

    /// <summary>
    /// Returns whether the transcript viewport is close enough to the bottom to keep following streamed content.
    /// </summary>
    public static bool IsAtEnd(double currentOffset, double scrollableHeight) {
        var maximum = Math.Max(0, scrollableHeight);
        var current = Math.Clamp(currentOffset, 0, maximum);
        return maximum - current <= FollowEndTolerance;
    }
}
