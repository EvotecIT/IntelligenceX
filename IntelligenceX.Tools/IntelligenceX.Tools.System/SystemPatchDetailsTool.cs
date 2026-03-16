using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.PatchDetails;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns MSRC monthly patch details (CVE/KB/severity/product) with optional filters.
/// </summary>
public sealed class SystemPatchDetailsTool : SystemToolBase, ITool {
    private sealed record PatchDetailsRequest(
        int Year,
        int Month,
        string? ProductFamily,
        string? ProductVersion,
        string? ProductBuild,
        string? ProductEdition,
        IReadOnlyList<string> ProductNameContains,
        IReadOnlyList<string> Severity,
        bool ExploitedOnly,
        bool PubliclyDisclosedOnly,
        string? CveContains,
        string? KbContains,
        int MaxResults);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_patch_details",
        "Return MSRC patch details for a month (default current UTC month), including CVE/KB/product metadata and summary counters.",
        ToolSchema.Object(
                ("year", ToolSchema.Integer("Optional release year. Defaults to current UTC year.")),
                ("month", ToolSchema.Integer("Optional release month (1-12). Defaults to current UTC month.")),
                ("product_family", ToolSchema.String("Optional product family for mapped filtering (e.g. Windows, SQL Server, .NET).")),
                ("product_version", ToolSchema.String("Optional product version/line for mapped filtering (e.g. 11, Server 2022).")),
                ("product_build", ToolSchema.String("Optional product build hint used by mapping (e.g. 26100).")),
                ("product_edition", ToolSchema.String("Optional product edition hint (e.g. Enterprise, Datacenter).")),
                ("product_name_contains", ToolSchema.Array(ToolSchema.String(), "Optional product-name substring filters matched against MSRC product names.")),
                ("severity", ToolSchema.Array(ToolSchema.String().Enum("Critical", "Important", "Moderate", "Low"), "Optional severity allowlist.")),
                ("exploited_only", ToolSchema.Boolean("When true, keep only vulnerabilities flagged as exploited in the wild.")),
                ("publicly_disclosed_only", ToolSchema.Boolean("When true, keep only publicly disclosed vulnerabilities.")),
                ("cve_contains", ToolSchema.String("Optional case-insensitive substring filter against CVE ID.")),
                ("kb_contains", ToolSchema.String("Optional case-insensitive substring filter against KB identifiers.")),
                ("max_results", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPatchDetailsTool"/> class.
    /// </summary>
    public SystemPatchDetailsTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<PatchDetailsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var nowUtc = DateTime.UtcNow;
            var year = nowUtc.Year;
            var month = nowUtc.Month;

            var yearRaw = reader.OptionalInt64("year");
            if (yearRaw.HasValue) {
                if (yearRaw.Value < 2000 || yearRaw.Value > 2100) {
                    return ToolRequestBindingResult<PatchDetailsRequest>.Failure("year must be between 2000 and 2100.");
                }

                year = (int)yearRaw.Value;
            }

            var monthRaw = reader.OptionalInt64("month");
            if (monthRaw.HasValue) {
                if (monthRaw.Value < 1 || monthRaw.Value > 12) {
                    return ToolRequestBindingResult<PatchDetailsRequest>.Failure("month must be between 1 and 12.");
                }

                month = (int)monthRaw.Value;
            }

            var productFamily = reader.OptionalString("product_family");
            var productVersion = reader.OptionalString("product_version");
            var productBuild = reader.OptionalString("product_build");
            var productEdition = reader.OptionalString("product_edition");
            if (string.IsNullOrWhiteSpace(productFamily)
                && (!string.IsNullOrWhiteSpace(productVersion)
                    || !string.IsNullOrWhiteSpace(productBuild)
                    || !string.IsNullOrWhiteSpace(productEdition))) {
                return ToolRequestBindingResult<PatchDetailsRequest>.Failure(
                    "product_family is required when product_version/product_build/product_edition is provided.");
            }

            var productNameContains = reader.DistinctStringArray("product_name_contains");
            if (productNameContains.Count > 20) {
                productNameContains = productNameContains.Take(20).ToList();
            }

            var severityRaw = reader.DistinctStringArray("severity");
            var severity = new List<string>(severityRaw.Count);
            for (var i = 0; i < severityRaw.Count; i++) {
                var item = severityRaw[i];
                if (string.Equals(item, "Critical", StringComparison.OrdinalIgnoreCase)) {
                    severity.Add("Critical");
                } else if (string.Equals(item, "Important", StringComparison.OrdinalIgnoreCase)) {
                    severity.Add("Important");
                } else if (string.Equals(item, "Moderate", StringComparison.OrdinalIgnoreCase)) {
                    severity.Add("Moderate");
                } else if (string.Equals(item, "Low", StringComparison.OrdinalIgnoreCase)) {
                    severity.Add("Low");
                } else {
                    return ToolRequestBindingResult<PatchDetailsRequest>.Failure(
                        "severity contains unsupported value. Allowed: Critical, Important, Moderate, Low.");
                }
            }

            return ToolRequestBindingResult<PatchDetailsRequest>.Success(new PatchDetailsRequest(
                Year: year,
                Month: month,
                ProductFamily: productFamily,
                ProductVersion: productVersion,
                ProductBuild: productBuild,
                ProductEdition: productEdition,
                ProductNameContains: productNameContains,
                Severity: severity,
                ExploitedOnly: reader.Boolean("exploited_only", defaultValue: false),
                PubliclyDisclosedOnly: reader.Boolean("publicly_disclosed_only", defaultValue: false),
                CveContains: reader.OptionalString("cve_contains"),
                KbContains: reader.OptionalString("kb_contains"),
                MaxResults: ResolveMaxResults(arguments)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<PatchDetailsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        PatchCatalogQueryResult result;
        try {
            result = await PatchCatalogQuery.GetAsync(new PatchCatalogQueryOptions {
                Year = request.Year,
                Month = request.Month,
                ProductFamily = request.ProductFamily,
                ProductVersion = request.ProductVersion,
                ProductBuild = request.ProductBuild,
                ProductEdition = request.ProductEdition,
                ProductNameContains = request.ProductNameContains,
                Severity = request.Severity,
                ExploitedOnly = request.ExploitedOnly,
                PubliclyDisclosedOnly = request.PubliclyDisclosedOnly,
                CveContains = request.CveContains,
                KbContains = request.KbContains,
                MaxResults = request.MaxResults
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Patch details query failed.");
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Patches,
            viewRowsPath: "patches_view",
            title: "System patch details (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, request.MaxResults);
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
                if (result.ProductNameContains.Count > 0) {
                    meta.Add("product_name_contains", string.Join(", ", result.ProductNameContains));
                }
            });

        return response;
    }
}
