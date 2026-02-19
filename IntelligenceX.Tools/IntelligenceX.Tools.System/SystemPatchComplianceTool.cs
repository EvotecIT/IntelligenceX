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

        var windowsError = ValidateWindowsSupport("system_patch_compliance");
        if (windowsError is not null) {
            return windowsError;
        }

        var computerName = ToolArgs.GetOptionalTrimmed(arguments, "computer_name");
        var target = ResolveTargetComputerName(computerName);
        var includePendingLocal = ToolArgs.GetBoolean(arguments, "include_pending_local", defaultValue: false);
        var missingOnly = ToolArgs.GetBoolean(arguments, "missing_only", defaultValue: false);

        if (!TryResolvePatchReleaseWindow(arguments, out var year, out var month, out var releaseError)) {
            return releaseError!;
        }
        if (!TryResolvePatchProductFilter(
                arguments,
                out var productFamily,
                out var productVersion,
                out var productBuild,
                out var productEdition,
                out var productError)) {
            return productError!;
        }
        if (!TryResolvePatchSeverityAllowlist(arguments, out var severity, out var severityError)) {
            return severityError!;
        }

        var exploitedOnly = ToolArgs.GetBoolean(arguments, "exploited_only", defaultValue: false);
        var publiclyDisclosedOnly = ToolArgs.GetBoolean(arguments, "publicly_disclosed_only", defaultValue: false);
        var cveContains = ToolArgs.GetOptionalTrimmed(arguments, "cve_contains");
        var kbContains = ToolArgs.GetOptionalTrimmed(arguments, "kb_contains");
        var maxResults = ResolveMaxResults(arguments);

        var (monthly, patchError) = await TryGetMonthlyPatchDetailsAsync(
            year: year,
            month: month,
            productFamily: productFamily,
            productVersion: productVersion,
            productBuild: productBuild,
            productEdition: productEdition,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (patchError is not null) {
            return patchError;
        }

        if (!TryGetInstalledAndPendingUpdates(
                computerName: computerName,
                target: target,
                includePendingLocal: includePendingLocal,
                updates: out var updates,
                pendingIncluded: out var pendingIncluded,
                errorResponse: out var updateError)) {
            return updateError!;
        }

        var installedKbSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var update in updates) {
            foreach (var kb in SystemPatchKbNormalization.EnumerateNormalized(update.Kb)) {
                installedKbSet.Add(kb);
            }
            foreach (var kb in SystemPatchKbNormalization.EnumerateNormalized(update.Title)) {
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
                || SystemPatchKbNormalization.MatchesContainsFilter(x.Kbs, kbContains))
            .OrderByDescending(static x => x.IsExploited)
            .ThenByDescending(static x => x.Published ?? DateTime.MinValue)
            .ThenBy(static x => x.CveId, StringComparer.OrdinalIgnoreCase);

        var complianceRows = new List<ComplianceRow>();
        foreach (var item in filtered) {
            var expected = SystemPatchKbNormalization.NormalizeDistinct(item.Kbs);

            var installed = expected
                .Where(installedKbSet.Contains)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var missing = expected
                .Where(kb => !installedKbSet.Contains(kb))
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var state = expected.Count == 0
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

        var rows = CapRows(complianceRows, maxResults, out var scanned, out var truncated);
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

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "compliance_view",
            title: "Patch compliance (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, target);
                AddMaxResultsMeta(meta, maxResults);
                AddPendingLocalMeta(meta, includePendingLocal, pendingIncluded);
                AddPatchFilterMeta(
                    meta: meta,
                    year: year,
                    month: month,
                    release: release,
                    productFamily: productFamily,
                    productVersion: productVersion,
                    productBuild: productBuild,
                    productEdition: productEdition,
                    severity: severity,
                    exploitedOnly: exploitedOnly,
                    publiclyDisclosedOnly: publiclyDisclosedOnly,
                    cveContains: cveContains,
                    kbContains: kbContains);
                if (missingOnly) {
                    meta.Add("missing_only", true);
                }
            });
        return response;
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
