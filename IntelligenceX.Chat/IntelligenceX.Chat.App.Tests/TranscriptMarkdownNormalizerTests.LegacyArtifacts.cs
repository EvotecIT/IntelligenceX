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

    /// <summary>
    /// Ensures stale standalone hash separator lines before headings are removed during legacy repair.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RemovesStandaloneHashSeparatorBeforeHeading() {
        var malformed = """
                        #

                        ### Forest Replication Status
                        - Overall health ✅ Healthy****
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.DoesNotContain("\n#\n", fixedText, StringComparison.Ordinal);
        Assert.Contains("### Forest Replication Status", fixedText, StringComparison.Ordinal);
        Assert.Contains("**Healthy**", fixedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures ordinary standalone hash lines are preserved when they are not acting as heading-adjacent artifacts.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_PreservesStandaloneHashLineOutsideHeadingArtifactCase() {
        var clean = """
                    Inventory legend:
                    #
                    keep this line as-is
                    """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(clean, out var fixedText);

        Assert.False(repaired);
        Assert.Equal(clean, fixedText);
    }

    /// <summary>
    /// Ensures broken two-line strong labels are folded into one readable line during legacy repair.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RepairsBrokenTwoLineStrongLabel() {
        var malformed = """
                        **Result
                        all 5 are healthy for directory access** with recommended LDAPS endpoints.
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.Equal("**Result:** all 5 are healthy for directory access with recommended LDAPS endpoints.", fixedText);
    }

    /// <summary>
    /// Ensures multiple broken two-line strong labels are repaired in one transcript instead of only the first occurrence.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RepairsMultipleBrokenTwoLineStrongLabels() {
        var malformed = """
                        **Result
                        first section** remains actionable.

                        **Result
                        second section** remains actionable too.
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.DoesNotContain("**Result\n", fixedText, StringComparison.Ordinal);
        Assert.Contains("**Result:** first section remains actionable.", fixedText, StringComparison.Ordinal);
        Assert.Contains("**Result:** second section remains actionable too.", fixedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures broken two-line strong labels preserve surrounding paragraph boundaries instead of flattening the remaining transcript.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RepairsBrokenTwoLineStrongLabelWithoutCollapsingFollowingParagraphs() {
        var malformed = """
                        Intro paragraph.

                        **Result
                        all 5 are healthy for directory access** with recommended LDAPS endpoints.

                        Follow-up paragraph remains separate.
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.Equal(
            """
            Intro paragraph.

            **Result:** all 5 are healthy for directory access with recommended LDAPS endpoints.

            Follow-up paragraph remains separate.
            """,
            fixedText);
    }

    /// <summary>
    /// Ensures a broken two-line strong label without trailing inline prose is still repaired cleanly.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RepairsBrokenTwoLineStrongLabelWithoutInlineSuffix() {
        var malformed = """
                        **Result
                        all 5 are healthy for directory access**
                        """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.Equal("**Result:** all 5 are healthy for directory access", fixedText);
    }

    /// <summary>
    /// Ensures legitimate multiline bold content is preserved instead of being rewritten as a label artifact.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_PreservesLegitimateMultiLineBoldContent() {
        var clean = """
                    **Keep
                    this together**
                    """;

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(clean, out var fixedText);

        Assert.False(repaired);
        Assert.Equal(clean, fixedText);
    }
}
