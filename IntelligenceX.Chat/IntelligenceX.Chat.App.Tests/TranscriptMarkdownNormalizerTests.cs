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
    /// Ensures ordered-list markers in <c>1)</c> form are normalized for markdown engines that require <c>1.</c>.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_ConvertsParenOrderedListMarkersToDotStyle() {
        var text = "1) First check\n2) Second check";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("1. First check\n2. Second check", normalized);
    }

    /// <summary>
    /// Ensures ordered-list markers get spacing before strong spans in compact model output.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_AddsMissingSpaceAfterOrderedMarkersBeforeStrongSpans() {
        var text = "1)**Privilege hygiene sweep**\n2.**Delegation risk audit**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("1. **Privilege hygiene sweep**\n2. **Delegation risk audit**", normalized);
    }

    /// <summary>
    /// Ensures compact ordered-list items chained after closing parenthesis are split into lines.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_SplitsCollapsedOrderedListItemsAfterParenthesis() {
        var text = "1. **Privilege hygiene sweep**(Domain Admins + nested exposure)2.**Delegation risk audit**(unconstrained)";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(
            "1. **Privilege hygiene sweep** (Domain Admins + nested exposure)\n2. **Delegation risk audit** (unconstrained)",
            normalized);
    }

    /// <summary>
    /// Ensures single-line collapsed numbered menus are expanded into readable markdown list items.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_ExpandsCollapsedSingleLineNumberedMenuWithStrongDetails() {
        var text =
            "1) **Privilege hygiene sweep(Domain Admins + other privileged groups, nested exposure) 2)** Delegation risk audit**(unconstrained / constrained / protocol transition) 3)** Replication + DC health snapshot** (stale links, failing partners, LDAP/Kerberos basics)";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(
            "1. **Privilege hygiene sweep** (Domain Admins + other privileged groups, nested exposure)\n"
            + "2. **Delegation risk audit** (unconstrained / constrained / protocol transition)\n"
            + "3. **Replication + DC health snapshot** (stale links, failing partners, LDAP/Kerberos basics)",
            normalized);
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

    /// <summary>
    /// Ensures malformed bullet spacing at line start is normalized for transcript readability.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FixesLineStartBulletSpacingArtifacts() {
        var text = "-AD1 starkes Muster\n-** AD2** eher Secure-Channel";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- AD1 starkes Muster\n- **AD2** eher Secure-Channel", normalized);
    }

    /// <summary>
    /// Ensures nested strong markers inside signal bullets are flattened into one strong span.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FlattensNestedStrongMarkersInsideSignalBullets() {
        var text = "- Signal **AD1 has very high `7034/7023` volume, mostly from **Service Control Manager**.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- Signal **AD1 has very high `7034/7023` volume, mostly from Service Control Manager.**", normalized);
        Assert.DoesNotContain("from **Service", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures dash-prefixed command flags and numeric literals are not rewritten as malformed list bullets.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotRewriteDashPrefixedFlagsOrNumbers() {
        var text = "-X POST\n-1 means one\n-k keepalive";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures signal-line cleanup preserves literal double-asterisk tokens inside inline code spans.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesLiteralDoubleAsterisksInsideSignalCodeSpan() {
        var text = "- Signal **pattern `a**b` seen, mostly from **Service Control Manager**.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("`a**b`", normalized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("from **Service", normalized, System.StringComparison.Ordinal);
        Assert.Equal("- Signal **pattern `a**b` seen, mostly from Service Control Manager.**", normalized);
    }

    /// <summary>
    /// Ensures unmatched inline-code tails are preserved and do not get corrupted by strong-span flattening.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesUnmatchedInlineCodeTailInSignalBullets() {
        var text = "- Signal **pattern `a**b seen, mostly from **Service Control Manager**.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("`a**b seen, mostly from **Service Control Manager**.**", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures placeholder-like user content is preserved when inline-code spans are protected/restored.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesPlaceholderLikeUserContent() {
        var sentinel = "\u001FIXCODE0\u001E";
        var text = "- Signal **prefix " + sentinel + " and `a**b` from **Service Control Manager**.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains(sentinel, normalized, System.StringComparison.Ordinal);
        Assert.Contains("`a**b`", normalized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("from **Service", normalized, System.StringComparison.Ordinal);
    }
}
