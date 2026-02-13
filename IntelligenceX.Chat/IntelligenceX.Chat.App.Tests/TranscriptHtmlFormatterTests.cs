using System;
using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for transcript HTML rendering.
/// </summary>
public sealed class TranscriptHtmlFormatterTests {
    /// <summary>
    /// Ensures rows, role styling, continuation classes, and copy indexes are rendered.
    /// </summary>
    [Fact]
    public void Format_RendersRoleRowsAndContinuationMarkers() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 11, 18, 45, 31, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "first", now),
            ("Assistant", "second", now.AddSeconds(2)),
            ("User", "hello", now.AddSeconds(3)),
            ("Tools", "tool output", now.AddSeconds(4)),
            ("System", "notice", now.AddSeconds(5))
        }, "HH:mm:ss", options);

        Assert.Contains("msg-row assistant", html);
        Assert.Contains("msg-row assistant cont", html);
        Assert.Contains("msg-row user", html);
        Assert.Contains("msg-row tools", html);
        Assert.Contains("msg-row system", html);
        Assert.Contains("class='meta hidden'", html);
        Assert.Contains("data-msg-index='0'", html);
        Assert.Contains("data-msg-index='1'", html);
        Assert.Contains("data-msg-index='2'", html);
        Assert.Contains("data-msg-index='3'", html);
        Assert.Contains("data-msg-index='4'", html);
        Assert.Contains("IntelligenceX &middot; 18:45:31", html);
        Assert.Contains("You &middot; 18:45:34", html);
    }

    /// <summary>
    /// Ensures blank transcript text is skipped while preserving index progression.
    /// </summary>
    [Fact]
    public void Format_SkipsBlankMessagesButKeepsIndexProgression() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 11, 19, 0, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "a", now),
            ("Assistant", "   ", now.AddSeconds(1)),
            ("Assistant", "b", now.AddSeconds(2))
        }, "HH:mm:ss", options);

        Assert.Contains("data-msg-index='0'", html);
        Assert.Contains("data-msg-index='2'", html);
        Assert.DoesNotContain("data-msg-index='1'", html);
    }

    /// <summary>
    /// Ensures assistant error outcomes render as structured callout cards.
    /// </summary>
    [Fact]
    public void Format_RendersAssistantErrorAsCalloutCard() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 11, 19, 10, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "[error] Chat failed\n\nChatGPT usage limit reached. Try again later.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-error", html);
        Assert.Contains("outcome-badge'>Error</span>", html);
        Assert.Contains("outcome-title'>Chat failed</span>", html);
        Assert.Contains("usage limit reached", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures transcript rendering normalizes common token-join artifacts before markdown conversion.
    /// </summary>
    [Fact]
    public void Format_NormalizesCommonMarkdownSpacingArtifacts() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 13, 19, 25, 24, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "✅I can run 1) LDAP checks, or2) cert checks.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("✅ I", html);
        Assert.Contains("or 2)", html);
        Assert.DoesNotContain("✅I", html);
        Assert.DoesNotContain("or2)", html);
    }

    /// <summary>
    /// Ensures malformed collapsed status metrics render as proper bullet rows rather than literal markdown markers.
    /// </summary>
    [Fact]
    public void Format_RepairsCollapsedStatusMetricMarkdownBeforeRender() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 13, 19, 25, 24, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "**Status: HEALTHY** - **Servers checked:**5 -**Replication edges:**62 -*Failed edges:**0 -*Stale edges (>24h):**0 - **Servers with failures:**0", now)
        }, "HH:mm:ss", options);

        Assert.Contains("Status <strong>HEALTHY</strong>", html);
        Assert.Contains("<li>Servers checked <strong>5</strong></li>", html);
        Assert.Contains("<li>Replication edges <strong>62</strong></li>", html);
        Assert.Contains("<li>Failed edges <strong>0</strong></li>", html);
        Assert.Contains("<li>Stale edges (&gt;24h) <strong>0</strong></li>", html);
        Assert.Contains("<li>Servers with failures <strong>0</strong></li>", html);
        Assert.DoesNotContain("**Servers checked:**", html);
        Assert.DoesNotContain("**Replication edges:**", html);
    }
}
