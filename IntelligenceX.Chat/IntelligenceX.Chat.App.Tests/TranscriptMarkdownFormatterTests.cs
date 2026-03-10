using System;
using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for transcript markdown export formatting.
/// </summary>
public sealed class TranscriptMarkdownFormatterTests {
    /// <summary>
    /// Ensures transcript entries render with role/time headings and message body.
    /// </summary>
    [Fact]
    public void Format_RendersRoleHeadingsAndBody() {
        var now = new DateTime(2026, 2, 11, 13, 45, 30, DateTimeKind.Local);
        var markdown = TranscriptMarkdownFormatter.Format(new[] {
            ("User", "hello", now),
            ("Assistant", "hi there", now.AddSeconds(8))
        }, "HH:mm:ss");

        Assert.Contains("### User (13:45:30)", markdown);
        Assert.Contains("### Assistant (13:45:38)", markdown);
        Assert.Contains("hello", markdown);
        Assert.Contains("hi there", markdown);
    }

    /// <summary>
    /// Ensures markdown export normalizes list-marker artifacts for strict markdown renderers.
    /// </summary>
    [Fact]
    public void Format_NormalizesParenOrderedListMarkers() {
        var now = new DateTime(2026, 2, 11, 13, 45, 30, DateTimeKind.Local);
        var markdown = TranscriptMarkdownFormatter.Format(new[] {
            ("Assistant", "1) First check\n2) Second check", now)
        }, "HH:mm:ss");

        Assert.Contains("1. First check", markdown);
        Assert.Contains("2. Second check", markdown);
        Assert.DoesNotContain("1) First check", markdown);
    }

    /// <summary>
    /// Ensures markdown export applies the same adjacent ordered-list spacing repair used by transcript rendering.
    /// </summary>
    [Fact]
    public void Format_InsertsBlankLineBetweenAdjacentOrderedItems() {
        var now = new DateTime(2026, 3, 10, 10, 22, 14, DateTimeKind.Local);
        var markdown = TranscriptMarkdownFormatter.Format(new[] {
            ("Assistant", "1. First check\n2. Second check", now)
        }, "HH:mm:ss").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("1. First check\n\n2. Second check", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures markdown export also benefits from the shared typography cleanup used by DOCX export.
    /// </summary>
    [Fact]
    public void Format_NormalizesSharedSignalFlowTypographyArtifacts() {
        var now = new DateTime(2026, 3, 10, 10, 30, 0, DateTimeKind.Local);
        var markdown = TranscriptMarkdownFormatter.Format(new[] {
            ("Assistant", "- Signal **Only total count checked, not origin split -> **Why it matters:**external/custom rules can drift ->**Next action:**break down origins.**\n- TestimoX rules available ****359****", now)
        }, "HH:mm:ss");

        Assert.Contains("**Only total count checked, not origin split** -> **Why it matters:** external/custom rules can drift", markdown, StringComparison.Ordinal);
        Assert.Contains("**Next action:** break down origins.", markdown, StringComparison.Ordinal);
        Assert.Contains("**359**", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("****359****", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures assistant transcript exports include a model comment for per-turn provenance.
    /// </summary>
    [Fact]
    public void Format_IncludesAssistantModelCommentWhenProvided() {
        var now = new DateTime(2026, 2, 20, 19, 12, 7, DateTimeKind.Local);
        var markdown = TranscriptMarkdownFormatter.Format(new[] {
            ("Assistant", "Ready.", now, (string?)"ibm/granite-4-h-tiny")
        }, "HH:mm:ss");

        Assert.Contains("### Assistant (19:12:07)", markdown);
        Assert.Contains("<!-- ix:model: ibm/granite-4-h-tiny -->", markdown);
    }

    /// <summary>
    /// Ensures model comment text does not terminate the enclosing HTML comment.
    /// </summary>
    [Fact]
    public void Format_EscapesModelCommentTerminators() {
        var now = new DateTime(2026, 2, 20, 19, 13, 7, DateTimeKind.Local);
        var markdown = TranscriptMarkdownFormatter.Format(new[] {
            ("Assistant", "Ready.", now, (string?)"model->beta--x")
        }, "HH:mm:ss");

        Assert.Contains("<!-- ix:model: model-&gt;beta- -x -->", markdown);
    }
}
