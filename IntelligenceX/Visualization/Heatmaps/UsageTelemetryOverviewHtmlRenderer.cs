using System;
using System.Text;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;
namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Renders a bundled HTML report for telemetry usage overviews.
/// </summary>
internal static class UsageTelemetryOverviewHtmlRenderer {
    public static string Render(
        UsageTelemetryOverviewDocument overview,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary = null,
        GitCodeChurnSummaryData? gitCodeChurnSummary = null) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(overview, gitHubObservabilitySummary, gitCodeChurnSummary);
        var sb = new StringBuilder(24 * 1024);
        UsageTelemetryReportPageShellHtmlRenderer.AppendOverviewHeader(sb, page);
        UsageTelemetryReportDiagnosticsHtmlRenderer.Append(sb, page.Diagnostics);
        if (page.ConversationPulse is not null) {
            AppendConversationPulseSection(sb, page.ConversationPulse);
        }
        if (page.CodeChurn is not null) {
            AppendCodeChurnSection(sb, page.CodeChurn);
        }
        if (page.ChurnUsageCorrelation is not null) {
            AppendChurnUsageCorrelationSection(sb, page.ChurnUsageCorrelation);
        }
        if (page.GitHubLocalAlignment is not null) {
            AppendGitHubLocalAlignmentSection(sb, page.GitHubLocalAlignment);
        }
        if (page.GitHubRepoClusters is not null) {
            AppendGitHubRepoClusterSection(sb, page.GitHubRepoClusters);
        }

        foreach (var providerSection in page.Sections) {
            AppendProviderSection(sb, providerSection);
        }

