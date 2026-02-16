using IntelligenceX.Chat.App.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for markdown normalization before transcript rendering.
/// </summary>
public sealed class TranscriptMarkdownNormalizerTests {
    /// <summary>
    /// Ensures joined emoji/choice tokens are normalized for better markdown readability.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FixesTokenJoinArtifacts() {
        var text =
            "Yep — **LDAP on `ad0` looks reachable** from this session ✅I had to retry.\n"
            + "If you want, I can run 1) ldap probe, or2) cert sanity.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("✅ I", normalized);
        Assert.Contains("or 2)", normalized);
    }

    /// <summary>
    /// Ensures glued bold/word boundaries are normalized into readable spacing.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_SplitsGluedBoldWordBoundaries() {
        var text = "Status **Healthy**next and check **Now**please.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("Status **Healthy** next and check **Now** please.", normalized);
    }

    /// <summary>
    /// Ensures collapsed inline bullets are split into proper markdown list lines.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_SplitsCollapsedBulletsIntoLines() {
        var text = "**Status: HEALTHY** - **Servers checked:** 5 - **Replication edges:** 62";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("Status **HEALTHY**\n- Servers checked **5**", normalized);
        Assert.Contains("\n- Replication edges **62**", normalized);
    }

    /// <summary>
    /// Ensures inline hyphen prose is not treated as a collapsed bullet list.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotSplitInlineHyphenProse() {
        var text = "Health note: foo - **bar** should stay inline.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures normalization preserves intentional leading/trailing whitespace and native line endings.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesEdgeWhitespace() {
        var text = "\r\n  line  \r\n";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("\r\n  line  \r\n", normalized);
    }

    /// <summary>
    /// Ensures malformed collapsed status chains are expanded into markdown list lines.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_ExpandsMalformedStatusMetricChains() {
        var text = "**Status: HEALTHY** - **Servers checked:**5 -**Replication edges:**62 -*Failed edges:**0 -*Stale edges (>24h):**0 - **Servers with failures:**0";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("Status **HEALTHY**", normalized);
        Assert.Contains("- Servers checked **5**", normalized);
        Assert.Contains("- Replication edges **62**", normalized);
        Assert.Contains("- Failed edges **0**", normalized);
        Assert.Contains("- Stale edges (>24h) **0**", normalized);
        Assert.Contains("- Servers with failures **0**", normalized);
        Assert.DoesNotContain("-**", normalized);
        Assert.DoesNotContain(":**0", normalized);
        Assert.DoesNotContain(":**5", normalized);
        Assert.DoesNotContain("**Servers checked:**", normalized);
    }

    /// <summary>
    /// Ensures legacy repair only triggers for malformed transcript artifacts.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_RepairsOnlyWhenLegacyArtifactsDetected() {
        var malformed = "**Status: HEALTHY** - **Servers checked:**5 -**Replication edges:**62";
        var clean = "Quick AD replication check\n- Servers checked **5**";

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);
        var cleanChanged = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(clean, out var cleanText);

        Assert.True(repaired);
        Assert.Contains("Status **HEALTHY**", fixedText);
        Assert.False(cleanChanged);
        Assert.Equal(clean, cleanText);
    }

    /// <summary>
    /// Ensures normalization does not rewrite markdown artifacts inside fenced code blocks.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotMutateFencedCodeBlocks() {
        var text = """
                   ```text
                   ✅I can run 1) LDAP checks, or2) cert checks.
                   **Status: HEALTHY** - **Servers checked:**5
                   ```
                   """;

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures prose cleanup fixes common bold/list artifacts seen in model output.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_RepairsBoldAndCollapsedOrderedListArtifacts() {
        var text =
            "Those 3 weren't user accounts; they were ** unresolved privileged SIDs** in the sweep.\n"
            + "1. **Deleted object remnants**(SID left in ACL path) 2.^ **Foreign/trusted principal no longer resolvable**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("**unresolved privileged SIDs**", normalized);
        Assert.Contains("**Deleted object remnants** (SID left in ACL path)", normalized);
        Assert.Contains("\n2. **Foreign/trusted principal no longer resolvable**", normalized);
        Assert.DoesNotContain("2.^", normalized);
    }

    /// <summary>
    /// Ensures strong spans with smart quotes and hyphenated terms remain parseable.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesQuotedAndHyphenatedStrongSpans() {
        var text =
            "Run **“Top 8 high-signal security pack”** now, or only **GPO-related** reports.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }
}
