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
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var verifyInheritance = ToolArgs.GetBoolean(arguments, "verify_inheritance", defaultValue: false);
        var includeConsistent = ToolArgs.GetBoolean(arguments, "include_consistent", defaultValue: false);
        var topLevelInconsistentOnly = ToolArgs.GetBoolean(arguments, "top_level_inconsistent_only", defaultValue: false);
        var insideInconsistentOnly = ToolArgs.GetBoolean(arguments, "inside_inconsistent_only", defaultValue: false);
        if (insideInconsistentOnly && !verifyInheritance) {
            return Task.FromResult(ToolResponse.Error(
                "invalid_argument",
                "inside_inconsistent_only requires verify_inheritance=true."));
        }

        var maxGpos = ToolArgs.GetCappedInt32(arguments, "max_gpos", 50000, 1, 200000);
        var sysvolScanCap = ToolArgs.GetCappedInt32(arguments, "sysvol_scan_cap", 2000, 1, 500000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var view = GpoPermissionConsistencyService.Get(
            domainName,
            verifyInheritance: verifyInheritance,
            includeConsistent: includeConsistent,
            maxGpos: maxGpos,
            sysvolScanCap: sysvolScanCap);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "GPO permission consistency query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var filtered = view.Items
            .Where(row => !topLevelInconsistentOnly || row.AclConsistent == false)
            .Where(row => !insideInconsistentOnly || row.AclConsistentInside == false)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<GpoPermissionConsistency> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

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

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Permission Consistency (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("verify_inheritance", verifyInheritance);
                meta.Add("include_consistent", includeConsistent);
                meta.Add("top_level_inconsistent_only", topLevelInconsistentOnly);
                meta.Add("inside_inconsistent_only", insideInconsistentOnly);
                meta.Add("max_gpos", maxGpos);
                meta.Add("sysvol_scan_cap", sysvolScanCap);
                meta.Add("max_results", maxResults);
            }));
    }
}
