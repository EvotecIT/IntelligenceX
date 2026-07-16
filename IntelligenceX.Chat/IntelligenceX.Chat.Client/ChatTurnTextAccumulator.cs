using System.Text;

namespace IntelligenceX.Chat.Client;

/// <summary>
/// Builds one assistant draft from incremental or snapshot-style stream fragments.
/// </summary>
public sealed class ChatTurnTextAccumulator {
    private const int MaxBufferedChars = 64 * 1024;
    private const int TrimmedBufferChars = 48 * 1024;
    private const int SnapshotOverlapCandidateThresholdChars = 6;

    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();
    private bool _receivedFragment;
    private bool _receivedProvisionalFragment;
    private bool _deltaFragmentsLookLikeSnapshots;
    private bool _provisionalFragmentsLookLikeSnapshots;

    /// <summary>
    /// Gets whether any assistant stream fragment has been observed since the last reset.
    /// </summary>
    public bool HasReceivedFragment {
        get {
            lock (_sync) {
                return _receivedFragment;
            }
        }
    }

    /// <summary>
    /// Gets whether any provisional assistant fragment has been observed since the last reset.
    /// </summary>
    public bool HasReceivedProvisionalFragment {
        get {
            lock (_sync) {
                return _receivedProvisionalFragment;
            }
        }
    }

    /// <summary>
    /// Gets the current buffered character count.
    /// </summary>
    public int BufferedLength {
        get {
            lock (_sync) {
                return _buffer.Length;
            }
        }
    }

    /// <summary>
    /// Clears all buffered text and stream-shape observations.
    /// </summary>
    public void Reset() {
        lock (_sync) {
            _buffer.Clear();
            _receivedFragment = false;
            _receivedProvisionalFragment = false;
            _deltaFragmentsLookLikeSnapshots = false;
            _provisionalFragmentsLookLikeSnapshots = false;
        }
    }

    /// <summary>
    /// Merges a stream fragment and returns the complete current draft.
    /// </summary>
    public string Append(string fragment, bool fromProvisionalEvent = false) {
        ArgumentNullException.ThrowIfNull(fragment);

        lock (_sync) {
            if (fragment.Length > 0) {
                if (!TryMergeStreamingFragmentLocked(fragment, fromProvisionalEvent)) {
                    _buffer.Append(fragment);
                }

                TrimBufferIfNeededLocked();
                _receivedFragment = true;
                if (fromProvisionalEvent) {
                    _receivedProvisionalFragment = true;
                }
            }

            return _buffer.ToString();
        }
    }

    /// <summary>
    /// Returns the complete current draft without mutating it.
    /// </summary>
    public string Snapshot() {
        lock (_sync) {
            return _buffer.ToString();
        }
    }

    /// <summary>
    /// Clears only the general received-fragment signal while preserving the buffered draft.
    /// </summary>
    public void ClearReceivedFragment() {
        lock (_sync) {
            _receivedFragment = false;
        }
    }

    private bool TryMergeStreamingFragmentLocked(string fragment, bool fromProvisionalEvent) {
        if (_buffer.Length == 0) {
            return false;
        }

        var current = _buffer.ToString();
        if (string.Equals(current, fragment, StringComparison.Ordinal)) {
            return true;
        }

        var shouldTrySnapshotMerge = fromProvisionalEvent
            ? _provisionalFragmentsLookLikeSnapshots || !_receivedProvisionalFragment || fragment.Length >= current.Length
            : _deltaFragmentsLookLikeSnapshots || !_receivedFragment || fragment.Length >= current.Length;
        if (shouldTrySnapshotMerge
            && TryMergeAsSnapshotFragmentLocked(current, fragment, out var snapshotDetected)) {
            if (snapshotDetected) {
                if (fromProvisionalEvent) {
                    _provisionalFragmentsLookLikeSnapshots = true;
                } else {
                    _deltaFragmentsLookLikeSnapshots = true;
                }
            }
            return true;
        }

        return current.EndsWith(fragment, StringComparison.Ordinal);
    }

    private bool TryMergeAsSnapshotFragmentLocked(string current, string fragment, out bool snapshotDetected) {
        snapshotDetected = false;
        if (fragment.StartsWith(current, StringComparison.Ordinal)) {
            _buffer.Append(fragment.AsSpan(current.Length));
            snapshotDetected = true;
            return true;
        }

        if (current.StartsWith(fragment, StringComparison.Ordinal)) {
            snapshotDetected = true;
            return true;
        }

        var overlap = FindLongestSuffixPrefixOverlap(current, fragment);
        var looksLikeSnapshotOverlap = overlap >= SnapshotOverlapCandidateThresholdChars
                                       || overlap == current.Length
                                       || overlap == fragment.Length;
        if (overlap < 1 || !looksLikeSnapshotOverlap) {
            return false;
        }

        if (overlap < fragment.Length) {
            _buffer.Append(fragment.AsSpan(overlap));
        }
        snapshotDetected = true;
        return true;
    }

    private static int FindLongestSuffixPrefixOverlap(string left, string right) {
        var max = Math.Min(left.Length, right.Length);
        for (var length = max; length >= 1; length--) {
            if (left.AsSpan(left.Length - length).SequenceEqual(right.AsSpan(0, length))) {
                return length;
            }
        }

        return 0;
    }

    private void TrimBufferIfNeededLocked() {
        if (_buffer.Length <= MaxBufferedChars) {
            return;
        }

        _buffer.Remove(0, _buffer.Length - TrimmedBufferChars);
    }
}
