using System;
using System.Text;
using IntelligenceX.Chat.App.Rendering;

namespace IntelligenceX.Chat.App;

internal sealed class AssistantStreamingState {
    private const int MaxBufferedChars = 64 * 1024;
    private const int TrimmedBufferChars = 48 * 1024;
    private const int SnapshotOverlapCandidateThresholdChars = 6;

    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();
    private bool _receivedDelta;
    private bool _receivedProvisionalDelta;
    private bool _deltaFragmentsLookLikeSnapshots;
    private bool _provisionalFragmentsLookLikeSnapshots;
    private string _normalizedPreviewCache = string.Empty;
    private bool _normalizedPreviewDirty;

    public void Reset() {
        lock (_sync) {
            _buffer.Clear();
            _receivedDelta = false;
            _receivedProvisionalDelta = false;
            _deltaFragmentsLookLikeSnapshots = false;
            _provisionalFragmentsLookLikeSnapshots = false;
            _normalizedPreviewCache = string.Empty;
            _normalizedPreviewDirty = false;
        }
    }

    public string AppendDeltaAndNormalizePreview(string delta, bool fromProvisionalEvent = false) {
        ArgumentNullException.ThrowIfNull(delta);

        if (string.IsNullOrEmpty(delta)) {
            lock (_sync) {
                return GetNormalizedPreviewLocked();
            }
        }

        lock (_sync) {
            if (!TryMergeStreamingFragmentLocked(delta, fromProvisionalEvent)) {
                _buffer.Append(delta);
            }
            TrimBufferIfNeeded();
            _receivedDelta = true;
            if (fromProvisionalEvent) {
                _receivedProvisionalDelta = true;
            }
            _normalizedPreviewDirty = true;
            return GetNormalizedPreviewLocked();
        }
    }

    private bool TryMergeStreamingFragmentLocked(string delta, bool fromProvisionalEvent) {
        if (_buffer.Length == 0) {
            return false;
        }

        if (string.IsNullOrEmpty(delta)) {
            return true;
        }

        var current = _buffer.ToString();
        if (string.Equals(current, delta, StringComparison.Ordinal)) {
            return true;
        }

        var shouldTrySnapshotMerge = fromProvisionalEvent
            ? _provisionalFragmentsLookLikeSnapshots || !_receivedProvisionalDelta || delta.Length >= current.Length
            : _deltaFragmentsLookLikeSnapshots || !_receivedDelta || delta.Length >= current.Length;
        if (shouldTrySnapshotMerge
            && TryMergeAsSnapshotFragmentLocked(current, delta, out var snapshotDetected)) {
            if (snapshotDetected) {
                if (fromProvisionalEvent) {
                    _provisionalFragmentsLookLikeSnapshots = true;
                } else {
                    _deltaFragmentsLookLikeSnapshots = true;
                }
            }
            return true;
        }

        if (current.EndsWith(delta, StringComparison.Ordinal)) {
            return true;
        }

        return false;
    }

    private bool TryMergeAsSnapshotFragmentLocked(string current, string delta, out bool snapshotDetected) {
        snapshotDetected = false;
        if (delta.Length == 0) {
            return true;
        }

        if (delta.StartsWith(current, StringComparison.Ordinal)) {
            _buffer.Append(delta.AsSpan(current.Length));
            snapshotDetected = true;
            return true;
        }

        if (current.StartsWith(delta, StringComparison.Ordinal)) {
            // Keep the richer/larger current draft.
            snapshotDetected = true;
            return true;
        }

        var overlap = FindLongestSuffixPrefixOverlap(current, delta);
        if (overlap < 1) {
            return false;
        }

        var looksLikeSnapshotOverlap = overlap >= SnapshotOverlapCandidateThresholdChars
                                       || overlap == current.Length
                                       || overlap == delta.Length;
        if (!looksLikeSnapshotOverlap) {
            return false;
        }

        if (overlap < delta.Length) {
            _buffer.Append(delta.AsSpan(overlap));
        }
        snapshotDetected = true;
        return true;
    }

    private static int FindLongestSuffixPrefixOverlap(string left, string right) {
        if (left.Length == 0 || right.Length == 0) {
            return 0;
        }

        var max = Math.Min(left.Length, right.Length);
        for (var len = max; len >= 1; len--) {
            if (left.AsSpan(left.Length - len).SequenceEqual(right.AsSpan(0, len))) {
                return len;
            }
        }

        return 0;
    }

    public bool HasBufferedContent() {
        lock (_sync) {
            return _buffer.Length > 0;
        }
    }

    public bool HasReceivedDelta() {
        lock (_sync) {
            return _receivedDelta;
        }
    }

    public bool HasReceivedProvisionalDelta() {
        lock (_sync) {
            return _receivedProvisionalDelta;
        }
    }

    public void ClearReceivedDelta() {
        lock (_sync) {
            _receivedDelta = false;
        }
    }

    public string SnapshotNormalizedPreview() {
        lock (_sync) {
            return GetNormalizedPreviewLocked();
        }
    }

    internal int BufferedLengthForTesting() {
        lock (_sync) {
            return _buffer.Length;
        }
    }

    private void TrimBufferIfNeeded() {
        if (_buffer.Length <= MaxBufferedChars) {
            return;
        }

        var removeCount = _buffer.Length - TrimmedBufferChars;
        if (removeCount <= 0) {
            return;
        }

        _buffer.Remove(0, removeCount);
    }

    private string GetNormalizedPreviewLocked() {
        if (!_normalizedPreviewDirty) {
            return _normalizedPreviewCache;
        }

        _normalizedPreviewCache = TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(_buffer.ToString());
        _normalizedPreviewDirty = false;
        return _normalizedPreviewCache;
    }
}
