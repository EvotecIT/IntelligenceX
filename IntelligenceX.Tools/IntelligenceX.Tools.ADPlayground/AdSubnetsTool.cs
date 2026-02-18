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
/// Lists Active Directory subnets or emits subnet rollup summary (read-only).
/// </summary>
public sealed class AdSubnetsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_subnets",
        "List Active Directory subnets with site bindings or return a summary rollup (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name (defaults to current forest).")),
                ("summary", ToolSchema.Boolean("When true, returns aggregated subnet summary grouped by site.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdSubnetsResult(
        string? ForestName,
        int Scanned,
        bool Truncated,
        int Total,
        int Orphaned,
        IReadOnlyList<SubnetInfoEx> Subnets);

    private sealed record AdSubnetsSummaryResult(
        string? ForestName,
        int Scanned,
        bool Truncated,
        SubnetSummary Summary,
        IReadOnlyList<SubnetSiteCount> BySite);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSubnetsTool"/> class.
    /// </summary>
    public AdSubnetsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var summary = ToolArgs.GetBoolean(arguments, "summary", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (summary) {
            SubnetSummary summaryModel;
            try {
                summaryModel = TopologyService.GetSubnetSummary(forestName);
            } catch (Exception ex) {
                return Task.FromResult(ErrorFromException(ex, defaultMessage: "Subnet summary query failed.", invalidOperationErrorCode: "query_failed"));
            }

            var scanned = summaryModel.BySite.Count;
            var bySiteRows = scanned > maxResults ? summaryModel.BySite.Take(maxResults).ToArray() : summaryModel.BySite;
            var truncated = scanned > bySiteRows.Count;

            var result = new AdSubnetsSummaryResult(
                ForestName: forestName,
                Scanned: scanned,
                Truncated: truncated,
                Summary: summaryModel,
                BySite: bySiteRows);

            ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
                arguments: arguments,
                model: result,
                sourceRows: bySiteRows,
                viewRowsPath: "by_site_view",
                title: "Active Directory: Subnets Summary (preview)",
                maxTop: MaxViewTop,
                baseTruncated: truncated,
                response: out var summaryResponse,
                scanned: scanned,
                metaMutate: meta => {
                    meta.Add("mode", "summary");
                    meta.Add("max_results", maxResults);
                    if (!string.IsNullOrWhiteSpace(forestName)) {
                        meta.Add("forest_name", forestName);
                    }
                });
            return Task.FromResult(summaryResponse);
        }

        IReadOnlyList<SubnetInfoEx> allSubnets;
        try {
            allSubnets = TopologyService.GetSubnets(forestName);
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Subnet query failed.", invalidOperationErrorCode: "query_failed"));
        }

        var scannedSubnets = allSubnets.Count;
        var rows = scannedSubnets > maxResults ? allSubnets.Take(maxResults).ToArray() : allSubnets;
        var truncatedSubnets = scannedSubnets > rows.Count;

        var resultModel = new AdSubnetsResult(
            ForestName: forestName,
            Scanned: scannedSubnets,
            Truncated: truncatedSubnets,
            Total: scannedSubnets,
            Orphaned: allSubnets.Count(static subnet => subnet.IsOrphaned),
            Subnets: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: resultModel,
            sourceRows: rows,
            viewRowsPath: "subnets_view",
            title: "Active Directory: Subnets (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncatedSubnets,
            response: out var response,
            scanned: scannedSubnets,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                meta.Add("max_results", maxResults);
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            });
        return Task.FromResult(response);
    }
}

