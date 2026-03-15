using IntelligenceX.Chat.App;
using OfficeIMO.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the explicit OfficeIMO markdown input-normalization seam used during transcript cleanup.
/// </summary>
public sealed class OfficeImoMarkdownInputNormalizationRuntimeContractTests {
    /// <summary>
    /// Ensures the runtime contract applies OfficeIMO ordered-list normalization through the explicit transcript preset.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_NormalizesOrderedListParenMarkers() {
        const string markdown = "1)First check\n2)   Second check";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);

        Assert.Equal("1. First check\n2. Second check", normalized);
    }

    /// <summary>
    /// Ensures the runtime contract keeps fenced code unchanged while normalizing surrounding transcript text.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_DoesNotMutateFencedCode() {
        const string markdown = """
```text
1)First check
2)   Second check
```
""";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);

        Assert.Equal(markdown, normalized);
    }

    /// <summary>
    /// Ensures the runtime contract picks up newer shared transcript repairs such as split host bullets.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_RepairsHostLabelBulletArtifacts() {
        const string markdown = "-AD1\nhealthy for directory access";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);

        Assert.Equal("- AD1 healthy for directory access", normalized);
    }

    /// <summary>
    /// Ensures the runtime contract picks up shared two-line result lead-in repair.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_RepairsBrokenTwoLineStrongLeadIns() {
        const string markdown = """
**Result
all 5 are healthy for directory access** with recommended LDAPS endpoints.
""";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);

        Assert.Equal("**Result:** all 5 are healthy for directory access with recommended LDAPS endpoints.", normalized);
    }

    /// <summary>
    /// Ensures the runtime contract picks up shared collapsed ordered-list repairs from the OfficeIMO transcript preset.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_RepairsCollapsedOrderedListTranscriptArtifacts() {
        const string markdown = "1. **Privilege hygiene sweep**(Domain Admins + nested exposure)2.**Delegation risk audit**(unconstrained)";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);

        Assert.Equal("1. **Privilege hygiene sweep** (Domain Admins + nested exposure)\n2. **Delegation risk audit** (unconstrained)", normalized);
    }

    /// <summary>
    /// Ensures the runtime contract preserves empty normalization results instead of reviving stripped zero-width artifacts.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_ReturnsEmptyWhenTranscriptPresetStripsOnlyZeroWidthArtifacts() {
        const string markdown = "\u200B";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);

        Assert.Equal(string.Empty, normalized);
    }

    /// <summary>
    /// Ensures IX uses the shared OfficeIMO transcript normalization preset rather than maintaining a separate property-level bridge.
    /// </summary>
    [Fact]
    public void NormalizeForTranscriptCleanup_MatchesOfficeImoTranscriptPreset() {
        const string markdown = """
1)First check
-AD1
healthy for directory access
""";

        var normalized = OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(markdown);
        var expected = MarkdownInputNormalizer.Normalize(markdown, MarkdownInputNormalizationPresets.CreateIntelligenceXTranscript());

        Assert.Equal(expected, normalized);
    }
}
