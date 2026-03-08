using IntelligenceX.Chat.App.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers malformed metric strong-close repairs used by live rendering and stale transcript reload.
/// </summary>
public sealed partial class TranscriptMarkdownNormalizerTests {
    /// <summary>
    /// Ensures fresh assistant metric bullets with a missing trailing strong closer are repaired.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_RepairsFreshMetricBulletsMissingTrailingStrongCloser() {
        var text = "- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers*";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal("- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers**", normalized);
    }

    /// <summary>
    /// Ensures already-valid metric bullets with a proper trailing strong closer are left unchanged.
    /// </summary>
    [Fact]
    public void NormalizeForRendering_PreservesFreshMetricBulletsWithValidTrailingStrongCloser() {
        var text = "- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers**";

        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        Assert.Equal(text, normalized);
    }
}
