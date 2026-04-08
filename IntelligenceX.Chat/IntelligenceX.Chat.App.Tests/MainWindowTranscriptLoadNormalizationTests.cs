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

    /// <summary>
    /// Ensures assistant transcript preparation strips leaked runtime-only metadata and repairs collapsed Mermaid fences before display.
    /// </summary>
    [Fact]
    public void PrepareMessageBody_ForAssistant_StripsRuntimeMetadataAndRepairsMermaidFence() {
        const string input = """
            ```ix_memory{"upserts":[{"fact":"User likes visuals"}]}
            ```[Answer progression plan]
            ix: answer-plan: v1user_goal: show the topologyadvance_reason: provide a usable diagramJasne — tu masz diagram.

            ~~~mermaidflowchart LR F["Forest<br/>ad.evotec.xyz"]
            D1["Domain<br/>ad.evotec.xyz"]
            F --> D1```
            Interpretacja: to jest diagram struktury.
            """;

        var prepared = TranscriptMarkdownPreparation.PrepareMessageBody("Assistant", input)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ix_memory", prepared, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answer-plan", prepared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jasne — tu masz diagram.", prepared, StringComparison.Ordinal);
        Assert.Contains("```mermaid\nflowchart LR", prepared, StringComparison.Ordinal);
        Assert.Contains("F --> D1\n```", prepared, StringComparison.Ordinal);
        Assert.Contains("Interpretacja: to jest diagram struktury.", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures user-authored control-looking markdown remains untouched when explicitly rendered as a user message.
    /// </summary>
    [Fact]
    public void PrepareMessageBody_ForUser_PreservesUserAuthoredStructuredBlocks() {
        const string input = """
            ```ix_memory
            {"upserts":[{"fact":"keep this visible"}]}
            ```
            """;

        var prepared = TranscriptMarkdownPreparation.PrepareMessageBody("User", input)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("```ix_memory", prepared, StringComparison.Ordinal);
        Assert.Contains("keep this visible", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures assistant streaming previews also suppress leaked runtime metadata while partial text is still arriving.
    /// </summary>
    [Fact]
    public void PrepareStreamingPreview_StripsLeakedAnswerPlanArtifacts() {
        const string input = """
            [Answer progression plan]
            ix: answer-plan: v1
            user_goal: show the topology
            advance_reason: provide a useful diagram

            Jasne — tworzę diagram.
            """;

        var preview = TranscriptMarkdownPreparation.PrepareStreamingPreview(input);

        Assert.DoesNotContain("answer-plan", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jasne — tworzę diagram.", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures common compact Mermaid examples are repaired to the newline-separated syntax required by the bundled runtime.
    /// </summary>
    [Fact]
    public void PrepareMessageBody_ForAssistant_SplitsCompactMermaidDirectiveLine() {
        const string input = """
            ```mermaid
            flowchart LR U[Zakres wejściowy] --> A[AD / DNS discovery]
            A --> B[Hosty i role]
            ```
            """;

        var prepared = TranscriptMarkdownPreparation.PrepareMessageBody("Assistant", input)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("flowchart LR\nU[Zakres wejściowy] --> A[AD / DNS discovery]", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures collapsed Mermaid subgraph endings and repeated edge statements are split back onto separate lines.
    /// </summary>
    [Fact]
    public void PrepareMessageBody_ForAssistant_SplitsCollapsedMermaidStatements() {
        const string input = """
            ```mermaid
            flowchart LR
            subgraph SITE1["Default-First-Site"]
             DC1["DC1"]
             DC2["DC2"]
             end subgraph SITE2["Branch-Site"]
             DC3["DC3"]
             end DC1 -->|RPC OK\nlast success:2025-02-24T08:15:00Z UTC| DC2 DC2 -->|RPC OK\nlast success:2025-02-24T08:14:32Z UTC| DC3 DC3 -->|FAIL172\nlast attempt:2025-02-24T08:10:11Z UTC| DC1
            ```
            """;

        var prepared = TranscriptMarkdownPreparation.PrepareMessageBody("Assistant", input)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("DC2[\"DC2\"]\n end\n subgraph SITE2[\"Branch-Site\"]", prepared, StringComparison.Ordinal);
        Assert.Contains("DC3[\"DC3\"]\n end\n DC1 -->|RPC OK\\nlast success:2025-02-24T08:15:00Z UTC| DC2", prepared, StringComparison.Ordinal);
        Assert.Contains("DC2 -->|RPC OK\\nlast success:2025-02-24T08:14:32Z UTC| DC3", prepared, StringComparison.Ordinal);
        Assert.Contains("DC3 -->|FAIL172\\nlast attempt:2025-02-24T08:10:11Z UTC| DC1", prepared, StringComparison.Ordinal);
    }
}
