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

/// <summary>
/// Owns the interaction state for one native transcript viewport.
/// </summary>
/// <remarks>
/// WinUI reports programmatic and pointer-driven <c>ViewChanged</c> events through the same
/// surface. Keeping the active operation here prevents a wheel gesture from leaving an older
/// follow-to-end operation stuck and suppressing later streamed-content updates.
/// </remarks>
internal sealed class NativeTranscriptScrollState {
    private NativeTranscriptScrollOperation _operation;
    private bool _followUpdateQueued;
    private double? _pendingWheelOffset;
    private long _operationVersion;

    /// <summary>
    /// Gets whether new transcript extent should remain pinned to the end.
    /// </summary>
    public bool IsFollowingEnd { get; private set; } = true;

    /// <summary>
    /// Gets the most recent programmatic operation version.
    /// </summary>
    public long OperationVersion => _operationVersion;

    /// <summary>
    /// Requests end-following and supersedes any manual or programmatic view change in flight.
    /// </summary>
    public void RequestFollowToEnd() {
        IsFollowingEnd = true;
        _pendingWheelOffset = null;
        _operation = NativeTranscriptScrollOperation.None;
        _operationVersion++;
    }

    /// <summary>
    /// Plans one coalesced mouse-wheel move and makes it the only active view operation.
    /// </summary>
    public bool TryPlanWheelChange(
        double currentOffset,
        double scrollableHeight,
        int wheelDelta,
        out double target,
        out long operationVersion) {
        var startingOffset = _pendingWheelOffset ?? currentOffset;
        target = NativeTranscriptScrollBehavior.CalculateWheelTarget(
            startingOffset,
            scrollableHeight,
            wheelDelta);
        operationVersion = _operationVersion;
        if (Math.Abs(target - startingOffset) < 0.5) {
            return false;
        }

        IsFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(target, scrollableHeight);
        _pendingWheelOffset = target;
        _operation = NativeTranscriptScrollOperation.Manual;
        operationVersion = ++_operationVersion;
        return true;
    }

    /// <summary>
    /// Reserves one dispatcher callback for a required end-follow update.
    /// </summary>
    public bool TryQueueFollowUpdate(int messageCount, double currentOffset, double scrollableHeight) {
        if (!IsFollowingEnd
            || messageCount <= 0
            || _followUpdateQueued
            || _operation == NativeTranscriptScrollOperation.Follow
            || !NativeTranscriptScrollBehavior.RequiresFollowUpdate(currentOffset, scrollableHeight)) {
            return false;
        }

        _followUpdateQueued = true;
        return true;
    }

    /// <summary>
    /// Converts a queued follow request into the active programmatic view change.
    /// </summary>
    public bool TryBeginQueuedFollow(
        int messageCount,
        double currentOffset,
        double scrollableHeight,
        out double target,
        out long operationVersion) {
        _followUpdateQueued = false;
        target = Math.Max(0, scrollableHeight);
        operationVersion = _operationVersion;
        if (!IsFollowingEnd
            || messageCount <= 0
            || !NativeTranscriptScrollBehavior.RequiresFollowUpdate(currentOffset, scrollableHeight)) {
            return false;
        }

        _pendingWheelOffset = null;
        _operation = NativeTranscriptScrollOperation.Follow;
        operationVersion = ++_operationVersion;
        return true;
    }

    /// <summary>
    /// Releases a dispatcher reservation when the callback could not be queued.
    /// </summary>
    public void CancelQueuedFollow() => _followUpdateQueued = false;

    /// <summary>
    /// Completes the current operation when it still matches the supplied version.
    /// </summary>
    public bool TryCompleteOperation(
        long operationVersion,
        double currentOffset,
        double scrollableHeight) {
        if (_operation == NativeTranscriptScrollOperation.None
            || operationVersion != _operationVersion) {
            return false;
        }

        if (_operation == NativeTranscriptScrollOperation.Manual) {
            IsFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(currentOffset, scrollableHeight);
        }

        _pendingWheelOffset = null;
        _operation = NativeTranscriptScrollOperation.None;
        _operationVersion++;
        return true;
    }

    /// <summary>
    /// Completes whichever operation generated the final WinUI view notification.
    /// </summary>
    public bool CompleteActiveOperation(double currentOffset, double scrollableHeight) {
        if (_operation == NativeTranscriptScrollOperation.None) {
            ObserveUserView(currentOffset, scrollableHeight);
            return false;
        }

        return TryCompleteOperation(_operationVersion, currentOffset, scrollableHeight);
    }

    /// <summary>
    /// Cancels a rejected manual view change and restores follow state from the real viewport.
    /// </summary>
    public void CancelManualOperation(
        long operationVersion,
        double currentOffset,
        double scrollableHeight) {
        if (_operation != NativeTranscriptScrollOperation.Manual
            || operationVersion != _operationVersion) {
            return;
        }

        IsFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(currentOffset, scrollableHeight);
        _pendingWheelOffset = null;
        _operation = NativeTranscriptScrollOperation.None;
        _operationVersion++;
    }

    /// <summary>
    /// Records a view change that was not initiated by this state owner.
    /// </summary>
    public void ObserveUserView(double currentOffset, double scrollableHeight) {
        if (_operation != NativeTranscriptScrollOperation.None) {
            return;
        }

        IsFollowingEnd = NativeTranscriptScrollBehavior.IsAtEnd(currentOffset, scrollableHeight);
    }

    /// <summary>
    /// Returns whether a delayed completion callback still belongs to the current operation.
    /// </summary>
    public bool IsCurrentOperation(long operationVersion) =>
        _operation != NativeTranscriptScrollOperation.None
        && operationVersion == _operationVersion;

    /// <summary>
    /// Returns whether a delayed callback still belongs to the latest state transition.
    /// </summary>
    public bool IsCurrentVersion(long operationVersion) => operationVersion == _operationVersion;

    private enum NativeTranscriptScrollOperation {
        None,
        Manual,
        Follow
    }
}
