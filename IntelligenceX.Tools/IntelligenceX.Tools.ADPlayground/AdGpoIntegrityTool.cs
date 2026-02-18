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
/// Returns GPO AD/SYSVOL integrity mismatches for one domain (read-only).
/// </summary>
public sealed class AdGpoIntegrityTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_integrity",
        "Detect GPO integrity mismatches (AD object missing vs SYSVOL folder missing) for one domain (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("sysvol_missing_only", ToolSchema.Boolean("When true, return only rows where SYSVOL path is missing for an AD-backed GPO.")),
                ("ad_missing_only", ToolSchema.Boolean("When true, return only rows where a SYSVOL folder has no AD object.")),
                ("errors_only", ToolSchema.Boolean("When true, return only rows that include error text.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoIntegrityResult(
        string DomainName,
        bool SysvolMissingOnly,
        bool AdMissingOnly,
        bool ErrorsOnly,
        int Scanned,
        bool Truncated,
        int SysvolMissingCount,
        int AdMissingCount,
        int ErrorCount,
        IReadOnlyList<GpoIntegrityService.GpoBrokenItem> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoIntegrityTool"/> class.
    /// </summary>
    public AdGpoIntegrityTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var sysvolMissingOnly = ToolArgs.GetBoolean(arguments, "sysvol_missing_only", defaultValue: false);
        var adMissingOnly = ToolArgs.GetBoolean(arguments, "ad_missing_only", defaultValue: false);
        var errorsOnly = ToolArgs.GetBoolean(arguments, "errors_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var view = GpoIntegrityService.Get(domainName);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "GPO integrity query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var filtered = view.Items
            .Where(row => !sysvolMissingOnly || row.Status.Contains("SYSVOL", StringComparison.OrdinalIgnoreCase))
            .Where(row => !adMissingOnly || row.Status.Contains("AD object missing", StringComparison.OrdinalIgnoreCase))
            .Where(row => !errorsOnly || !string.IsNullOrWhiteSpace(row.Error))
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<GpoIntegrityService.GpoBrokenItem> rows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > rows.Count;

        var result = new AdGpoIntegrityResult(
            DomainName: domainName,
            SysvolMissingOnly: sysvolMissingOnly,
            AdMissingOnly: adMissingOnly,
            ErrorsOnly: errorsOnly,
            Scanned: scanned,
            Truncated: truncated,
            SysvolMissingCount: filtered.Count(static row => row.Status.Contains("SYSVOL", StringComparison.OrdinalIgnoreCase)),
            AdMissingCount: filtered.Count(static row => row.Status.Contains("AD object missing", StringComparison.OrdinalIgnoreCase)),
            ErrorCount: filtered.Count(static row => !string.IsNullOrWhiteSpace(row.Error)),
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Integrity (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("sysvol_missing_only", sysvolMissingOnly);
                meta.Add("ad_missing_only", adMissingOnly);
                meta.Add("errors_only", errorsOnly);
                meta.Add("max_results", maxResults);
            }));
    }
}
