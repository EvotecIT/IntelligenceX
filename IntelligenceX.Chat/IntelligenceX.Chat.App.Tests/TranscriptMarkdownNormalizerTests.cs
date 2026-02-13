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
    /// Ensures collapsed inline bullets are split into proper markdown list lines.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_SplitsCollapsedBulletsIntoLines() {
        var text = "**Status: HEALTHY** - **Servers checked:** 5 - **Replication edges:** 62";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Contains("**Status: HEALTHY**\n- **Servers checked:** 5\n- **Replication edges:** 62", normalized);
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
}
