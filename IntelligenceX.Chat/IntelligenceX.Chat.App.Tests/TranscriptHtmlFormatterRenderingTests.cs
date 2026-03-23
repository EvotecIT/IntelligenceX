using System;
using System.IO;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for transcript HTML rendering that focus on normalization, callouts, and action extraction.
/// </summary>
public sealed partial class TranscriptHtmlFormatterTests {
    /// <summary>
    /// Ensures assistant error outcomes render as structured callout cards.
    /// </summary>
    [Fact]
    public void Format_RendersAssistantErrorAsCalloutCard() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
    /// Ensures startup-prefixed system messages render as structured callout cards instead of raw bracket tags.
    /// </summary>
    [Fact]
    public void Format_RendersStartupSystemMessageAsCalloutCard() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 8, 2, 39, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("System", "[startup] Runtime tool bootstrap summary\n\n- Total: 9.6s\n- Packs loaded: 10, disabled: 1, tools: 187", now)
        }, "HH:mm:ss", options);

        Assert.Contains("msg-row system", html);
        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-neutral outcome-kind-startup outcome-role-system", html);
        Assert.Contains("outcome-badge'>Startup</span>", html);
        Assert.Contains("Runtime tool bootstrap summary", html);
        Assert.Contains("Packs loaded", html);
    }

    /// <summary>
    /// Ensures cached evidence fallback headers render as structured callouts with readable body content.
    /// </summary>
    [Fact]
    public void Format_RendersCachedEvidenceFallbackAsCalloutCard() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 8, 12, 36, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "[Cached evidence fallback]\nix:cached-tool-evidence:v1\n\n#### ad_environment_discover\n### Active Directory: Environment Discovery", now)
        }, "HH:mm:ss", options);

        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-neutral outcome-kind-cached-evidence-fallback outcome-role-assistant", html);
        Assert.Contains("outcome-badge'>Cached</span>", html);
        Assert.DoesNotContain("ix:cached-tool-evidence:v1", html);
        Assert.Contains("Active Directory: Environment Discovery", html);
    }

    /// <summary>
    /// Ensures historical cached-evidence tool slug bullet headings are normalized away during transcript rendering.
    /// </summary>
    [Fact]
    public void Format_NormalizesLegacyCachedEvidenceToolHeadingBullets() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 8, 12, 36, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "[Cached evidence fallback]\nix:cached-tool-evidence:v1\n\nRecent evidence:\n- eventlog_top_events: ### Top 30 recent events (preview)", now)
        }, "HH:mm:ss", options);

        Assert.Contains("Top 30 recent events (preview)", html);
        Assert.DoesNotContain("eventlog_top_events:", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures hyphenated cached-evidence prefixes normalize into the structured cached callout path.
    /// </summary>
    [Fact]
    public void Format_RendersHyphenatedCachedEvidenceFallbackPrefixAsCalloutCard() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 8, 12, 36, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "[cached-evidence-fallback]\nix:cached-tool-evidence:v1\n\nCached tool output reused.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-neutral outcome-kind-cached-evidence-fallback outcome-role-assistant", html);
        Assert.Contains("outcome-badge'>Cached</span>", html);
        Assert.DoesNotContain("ix:cached-tool-evidence:v1", html);
        Assert.Contains("Cached tool output reused.", html);
    }

    /// <summary>
    /// Ensures transcript rendering normalizes common token-join artifacts before markdown conversion.
    /// </summary>
    [Fact]
    public void Format_NormalizesCommonMarkdownSpacingArtifacts() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
    /// Ensures transcript rendering applies the shared adjacent ordered-list spacing repair before HTML conversion.
    /// </summary>
    [Fact]
    public void Format_RendersAdjacentOrderedItemsAsSeparateListEntries() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 10, 10, 22, 14, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "1. First check\n2. Second check", now)
        }, "HH:mm:ss", options);

        Assert.Contains("<li>First check</li>", html, StringComparison.Ordinal);
        Assert.Contains("<li>Second check</li>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures malformed collapsed status metrics render as proper bullet rows rather than literal markdown markers.
    /// </summary>
    [Fact]
    public void Format_RepairsCollapsedStatusMetricMarkdownBeforeRender() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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

    private static int CountOccurrences(string value, string token) {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string ReadOfficeImoHtmlFixture(string fileName) {
        string path = Path.Combine(GetOfficeImoTestsRoot(), "Markdown", "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    private static string GetOfficeImoTestsRoot() {
        string testsProjectRoot = GetAppTestsProjectRoot();
        string[] fallbackCandidates = new[] {
            Path.GetFullPath(Path.Combine(testsProjectRoot, "..", "..", "..", "OfficeIMO", "OfficeIMO.Tests")),
            Path.GetFullPath(Path.Combine(testsProjectRoot, "..", "..", "..", "..", "OfficeIMO", "OfficeIMO.Tests"))
        };

        for (int i = 0; i < fallbackCandidates.Length; i++) {
            string candidate = fallbackCandidates[i];
            if (File.Exists(Path.Combine(candidate, "OfficeIMO.Tests.csproj"))) {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate OfficeIMO.Tests project root from IntelligenceX.Chat.App.Tests.");
    }

    private static string GetAppTestsProjectRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) {
            string candidate = Path.Combine(dir.FullName, "IntelligenceX.Chat.App.Tests.csproj");
            if (File.Exists(candidate)) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IntelligenceX.Chat.App.Tests project root from test runtime base directory.");
    }

    /// <summary>
    /// Ensures pending action markdown summary lines render as actionable chips instead of raw /act text lines.
    /// </summary>
    [Fact]
    public void Format_RendersPendingActionsAsActionChips() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
    /// Ensures stale historical replication summaries do not surface literal separator headings or dangling strong markers.
    /// </summary>
    [Fact]
    public void Format_RepairsHistoricalReplicationSummaryArtifacts() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 18, 6, 28, DateTimeKind.Local);
        var text = """
                   Nice—forest replication check is clean. No zombies escaped the tomb. 🧟‍♂️

                   #

                   ### Forest Replication Status
                   - Overall health ✅ Healthy****
                   - Replication edges 44 total
                   - Failures 0
                   """;
        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.DoesNotContain(
            normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'),
            static line => string.Equals(line.Trim(), "#", StringComparison.Ordinal));
        Assert.DoesNotContain(">#<", html, StringComparison.Ordinal);
        Assert.Contains("Forest Replication Status", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Healthy</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Healthy****", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures broken two-line strong result labels are folded into one readable line before rendering.
    /// </summary>
    [Fact]
    public void Format_RepairsBrokenTwoLineStrongResultLabelArtifacts() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 18, 8, 40, DateTimeKind.Local);
        var text = """
                   ## 2) LDAP/LDAPS check on all 5 servers
                   **Result
                   all 5 are healthy for directory access** with recommended LDAPS endpoints.
                   """;

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<strong>Result:</strong>", html, StringComparison.Ordinal);
        Assert.Contains("all 5 are healthy for directory access", html, StringComparison.Ordinal);
        Assert.DoesNotContain("**Result", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures split host-label bullets render as proper list items when label and sentence arrive on separate lines.
    /// </summary>
    [Fact]
    public void Format_RepairsSplitHostLabelBulletsIntoRenderableListItems() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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

    /// <summary>
    /// Ensures unicode-dash bullets are normalized before markdown render so list HTML is stable.
    /// </summary>
    [Fact]
    public void Format_NormalizesUnicodeDashBulletsForDisplayHtml() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 19, 10, 12, 0, DateTimeKind.Local);
        var text = "—** AD2** eher Secure-Channel/TLS";

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<li><strong>AD2</strong> eher Secure-Channel/TLS</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("—** AD2**", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures live streaming preview plus transcript HTML rendering repairs tight signal-flow labels and overwrapped strong spans.
    /// </summary>
    [Fact]
    public void Format_StreamingPreviewPipelineRepairsSignalTypographyArtifacts() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 20, 11, 8, 0, DateTimeKind.Local);
        var raw = string.Join('\n', [
            "- Signal **Catalog count includes hidden/disabled/deprecated rules -> **Why it matters:**external/custom rules can drift or disappear between hosts ->**Next action:**break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.**",
            "- TestimoX rules available ****359****"
        ]);

        var preview = TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(raw);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", preview, now)
        }, "HH:mm:ss", options);

        Assert.Contains("**Why it matters:** external/custom rules can drift", preview, StringComparison.Ordinal);
        Assert.Contains("**Next action:** break down `rule_origin`", preview, StringComparison.Ordinal);
        Assert.Contains("TestimoX rules available **359**", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("****359****", preview, StringComparison.Ordinal);

        Assert.Contains("<strong>Catalog count includes hidden/disabled/deprecated rules</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Why it matters:</strong> external/custom rules can drift or disappear between hosts", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Next action:</strong> break down <code>rule_origin</code> (<code>builtin</code> vs <code>external</code>)", html, StringComparison.Ordinal);
        Assert.Contains("TestimoX rules available <strong>359</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</strong>external/custom", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</strong>break down", html, StringComparison.Ordinal);
    }

    private static void AssertIxChartAliasRendersAsNativeChartVisual(string html) {
        Assert.True(
            html.Contains("data-omd-visual-kind=\"chart\"", StringComparison.Ordinal),
            "Expected ix-chart alias composition to produce a native chart visual.");
        Assert.Contains("omd-visual", html, StringComparison.Ordinal);
        Assert.Contains("omd-chart", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-visual-contract=\"v1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-config-encoding=\"base64-utf8\"", html, StringComparison.Ordinal);
    }

    private static void AssertIxNetworkAliasRendersAsNativeNetworkVisual(string html) {
        Assert.True(
            html.Contains("data-omd-visual-kind=\"network\"", StringComparison.Ordinal),
            "Expected ix-network alias composition to produce a native network visual.");
        Assert.Contains("omd-visual", html, StringComparison.Ordinal);
        Assert.Contains("omd-network", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-visual-contract=\"v1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-config-encoding=\"base64-utf8\"", html, StringComparison.Ordinal);
    }

}
