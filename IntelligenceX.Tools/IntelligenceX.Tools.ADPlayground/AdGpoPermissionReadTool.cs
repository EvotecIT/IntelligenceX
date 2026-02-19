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
/// Returns Authenticated Users read/apply baseline posture for GPOs in one domain (read-only).
/// </summary>
public sealed class AdGpoPermissionReadTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_permission_read",
        "Assess Authenticated Users read/apply permissions on GPOs (GPOPermissionsRead parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_compliant", ToolSchema.Boolean("When true, include compliant rows alongside findings.")),
                ("deny_only", ToolSchema.Boolean("When true, return only rows where Authenticated Users has an explicit deny.")),
                ("max_gpos", ToolSchema.Integer("Maximum GPOs to process (capped). Default 50000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoPermissionReadResult(
        string DomainName,
        bool IncludeCompliant,
        bool DenyOnly,
        int MaxGpos,
        int Scanned,
        bool Truncated,
        int NonCompliantCount,
        int DenyCount,
        int ErrorCount,
        IReadOnlyList<GpoPermissionReadRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoPermissionReadTool"/> class.
    /// </summary>
    public AdGpoPermissionReadTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainName(arguments, out var domainName, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var includeCompliant = ToolArgs.GetBoolean(arguments, "include_compliant", defaultValue: false);
        var denyOnly = ToolArgs.GetBoolean(arguments, "deny_only", defaultValue: false);
        var maxGpos = ToolArgs.GetCappedInt32(arguments, "max_gpos", 50000, 1, 200000);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryExecuteCollectionQuery(
                query: () => GpoPermissionReadService.Get(domainName, includeCompliant: includeCompliant, maxGpos: maxGpos),
                collectionSucceededSelector: static view => view.CollectionSucceeded,
                collectionErrorSelector: static view => view.CollectionError,
                result: out var view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "GPO read-permission baseline query failed.")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(row => !denyOnly || row.HasAuthenticatedUsersDeny)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new AdGpoPermissionReadResult(
            DomainName: domainName,
            IncludeCompliant: includeCompliant,
            DenyOnly: denyOnly,
            MaxGpos: maxGpos,
            Scanned: scanned,
            Truncated: truncated,
            NonCompliantCount: filtered.Count(static row => !row.IsCompliant),
            DenyCount: filtered.Count(static row => row.HasAuthenticatedUsersDeny),
            ErrorCount: filtered.Count(static row => !string.IsNullOrWhiteSpace(row.Error)),
            Rows: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Permission Read Baseline (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("include_compliant", includeCompliant);
                meta.Add("deny_only", denyOnly);
                meta.Add("max_gpos", maxGpos);
            }));
    }
}
