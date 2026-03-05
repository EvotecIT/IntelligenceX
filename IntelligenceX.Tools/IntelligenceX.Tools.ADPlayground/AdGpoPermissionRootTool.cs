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
    private const int DefaultMaxRows = 100000;
    private const int MaxRowsCap = 1000000;

    internal readonly record struct GpoPermissionRootBindingContract(
        string? Permission,
        bool DenyOnly,
        bool InheritedOnly,
        int MaxRows);

    private sealed record GpoPermissionRootRequest(
        string? Permission,
        bool DenyOnly,
        bool InheritedOnly,
        int MaxRows);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<GpoPermissionRootRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var permission = reader.OptionalString("permission");
            if (!string.IsNullOrWhiteSpace(permission) &&
                !string.Equals(permission, "GpoRootCreate", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(permission, "GpoRootOwner", StringComparison.OrdinalIgnoreCase)) {
                return ToolRequestBindingResult<GpoPermissionRootRequest>.Failure(
                    "permission must be one of: GpoRootCreate, GpoRootOwner.");
            }

            return ToolRequestBindingResult<GpoPermissionRootRequest>.Success(new GpoPermissionRootRequest(
                Permission: permission,
                DenyOnly: reader.Boolean("deny_only", defaultValue: false),
                InheritedOnly: reader.Boolean("inherited_only", defaultValue: false),
                MaxRows: reader.CappedInt32("max_rows", DefaultMaxRows, 1, MaxRowsCap)));
        });
    }

    internal static ToolRequestBindingResult<GpoPermissionRootBindingContract> BindRequestContract(JsonObject? arguments) {
        var binding = BindRequest(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return ToolRequestBindingResult<GpoPermissionRootBindingContract>.Failure(
                binding.Error,
                binding.ErrorCode,
                binding.Hints,
                binding.IsTransient);
        }

        var request = binding.Request;
        return ToolRequestBindingResult<GpoPermissionRootBindingContract>.Success(new GpoPermissionRootBindingContract(
            Permission: request.Permission,
            DenyOnly: request.DenyOnly,
            InheritedOnly: request.InheritedOnly,
            MaxRows: request.MaxRows));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoPermissionRootRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        return ExecuteDomainRowsViewTool(
            arguments: context.Arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: GPO Root Permissions (preview)",
            defaultErrorMessage: "GPO root-permission query failed.",
            maxTop: MaxViewTop,
            query: domainName => GpoPermissionRootService.Get(domainName, maxRows: request.MaxRows),
            collectionSucceededSelector: static view => view.CollectionSucceeded,
            collectionErrorSelector: static view => view.CollectionError,
            allRowsSelector: view => view.Items
                .Where(row => string.IsNullOrWhiteSpace(request.Permission) || string.Equals(row.Permission, request.Permission, StringComparison.OrdinalIgnoreCase))
                .Where(row => !request.DenyOnly || row.PermissionType == GpoPermissionType.Deny)
                .Where(row => !request.InheritedOnly || row.Inherited)
                .ToArray(),
            resultFactory: (domainName, _, allRows, rows, scanned, truncated) => new AdGpoPermissionRootResult(
                DomainName: domainName,
                Permission: request.Permission,
                DenyOnly: request.DenyOnly,
                InheritedOnly: request.InheritedOnly,
                MaxRows: request.MaxRows,
                Scanned: scanned,
                Truncated: truncated,
                DenyCount: allRows.Count(static row => row.PermissionType == GpoPermissionType.Deny),
                OwnerPermissionCount: allRows.Count(static row => string.Equals(row.Permission, "GpoRootOwner", StringComparison.OrdinalIgnoreCase)),
                CreatePermissionCount: allRows.Count(static row => string.Equals(row.Permission, "GpoRootCreate", StringComparison.OrdinalIgnoreCase)),
                Rows: rows),
            additionalMetaMutate: (meta, _, _, _) => {
                meta.Add("deny_only", request.DenyOnly);
                meta.Add("inherited_only", request.InheritedOnly);
                meta.Add("max_rows", request.MaxRows);
                if (!string.IsNullOrWhiteSpace(request.Permission)) {
                    meta.Add("permission", request.Permission);
                }
            });
    }
}
