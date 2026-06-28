using System;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private static bool IsSampleDataRequested() {
        var value = Environment.GetEnvironmentVariable("IXCHAT_NATIVE_SAMPLE_DATA");
        return !string.IsNullOrWhiteSpace(value)
               && (string.Equals(value.Trim(), "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value.Trim(), "yes", StringComparison.OrdinalIgnoreCase));
    }

    private void SeedSampleTranscriptIfRequested() {
        if (!IsSampleDataRequested()) {
            return;
        }

        LoadSampleTranscript(_selectedSidebarItem);
    }

    private void LoadSampleTranscript(NativeSidebarItem item) {
        _viewModel.Transcript.Clear();
        foreach (var transcriptItem in NativeSampleTranscriptFactory.Create(item, DateTimeOffset.Now)) {
            _viewModel.Transcript.Add(transcriptItem);
        }

        ScrollTranscriptToStart();
    }
}
