using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the optional OfficeIMO markdown input-normalizer runtime seam used during transcript cleanup.
/// </summary>
public sealed class OfficeImoMarkdownInputNormalizationRuntimeContractTests {
    /// <summary>
    /// Ensures the runtime contract applies OfficeIMO ordered-list normalization when a compatible runtime is present.
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
}
