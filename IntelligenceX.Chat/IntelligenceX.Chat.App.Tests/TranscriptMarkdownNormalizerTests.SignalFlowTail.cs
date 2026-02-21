using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

public sealed partial class TranscriptMarkdownNormalizerTests {
    /// <summary>
    /// Ensures malformed signal-flow bullets with tight label spacing normalize into readable markdown.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FixesTightLabelSpacingInWrappedSignalFlowBullets() {
        var text =
            "- Signal **Only total count checked, not origin split ->**Why it matters:**external/custom rules can drift or disappear between hosts ->**Next action:**break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(
            "- Signal **Only total count checked, not origin split** -> **Why it matters:** external/custom rules can drift or disappear between hosts -> **Next action:** break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.",
            normalized);
    }

    /// <summary>
    /// Ensures compact plain-label signal-flow text gets spacing after labels for readability.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FixesTightSpacingAfterPlainSignalFlowLabels() {
        var text = "- Signal **Point-in-time snapshot only** -> Why it matters:trend coverage is missing -> Action:collect data every 15 minutes for 24h.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(
            "- Signal **Point-in-time snapshot only** -> Why it matters: trend coverage is missing -> Action: collect data every 15 minutes for 24h.",
            normalized);
    }

    /// <summary>
    /// Ensures signal-flow spacing repair remains language-neutral for non-English labels.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FixesTightSpacingAfterLocalizedSignalFlowLabels() {
        var text = "- Sygnal **Migawka punktowa** -> Dlaczego to wazne:brak trendu historycznego -> Nastepna akcja:zbuduj probke 24h.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(
            "- Sygnal **Migawka punktowa** -> Dlaczego to wazne: brak trendu historycznego -> Nastepna akcja: zbuduj probke 24h.",
            normalized);
    }

    /// <summary>
    /// Ensures compact arrow-to-strong transitions normalize without relying on fixed label words.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_FixesLocalizedArrowToStrongSpacing() {
        var text = "- Signal **Punkt kontrolny** ->**Znaczenie:**brak pelnej probki.";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- Signal **Punkt kontrolny** -> **Znaczenie:** brak pelnej probki.", normalized);
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
