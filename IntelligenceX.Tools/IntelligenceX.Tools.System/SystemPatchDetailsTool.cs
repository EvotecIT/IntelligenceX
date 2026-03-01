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

    private sealed record SystemPatchDetailsResult(
        int Year,
        int Month,
        string Release,
        bool ProductMappedFilterApplied,
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
        int Scanned,
        bool Truncated,
        PatchDetailsSummary Summary,
        IReadOnlyList<PatchDetailsInfo> Patches);

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
        var (monthly, patchError) = await TryGetMonthlyPatchDetailsAsync(
            year: request.Year,
            month: request.Month,
            productFamily: request.ProductFamily,
            productVersion: request.ProductVersion,
            productBuild: request.ProductBuild,
            productEdition: request.ProductEdition,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (patchError is not null) {
            return patchError;
        }

        var severitySet = request.Severity.Count == 0
            ? null
            : new HashSet<string>(request.Severity, StringComparer.OrdinalIgnoreCase);

        var filtered = monthly
            .Where(x => severitySet is null || severitySet.Contains(x.Severity))
            .Where(x => !request.ExploitedOnly || x.IsExploited)
            .Where(x => !request.PubliclyDisclosedOnly || x.PubliclyDisclosed)
            .Where(x => string.IsNullOrWhiteSpace(request.CveContains)
                || x.CveId.Contains(request.CveContains, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(request.KbContains)
                || SystemPatchKbNormalization.MatchesContainsFilter(x.Kbs, request.KbContains))
            .Where(x => request.ProductNameContains.Count == 0
                || (x.Products?.Any(product =>
                    request.ProductNameContains.Any(filter => product.Contains(filter, StringComparison.OrdinalIgnoreCase))) ?? false))
            .OrderByDescending(static x => x.Published ?? DateTime.MinValue)
            .ThenBy(static x => x.CveId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = CapRows(filtered, request.MaxResults, out var scanned, out var truncated);
        var summary = BuildSummary(filtered);
        var release = new DateTime(request.Year, request.Month, 1).ToString("yyyy-MM");

        var result = new SystemPatchDetailsResult(
            Year: request.Year,
            Month: request.Month,
            Release: release,
            ProductMappedFilterApplied: !string.IsNullOrWhiteSpace(request.ProductFamily),
            ProductFamily: request.ProductFamily,
            ProductVersion: request.ProductVersion,
            ProductBuild: request.ProductBuild,
            ProductEdition: request.ProductEdition,
            ProductNameContains: request.ProductNameContains,
            Severity: request.Severity,
            ExploitedOnly: request.ExploitedOnly,
            PubliclyDisclosedOnly: request.PubliclyDisclosedOnly,
            CveContains: request.CveContains,
            KbContains: request.KbContains,
            Scanned: scanned,
            Truncated: truncated,
            Summary: summary,
            Patches: rows);

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "patches_view",
            title: "System patch details (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, request.MaxResults);
                AddPatchFilterMeta(
                    meta: meta,
                    year: request.Year,
                    month: request.Month,
                    release: release,
                    productFamily: request.ProductFamily,
                    productVersion: request.ProductVersion,
                    productBuild: request.ProductBuild,
                    productEdition: request.ProductEdition,
                    severity: request.Severity,
                    exploitedOnly: request.ExploitedOnly,
                    publiclyDisclosedOnly: request.PubliclyDisclosedOnly,
                    cveContains: request.CveContains,
                    kbContains: request.KbContains);
                if (request.ProductNameContains.Count > 0) {
                    meta.Add("product_name_contains", string.Join(", ", request.ProductNameContains));
                }
            });

        return response;
    }

    private static PatchDetailsSummary BuildSummary(IReadOnlyList<PatchDetailsInfo> rows) {
        var summary = new PatchDetailsSummary {
            Total = rows.Count
        };

        var newKbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notable = new List<string>();
        var highestRated = new List<string>();

        foreach (var x in rows) {
            if (x.IsExploited) {
                summary.Exploited++;
                if (!string.IsNullOrWhiteSpace(x.CveId)) {
                    notable.Add(x.CveId);
                }
            }
            if (x.PubliclyDisclosed) {
                summary.PubliclyDisclosed++;
            }
            if (x.ExploitationMoreLikely) {
                summary.ExploitationMoreLikely++;
            }

            if (string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase)) {
                summary.Critical++;
            } else if (string.Equals(x.Severity, "Important", StringComparison.OrdinalIgnoreCase)) {
                summary.Important++;
            }

            var category = x.Category ?? string.Empty;
            if (category.IndexOf("Elevation of Privilege", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.ElevationOfPrivilege++;
            } else if (category.IndexOf("Security Feature Bypass", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.SecurityFeatureBypass++;
            } else if (category.IndexOf("Remote Code Execution", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.RemoteCodeExecution++;
            } else if (category.IndexOf("Information Disclosure", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.InformationDisclosure++;
            } else if (category.IndexOf("Denial of Service", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.DenialOfService++;
            } else if (category.IndexOf("Spoofing", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.Spoofing++;
            } else if (category.IndexOf("Edge - Chromium", StringComparison.OrdinalIgnoreCase) >= 0) {
                summary.EdgeChromium++;
            }

            if (x.Cvss.HasValue && x.Cvss.Value >= 8.0 && !string.IsNullOrWhiteSpace(x.CveId)) {
                highestRated.Add(x.CveId);
            }

            foreach (var kb in SystemPatchKbNormalization.NormalizeDistinct(x.Kbs)) {
                newKbs.Add(kb);
            }
        }

        summary.NewKbs = newKbs.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        summary.NotableCves = notable.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        summary.HighestRatedCves = highestRated.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return summary;
    }
}