        UsageTelemetrySupportingBreakdownHtmlRenderer.AppendSection(sb, page.SupportingBreakdowns);
        UsageTelemetryReportPageShellHtmlRenderer.AppendFootnote(sb, page.Footnote);
        return UsageTelemetryReportStaticAssets.RenderOverviewPage(
            page.Title,
            sb.ToString(),
            page.BootstrapJson);
    }

    private static void AppendProviderSection(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        UsageTelemetryProviderSectionHtmlRenderer.AppendSection(sb, model);
    }

    private static void AppendConversationPulseSection(StringBuilder sb, UsageTelemetryConversationPulsePageModel model) {
        sb.AppendLine("    <section class=\"provider-section conversation-pulse-section\" id=\"conversation-usage\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight conversation-insights\">");
        sb.AppendLine("          <aside class=\"conversation-summary-panel\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Raw sessions</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </aside>");
        AppendConversationRows(sb, model);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendConversationRows(StringBuilder sb, UsageTelemetryConversationPulsePageModel model) {
        sb.AppendLine("          <article class=\"conversation-list-card\">");
        sb.Append("            <div class=\"provider-feature-kicker\">").Append(Html(model.Conversations.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Conversations.Headline)) {
            sb.Append("            <div class=\"conversation-list-headline\">").Append(Html(model.Conversations.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(model.Conversations.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Conversations.Note!)).AppendLine("</div>");
        }

        if (model.Rows.Count == 0) {
            sb.AppendLine("            <div class=\"estimate-note\">No detailed rows available.</div>");
        } else {
            sb.AppendLine("            <div class=\"conversation-toolbar\">");
            sb.AppendLine("              <label class=\"conversation-search\" aria-label=\"Search conversations\">");
            sb.AppendLine("                <span class=\"conversation-search-label\">Search</span>");
            sb.AppendLine("                <input type=\"search\" class=\"conversation-search-input\" data-conversation-search name=\"conversation-search\" placeholder=\"Search title, repo, workspace, account, or session...\" autocomplete=\"off\" spellcheck=\"false\">");
            sb.AppendLine("              </label>");
            sb.AppendLine("              <div class=\"conversation-toolbar-actions\">");
            sb.Append("                <div class=\"conversation-search-count\" data-conversation-count>")
                .Append(Html(model.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " visible"))
                .AppendLine("</div>");
            sb.AppendLine("                <button type=\"button\" class=\"conversation-reset-button hidden\" data-conversation-reset aria-label=\"Clear conversation search and filters\">Clear All</button>");
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
            AppendConversationQuickFilters(sb, model.Rows);
            sb.AppendLine("            <div class=\"conversation-active-bar\" data-conversation-active>");
            sb.AppendLine("              <div class=\"conversation-active-label\">Current view</div>");
            sb.AppendLine("              <div class=\"conversation-active-copy\" data-conversation-active-text>Showing all conversations by token share.</div>");
            sb.AppendLine("              <div class=\"conversation-active-chips\" data-conversation-active-chips></div>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"conversation-snapshot-grid\" data-conversation-snapshot>");
            AppendConversationSnapshotCard(sb, "Conversations", "0", "No conversations loaded yet.", "count");
            AppendConversationSnapshotCard(sb, "Tokens In View", "0", "Token share for the current slice.", "tokens");
            AppendConversationSnapshotCard(sb, "Cost In View", "$0", "Estimated and exact cost in this slice.", "cost");
            AppendConversationSnapshotCard(sb, "Average Span", "n/a", "Average session length in this slice.", "duration");
            AppendConversationSnapshotCard(sb, "Top Context", "No repo or workspace", "No conversation context is available yet.", "context");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"conversation-context-shell\" data-conversation-context-shell>");
            sb.AppendLine("              <div class=\"conversation-context-header\">");
            sb.AppendLine("                <div>");
            sb.AppendLine("                  <div class=\"conversation-context-kicker\">Context Breakdown</div>");
            sb.AppendLine("                  <div class=\"conversation-context-copy\" data-conversation-context-copy>Which repo or workspace dominates the current slice.</div>");
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"conversation-context-lenses\" role=\"tablist\" aria-label=\"Context breakdown lens\">");
            AppendConversationContextLensTab(sb, "tokens", "Tokens", true);
            AppendConversationContextLensTab(sb, "cost", "Cost", false);
            AppendConversationContextLensTab(sb, "count", "Conversations", false);
            sb.AppendLine("                </div>");
            sb.AppendLine("              </div>");
            sb.AppendLine("              <div class=\"conversation-context-list\" data-conversation-context-list>");
            sb.AppendLine("                <div class=\"conversation-context-empty\">No repo or workspace data yet.</div>");
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"conversation-sort-toolbar\" role=\"tablist\" aria-label=\"Conversation sorting\">");
            AppendConversationSortTab(sb, "tokens", "Tokens", true);
            AppendConversationSortTab(sb, "cost", "Cost", false);
            AppendConversationSortTab(sb, "duration", "Duration", false);
            AppendConversationSortTab(sb, "turns", "Turns", false);
            AppendConversationSortTab(sb, "compacts", "Compacts", false);
            sb.AppendLine("            </div>");
            sb.AppendLine("            <div class=\"conversation-explorer\">");
            sb.AppendLine("              <div class=\"conversation-list\">");
            foreach (var row in model.Rows) {
                sb.Append("                <button type=\"button\" class=\"conversation-row");
                if (row.Rank == 1) {
                    sb.Append(" active");
                }
                sb.AppendLine("\" data-conversation-button");
                AppendConversationRowDataset(sb, "rank", "#" + row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AppendConversationRowDataset(sb, "title", row.TitleText);
                AppendConversationRowDataset(sb, "sessionLabel", row.SessionLabel);
                AppendConversationRowDataset(sb, "sessionCode", row.SessionCode);
                AppendConversationRowDataset(sb, "tokens", row.TokenText);
                AppendConversationRowDataset(sb, "share", row.ShareText);
                AppendConversationRowDataset(sb, "started", row.StartedText);
                AppendConversationRowDataset(sb, "span", row.SpanText);
                AppendConversationRowDataset(sb, "active", row.ActiveText);
                AppendConversationRowDataset(sb, "turns", row.TurnText);
                AppendConversationRowDataset(sb, "compacts", row.CompactText);
                AppendConversationRowDataset(sb, "cost", row.CostText);
                AppendConversationRowDataset(sb, "context", row.ContextText);
                AppendConversationRowDataset(sb, "repository", row.RepositoryText);
                AppendConversationRowDataset(sb, "workspace", row.WorkspaceText);
                AppendConversationRowDataset(sb, "account", row.AccountText);
                AppendConversationRowDataset(sb, "model", row.ModelText);
                AppendConversationRowDataset(sb, "surface", row.SurfaceText);
                AppendConversationRowDataset(sb, "sortTokens", row.TotalTokensRaw.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AppendConversationRowDataset(sb, "sortCost", row.CostUsdRaw.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
                AppendConversationRowDataset(sb, "sortDuration", row.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AppendConversationRowDataset(sb, "sortTurns", row.TurnCountRaw.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AppendConversationRowDataset(sb, "sortCompacts", row.CompactCountRaw.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("                  aria-pressed=\"" + (row.Rank == 1 ? "true" : "false") + "\">");
                sb.Append("                  <div class=\"conversation-rank\">#").Append(row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine("</div>");
                sb.AppendLine("                  <div class=\"conversation-main\">");
                sb.AppendLine("                    <div class=\"conversation-row-head\">");
                sb.AppendLine("                      <div class=\"conversation-title-block\">");
                sb.Append("                        <div class=\"conversation-title\"><span>").Append(Html(row.TitleText)).AppendLine("</span></div>");
                sb.Append("                        <div class=\"conversation-session\">").Append(Html(row.SessionLabel)).Append(" <code>")
                    .Append(Html(row.SessionCode)).AppendLine("</code></div>");
                sb.AppendLine("                      </div>");
                sb.AppendLine("                      <div class=\"conversation-tokens\">");
                sb.Append("                        <strong>").Append(Html(row.TokenText)).AppendLine("</strong>");
                sb.Append("                        <span>").Append(Html(row.ShareText)).AppendLine("</span>");
                sb.AppendLine("                      </div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"conversation-chips\">");
                if (!string.IsNullOrWhiteSpace(row.RepositoryText)) {
                    AppendConversationChip(sb, row.RepositoryText!);
                }
                if (!string.IsNullOrWhiteSpace(row.WorkspaceText) &&
                    !string.Equals(row.WorkspaceText, row.RepositoryText, StringComparison.OrdinalIgnoreCase)) {
                    AppendConversationChip(sb, row.WorkspaceText!);
                }
                if (string.IsNullOrWhiteSpace(row.RepositoryText) && string.IsNullOrWhiteSpace(row.WorkspaceText) && !string.IsNullOrWhiteSpace(row.ContextText)) {
                    AppendConversationChip(sb, row.ContextText!);
                }
                AppendConversationChip(sb, row.AccountText);
                AppendConversationChip(sb, row.ModelText);
                AppendConversationChip(sb, row.SurfaceText);
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"conversation-facts\">");
                AppendConversationFact(sb, "Started", row.StartedText);
                AppendConversationFact(sb, "Span", row.SpanText);
                if (!string.IsNullOrWhiteSpace(row.ActiveText)) {
                    AppendConversationFact(sb, "Active", row.ActiveText!);
                }
                AppendConversationFact(sb, "Turns", row.TurnText);
                if (!string.IsNullOrWhiteSpace(row.CompactText)) {
                    AppendConversationFact(sb, "Compacts", row.CompactText!);
                }
                if (!string.IsNullOrWhiteSpace(row.CostText)) {
                    AppendConversationFact(sb, "Cost", row.CostText!);
                }
                sb.AppendLine("                    </div>");
                sb.AppendLine("                    <div class=\"conversation-bar\">");
                sb.Append("                      <div class=\"conversation-bar-fill\" style=\"width:")
                    .Append(row.RatioPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine("%\"></div>");
                sb.AppendLine("                    </div>");
                sb.AppendLine("                  </div>");
                sb.AppendLine("                </button>");
            }
            sb.AppendLine("              </div>");
            AppendConversationDetailPanel(sb, model.Rows[0]);
            sb.AppendLine("              <div class=\"conversation-empty hidden\" data-conversation-empty>No conversations match this filter yet. Try a broader search.</div>");
            sb.AppendLine("            </div>");
        }

        sb.AppendLine("          </article>");
    }

    private static void AppendConversationChip(StringBuilder sb, string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        sb.Append("                    <span>").Append(Html(value)).AppendLine("</span>");
    }

    private static void AppendConversationFact(StringBuilder sb, string label, string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        sb.AppendLine("                    <div class=\"conversation-fact\">");
        sb.Append("                      <div class=\"conversation-fact-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("                      <div class=\"conversation-fact-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("                    </div>");
    }

    private static void AppendConversationQuickFilters(StringBuilder sb, IReadOnlyList<UsageTelemetryConversationPulseRowPageModel> rows) {
        var topAccounts = rows
            .GroupBy(static row => row.AccountText, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(static group => group.Key)
            .ToArray();
        var topContexts = rows
            .Select(static row => row.RepositoryText ?? row.WorkspaceText ?? row.ContextText)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(static group => group.Key)
            .ToArray();

        sb.AppendLine("            <div class=\"conversation-filter-groups\">");
        sb.AppendLine("              <div class=\"conversation-filter-group\">");
        sb.AppendLine("                <div class=\"conversation-filter-label\">Quick filters</div>");
        sb.AppendLine("                <div class=\"conversation-filter-chips\">");
        AppendConversationFilterChip(sb, "named", "Named only", "named", true);
        AppendConversationFilterChip(sb, "named", "Unnamed only", "unnamed", false);
        sb.AppendLine("                </div>");
        sb.AppendLine("              </div>");
        sb.AppendLine("              <div class=\"conversation-filter-group\">");
        sb.AppendLine("                <div class=\"conversation-filter-label\">Session signals</div>");
        sb.AppendLine("                <div class=\"conversation-filter-chips\">");
        AppendConversationFilterChip(sb, "profile", "Bursty", "bursty", false);
        AppendConversationFilterChip(sb, "profile", "Marathon", "marathon", false);
        AppendConversationFilterChip(sb, "profile", "Compact-heavy", "compact-heavy", false);
        sb.AppendLine("                </div>");
        sb.AppendLine("              </div>");
        if (topAccounts.Length > 0) {
            sb.AppendLine("              <div class=\"conversation-filter-group\">");
            sb.AppendLine("                <div class=\"conversation-filter-label\">Accounts</div>");
            sb.AppendLine("                <div class=\"conversation-filter-chips\">");
            foreach (var account in topAccounts) {
                AppendConversationFilterChip(sb, "account", account, account, false);
            }
            sb.AppendLine("                </div>");
            sb.AppendLine("              </div>");
        }
        if (topContexts.Length > 0) {
            sb.AppendLine("              <div class=\"conversation-filter-group\">");
            sb.AppendLine("                <div class=\"conversation-filter-label\">Repos and workspaces</div>");
            sb.AppendLine("                <div class=\"conversation-filter-chips\">");
            foreach (var context in topContexts) {
                AppendConversationFilterChip(sb, "context", context, context, false);
            }
            sb.AppendLine("                </div>");
            sb.AppendLine("              </div>");
        }
        sb.AppendLine("            </div>");
    }

    private static void AppendConversationFilterChip(StringBuilder sb, string group, string label, string value, bool isNegated) {
        sb.Append("                  <button type=\"button\" class=\"conversation-filter-chip\" data-conversation-filter-group=\"")
            .Append(Html(group))
            .Append("\" data-conversation-filter-value=\"")
            .Append(Html(value))
            .Append("\"");
        if (isNegated) {
            sb.Append(" data-conversation-filter-negated=\"true\"");
        }
        sb.Append(">")
            .Append(Html(label))
            .AppendLine("</button>");
    }

    private static void AppendConversationSortTab(StringBuilder sb, string key, string label, bool isActive) {
        sb.Append("              <button type=\"button\" class=\"provider-dataset-tab conversation-sort-tab");
        if (isActive) {
            sb.Append(" active");
        }
        sb.Append("\" data-conversation-sort=\"")
            .Append(Html(key))
            .Append("\" role=\"tab\" aria-selected=\"")
            .Append(isActive ? "true" : "false")
            .Append("\">")
            .Append(Html(label))
            .AppendLine("</button>");
    }

    private static void AppendConversationContextLensTab(StringBuilder sb, string key, string label, bool isActive) {
        sb.Append("                  <button type=\"button\" class=\"provider-dataset-tab conversation-context-lens");
        if (isActive) {
            sb.Append(" active");
        }
        sb.Append("\" data-conversation-context-lens=\"")
            .Append(Html(key))
            .Append("\" role=\"tab\" aria-selected=\"")
            .Append(isActive ? "true" : "false")
            .Append("\">")
            .Append(Html(label))
            .AppendLine("</button>");
    }

    private static void AppendConversationSnapshotCard(StringBuilder sb, string label, string value, string copy, string key) {
        sb.AppendLine("              <div class=\"conversation-snapshot-card\">");
        sb.Append("                <div class=\"conversation-snapshot-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("                <div class=\"conversation-snapshot-value\" data-conversation-snapshot-")
            .Append(Html(key))
            .Append(">")
            .Append(Html(value))
            .AppendLine("</div>");
        sb.Append("                <div class=\"conversation-snapshot-copy\" data-conversation-snapshot-")
            .Append(Html(key))
            .Append("-copy>")
            .Append(Html(copy))
            .AppendLine("</div>");
        sb.AppendLine("              </div>");
    }

    private static void AppendConversationRowDataset(StringBuilder sb, string name, string? value) {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) {
            return;
        }

        sb.Append(" data-detail-")
            .Append(Html(name))
            .Append("=\"")
            .Append(Html(value!))
            .Append("\"");
    }

    private static void AppendConversationDetailPanel(StringBuilder sb, UsageTelemetryConversationPulseRowPageModel row) {
        sb.AppendLine("              <aside class=\"conversation-detail-card\" data-conversation-detail>");
        sb.AppendLine("                <div class=\"conversation-detail-kicker\">Selected conversation</div>");
        sb.AppendLine("                <div class=\"conversation-detail-header\">");
        sb.Append("                  <div class=\"conversation-detail-rank\" data-detail-rank>")
            .Append(Html("#" + row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .AppendLine("</div>");
        sb.AppendLine("                  <div class=\"conversation-detail-title-block\">");
        sb.Append("                    <div class=\"conversation-detail-title\" data-detail-title>").Append(Html(row.TitleText)).AppendLine("</div>");
        sb.Append("                    <div class=\"conversation-detail-session\" data-detail-session>")
            .Append(Html(row.SessionLabel + " " + row.SessionCode))
            .AppendLine("</div>");
        sb.AppendLine("                  </div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class=\"conversation-detail-metrics\">");
        AppendConversationDetailMetric(sb, "Tokens", row.TokenText, "data-detail-tokens");
        AppendConversationDetailMetric(sb, "Share", row.ShareText, "data-detail-share");
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class=\"conversation-detail-chips\" data-detail-chip-host>");
        AppendConversationDetailChip(sb, row.RepositoryText);
        if (!string.IsNullOrWhiteSpace(row.WorkspaceText) &&
            !string.Equals(row.WorkspaceText, row.RepositoryText, StringComparison.OrdinalIgnoreCase)) {
            AppendConversationDetailChip(sb, row.WorkspaceText);
        }
        if (string.IsNullOrWhiteSpace(row.RepositoryText) && string.IsNullOrWhiteSpace(row.WorkspaceText)) {
            AppendConversationDetailChip(sb, row.ContextText);
        }
        AppendConversationDetailChip(sb, row.AccountText);
        AppendConversationDetailChip(sb, row.ModelText);
        AppendConversationDetailChip(sb, row.SurfaceText);
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class=\"conversation-detail-grid\">");
        AppendConversationDetailMetric(sb, "Started", row.StartedText, "data-detail-started");
        AppendConversationDetailMetric(sb, "Span", row.SpanText, "data-detail-span");
        AppendConversationDetailMetric(sb, "Active", row.ActiveText, "data-detail-active");
        AppendConversationDetailMetric(sb, "Turns", row.TurnText, "data-detail-turns");
        AppendConversationDetailMetric(sb, "Compacts", row.CompactText, "data-detail-compacts");
        AppendConversationDetailMetric(sb, "Cost", row.CostText, "data-detail-cost");
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class=\"conversation-detail-summary\">");
        sb.Append("                  <div class=\"conversation-detail-summary-copy\" data-detail-summary>")
            .Append(Html(BuildConversationDetailSummary(row)))
            .AppendLine("</div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("              </aside>");
    }

    private static void AppendConversationDetailMetric(StringBuilder sb, string label, string? value, string valueAttribute) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        sb.AppendLine("                  <div class=\"conversation-detail-metric\">");
        sb.Append("                    <div class=\"conversation-detail-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("                    <div class=\"conversation-detail-value\" ").Append(valueAttribute).Append(">")
            .Append(Html(value!))
            .AppendLine("</div>");
        sb.AppendLine("                  </div>");
    }

    private static void AppendConversationDetailChip(StringBuilder sb, string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        sb.Append("                  <span>").Append(Html(value!)).AppendLine("</span>");
    }

    private static string BuildConversationDetailSummary(UsageTelemetryConversationPulseRowPageModel row) {
        var parts = new List<string> {
            row.TokenText + " tokens",
            row.ShareText
        };
        if (!string.IsNullOrWhiteSpace(row.SpanText)) {
            parts.Add(row.SpanText);
        }
        if (!string.IsNullOrWhiteSpace(row.ActiveText)) {
            parts.Add(row.ActiveText!);
        }
        if (!string.IsNullOrWhiteSpace(row.TurnText)) {
            parts.Add(row.TurnText);
        }
        if (!string.IsNullOrWhiteSpace(row.CompactText)) {
            parts.Add(row.CompactText!);
        }
        if (!string.IsNullOrWhiteSpace(row.CostText)) {
            parts.Add(row.CostText!);
        }

        return string.Join(" • ", parts);
    }

    private static void AppendCodeChurnSection(StringBuilder sb, UsageTelemetryCodeChurnPageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"code-churn\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Local repository</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.DailyBreakdown);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendChurnUsageCorrelationSection(StringBuilder sb, UsageTelemetryChurnUsageSignalPageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"churn-usage-correlation\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Recent window</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.ProviderSignals);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendGitHubLocalAlignmentSection(StringBuilder sb, UsageTelemetryGitHubLocalPulsePageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"github-local-alignment\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Recent window</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.Repositories);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendGitHubRepoClusterSection(StringBuilder sb, UsageTelemetryGitHubRepoClusterPageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"github-repo-clusters\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Recent window</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.Clusters);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }
}
