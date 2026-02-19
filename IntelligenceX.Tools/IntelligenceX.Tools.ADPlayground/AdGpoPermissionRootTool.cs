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
/// Returns Policies container root permission posture for one domain (read-only).
/// </summary>
public sealed class AdGpoPermissionRootTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_permission_root",
        "Inspect CN=Policies root permissions (create/owner) for one domain (GPOPermissionsRoot parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("permission", ToolSchema.String("Optional permission filter: GpoRootCreate or GpoRootOwner.")),
                ("deny_only", ToolSchema.Boolean("When true, return only deny rows.")),
                ("inherited_only", ToolSchema.Boolean("When true, return only inherited rows.")),
                ("max_rows", ToolSchema.Integer("Maximum rows to collect before projection (capped). Default 100000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoPermissionRootResult(
        string DomainName,
        string? Permission,
        bool DenyOnly,
        bool InheritedOnly,
        int MaxRows,
        int Scanned,
        bool Truncated,
        int DenyCount,
        int OwnerPermissionCount,
        int CreatePermissionCount,
        IReadOnlyList<GpoPermissionRootRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoPermissionRootTool"/> class.
    /// </summary>
    public AdGpoPermissionRootTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainName(arguments, out var domainName, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var permission = ToolArgs.GetOptionalTrimmed(arguments, "permission");
        if (!string.IsNullOrWhiteSpace(permission) &&
            !string.Equals(permission, "GpoRootCreate", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(permission, "GpoRootOwner", StringComparison.OrdinalIgnoreCase)) {
            return Task.FromResult(ToolResponse.Error(
                "invalid_argument",
                "permission must be one of: GpoRootCreate, GpoRootOwner."));
        }

        var denyOnly = ToolArgs.GetBoolean(arguments, "deny_only", defaultValue: false);
        var inheritedOnly = ToolArgs.GetBoolean(arguments, "inherited_only", defaultValue: false);
        var maxRows = ToolArgs.GetCappedInt32(arguments, "max_rows", 100000, 1, 1000000);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryExecuteCollectionQuery(
                query: () => GpoPermissionRootService.Get(domainName, maxRows: maxRows),
                collectionSucceededSelector: static view => view.CollectionSucceeded,
                collectionErrorSelector: static view => view.CollectionError,
                result: out var view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "GPO root-permission query failed.")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(row => string.IsNullOrWhiteSpace(permission) || string.Equals(row.Permission, permission, StringComparison.OrdinalIgnoreCase))
            .Where(row => !denyOnly || row.PermissionType == GpoPermissionType.Deny)
            .Where(row => !inheritedOnly || row.Inherited)
            .ToArray();

        var rows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new AdGpoPermissionRootResult(
            DomainName: domainName,
            Permission: permission,
            DenyOnly: denyOnly,
            InheritedOnly: inheritedOnly,
            MaxRows: maxRows,
            Scanned: scanned,
            Truncated: truncated,
            DenyCount: filtered.Count(static row => row.PermissionType == GpoPermissionType.Deny),
            OwnerPermissionCount: filtered.Count(static row => string.Equals(row.Permission, "GpoRootOwner", StringComparison.OrdinalIgnoreCase)),
            CreatePermissionCount: filtered.Count(static row => string.Equals(row.Permission, "GpoRootCreate", StringComparison.OrdinalIgnoreCase)),
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Root Permissions (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("deny_only", denyOnly);
                meta.Add("inherited_only", inheritedOnly);
                meta.Add("max_rows", maxRows);
                if (!string.IsNullOrWhiteSpace(permission)) {
                    meta.Add("permission", permission);
                }
            }));
    }
}

