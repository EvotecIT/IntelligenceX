using System;
using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers App transcript normalization entrypoints used when conversations are loaded or previews stream in.
/// </summary>
public sealed class MainWindowTranscriptLoadNormalizationTests {
    /// <summary>
    /// Ensures assistant transcript artifacts are normalized before they are re-persisted.
    /// </summary>
    [Fact]
    public void NormalizePersistedTranscriptText_RepairsAssistantArtifactsBeforePersistence() {
        var input = "- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers*";

        var repaired = TranscriptMarkdownPreparation.NormalizePersistedTranscriptText("Assistant", input, out var wasRepaired);

        Assert.True(wasRepaired);
        Assert.Equal("- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers**", repaired);
    }

    /// <summary>
    /// Ensures assistant transcript load repair now applies the same shared ordered-list body preparation used by rendering.
    /// </summary>
    [Fact]
    public void NormalizePersistedTranscriptText_RepairsAssistantOrderedListSpacingBeforePersistence() {
        const string input = """
            1. First check
            2. Second check
            """;

        var repaired = TranscriptMarkdownPreparation.NormalizePersistedTranscriptText("Assistant", input, out var wasRepaired)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(wasRepaired);
        Assert.Contains("1. First check\n\n2. Second check", repaired, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures user-authored markdown is preserved when loading persisted transcripts.
    /// </summary>
    [Fact]
    public void NormalizePersistedTranscriptText_DoesNotRewriteUserMarkdown() {
        var input = "- LDAP/LDAPS across all DCs **healthy on FQDN endpoints for all 5 servers*";

        var repaired = TranscriptMarkdownPreparation.NormalizePersistedTranscriptText("User", input, out var wasRepaired);

        Assert.False(wasRepaired);
        Assert.Equal(input, repaired);
    }

    /// <summary>
    /// Ensures user-authored markdown is preserved even when it matches explicit transcript-repair patterns.
    /// </summary>
    [Fact]
    public void NormalizePersistedTranscriptText_DoesNotRewriteUserMarkdownWhenOfficeImoTranscriptCleanupWouldChangeIt() {
        var input = """
                    [Cached evidence fallback]
                    ix:cached-tool-evidence:v1

                    ```json
                    {"nodes":[{"id":"A","label":"Forest: ad.evotec.xyz"}],"edges":[{"source":"forest_ad.evotec.xyz","target":"domain_ad.evotec.xyz","label":"contains"}]}
                    ```
                    """;

        var repaired = TranscriptMarkdownPreparation.NormalizePersistedTranscriptText("User", input, out var wasRepaired);

        Assert.False(wasRepaired);
        Assert.Equal(input, repaired);
    }

    /// <summary>
    /// Ensures streaming-preview normalization stays reachable through the App entrypoint instead of direct normalizer calls.
    /// </summary>
    [Fact]
    public void PrepareStreamingPreview_RepairsSignalFlowTypographyArtifacts() {
        var input = string.Join('\n', [
            "- Signal **Catalog count includes hidden/disabled/deprecated rules -> **Why it matters:**external/custom rules can drift or disappear between hosts ->**Next action:**break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.**",
            "- TestimoX rules available ****359****"
        ]);

        var preview = TranscriptMarkdownPreparation.PrepareStreamingPreview(input);

        var expected = string.Join('\n', [
            "- Signal **Catalog count includes hidden/disabled/deprecated rules** -> **Why it matters:** external/custom rules can drift or disappear between hosts -> **Next action:** break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.",
            "- TestimoX rules available **359**"
        ]);

        Assert.Equal(expected, preview);
    }
}
