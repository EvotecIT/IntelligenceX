using System;
using System.Text;
using IntelligenceX.Chat.App.Rendering;

namespace IntelligenceX.Chat.App;

internal sealed class AssistantStreamingState {
    private readonly object _sync = new();
    private readonly StringBuilder _buffer = new();
    private bool _receivedDelta;

    public void Reset() {
        lock (_sync) {
            _buffer.Clear();
            _receivedDelta = false;
        }
    }

    public string AppendDeltaAndNormalizePreview(string delta) {
        if (string.IsNullOrEmpty(delta)) {
            return string.Empty;
        }

        lock (_sync) {
            _buffer.Append(delta);
            _receivedDelta = true;
            return TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(_buffer.ToString());
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

    public void ClearReceivedDelta() {
        lock (_sync) {
            _receivedDelta = false;
        }
    }

    public string SnapshotNormalizedPreview() {
        lock (_sync) {
            return TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(_buffer.ToString());
        }
    }
}
