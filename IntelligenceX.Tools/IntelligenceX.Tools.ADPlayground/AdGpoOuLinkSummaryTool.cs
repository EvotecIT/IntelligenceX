using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns OU-level GPO link summary posture for one domain (read-only).
/// </summary>
public sealed class AdGpoOuLinkSummaryTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_ou_link_summary",
        "Summarize GPO link posture per OU (link counts, enforced/disabled/broken, blocked inheritance) for one domain (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("link_count_at_least", ToolSchema.Integer("Optional minimum link count filter.")),
                ("broken_only", ToolSchema.Boolean("When true, return only OUs with one or more broken links.")),
                ("max_gpos", ToolSchema.Integer("Maximum GPOs to process (capped). Default 25000.")),
                ("max_ous", ToolSchema.Integer("Maximum OU rows to collect before projection (capped). Default 50000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoOuLinkSummaryResult(
        string DomainName,
        int LinkCountAtLeast,
        bool BrokenOnly,
        int MaxGpos,
        int MaxOus,
        int Scanned,
        bool Truncated,
        int WithBlockedInheritanceCount,
        int WithBrokenLinksCount,
        IReadOnlyList<GpoOuLinkSummaryService.Row> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoOuLinkSummaryTool"/> class.
    /// </summary>
    public AdGpoOuLinkSummaryTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var linkCountAtLeast = ToolArgs.GetCappedInt32(arguments, "link_count_at_least", 0, 0, 100000);
        var brokenOnly = ToolArgs.GetBoolean(arguments, "broken_only", defaultValue: false);
        var maxGpos = ToolArgs.GetCappedInt32(arguments, "max_gpos", 25000, 1, 250000);
        var maxOus = ToolArgs.GetCappedInt32(arguments, "max_ous", 50000, 1, 250000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var view = GpoOuLinkSummaryService.Get(domainName, maxGpos: maxGpos, maxOus: maxOus);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "GPO OU link summary query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var filtered = view.Items
            .Where(row => row.LinkCount >= linkCountAtLeast)
            .Where(row => !brokenOnly || row.BrokenCount > 0)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<GpoOuLinkSummaryService.Row> rows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > rows.Count;

        var result = new AdGpoOuLinkSummaryResult(
            DomainName: domainName,
            LinkCountAtLeast: linkCountAtLeast,
            BrokenOnly: brokenOnly,
            MaxGpos: maxGpos,
            MaxOus: maxOus,
            Scanned: scanned,
            Truncated: truncated,
            WithBlockedInheritanceCount: filtered.Count(static row => row.BlockedInheritance),
            WithBrokenLinksCount: filtered.Count(static row => row.BrokenCount > 0),
            Rows: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO OU Link Summary (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("link_count_at_least", linkCountAtLeast);
                meta.Add("broken_only", brokenOnly);
                meta.Add("max_gpos", maxGpos);
                meta.Add("max_ous", maxOus);
                meta.Add("max_results", maxResults);
            });
        return Task.FromResult(response);
    }
}
