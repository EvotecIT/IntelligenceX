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

    /// <summary>
    /// Ensures legacy trailing four-asterisk close markers are upgraded into balanced strong spans.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RepairsDanglingTrailingStrongCloseArtifacts() {
        var malformed = "- Overall health ✅ Healthy****";

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.Equal("- Overall health ✅ **Healthy**", fixedText);
    }

    /// <summary>
    /// Ensures dangling strong-close repair does not rewrite ordinary non-bullet prose.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_DoesNotRewriteOrdinaryTrailingAsteriskProse() {
        const string clean = "Literal marker code****";

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(clean, out var fixedText);

        Assert.False(repaired);
        Assert.Equal(clean, fixedText);
    }

    /// <summary>
    /// Ensures indented legacy network JSON blocks are upgraded into ix-network fenced visuals.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_UpgradesIndentedLegacyNetworkJsonBlock() {
        var malformed = """
                        Scope graph preview:

                            {
                              "nodes": [
                                { "id": "forest_ad.evotec.xyz", "label": "Forest: ad.evotec.xyz" }
                              ],
                              "edges": [
                                { "source": "forest_ad.evotec.xyz", "target": "domain_ad.evotec.xyz", "label": "contains" }
                              ]
                            }
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.Contains("```ix-network", fixedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Scope graph preview:\n\n    {", fixedText, StringComparison.Ordinal);
        Assert.Contains("\"nodes\": [", fixedText, StringComparison.Ordinal);
    }
}
