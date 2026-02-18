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

    private static readonly string[] AllowedSeverities = {
        "Critical",
        "Important",
        "Moderate",
        "Low"
    };

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

    private sealed record ComplianceRow(
        string CveId,
        string Severity,
        bool IsExploited,
        bool PubliclyDisclosed,
        DateTime? Published,
        string? Category,
        IReadOnlyList<string> Kbs,
        IReadOnlyList<string> InstalledKbs,
        IReadOnlyList<string> MissingKbs,
        string ComplianceState);

    private sealed record PatchComplianceSummary(
        int Total,
        int Installed,
        int Missing,
        int UnknownNoKb,
        int ExploitedMissing,
        int PubliclyDisclosedMissing,
        int CriticalMissing,
        IReadOnlyList<string> MissingKbs);

    private sealed record SystemPatchComplianceResult(
        string ComputerName,
        int Year,
        int Month,
        string Release,
        bool ProductMappedFilterApplied,
        string? ProductFamily,
        string? ProductVersion,
        string? ProductBuild,
        string? ProductEdition,
        IReadOnlyList<string> Severity,
        bool ExploitedOnly,
        bool PubliclyDisclosedOnly,
        bool MissingOnly,
        bool IncludePendingLocal,
        bool PendingIncluded,
        string? CveContains,
        string? KbContains,
        int Scanned,
        bool Truncated,
        PatchComplianceSummary Summary,
        IReadOnlyList<ComplianceRow> Compliance);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemPatchComplianceTool"/> class.
    /// </summary>
    public SystemPatchComplianceTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows()) {
            return ToolResponse.Error("not_supported", "system_patch_compliance is available only on Windows hosts.");
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = string.IsNullOrWhiteSpace(computerName) ? Environment.MachineName : computerName!;
        var includePendingLocal = ToolArgs.GetBoolean(arguments, "include_pending_local", defaultValue: false);
        var missingOnly = ToolArgs.GetBoolean(arguments, "missing_only", defaultValue: false);

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

        IEnumerable<UpdateInfo> updates;
        try {
            updates = Updates.GetInstalled(computerName);
        } catch (Exception ex) {
            return ToolResponse.Error("query_failed", $"Installed updates query failed: {ex.Message}");
        }

        var isLocalTarget = string.IsNullOrWhiteSpace(computerName)
            || string.Equals(computerName, ".", StringComparison.Ordinal)
            || string.Equals(target, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        var pendingIncluded = false;
        if (includePendingLocal && isLocalTarget) {
            try {
                updates = updates.Concat(Updates.GetPending());
                pendingIncluded = true;
            } catch {
                pendingIncluded = false;
            }
        }

        var installedKbSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var update in updates) {
            foreach (var kb in EnumerateKbs(update.Kb)) {
                installedKbSet.Add(kb);
            }
            foreach (var kb in EnumerateKbs(update.Title)) {
                installedKbSet.Add(kb);
            }
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
            .OrderByDescending(static x => x.IsExploited)
            .ThenByDescending(static x => x.Published ?? DateTime.MinValue)
            .ThenBy(static x => x.CveId, StringComparer.OrdinalIgnoreCase);

        var complianceRows = new List<ComplianceRow>();
        foreach (var item in filtered) {
            var expected = item.Kbs?
                .SelectMany(EnumerateKbs)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            var installed = expected
                .Where(installedKbSet.Contains)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var missing = expected
                .Where(kb => !installedKbSet.Contains(kb))
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var state = expected.Length == 0
                ? "unknown_no_kb"
                : missing.Length == 0
                    ? "installed"
                    : "missing";
            if (missingOnly && !string.Equals(state, "missing", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            complianceRows.Add(new ComplianceRow(
                CveId: item.CveId,
                Severity: item.Severity,
                IsExploited: item.IsExploited,
                PubliclyDisclosed: item.PubliclyDisclosed,
                Published: item.Published,
                Category: item.Category,
                Kbs: expected,
                InstalledKbs: installed,
                MissingKbs: missing,
                ComplianceState: state));
        }

        var scanned = complianceRows.Count;
        IReadOnlyList<ComplianceRow> rows = scanned > maxResults
            ? complianceRows.Take(maxResults).ToArray()
            : complianceRows;
        var truncated = scanned > rows.Count;
        var summary = BuildSummary(complianceRows);
        var release = new DateTime(year, month, 1).ToString("yyyy-MM");

        var result = new SystemPatchComplianceResult(
            ComputerName: target,
            Year: year,
            Month: month,
            Release: release,
            ProductMappedFilterApplied: !string.IsNullOrWhiteSpace(productFamily),
            ProductFamily: productFamily,
            ProductVersion: productVersion,
            ProductBuild: productBuild,
            ProductEdition: productEdition,
            Severity: severity,
            ExploitedOnly: exploitedOnly,
            PubliclyDisclosedOnly: publiclyDisclosedOnly,
            MissingOnly: missingOnly,
            IncludePendingLocal: includePendingLocal,
            PendingIncluded: pendingIncluded,
            CveContains: cveContains,
            KbContains: kbContains,
            Scanned: scanned,
            Truncated: truncated,
            Summary: summary,
            Compliance: rows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "compliance_view",
            title: "Patch compliance (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("computer_name", target);
                meta.Add("year", year);
                meta.Add("month", month);
                meta.Add("release", release);
                meta.Add("max_results", maxResults);
                meta.Add("include_pending_local", includePendingLocal);
                meta.Add("pending_included", pendingIncluded);
                if (missingOnly) {
                    meta.Add("missing_only", true);
                }
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

    private static IEnumerable<string> EnumerateKbs(string? input) {
        if (string.IsNullOrWhiteSpace(input)) {
            yield break;
        }

        var s = input!;
        for (var i = 0; i < s.Length - 2; i++) {
            if ((s[i] == 'K' || s[i] == 'k') && (s[i + 1] == 'B' || s[i + 1] == 'b')) {
                var j = i + 2;
                while (j < s.Length && char.IsWhiteSpace(s[j])) {
                    j++;
                }
                var start = j;
                while (j < s.Length && char.IsDigit(s[j])) {
                    j++;
                }
                if (j > start) {
                    yield return "KB" + s.Substring(start, j - start);
                }
                i = j;
            }
        }
    }

    private static PatchComplianceSummary BuildSummary(IReadOnlyList<ComplianceRow> rows) {
        var installed = 0;
        var missing = 0;
        var unknownNoKb = 0;
        var exploitedMissing = 0;
        var publiclyDisclosedMissing = 0;
        var criticalMissing = 0;
        var missingKbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows) {
            if (string.Equals(row.ComplianceState, "installed", StringComparison.OrdinalIgnoreCase)) {
                installed++;
            } else if (string.Equals(row.ComplianceState, "missing", StringComparison.OrdinalIgnoreCase)) {
                missing++;
                if (row.IsExploited) {
                    exploitedMissing++;
                }
                if (row.PubliclyDisclosed) {
                    publiclyDisclosedMissing++;
                }
                if (string.Equals(row.Severity, "Critical", StringComparison.OrdinalIgnoreCase)) {
                    criticalMissing++;
                }
                foreach (var kb in row.MissingKbs) {
                    missingKbs.Add(kb);
                }
            } else {
                unknownNoKb++;
            }
        }

        return new PatchComplianceSummary(
            Total: rows.Count,
            Installed: installed,
            Missing: missing,
            UnknownNoKb: unknownNoKb,
            ExploitedMissing: exploitedMissing,
            PubliclyDisclosedMissing: publiclyDisclosedMissing,
            CriticalMissing: criticalMissing,
            MissingKbs: missingKbs.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
