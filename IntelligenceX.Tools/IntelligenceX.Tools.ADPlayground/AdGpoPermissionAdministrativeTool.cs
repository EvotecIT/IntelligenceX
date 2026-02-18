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
/// Returns Domain Admins/Enterprise Admins GPO permission posture for one domain (read-only).
/// </summary>
public sealed class AdGpoPermissionAdministrativeTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_permission_administrative",
        "Assess Domain Admins and Enterprise Admins management rights on GPOs (GPOPermissionsAdministrative parity, read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_compliant", ToolSchema.Boolean("When true, include compliant rows alongside findings.")),
                ("errors_only", ToolSchema.Boolean("When true, return only rows where evaluation produced an error.")),
                ("max_gpos", ToolSchema.Integer("Maximum GPOs to process (capped). Default 50000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdGpoPermissionAdministrativeResult(
        string DomainName,
        bool IncludeCompliant,
        bool ErrorsOnly,
        int MaxGpos,
        int Scanned,
        bool Truncated,
        int NonCompliantCount,
        int ErrorCount,
        IReadOnlyList<GpoPermissionAdministrativeRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoPermissionAdministrativeTool"/> class.
    /// </summary>
    public AdGpoPermissionAdministrativeTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var includeCompliant = ToolArgs.GetBoolean(arguments, "include_compliant", defaultValue: false);
        var errorsOnly = ToolArgs.GetBoolean(arguments, "errors_only", defaultValue: false);
        var maxGpos = ToolArgs.GetCappedInt32(arguments, "max_gpos", 50000, 1, 200000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var view = GpoPermissionAdministrativeService.Get(domainName, includeCompliant: includeCompliant, maxGpos: maxGpos);
        if (!view.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(view.CollectionError)
                ? "GPO administrative-permission baseline query failed."
                : view.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var filtered = view.Items
            .Where(row => !errorsOnly || !string.IsNullOrWhiteSpace(row.Error))
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<GpoPermissionAdministrativeRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdGpoPermissionAdministrativeResult(
            DomainName: domainName,
            IncludeCompliant: includeCompliant,
            ErrorsOnly: errorsOnly,
            MaxGpos: maxGpos,
            Scanned: scanned,
            Truncated: truncated,
            NonCompliantCount: filtered.Count(static row => !row.IsCompliant),
            ErrorCount: filtered.Count(static row => !string.IsNullOrWhiteSpace(row.Error)),
            Rows: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO Administrative Permissions (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("include_compliant", includeCompliant);
                meta.Add("errors_only", errorsOnly);
                meta.Add("max_gpos", maxGpos);
                meta.Add("max_results", maxResults);
            });
        return Task.FromResult(response);
    }
}
