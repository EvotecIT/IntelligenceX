using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Computes AD site coverage metrics and optional raw topology payload (read-only).
/// </summary>
public sealed class AdSiteCoverageTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_site_coverage",
        "Compute AD site coverage metrics (DC/subnet coverage and orphaned subnet counts) with optional raw topology payload (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name (defaults to current forest).")),
                ("include_registry", ToolSchema.Boolean("When true, attempts Netlogon AutoSiteCoverage registry reads from site DCs (opt-in).")),
                ("raw", ToolSchema.Boolean("When true, include capped raw site/subnet payload alongside coverage summary.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return for each collection (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdSiteCoverageSummaryResult(
        string? ForestName,
        bool IncludeRegistry,
        int Scanned,
        bool Truncated,
        IReadOnlyList<CoverageSummary> Summary);

    private sealed record AdSiteCoverageRawResult(
        string? ForestName,
        bool IncludeRegistry,
        int SummaryScanned,
        int SitesScanned,
        int SubnetsScanned,
        bool SummaryTruncated,
        bool SitesTruncated,
        bool SubnetsTruncated,
        IReadOnlyList<CoverageSummary> Summary,
        IReadOnlyList<SiteInfoEx> Sites,
        IReadOnlyList<SubnetInfoEx> Subnets);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSiteCoverageTool"/> class.
    /// </summary>
    public AdSiteCoverageTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeRegistry = ToolArgs.GetBoolean(arguments, "include_registry", defaultValue: false);
        var raw = ToolArgs.GetBoolean(arguments, "raw", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        try {
            if (!raw) {
                var allSummary = CoverageService.GetCoverage(forestName, includeRegistry);
                var scanned = allSummary.Count;
                var rows = scanned > maxResults ? allSummary.Take(maxResults).ToArray() : allSummary;
                var truncated = scanned > rows.Count;

                var result = new AdSiteCoverageSummaryResult(
                    ForestName: forestName,
                    IncludeRegistry: includeRegistry,
                    Scanned: scanned,
                    Truncated: truncated,
                    Summary: rows);

                ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
                    arguments: arguments,
                    model: result,
                    sourceRows: rows,
                    viewRowsPath: "summary_view",
                    title: "Active Directory: Site Coverage (preview)",
                    maxTop: MaxViewTop,
                    baseTruncated: truncated,
                    response: out var summaryResponse,
                    scanned: scanned,
                    metaMutate: meta => {
                        meta.Add("mode", "summary");
                        meta.Add("max_results", maxResults);
                        meta.Add("include_registry", includeRegistry);
                        if (!string.IsNullOrWhiteSpace(forestName)) {
                            meta.Add("forest_name", forestName);
                        }
                    });
                return Task.FromResult(summaryResponse);
            }

            var report = CoverageService.GetReport(forestName, includeRegistry);
            var summaryScanned = report.Summary.Count;
            var sitesScanned = report.Sites.Count;
            var subnetsScanned = report.Subnets.Count;

            var summaryRows = summaryScanned > maxResults ? report.Summary.Take(maxResults).ToArray() : report.Summary;
            var siteRows = sitesScanned > maxResults ? report.Sites.Take(maxResults).ToArray() : report.Sites;
            var subnetRows = subnetsScanned > maxResults ? report.Subnets.Take(maxResults).ToArray() : report.Subnets;

            var summaryTruncated = summaryScanned > summaryRows.Count;
            var sitesTruncated = sitesScanned > siteRows.Count;
            var subnetsTruncated = subnetsScanned > subnetRows.Count;

            var rawResult = new AdSiteCoverageRawResult(
                ForestName: forestName,
                IncludeRegistry: includeRegistry,
                SummaryScanned: summaryScanned,
                SitesScanned: sitesScanned,
                SubnetsScanned: subnetsScanned,
                SummaryTruncated: summaryTruncated,
                SitesTruncated: sitesTruncated,
                SubnetsTruncated: subnetsTruncated,
                Summary: summaryRows,
                Sites: siteRows,
                Subnets: subnetRows);

            ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
                arguments: arguments,
                model: rawResult,
                sourceRows: summaryRows,
                viewRowsPath: "summary_view",
                title: "Active Directory: Site Coverage (raw preview)",
                maxTop: MaxViewTop,
                baseTruncated: summaryTruncated || sitesTruncated || subnetsTruncated,
                response: out var rawResponse,
                scanned: summaryScanned,
                metaMutate: meta => {
                    meta.Add("mode", "raw");
                    meta.Add("max_results", maxResults);
                    meta.Add("include_registry", includeRegistry);
                    meta.Add("sites_scanned", sitesScanned);
                    meta.Add("subnets_scanned", subnetsScanned);
                    meta.Add("sites_truncated", sitesTruncated);
                    meta.Add("subnets_truncated", subnetsTruncated);
                    if (!string.IsNullOrWhiteSpace(forestName)) {
                        meta.Add("forest_name", forestName);
                    }
                });
            return Task.FromResult(rawResponse);
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", $"Site coverage query failed: {ex.Message}"));
        }
    }
}
