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
/// Returns redirected/mismatched GPO SYSVOL path findings for one domain (read-only).
/// </summary>
public sealed class AdGpoRedirectTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_redirect",
        "Find GPOs where gPCFileSysPath differs from expected domain SYSVOL path (GPORedirect parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("gpo_ids", ToolSchema.Array(ToolSchema.String(), "Optional array of GPO GUID filters.")),
                ("gpo_names", ToolSchema.Array(ToolSchema.String(), "Optional wildcard display-name filters (supports * and ?).")),
                ("actual_path_contains", ToolSchema.String("Optional substring filter applied to the actual SYSVOL path.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoRedirectResult(
        string DomainName,
        int GpoIdsCount,
        int GpoNamesCount,
        string? ActualPathContains,
        int Scanned,
        bool Truncated,
        IReadOnlyList<GpoRedirectFinding> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoRedirectTool"/> class.
    /// </summary>
    public AdGpoRedirectTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainName(arguments, out var domainName, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var gpoIdsRaw = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("gpo_ids"));
        var gpoIds = new List<Guid>(gpoIdsRaw.Count);
        foreach (var raw in gpoIdsRaw) {
            if (!Guid.TryParse(raw, out var id) || id == Guid.Empty) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", $"Invalid gpo_ids value: '{raw}'."));
            }
            gpoIds.Add(id);
        }

        var gpoNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("gpo_names"));
        var actualPathContains = ToolArgs.GetOptionalTrimmed(arguments, "actual_path_contains");
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryExecuteCollectionQuery(
                query: () => GpoRedirectAnalyzer.Get(domainName, ids: gpoIds.Count == 0 ? null : gpoIds.ToArray(), names: gpoNames.Count == 0 ? null : gpoNames.ToArray()),
                collectionSucceededSelector: static view => view.CollectionSucceeded,
                collectionErrorSelector: static view => view.CollectionError,
                result: out var view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "GPO redirect query failed.")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(row => string.IsNullOrWhiteSpace(actualPathContains) || row.ActualSysvolPath.Contains(actualPathContains, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var rows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new AdGpoRedirectResult(
            DomainName: domainName,
            GpoIdsCount: gpoIds.Count,
            GpoNamesCount: gpoNames.Count,
            ActualPathContains: actualPathContains,
            Scanned: scanned,
            Truncated: truncated,
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Redirect Findings (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("gpo_ids_count", gpoIds.Count);
                meta.Add("gpo_names_count", gpoNames.Count);
                if (!string.IsNullOrWhiteSpace(actualPathContains)) {
                    meta.Add("actual_path_contains", actualPathContains);
                }
            }));
    }
}

