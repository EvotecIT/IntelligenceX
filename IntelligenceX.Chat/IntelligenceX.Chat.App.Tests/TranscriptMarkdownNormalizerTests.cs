using System;
using System.Collections.Generic;
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
    /// Ensures zero-width joiners required for emoji composition are preserved.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesEmojiJoiners() {
        var text = "Engineer emoji: 👩‍💻 stays intact.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures zero-width non-joiners used in script text are preserved.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesScriptNonJoiners() {
        var text = "Persian sample: می‌خواهم this should stay unchanged.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures cleanup still removes zero-width spacing artifacts that break markdown readability.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_RemovesZeroWidthSpaceArtifacts() {
        var text = "Item\u200BOne";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("ItemOne", normalized);
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
    /// Ensures legacy repair detection catches host-label bullet artifacts without spaces after dashes.
    /// </summary>
    [Fact]
    public void TryRepairLegacyTranscript_DetectsHostLabelBulletArtifacts() {
        var malformed = "-AD1 starkes Muster\n-AD2 eher Secure-Channel";

        var repaired = TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(malformed, out var fixedText);

        Assert.True(repaired);
        Assert.Equal("- AD1 starkes Muster\n- AD2 eher Secure-Channel", fixedText);
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
    /// Ensures line-start hyphenated prose is not rewritten as a markdown bullet.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotRewriteLeadingHyphenatedProseWord() {
        var text = "-Windows-only behavior remains valid prose.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures split host-label bullets are merged with their continuation line so markdown stays list-parseable.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_MergesSplitHostLabelBulletContinuationLines() {
        var text =
            "-AD1\n"
            + "starkes Muster von Dienstabbrüchen/-fehlern (`7034/7023`).\n"
            + "-** AD2**\n"
            + "eher Secure-Channel/TLS/Policy/Power-Signale (`3210/1129/36874/41`).";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(
            "- AD1 starkes Muster von Dienstabbrüchen/-fehlern (`7034/7023`).\n"
            + "- **AD2** eher Secure-Channel/TLS/Policy/Power-Signale (`3210/1129/36874/41`).",
            normalized);
    }

    /// <summary>
    /// Ensures Unicode dash bullets normalize to ASCII list markers for consistent markdown parsing.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_NormalizesUnicodeDashBulletMarkers() {
        var text = "–** AD2** eher Secure-Channel";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- **AD2** eher Secure-Channel", normalized);
    }

    /// <summary>
    /// Ensures bullet repairs do not run inside fenced code blocks, including host-label and unicode-dash variants.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotApplyLineStartBulletRepairsInsideFencedCode() {
        var text = """
                   ```text
                   -AD1
                   -** AD2** eher Secure-Channel
                   —** AD3** eher Policy
                   ```
                   """;

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures streaming preview sanitizer performs lightweight line-start repairs without full markdown reshaping.
    /// </summary>
    [Fact]
    public void NormalizeForStreamingPreview_RepairsLineStartBulletsConservatively() {
        var text = "-AD1\nstarkes Muster\n—** AD2** eher Secure-Channel";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(text);

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
    /// Ensures nested strong markers are flattened for labeled bullets that use a full-line outer strong span.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FlattensNestedStrongMarkersInsideLabeledBullets() {
        var text = "- Why it matters **Current comparison used **System** log only.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- Why it matters **Current comparison used System log only.**", normalized);
        Assert.DoesNotContain("used **System", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures sentence-collapsed bullets are split into separate list lines.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_SplitsSentenceCollapsedBulletsWithStrongLabels() {
        var text = "- AD1 starkes Muster von Dienstabbrüchen.-** AD2** eher Secure-Channel/TLS";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- AD1 starkes Muster von Dienstabbrüchen.\n- **AD2** eher Secure-Channel/TLS", normalized);
    }

    /// <summary>
    /// Ensures labeled bullets with multiple intentional strong spans are preserved.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotFlattenIndependentStrongSpansInLabeledBullet() {
        var text = "- Note **One** and **Two**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures transcript artifacts observed in real chat exports normalize into stable markdown.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_RepairsKnownTranscriptArtifacts() {
        var text = string.Join('\n', [
            "- Signal **AD1 has very high `7034/7023` volume, mostly from **Service Control Manager**.**",
            "- Signal **Current comparison used **System** log only.**",
            "- **AD1** starkes Muster von Dienstabbrüchen.-** AD2** eher Secure-Channel/TLS"
        ]);

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("- Signal **AD1 has very high `7034/7023` volume, mostly from Service Control Manager.**", normalized, System.StringComparison.Ordinal);
        Assert.Contains("- Signal **Current comparison used System log only.**", normalized, System.StringComparison.Ordinal);
        Assert.Contains("- **AD1** starkes Muster von Dienstabbrüchen.\n- **AD2** eher Secure-Channel/TLS", normalized, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures overwrapped strong spans like <c>****359****</c> are normalized into valid markdown.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_NormalizesOverwrappedStrongSpans() {
        var text = "- TestimoX rules available ****359****";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- TestimoX rules available **359**", normalized);
    }

    /// <summary>
    /// Ensures malformed signal-flow bullets wrapped in one outer strong span are repaired to stable markdown.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_RepairsMalformedWrappedSignalFlowBullets() {
        var text = string.Join('\n', [
            "- Signal **Catalog count includes hidden/disabled/deprecated rules -> **Why it matters:** \"Available\" may be overstated for operational runs -> **Next action:** compare with a second listing using default filters (enabled + visible) to get the runnable baseline.**",
            "- Signal **Unclear execution baseline -> **Why it matters:** reviewers may run too many/non-runnable checks -> **Fix action:** generate a baseline inventory: enabled + visible + non-deprecated count, then use that for run scope.**"
        ]);

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        var expected = string.Join('\n', [
            "- Signal **Catalog count includes hidden/disabled/deprecated rules** -> **Why it matters:** \"Available\" may be overstated for operational runs -> **Next action:** compare with a second listing using default filters (enabled + visible) to get the runnable baseline.",
            "- Signal **Unclear execution baseline** -> **Why it matters:** reviewers may run too many/non-runnable checks -> **Fix action:** generate a baseline inventory: enabled + visible + non-deprecated count, then use that for run scope."
        ]);

        Assert.Equal(expected, normalized);
    }

    /// <summary>
    /// Ensures already-valid signal-flow bullets stay unchanged.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesWellFormedSignalFlowBullets() {
        var text = "- Signal **No current failures** -> **Why it matters:** transport/auth issues can still be latent -> **Action:** validate LDAP/GC/LDAPS endpoint health on all DCs.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures wrapped-signal-flow repair does not rewrite literal marker text when it appears inside inline code.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_DoesNotRewriteSignalFlowMarkerInsideInlineCode() {
        var text = "- Signal **Use `literal -> **marker**` for parser tests.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }

    /// <summary>
    /// Ensures inline-code safety on one line does not suppress wrapped-signal repair on other lines.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_RepairsWrappedSignalFlowOnOtherLinesWhenInlineCodeExists() {
        var text = string.Join('\n', [
            "- Signal **Use `literal -> **marker**` for parser tests.**",
            "- Signal **Catalog count includes hidden rules -> **Why it matters:** runnable scope may be overstated -> **Next action:** compare with enabled + visible listing.**"
        ]);

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        var expected = string.Join('\n', [
            "- Signal **Use `literal -> **marker**` for parser tests.**",
            "- Signal **Catalog count includes hidden rules** -> **Why it matters:** runnable scope may be overstated -> **Next action:** compare with enabled + visible listing."
        ]);

        Assert.Equal(expected, normalized);
    }

    /// <summary>
    /// Ensures sentence-collapsed bullets after a closing parenthesis are split and normalized.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_SplitsSentenceCollapsedBulletsAfterClosingParenthesis() {
        var text = "- AD1 Muster (`7034/7023`).-** AD2** eher Secure-Channel";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- AD1 Muster (`7034/7023`).\n- **AD2** eher Secure-Channel", normalized);
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

    /// <summary>
    /// Ensures large collapsed metric chains expand deterministically without malformed leftovers.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_ExpandsLargeCollapsedMetricChainsDeterministically() {
        var segments = new List<string> { "**Status: HEALTHY**" };
        for (var i = 1; i <= 128; i++) {
            segments.Add("**Metric " + i + ":**" + i);
        }

        var text = string.Join(" - ", segments);
        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("Status **HEALTHY**", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("-**", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("**Metric 1:**", normalized, StringComparison.Ordinal);
        for (var i = 1; i <= 128; i++) {
            Assert.Contains("- Metric " + i + " **" + i + "**", normalized, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Ensures deep nested strong artifacts flatten to a single outer strong pair.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FlattensDeepNestedStrongSpansWithinBoundedPasses() {
        var levels = new List<string>();
        for (var i = 1; i <= 40; i++) {
            levels.Add("L" + i);
        }

        var text = "- Signal **" + string.Join(" **", levels) + " complete.**";
        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);
        var normalizedAgain = TranscriptMarkdownNormalizer.NormalizeForRendering(normalized);

        var strongMarkerCount = normalized.Split("**", StringSplitOptions.None).Length - 1;
        Assert.True(strongMarkerCount <= 4, "Expected bounded strong markers, got " + strongMarkerCount.ToString());
        Assert.Contains("L40 complete.", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("**L2 **", normalized, StringComparison.Ordinal);
        Assert.Equal(normalized, normalizedAgain);
    }

    /// <summary>
    /// Ensures long host-label bullet stacks merge continuation lines at scale.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_MergesLongHostLabelBulletSeries() {
        var lines = new List<string>();
        for (var i = 1; i <= 180; i++) {
            lines.Add("-AD" + i);
            lines.Add("host detail " + i);
        }

        var text = string.Join('\n', lines);
        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);
        var normalizedLines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(180, normalizedLines.Length);
        Assert.DoesNotContain("\n-AD", normalized, StringComparison.Ordinal);
        Assert.Contains("- AD1 host detail 1", normalized, StringComparison.Ordinal);
        Assert.Contains("- AD180 host detail 180", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures repeated normalization runs remain stable once malformed transcript artifacts are repaired.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_IsIdempotentForSignalAndBulletArtifacts() {
        var text =
            "-AD1 starkes Muster\n"
            + "-** AD2** eher Secure-Channel\n"
            + "- Signal **pattern `a**b` seen, mostly from **Service Control Manager**.**";

        var once = TranscriptMarkdownNormalizer.NormalizeForRendering(text);
        var twice = TranscriptMarkdownNormalizer.NormalizeForRendering(once);

        Assert.Equal(once, twice);
    }
}
