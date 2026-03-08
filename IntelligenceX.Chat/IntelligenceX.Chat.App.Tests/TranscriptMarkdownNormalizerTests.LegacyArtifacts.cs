using IntelligenceX.Chat.App.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies legacy transcript artifact repair remains structurally valid for exported and persisted history.
/// </summary>
public sealed partial class TranscriptMarkdownNormalizerTests {
    /// <summary>
    /// Ensures indented legacy cached-evidence bullet headings are promoted to real headings at column zero.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_PromotesIndentedLegacyToolHeadingBulletsToColumnZeroHeadings() {
        var malformed = """
                        [Cached evidence fallback]

                          - eventlog_top_events: ### Top 30 recent events (preview)
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.DoesNotContain("  ### Top 30 recent events (preview)", fixedText, StringComparison.Ordinal);
        Assert.Contains("\n### Top 30 recent events (preview)", fixedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures duplicate cached-evidence slug headings are removed before any supported heading depth.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RemovesDuplicateToolSlugHeadingBeforeDeeperHeading() {
        var malformed = """
                        [Cached evidence fallback]

                        #### ad_environment_discover
                        ##### Active Directory: Environment Discovery
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.DoesNotContain("#### ad_environment_discover", fixedText, StringComparison.Ordinal);
        Assert.Contains("##### Active Directory: Environment Discovery", fixedText, StringComparison.Ordinal);
    }
}
