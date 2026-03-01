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
/// Returns AD/SYSVOL GPO permission consistency posture for one domain (read-only).
/// </summary>
public sealed class AdGpoPermissionConsistencyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private sealed record GpoPermissionConsistencyRequest(
        string DomainName,
        bool VerifyInheritance,
        bool IncludeConsistent,
        bool TopLevelInconsistentOnly,
        bool InsideInconsistentOnly,
        int MaxGpos,
        int SysvolScanCap,
        int MaxResults);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_permission_consistency",
        "Assess AD-vs-SYSVOL GPO ACL consistency (GPOPermissionsConsistency parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("verify_inheritance", ToolSchema.Boolean("When true, scan inside SYSVOL folders for broken inheritance (best-effort).")),
                ("include_consistent", ToolSchema.Boolean("When true, include consistent rows alongside findings.")),
                ("top_level_inconsistent_only", ToolSchema.Boolean("When true, return only rows with top-level ACL inconsistency.")),
                ("inside_inconsistent_only", ToolSchema.Boolean("When true, return only rows with inner SYSVOL ACL inconsistency (requires verify_inheritance=true).")),
                ("max_gpos", ToolSchema.Integer("Maximum GPOs to process (capped). Default 50000.")),
                ("sysvol_scan_cap", ToolSchema.Integer("Maximum filesystem entries to scan per GPO when verify_inheritance is enabled.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoPermissionConsistencyResult(
        string DomainName,
        bool VerifyInheritance,
        bool IncludeConsistent,
        bool TopLevelInconsistentOnly,
        bool InsideInconsistentOnly,
        int MaxGpos,
        int SysvolScanCap,
        int Scanned,
        bool Truncated,
        int TopLevelMismatchCount,
        int InsideMismatchCount,
        int ErrorCount,
        IReadOnlyList<GpoPermissionConsistency> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoPermissionConsistencyTool"/> class.
    /// </summary>
    public AdGpoPermissionConsistencyTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<GpoPermissionConsistencyRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("domain_name", out var domainName, out var domainError)) {
                return ToolRequestBindingResult<GpoPermissionConsistencyRequest>.Failure(domainError);
            }

            var verifyInheritance = reader.Boolean("verify_inheritance", defaultValue: false);
            var includeConsistent = reader.Boolean("include_consistent", defaultValue: false);
            var topLevelInconsistentOnly = reader.Boolean("top_level_inconsistent_only", defaultValue: false);
            var insideInconsistentOnly = reader.Boolean("inside_inconsistent_only", defaultValue: false);
            if (insideInconsistentOnly && !verifyInheritance) {
                return ToolRequestBindingResult<GpoPermissionConsistencyRequest>.Failure(
                    "inside_inconsistent_only requires verify_inheritance=true.");
            }

            return ToolRequestBindingResult<GpoPermissionConsistencyRequest>.Success(new GpoPermissionConsistencyRequest(
                DomainName: domainName,
                VerifyInheritance: verifyInheritance,
                IncludeConsistent: includeConsistent,
                TopLevelInconsistentOnly: topLevelInconsistentOnly,
                InsideInconsistentOnly: insideInconsistentOnly,
                MaxGpos: reader.CappedInt32("max_gpos", 50000, 1, 200000),
                SysvolScanCap: reader.CappedInt32("sysvol_scan_cap", 2000, 1, 500000),
                MaxResults: reader.CappedInt32("max_results", Options.MaxResults, 1, Options.MaxResults)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoPermissionConsistencyRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var domainName = request.DomainName;
        var verifyInheritance = request.VerifyInheritance;
        var includeConsistent = request.IncludeConsistent;
        var topLevelInconsistentOnly = request.TopLevelInconsistentOnly;
        var insideInconsistentOnly = request.InsideInconsistentOnly;
        var maxGpos = request.MaxGpos;
        var sysvolScanCap = request.SysvolScanCap;
        var maxResults = request.MaxResults;

        if (!TryExecuteCollectionQuery(
                query: () => GpoPermissionConsistencyService.Get(domainName, verifyInheritance: verifyInheritance, includeConsistent: includeConsistent, maxGpos: maxGpos, sysvolScanCap: sysvolScanCap),
                collectionSucceededSelector: static view => view.CollectionSucceeded,
                collectionErrorSelector: static view => view.CollectionError,
                result: out var view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "GPO permission consistency query failed.")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(row => !topLevelInconsistentOnly || row.AclConsistent == false)
            .Where(row => !insideInconsistentOnly || row.AclConsistentInside == false)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var result = new AdGpoPermissionConsistencyResult(
            DomainName: domainName,
            VerifyInheritance: verifyInheritance,
            IncludeConsistent: includeConsistent,
            TopLevelInconsistentOnly: topLevelInconsistentOnly,
            InsideInconsistentOnly: insideInconsistentOnly,
            MaxGpos: maxGpos,
            SysvolScanCap: sysvolScanCap,
            Scanned: scanned,
            Truncated: truncated,
            TopLevelMismatchCount: filtered.Count(static row => row.AclConsistent == false),
            InsideMismatchCount: filtered.Count(static row => row.AclConsistentInside == false),
            ErrorCount: filtered.Count(static row => !string.IsNullOrWhiteSpace(row.Error)),
            Rows: projectedRows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Permission Consistency (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("verify_inheritance", verifyInheritance);
                meta.Add("include_consistent", includeConsistent);
                meta.Add("top_level_inconsistent_only", topLevelInconsistentOnly);
                meta.Add("inside_inconsistent_only", insideInconsistentOnly);
                meta.Add("max_gpos", maxGpos);
                meta.Add("sysvol_scan_cap", sysvolScanCap);
            }));
    }
}
