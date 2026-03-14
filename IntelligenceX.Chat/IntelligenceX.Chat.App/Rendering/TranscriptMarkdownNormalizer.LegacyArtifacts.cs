using System;

namespace IntelligenceX.Chat.App.Rendering;

internal static partial class TranscriptMarkdownNormalizer {
    private static bool RequiresLegacyTranscriptRepair(string text) {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        return LegacyRepairSignalRegex.IsMatch(text)
               || text.Contains("****", StringComparison.Ordinal)
               || text.IndexOf("ix:cached-tool-evidence:v1", StringComparison.OrdinalIgnoreCase) >= 0
               || LegacyToolHeadingBulletRegex.IsMatch(text)
               || LegacyToolSlugHeadingRegex.IsMatch(text)
               || StandaloneHashSeparatorBeforeHeadingSignalRegex.IsMatch(text)
               || text.Contains("**Result\n", StringComparison.Ordinal)
               || ContainsLegacyJsonVisualFenceCandidate(text);
    }
}
