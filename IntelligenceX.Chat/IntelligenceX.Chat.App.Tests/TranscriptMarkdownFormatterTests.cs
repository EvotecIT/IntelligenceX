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
}
