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
/// Lists Active Directory sites with optional subnet and site-options enrichment (read-only).
/// </summary>
public sealed class AdSitesTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_sites",
        "List Active Directory sites with optional subnet and site-options enrichment (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name (defaults to current forest).")),
                ("include_subnets", ToolSchema.Boolean("When true, attaches subnet CIDRs to each site.")),
                ("include_options", ToolSchema.Boolean("When true, includes NTDS site settings option flags.")),
                ("no_dc_only", ToolSchema.Boolean("When true, returns only sites with zero domain controllers.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdSitesResult(
        string? ForestName,
        bool IncludeSubnets,
        bool IncludeOptions,
        bool NoDcOnly,
        int Scanned,
        bool Truncated,
        IReadOnlyList<SiteInfoEx> Sites);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSitesTool"/> class.
    /// </summary>
    public AdSitesTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeSubnets = ToolArgs.GetBoolean(arguments, "include_subnets", defaultValue: false);
        var includeOptions = ToolArgs.GetBoolean(arguments, "include_options", defaultValue: false);
        var noDcOnly = ToolArgs.GetBoolean(arguments, "no_dc_only", defaultValue: false);
        var maxResults = ResolveMaxResults(arguments);

        if (!TryExecute(
                action: () => TopologyService.GetSites(
                    forestName: forestName,
                    includeSubnets: includeSubnets,
                    includeOptions: includeOptions,
                    onlySitesWithoutDc: noDcOnly),
                result: out IReadOnlyList<SiteInfoEx> allSites,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Site topology query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var rows = CapRows(allSites, maxResults, out var scanned, out var truncated);

        var result = new AdSitesResult(
            ForestName: forestName,
            IncludeSubnets: includeSubnets,
            IncludeOptions: includeOptions,
            NoDcOnly: noDcOnly,
            Scanned: scanned,
            Truncated: truncated,
            Sites: rows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "sites_view",
            title: "Active Directory: Sites (preview)",
            baseTruncated: truncated,
            scanned: scanned,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("include_subnets", includeSubnets);
                meta.Add("include_options", includeOptions);
                meta.Add("no_dc_only", noDcOnly);
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            });
        return Task.FromResult(response);
    }
}

