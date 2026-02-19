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
/// Returns unknown/unresolvable trustee findings on GPO ACLs for one domain (read-only).
/// </summary>
public sealed class AdGpoPermissionUnknownTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_permission_unknown",
        "Find unknown/unresolvable trustees in GPO ACLs (GPOPermissionsUnknown parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("resolution_error_contains", ToolSchema.String("Optional substring filter for trustee resolution errors.")),
                ("inherited_only", ToolSchema.Boolean("When true, return only inherited ACL rows.")),
                ("max_gpos", ToolSchema.Integer("Maximum GPOs to process (capped). Default 50000.")),
                ("max_findings", ToolSchema.Integer("Maximum findings to collect before projection (capped). Default 200000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoPermissionUnknownResult(
        string DomainName,
        string? ResolutionErrorContains,
        bool InheritedOnly,
        int MaxGpos,
        int MaxFindings,
        int Scanned,
        bool Truncated,
        int InheritedCount,
        int WithResolutionErrorCount,
        IReadOnlyList<GpoPermissionUnknownRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoPermissionUnknownTool"/> class.
    /// </summary>
    public AdGpoPermissionUnknownTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainName(arguments, out var domainName, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var resolutionErrorContains = ToolArgs.GetOptionalTrimmed(arguments, "resolution_error_contains");
        var inheritedOnly = ToolArgs.GetBoolean(arguments, "inherited_only", defaultValue: false);
        var maxGpos = ToolArgs.GetCappedInt32(arguments, "max_gpos", 50000, 1, 200000);
        var maxFindings = ToolArgs.GetCappedInt32(arguments, "max_findings", 200000, 1, 2000000);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryExecuteCollectionQuery(
                query: () => GpoPermissionUnknownService.Get(domainName, maxGpos: maxGpos, maxFindings: maxFindings),
                collectionSucceededSelector: static view => view.CollectionSucceeded,
                collectionErrorSelector: static view => view.CollectionError,
                result: out var view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "GPO unknown-permission query failed.")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(row => !inheritedOnly || row.Inherited)
            .Where(row => string.IsNullOrWhiteSpace(resolutionErrorContains) || !string.IsNullOrWhiteSpace(row.ResolutionError) && row.ResolutionError.Contains(resolutionErrorContains, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var rows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new AdGpoPermissionUnknownResult(
            DomainName: domainName,
            ResolutionErrorContains: resolutionErrorContains,
            InheritedOnly: inheritedOnly,
            MaxGpos: maxGpos,
            MaxFindings: maxFindings,
            Scanned: scanned,
            Truncated: truncated,
            InheritedCount: filtered.Count(static row => row.Inherited),
            WithResolutionErrorCount: filtered.Count(static row => !string.IsNullOrWhiteSpace(row.ResolutionError)),
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Unknown Permissions (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("inherited_only", inheritedOnly);
                meta.Add("max_gpos", maxGpos);
                meta.Add("max_findings", maxFindings);
                if (!string.IsNullOrWhiteSpace(resolutionErrorContains)) {
                    meta.Add("resolution_error_contains", resolutionErrorContains);
                }
            }));
    }
}

