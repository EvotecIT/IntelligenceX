using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for assistant action protocol extraction and visible-text normalization.
/// </summary>
public sealed class ActionModelProtocolTests {
    /// <summary>
    /// Ensures pending action protocol blocks are removed from visible text and converted to a concise action summary.
    /// </summary>
    [Fact]
    public void TryStripAndExtractPendingActions_StripsProtocolAndKeepsVisibleText() {
        const string text = """
                            I can retry this with safer defaults.

                            [Action]
                            ix:action:v1
                            id: act_ad0_sys_top5_bare
                            title: Retry AD0 System top 5 with bare defaults
                            request: Query AD0 System log for top 5 events using default output schema, then summarize key fields.
                            reply: /act act_ad0_sys_top5_bare
                            """;

        var normalized = ActionModelProtocol.TryStripAndExtractPendingActions(text, out var actions, out var cleaned);

        Assert.True(normalized);
        Assert.Single(actions);
        Assert.DoesNotContain("ix:action:v1", cleaned);
        Assert.DoesNotContain("[Action]", cleaned);
        Assert.Equal("act_ad0_sys_top5_bare", actions[0].Id);

        var visible = ActionModelProtocol.MergeVisibleTextWithPendingActions(cleaned, actions);
        Assert.Equal("I can retry this with safer defaults.", visible);
    }

    /// <summary>
    /// Ensures loose action blocks without the [Action]/marker envelope are still stripped and captured.
    /// </summary>
    [Fact]
    public void TryStripAndExtractPendingActions_StripsLooseActionBlockWithoutEnvelope() {
        const string text = """
                            id
                            act_repl_now
                            title
                            Run fresh AD replication summary now
                            request
                            Execute ad_replication_summary for current forest/domain scope and return current health, failed edges, stale links, and top replication errors.
                            reply
                            /act act_repl_now
                            """;

        var normalized = ActionModelProtocol.TryStripAndExtractPendingActions(text, out var actions, out var cleaned);

        Assert.True(normalized);
        Assert.Single(actions);
        Assert.Equal("act_repl_now", actions[0].Id);
        Assert.Equal(string.Empty, cleaned);
    }

    /// <summary>
    /// Ensures malformed action blocks are still stripped from visible text without exposing invalid follow-up actions.
    /// </summary>
    [Fact]
    public void TryStripAndExtractPendingActions_StripsMalformedBlocksWithoutCreatingActions() {
        const string text = """
                            We can try a recovery step.

                            [Action]
                            ix:action:v1
                            id: act_missing_reply
                            title: Retry with defaults
                            request: Retry now.

                            Done.
                            """;

        var normalized = ActionModelProtocol.TryStripAndExtractPendingActions(text, out var actions, out var cleaned);

        Assert.True(normalized);
        Assert.Empty(actions);
        Assert.DoesNotContain("ix:action:v1", cleaned);
        Assert.DoesNotContain("[Action]", cleaned);
        Assert.Contains("We can try a recovery step.", cleaned);
        Assert.Contains("Done.", cleaned);
    }

    /// <summary>
    /// Ensures fenced code boundary lines are preserved even when a malformed action marker was seen.
    /// </summary>
    [Fact]
    public void TryStripAndExtractPendingActions_KeepsFenceBoundariesWhenNoActionsExtracted() {
        const string text = """
                            Keep this code snippet:

                            ```json
                            { "status": "ok" }
                            ```

                            [Action]
                            ix:action:v1
                            id: act_invalid
                            title: Invalid action
                            request: Missing reply on purpose

                            Done.
                            """;

        var normalized = ActionModelProtocol.TryStripAndExtractPendingActions(text, out var actions, out var cleaned);

        Assert.True(normalized);
        Assert.Empty(actions);
        Assert.Contains("```json", cleaned);
        Assert.Contains("{ \"status\": \"ok\" }", cleaned);
        Assert.Contains("```", cleaned);
        Assert.DoesNotContain("ix:action:v1", cleaned);
        Assert.DoesNotContain("[Action]", cleaned);
        Assert.Contains("Done.", cleaned);
    }

    /// <summary>
    /// Ensures action markers inside fenced code blocks are treated as content and not parsed as executable actions.
    /// </summary>
    [Fact]
    public void TryStripAndExtractPendingActions_IgnoresProtocolInsideCodeFence() {
        const string text = """
                            Keep this snippet as-is:

                            ```text
                            [Action]
                            ix:action:v1
                            id: act_001
                            title: Demo action
                            request: Demo request
                            reply: /act act_001
                            ```
                            """;

        var normalized = ActionModelProtocol.TryStripAndExtractPendingActions(text, out var actions, out var cleaned);

        Assert.False(normalized);
        Assert.Empty(actions);
        Assert.Contains("```text", cleaned);
        Assert.Contains("ix:action:v1", cleaned);
        Assert.Contains("[Action]", cleaned);
    }

    /// <summary>
    /// Ensures overly large assistant payloads short-circuit parsing to keep extraction bounded.
    /// </summary>
    [Fact]
    public void TryStripAndExtractPendingActions_SkipsParsingWhenInputExceedsBound() {
        var oversized = new string('a', 70000);
        var text = oversized + """

                               [Action]
                               ix:action:v1
                               id: act_oversized
                               title: Oversized parse
                               request: Retry with defaults
                               reply: /act act_oversized
                               """;

        var normalized = ActionModelProtocol.TryStripAndExtractPendingActions(text, out var actions, out var cleaned);

        Assert.False(normalized);
        Assert.Empty(actions);
        Assert.Equal(text.Trim(), cleaned);
        Assert.Contains("ix:action:v1", cleaned);
    }
}
