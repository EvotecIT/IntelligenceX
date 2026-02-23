using System;
using System.Text;
using IntelligenceX.Chat.App.Rendering;

namespace IntelligenceX.Chat.App;

internal sealed class AssistantStreamingState {
    private const int MaxBufferedChars = 64 * 1024;
    private const int TrimmedBufferChars = 48 * 1024;

    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();
    private bool _receivedDelta;
    private bool _receivedProvisionalDelta;
    private string _normalizedPreviewCache = string.Empty;
    private bool _normalizedPreviewDirty;

    public void Reset() {
        lock (_sync) {
            _buffer.Clear();
            _receivedDelta = false;
            _receivedProvisionalDelta = false;
            _normalizedPreviewCache = string.Empty;
            _normalizedPreviewDirty = false;
        }
    }

    public string AppendDeltaAndNormalizePreview(string delta, bool fromProvisionalEvent = false) {
        if (string.IsNullOrEmpty(delta)) {
            lock (_sync) {
                return GetNormalizedPreviewLocked();
            }
        }

        lock (_sync) {
            _buffer.Append(delta);
            TrimBufferIfNeeded();
            _receivedDelta = true;
            if (fromProvisionalEvent) {
                _receivedProvisionalDelta = true;
            }
            _normalizedPreviewDirty = true;
            return GetNormalizedPreviewLocked();
        }
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
