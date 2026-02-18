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
    private const int MaxViewTop = 5000;

    private static readonly string[] AllowedSeverities = {
        "Critical",
        "Important",
        "Moderate",
        "Low"
    };

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
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTime.UtcNow;
        var year = nowUtc.Year;
        var month = nowUtc.Month;

        var yearRaw = arguments?.GetInt64("year");
        var monthRaw = arguments?.GetInt64("month");
        if (yearRaw.HasValue) {
            if (yearRaw.Value < 2000 || yearRaw.Value > 2100) {
                return ToolResponse.Error("invalid_argument", "year must be between 2000 and 2100.");
            }
            year = (int)yearRaw.Value;
        }
        if (monthRaw.HasValue) {
            if (monthRaw.Value < 1 || monthRaw.Value > 12) {
                return ToolResponse.Error("invalid_argument", "month must be between 1 and 12.");
            }
            month = (int)monthRaw.Value;
        }

        var productFamily = ToolArgs.GetOptionalTrimmed(arguments, "product_family");
        var productVersion = ToolArgs.GetOptionalTrimmed(arguments, "product_version");
        var productBuild = ToolArgs.GetOptionalTrimmed(arguments, "product_build");
        var productEdition = ToolArgs.GetOptionalTrimmed(arguments, "product_edition");
        if (string.IsNullOrWhiteSpace(productFamily)
            && (!string.IsNullOrWhiteSpace(productVersion)
                || !string.IsNullOrWhiteSpace(productBuild)
                || !string.IsNullOrWhiteSpace(productEdition))) {
            return ToolResponse.Error("invalid_argument", "product_family is required when product_version/product_build/product_edition is provided.");
        }

        var productNameContains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("product_name_contains"));
        if (productNameContains.Count > 20) {
            productNameContains = productNameContains.Take(20).ToList();
        }

        var severityRaw = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("severity"));
        var severity = new List<string>(severityRaw.Count);
        foreach (var item in severityRaw) {
            if (!TryNormalizeSeverity(item, out var normalized)) {
                return ToolResponse.Error("invalid_argument", "severity contains unsupported value. Allowed: Critical, Important, Moderate, Low.");
            }
            severity.Add(normalized);
        }

        var exploitedOnly = ToolArgs.GetBoolean(arguments, "exploited_only", defaultValue: false);
        var publiclyDisclosedOnly = ToolArgs.GetBoolean(arguments, "publicly_disclosed_only", defaultValue: false);
        var cveContains = ToolArgs.GetOptionalTrimmed(arguments, "cve_contains");
        var kbContains = ToolArgs.GetOptionalTrimmed(arguments, "kb_contains");
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        IReadOnlyList<PatchDetailsInfo> monthly;
        try {
            if (!string.IsNullOrWhiteSpace(productFamily)) {
                var descriptor = new ProductDescriptor {
                    Family = productFamily!,
                    Version = productVersion ?? string.Empty,
                    Build = productBuild,
                    Edition = productEdition
                };
                monthly = await PatchDetails.GetForProductsAsync(
                    products: new[] { descriptor },
                    since: new DateTime(year, month, 1),
                    ct: cancellationToken).ConfigureAwait(false);
            } else {
                monthly = await PatchDetails.GetMonthlyAsync(year, month, cancellationToken).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            return ToolResponse.Error("query_failed", $"Patch details query failed: {ex.Message}");
        }

        var severitySet = severity.Count == 0
            ? null
            : new HashSet<string>(severity, StringComparer.OrdinalIgnoreCase);

        var filtered = monthly
            .Where(x => severitySet is null || severitySet.Contains(x.Severity))
            .Where(x => !exploitedOnly || x.IsExploited)
            .Where(x => !publiclyDisclosedOnly || x.PubliclyDisclosed)
            .Where(x => string.IsNullOrWhiteSpace(cveContains)
                || x.CveId.Contains(cveContains, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(kbContains)
                || (x.Kbs?.Any(kb => kb.Contains(kbContains, StringComparison.OrdinalIgnoreCase)) ?? false))
            .Where(x => productNameContains.Count == 0
                || (x.Products?.Any(product =>
                    productNameContains.Any(filter => product.Contains(filter, StringComparison.OrdinalIgnoreCase))) ?? false))
            .OrderByDescending(static x => x.Published ?? DateTime.MinValue)
            .ThenBy(static x => x.CveId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<PatchDetailsInfo> rows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > rows.Count;
        var summary = BuildSummary(filtered);
        var release = new DateTime(year, month, 1).ToString("yyyy-MM");

        var result = new SystemPatchDetailsResult(
            Year: year,
            Month: month,
            Release: release,
            ProductMappedFilterApplied: !string.IsNullOrWhiteSpace(productFamily),
            ProductFamily: productFamily,
            ProductVersion: productVersion,
            ProductBuild: productBuild,
            ProductEdition: productEdition,
            ProductNameContains: productNameContains,
            Severity: severity,
            ExploitedOnly: exploitedOnly,
            PubliclyDisclosedOnly: publiclyDisclosedOnly,
            CveContains: cveContains,
            KbContains: kbContains,
            Scanned: scanned,
            Truncated: truncated,
            Summary: summary,
            Patches: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "patches_view",
            title: "System patch details (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("year", year);
                meta.Add("month", month);
                meta.Add("release", release);
                meta.Add("max_results", maxResults);
                meta.Add("product_mapped_filter_applied", !string.IsNullOrWhiteSpace(productFamily));
                if (!string.IsNullOrWhiteSpace(productFamily)) {
                    meta.Add("product_family", productFamily);
                }
                if (!string.IsNullOrWhiteSpace(productVersion)) {
                    meta.Add("product_version", productVersion);
                }
                if (!string.IsNullOrWhiteSpace(productBuild)) {
                    meta.Add("product_build", productBuild);
                }
                if (!string.IsNullOrWhiteSpace(productEdition)) {
                    meta.Add("product_edition", productEdition);
                }
                if (productNameContains.Count > 0) {
                    meta.Add("product_name_contains", string.Join(", ", productNameContains));
                }
                if (severity.Count > 0) {
                    meta.Add("severity", string.Join(", ", severity));
                }
                if (exploitedOnly) {
                    meta.Add("exploited_only", true);
                }
                if (publiclyDisclosedOnly) {
                    meta.Add("publicly_disclosed_only", true);
                }
                if (!string.IsNullOrWhiteSpace(cveContains)) {
                    meta.Add("cve_contains", cveContains);
                }
                if (!string.IsNullOrWhiteSpace(kbContains)) {
                    meta.Add("kb_contains", kbContains);
                }
            });

        return response;
    }

    private static bool TryNormalizeSeverity(string input, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        foreach (var allowed in AllowedSeverities) {
            if (allowed.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase)) {
                normalized = allowed;
                return true;
            }
        }
        return false;
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

            foreach (var kb in x.Kbs ?? Array.Empty<string>()) {
                if (!string.IsNullOrWhiteSpace(kb)) {
                    newKbs.Add(kb);
                }
            }
        }

        summary.NewKbs = newKbs.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        summary.NotableCves = notable.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        summary.HighestRatedCves = highestRated.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return summary;
    }
}
