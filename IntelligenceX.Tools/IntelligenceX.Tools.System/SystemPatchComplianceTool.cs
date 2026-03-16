using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.PatchDetails;
using ComputerX.Updates;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Correlates MSRC monthly patch details with locally installed updates to estimate patch compliance.
/// </summary>
public sealed class SystemPatchComplianceTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_patch_compliance",
        "Correlate monthly MSRC patch details with installed updates to estimate host patch compliance for KB-backed vulnerabilities.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("year", ToolSchema.Integer("Optional release year. Defaults to current UTC year.")),
                ("month", ToolSchema.Integer("Optional release month (1-12). Defaults to current UTC month.")),
                ("product_family", ToolSchema.String("Optional product family for mapped filtering (e.g. Windows, SQL Server, .NET).")),
                ("product_version", ToolSchema.String("Optional product version/line for mapped filtering (e.g. 11, Server 2022).")),
                ("product_build", ToolSchema.String("Optional product build hint used by mapping (e.g. 26100).")),
                ("product_edition", ToolSchema.String("Optional product edition hint (e.g. Enterprise, Datacenter).")),
                ("severity", ToolSchema.Array(ToolSchema.String().Enum("Critical", "Important", "Moderate", "Low"), "Optional severity allowlist.")),
                ("exploited_only", ToolSchema.Boolean("When true, keep only vulnerabilities flagged as exploited in the wild.")),
                ("publicly_disclosed_only", ToolSchema.Boolean("When true, keep only publicly disclosed vulnerabilities.")),
                ("missing_only", ToolSchema.Boolean("When true, return only rows with missing KB coverage.")),
                ("include_pending_local", ToolSchema.Boolean("When true and querying local machine, include pending local updates in KB matching.")),
                ("cve_contains", ToolSchema.String("Optional case-insensitive substring filter against CVE ID.")),
                ("kb_contains", ToolSchema.String("Optional case-insensitive substring filter against KB identifiers.")),
                ("max_results", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record PatchComplianceRequest(
        string? ComputerName,
        bool IncludePendingLocal,
        bool MissingOnly,
        bool ExploitedOnly,
        bool PubliclyDisclosedOnly,
        string? CveContains,
        string? KbContains,
        int MaxResults);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPatchComplianceTool"/> class.
    /// </summary>
    public SystemPatchComplianceTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<PatchComplianceRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => ToolRequestBindingResult<PatchComplianceRequest>.Success(new PatchComplianceRequest(
            ComputerName: reader.OptionalString("computer_name"),
            IncludePendingLocal: reader.Boolean("include_pending_local", defaultValue: false),
            MissingOnly: reader.Boolean("missing_only", defaultValue: false),
            ExploitedOnly: reader.Boolean("exploited_only", defaultValue: false),
            PubliclyDisclosedOnly: reader.Boolean("publicly_disclosed_only", defaultValue: false),
            CveContains: reader.OptionalString("cve_contains"),
            KbContains: reader.OptionalString("kb_contains"),
            MaxResults: ResolveMaxResults(arguments))));
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<PatchComplianceRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var windowsError = ValidateWindowsSupport("system_patch_compliance");
        if (windowsError is not null) {
            return windowsError;
        }

        var target = ResolveTargetComputerName(request.ComputerName);

        if (!TryResolvePatchReleaseWindow(context.Arguments, out var year, out var month, out var releaseError)) {
            return releaseError!;
        }
        if (!TryResolvePatchProductFilter(
                context.Arguments,
                out var productFamily,
                out var productVersion,
                out var productBuild,
                out var productEdition,
                out var productError)) {
            return productError!;
        }
        if (!TryResolvePatchSeverityAllowlist(context.Arguments, out var severity, out var severityError)) {
            return severityError!;
        }

        PatchComplianceQueryResult result;
        try {
            result = await PatchComplianceQuery.GetAsync(new PatchComplianceQueryOptions {
                ComputerName = request.ComputerName,
                Year = year,
                Month = month,
                ProductFamily = productFamily,
                ProductVersion = productVersion,
                ProductBuild = productBuild,
                ProductEdition = productEdition,
                Severity = severity,
                ExploitedOnly = request.ExploitedOnly,
                PubliclyDisclosedOnly = request.PubliclyDisclosedOnly,
                MissingOnly = request.MissingOnly,
                IncludePendingLocal = request.IncludePendingLocal,
                CveContains = request.CveContains,
                KbContains = request.KbContains,
                MaxResults = request.MaxResults
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Patch compliance query failed.");
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Compliance,
            viewRowsPath: "compliance_view",
            title: "Patch compliance (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, result.ComputerName);
                AddMaxResultsMeta(meta, request.MaxResults);
                AddPendingLocalMeta(meta, result.IncludePendingLocal, result.PendingIncluded);
                AddPatchFilterMeta(
                    meta: meta,
                    year: result.Year,
                    month: result.Month,
                    release: result.Release,
                    productFamily: result.ProductFamily,
                    productVersion: result.ProductVersion,
                    productBuild: result.ProductBuild,
                    productEdition: result.ProductEdition,
                    severity: result.Severity,
                    exploitedOnly: result.ExploitedOnly,
                    publiclyDisclosedOnly: result.PubliclyDisclosedOnly,
                    cveContains: result.CveContains,
                    kbContains: result.KbContains);
                if (result.MissingOnly) {
                    meta.Add("missing_only", true);
                }
            });
        return response;
    }
}
