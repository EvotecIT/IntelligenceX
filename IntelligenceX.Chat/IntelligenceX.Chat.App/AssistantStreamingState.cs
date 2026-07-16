using System;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App;

internal sealed class AssistantStreamingState {
    private readonly object _sync = new();
    private readonly ChatTurnTextAccumulator _accumulator = new();
    private string _rawPreviewCache = string.Empty;
    private string _normalizedPreviewCache = string.Empty;

    public void Reset() {
        lock (_sync) {
            _accumulator.Reset();
            _rawPreviewCache = string.Empty;
            _normalizedPreviewCache = string.Empty;
        }
    }

    public string AppendDeltaAndNormalizePreview(string delta, bool fromProvisionalEvent = false) {
        ArgumentNullException.ThrowIfNull(delta);

        lock (_sync) {
            var rawPreview = _accumulator.Append(delta, fromProvisionalEvent);
            return NormalizePreviewLocked(rawPreview);
        }
    }

    public bool HasBufferedContent() {
        return _accumulator.BufferedLength > 0;
    }

    public bool HasReceivedDelta() {
        return _accumulator.HasReceivedFragment;
    }

    public bool HasReceivedProvisionalDelta() {
        return _accumulator.HasReceivedProvisionalFragment;
    }

    public void ClearReceivedDelta() {
        _accumulator.ClearReceivedFragment();
    }

    public string SnapshotNormalizedPreview() {
        lock (_sync) {
            return NormalizePreviewLocked(_accumulator.Snapshot());
        }
    }

    internal int BufferedLengthForTesting() {
        return _accumulator.BufferedLength;
    }

    private string NormalizePreviewLocked(string rawPreview) {
        if (string.Equals(_rawPreviewCache, rawPreview, StringComparison.Ordinal)) {
            return _normalizedPreviewCache;
        }

        _rawPreviewCache = rawPreview;
        _normalizedPreviewCache = TranscriptMarkdownPreparation.PrepareStreamingPreview(rawPreview);
        return _normalizedPreviewCache;
    }
}
