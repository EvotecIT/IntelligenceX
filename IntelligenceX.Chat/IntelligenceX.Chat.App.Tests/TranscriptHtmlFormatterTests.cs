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
    /// Ensures repeated system rows never collapse metadata visibility.
    /// </summary>
    [Fact]
    public void Format_DoesNotHideMetaForSystemContinuations() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 17, 14, 22, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("System", "Restarting local runtime...", now),
            ("System", "Could not connect to local runtime.", now.AddSeconds(3))
        }, "yyyy-MM-dd HH:mm:ss", options);

        Assert.Contains("msg-row system", html);
        Assert.DoesNotContain("msg-row system cont", html);
        Assert.DoesNotContain("class='meta hidden'", html);
        Assert.Contains("System &middot; 2026-02-17 14:22:00", html);
        Assert.Contains("System &middot; 2026-02-17 14:22:03", html);
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
    /// Ensures system warnings use the same callout card styling as assistant outcomes.
    /// </summary>
    [Fact]
    public void Format_RendersSystemWarningAsCalloutCard() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 16, 18, 2, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("System", "[warning] Tool health checks need attention\n\nFound 1 startup warning.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("msg-row system", html);
        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-warn", html);
        Assert.Contains("outcome-badge'>Warning</span>", html);
        Assert.Contains("Tool health checks need attention", html);
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

    /// <summary>
    /// Ensures pending action markdown summary lines render as actionable chips instead of raw /act text lines.
    /// </summary>
    [Fact]
    public void Format_RendersPendingActionsAsActionChips() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 15, 20, 36, 14, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          You can run one of these follow-up actions:
                          1. Run failed logon report (4625) on ADO Security (`/act act_failed4625`)
                          2. Pull account lockout events (4740) (`/act act_lockout4740`)
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("ix-action-cta", html);
        Assert.Contains("class='ix-action-btn'", html);
        Assert.Contains("data-act-cmd='/act act_failed4625'", html);
        Assert.Contains("data-act-cmd='/act act_lockout4740'", html);
        Assert.DoesNotContain("You can run one of these follow-up actions:", html);
        Assert.DoesNotContain("`/act act_failed4625`", html);
    }

    /// <summary>
    /// Ensures /act-looking lines inside fenced code blocks are not converted into actionable chips.
    /// </summary>
    [Fact]
    public void Format_DoesNotExtractPendingActionsFromFencedCode() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 16, 8, 15, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Example script:
                          ~~~text
                          1. Dangerous demo (`/act act_danger`)
                          ~~~
                          You can run one of these follow-up actions:
                          1. Safe action (`/act act_safe`)
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("data-act-cmd='/act act_safe'", html);
        Assert.DoesNotContain("data-act-cmd='/act act_danger'", html);
    }

    /// <summary>
    /// Ensures inline backtick spans always end up as code tags in transcript HTML.
    /// </summary>
    [Fact]
    public void Format_AlwaysRendersInlineBackticksAsCodeTags() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 15, 23, 18, 6, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "Use `/act act_ad0_sys3210_msg` to run it.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("<code>/act act_ad0_sys3210_msg</code>", html);
        Assert.DoesNotContain("`/act act_ad0_sys3210_msg`", html);
    }

    /// <summary>
    /// Ensures common strong-emphasis phrases from assistant prose render as strong tags, not literal markers.
    /// </summary>
    [Fact]
    public void Format_RendersCommonStrongPhrasesWithoutLiteralMarkers() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 16, 13, 6, 34, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "If you want, I can run a **“Top 8 high-signal security pack”** now, or list only **GPO-related** reports.", now),
            ("Assistant", "Those were ** unresolved privileged SIDs** in a group sweep.", now.AddSeconds(1))
        }, "HH:mm:ss", options);

        Assert.Contains("<strong>“Top 8 high-signal security pack”</strong>", html);
        Assert.Contains("<strong>GPO-related</strong>", html);
        Assert.Contains("<strong>unresolved privileged SIDs</strong>", html);
        Assert.DoesNotContain("**“Top 8 high-signal security pack”**", html);
        Assert.DoesNotContain("**GPO-related**", html);
        Assert.DoesNotContain("** unresolved privileged SIDs**", html);
    }

    /// <summary>
    /// Ensures malformed compact ordered menu text is repaired before HTML rendering so literal markdown markers are not visible.
    /// </summary>
    [Fact]
    public void Format_RepairsCollapsedOrderedMenuWithoutLiteralMarkers() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 17, 13, 1, 40, DateTimeKind.Local);
        var text = "Love it 😄\nIf “oki doki” means *“we’re good for now”* — perfect.\n\nQuick next-step menu (pick one and I’ll run it right away):\n1) **Privilege hygiene sweep(Domain Admins + other privileged groups, nested exposure) 2)** Delegation risk audit**(unconstrained / constrained / protocol transition) 3)** Replication + DC health snapshot** (stale links, failing partners, LDAP/Kerberos basics)\n\nOr just say “done” and I’ll keep quiet like a well-configured service.";
        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);
        Assert.Contains("\n2. **Delegation risk audit** (unconstrained / constrained / protocol transition)", normalized);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<strong>Privilege hygiene sweep</strong>", html);
        Assert.Contains("<strong>Delegation risk audit</strong>", html);
        Assert.Contains("<strong>Replication + DC health snapshot</strong>", html);
        Assert.DoesNotContain("**", html);
    }

    /// <summary>
    /// Ensures display HTML repairs malformed AD comparison bullets and nested strong markers.
    /// </summary>
    [Fact]
    public void Format_RepairsAdComparisonBulletArtifactsForDisplayHtml() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 18, 19, 3, 10, DateTimeKind.Local);
        var text = "-AD1 starkes Muster\n-** AD2** eher Secure-Channel\n- Signal **AD1 has very high `7034/7023` volume, mostly from **Service Control Manager**.**";

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("AD1 starkes Muster", html);
        Assert.Contains("<strong>AD2</strong> eher Secure-Channel", html);
        Assert.Contains("Signal", html);
        Assert.Contains("7034/7023", html);
        Assert.Contains("from Service Control Manager.", html);
        Assert.DoesNotContain("-AD1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("from **Service", html, StringComparison.Ordinal);
        Assert.DoesNotContain("**.**", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures split host-label bullets render as proper list items when label and sentence arrive on separate lines.
    /// </summary>
    [Fact]
    public void Format_RepairsSplitHostLabelBulletsIntoRenderableListItems() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 19, 9, 7, 51, DateTimeKind.Local);
        var text = """
                   **Bewertung:**
                   Ja, es gibt auffällige Unterschiede (nicht symmetrisch):
                   -AD1
                   starkes Muster von Dienstabbrüchen/-fehlern (`7034/7023`).
                   -** AD2**
                   eher Secure-Channel/TLS/Policy/Power-Signale (`3210/1129/36874/41`).
                   """;

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<li>AD1", html, StringComparison.Ordinal);
        Assert.Contains("starkes Muster von Dienstabbr", html, StringComparison.Ordinal);
        Assert.Contains("7034/7023", html, StringComparison.Ordinal);
        Assert.Contains("<li><strong>AD2</strong>", html, StringComparison.Ordinal);
        Assert.Contains("3210/1129/36874/41", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-AD1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-** AD2**", html, StringComparison.Ordinal);
    }
}
