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
    private const int DefaultMaxGpos = 50000;
    private const int MaxGposCap = 200000;

    internal readonly record struct GpoPermissionReadBindingContract(
        bool IncludeCompliant,
        bool DenyOnly,
        int MaxGpos);

    private sealed record GpoPermissionReadRequest(
        bool IncludeCompliant,
        bool DenyOnly,
        int MaxGpos);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<GpoPermissionReadRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader =>
            ToolRequestBindingResult<GpoPermissionReadRequest>.Success(new GpoPermissionReadRequest(
                IncludeCompliant: reader.Boolean("include_compliant", defaultValue: false),
                DenyOnly: reader.Boolean("deny_only", defaultValue: false),
                MaxGpos: reader.CappedInt32("max_gpos", DefaultMaxGpos, 1, MaxGposCap))));
    }

    internal static ToolRequestBindingResult<GpoPermissionReadBindingContract> BindRequestContract(JsonObject? arguments) {
        var binding = BindRequest(arguments);
        if (!binding.IsValid || binding.Request is null) {
            return ToolRequestBindingResult<GpoPermissionReadBindingContract>.Failure(
                binding.Error,
                binding.ErrorCode,
                binding.Hints,
                binding.IsTransient);
        }

        var request = binding.Request;
        return ToolRequestBindingResult<GpoPermissionReadBindingContract>.Success(new GpoPermissionReadBindingContract(
            IncludeCompliant: request.IncludeCompliant,
            DenyOnly: request.DenyOnly,
            MaxGpos: request.MaxGpos));
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoPermissionReadRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        return ExecuteDomainRowsViewTool(
            arguments: context.Arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: GPO Permission Read Baseline (preview)",
            defaultErrorMessage: "GPO read-permission baseline query failed.",
            maxTop: MaxViewTop,
            query: domainName => GpoPermissionReadService.Get(domainName, includeCompliant: request.IncludeCompliant, maxGpos: request.MaxGpos),
            collectionSucceededSelector: static view => view.CollectionSucceeded,
            collectionErrorSelector: static view => view.CollectionError,
            allRowsSelector: view => view.Items
                .Where(row => !request.DenyOnly || row.HasAuthenticatedUsersDeny)
                .ToArray(),
            resultFactory: (domainName, _, allRows, rows, scanned, truncated) => new AdGpoPermissionReadResult(
                DomainName: domainName,
                IncludeCompliant: request.IncludeCompliant,
                DenyOnly: request.DenyOnly,
                MaxGpos: request.MaxGpos,
                Scanned: scanned,
                Truncated: truncated,
                NonCompliantCount: allRows.Count(static row => !row.IsCompliant),
                DenyCount: allRows.Count(static row => row.HasAuthenticatedUsersDeny),
                ErrorCount: allRows.Count(static row => !string.IsNullOrWhiteSpace(row.Error)),
                Rows: rows),
            additionalMetaMutate: (meta, _, _, _) => {
                meta.Add("include_compliant", request.IncludeCompliant);
                meta.Add("deny_only", request.DenyOnly);
                meta.Add("max_gpos", request.MaxGpos);
            });
    }
}
