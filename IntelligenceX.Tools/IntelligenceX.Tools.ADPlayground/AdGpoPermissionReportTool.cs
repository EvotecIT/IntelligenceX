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
/// Returns raw GPO permission rows for one domain with optional filtering (read-only).
/// </summary>
public sealed class AdGpoPermissionReportTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private sealed record GpoPermissionReportRequest(
        string DomainName,
        Guid? GpoId,
        string? GpoName,
        string? PrincipalContains,
        string? PermissionTypeRaw,
        GpoPermissionType? PermissionType,
        int MaxGpos,
        int MaxRows,
        int MaxResults);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_permission_report",
        "List raw GPO permission rows (principal + allow/deny + permission bucket) for one domain (GPOPermissionsReport parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("gpo_id", ToolSchema.String("Optional GPO GUID filter.")),
                ("gpo_name", ToolSchema.String("Optional wildcard GPO display-name filter (supports * and ?).")),
                ("principal_contains", ToolSchema.String("Optional substring filter for principal name or SID.")),
                ("permission_type", ToolSchema.String("Optional filter: allow or deny.")),
                ("max_gpos", ToolSchema.Integer("Maximum GPOs to process (capped). Default 50000.")),
                ("max_rows", ToolSchema.Integer("Maximum rows to collect before projection (capped). Default 200000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoPermissionReportResult(
        string DomainName,
        Guid? GpoId,
        string? GpoName,
        string? PrincipalContains,
        string? PermissionType,
        int MaxGpos,
        int MaxRows,
        int Scanned,
        bool Truncated,
        int AllowCount,
        int DenyCount,
        int InheritedCount,
        IReadOnlyList<GpoPermissionRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoPermissionReportTool"/> class.
    /// </summary>
    public AdGpoPermissionReportTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<GpoPermissionReportRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("domain_name", out var domainName, out var domainError)) {
                return ToolRequestBindingResult<GpoPermissionReportRequest>.Failure(domainError);
            }

            var gpoIdRaw = reader.OptionalString("gpo_id");
            Guid? gpoId = null;
            if (!string.IsNullOrWhiteSpace(gpoIdRaw)) {
                if (!Guid.TryParse(gpoIdRaw, out var parsed) || parsed == Guid.Empty) {
                    return ToolRequestBindingResult<GpoPermissionReportRequest>.Failure("gpo_id must be a valid non-empty GUID.");
                }

                gpoId = parsed;
            }

            var permissionTypeRaw = reader.OptionalString("permission_type");
            GpoPermissionType? permissionType = null;
            if (!string.IsNullOrWhiteSpace(permissionTypeRaw)) {
                if (string.Equals(permissionTypeRaw, "allow", StringComparison.OrdinalIgnoreCase)) {
                    permissionType = GpoPermissionType.Allow;
                } else if (string.Equals(permissionTypeRaw, "deny", StringComparison.OrdinalIgnoreCase)) {
                    permissionType = GpoPermissionType.Deny;
                } else {
                    return ToolRequestBindingResult<GpoPermissionReportRequest>.Failure("permission_type must be either 'allow' or 'deny'.");
                }
            }

            return ToolRequestBindingResult<GpoPermissionReportRequest>.Success(new GpoPermissionReportRequest(
                DomainName: domainName,
                GpoId: gpoId,
                GpoName: reader.OptionalString("gpo_name"),
                PrincipalContains: reader.OptionalString("principal_contains"),
                PermissionTypeRaw: permissionTypeRaw,
                PermissionType: permissionType,
                MaxGpos: reader.CappedInt32("max_gpos", 50000, 1, 200000),
                MaxRows: reader.CappedInt32("max_rows", 200000, 1, 2000000),
                MaxResults: reader.CappedInt32("max_results", Options.MaxResults, 1, Options.MaxResults)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoPermissionReportRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var domainName = request.DomainName;
        var gpoId = request.GpoId;
        var gpoName = request.GpoName;
        var principalContains = request.PrincipalContains;
        var permissionType = request.PermissionType;
        var permissionTypeRaw = request.PermissionTypeRaw;
        var maxGpos = request.MaxGpos;
        var maxRows = request.MaxRows;
        var maxResults = request.MaxResults;

        if (!TryExecuteCollectionQuery(
                query: () => GpoPermissionReportService.Get(domainName, gpoId: gpoId, gpoName: gpoName, maxGpos: maxGpos, maxRows: maxRows),
                collectionSucceededSelector: static view => view.CollectionSucceeded,
                collectionErrorSelector: static view => view.CollectionError,
                result: out var view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "GPO permission report query failed.")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(row => !permissionType.HasValue || row.PermissionType == permissionType.Value)
            .Where(row =>
                string.IsNullOrWhiteSpace(principalContains) ||
                row.PrincipalName.Contains(principalContains, StringComparison.OrdinalIgnoreCase) ||
                row.PrincipalSid.Contains(principalContains, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var rows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new AdGpoPermissionReportResult(
            DomainName: domainName,
            GpoId: gpoId,
            GpoName: gpoName,
            PrincipalContains: principalContains,
            PermissionType: permissionTypeRaw,
            MaxGpos: maxGpos,
            MaxRows: maxRows,
            Scanned: scanned,
            Truncated: truncated,
            AllowCount: filtered.Count(static row => row.PermissionType == GpoPermissionType.Allow),
            DenyCount: filtered.Count(static row => row.PermissionType == GpoPermissionType.Deny),
            InheritedCount: filtered.Count(static row => row.Inherited),
            Rows: rows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Permission Report (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("max_gpos", maxGpos);
                meta.Add("max_rows", maxRows);
                if (gpoId.HasValue) {
                    meta.Add("gpo_id", gpoId.Value.ToString());
                }
                if (!string.IsNullOrWhiteSpace(gpoName)) {
                    meta.Add("gpo_name", gpoName);
                }
                if (!string.IsNullOrWhiteSpace(principalContains)) {
                    meta.Add("principal_contains", principalContains);
                }
                if (!string.IsNullOrWhiteSpace(permissionTypeRaw)) {
                    meta.Add("permission_type", permissionTypeRaw);
                }
            }));
    }
}
