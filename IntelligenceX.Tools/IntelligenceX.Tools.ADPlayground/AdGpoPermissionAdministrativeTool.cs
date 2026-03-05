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
    private const int DefaultMaxGpos = 50000;
    private const int MaxGposCap = 200000;

    internal readonly record struct GpoPermissionAdministrativeBindingContract(
        bool IncludeCompliant,
        bool ErrorsOnly,
        int MaxGpos);

    private sealed record GpoPermissionAdministrativeRequest(
        bool IncludeCompliant,
        bool ErrorsOnly,
        int MaxGpos);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<GpoPermissionAdministrativeRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader =>
            ToolRequestBindingResult<GpoPermissionAdministrativeRequest>.Success(new GpoPermissionAdministrativeRequest(
                IncludeCompliant: reader.Boolean("include_compliant", defaultValue: false),
                ErrorsOnly: reader.Boolean("errors_only", defaultValue: false),
                MaxGpos: reader.CappedInt32("max_gpos", DefaultMaxGpos, 1, MaxGposCap))));
    }

    internal static ToolRequestBindingResult<GpoPermissionAdministrativeBindingContract> BindRequestContract(JsonObject? arguments) {
        var binding = BindRequest(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return ToolRequestBindingResult<GpoPermissionAdministrativeBindingContract>.Failure(
                binding.Error,
                binding.ErrorCode,
                binding.Hints,
                binding.IsTransient);
        }

        var request = binding.Request;
        return ToolRequestBindingResult<GpoPermissionAdministrativeBindingContract>.Success(new GpoPermissionAdministrativeBindingContract(
            IncludeCompliant: request.IncludeCompliant,
            ErrorsOnly: request.ErrorsOnly,
            MaxGpos: request.MaxGpos));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoPermissionAdministrativeRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        return ExecuteDomainRowsViewTool(
            arguments: context.Arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: GPO Administrative Permissions (preview)",
            defaultErrorMessage: "GPO administrative-permission baseline query failed.",
            maxTop: MaxViewTop,
            query: domainName => GpoPermissionAdministrativeService.Get(domainName, includeCompliant: request.IncludeCompliant, maxGpos: request.MaxGpos),
            collectionSucceededSelector: static view => view.CollectionSucceeded,
            collectionErrorSelector: static view => view.CollectionError,
            allRowsSelector: view => view.Items
                .Where(row => !request.ErrorsOnly || !string.IsNullOrWhiteSpace(row.Error))
                .ToArray(),
            resultFactory: (domainName, _, allRows, rows, scanned, truncated) => new AdGpoPermissionAdministrativeResult(
                DomainName: domainName,
                IncludeCompliant: request.IncludeCompliant,
                ErrorsOnly: request.ErrorsOnly,
                MaxGpos: request.MaxGpos,
                Scanned: scanned,
                Truncated: truncated,
                NonCompliantCount: allRows.Count(static row => !row.IsCompliant),
                ErrorCount: allRows.Count(static row => !string.IsNullOrWhiteSpace(row.Error)),
                Rows: rows),
            additionalMetaMutate: (meta, _, _, _) => {
                meta.Add("include_compliant", request.IncludeCompliant);
                meta.Add("errors_only", request.ErrorsOnly);
                meta.Add("max_gpos", request.MaxGpos);
            });
    }
}
