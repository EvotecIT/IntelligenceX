using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Computers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns LAPS coverage posture (Windows/legacy/DSRM) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdLapsCoverageTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_laps_coverage",
        "Check LAPS coverage posture (Windows LAPS, legacy LAPS, effective expiry, DSRM on DCs) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("coverage_below_percent", ToolSchema.Integer("Optional minimum either-LAPS coverage percent threshold (0-100). Rows below threshold are retained.")),
                ("expired_only", ToolSchema.Boolean("When true, return only rows where either-LAPS expired count is greater than zero.")),
                ("include_samples", ToolSchema.Boolean("When true, include missing/expired sample lists per domain.")),
                ("max_sample_rows_per_domain", ToolSchema.Integer("Maximum sample rows per category and domain. Default 50.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record LapsCoverageRow(
        string DomainName,
        int TotalComputers,
        double EitherLapsCoveragePercent,
        int EitherLapsExpiredCount,
        double EitherLapsExpiredPercent,
        int WindowsLapsCount,
        int LegacyLapsCount,
        int DomainControllers,
        int DsrmLapsCount,
        double DsrmLapsCoveragePercent,
        int DsrmLapsExpiredCount,
        bool AnyFinding);

    private sealed record LapsCoverageDetail(
        string DomainName,
        IReadOnlyList<LapsCoverageService.MinimalComputer> MissingEitherLaps,
        IReadOnlyList<LapsCoverageService.MinimalComputer> ExpiredWindowsLaps,
        IReadOnlyList<LapsCoverageService.MinimalComputer> ExpiredLegacyLaps,
        IReadOnlyList<LapsCoverageService.MinimalComputer> MissingDsrmLaps,
        IReadOnlyList<LapsCoverageService.MinimalComputer> ExpiredDsrmLaps);

    private sealed record LapsCoverageError(
        string Domain,
        string Message);

    private sealed record AdLapsCoverageResult(
        string? DomainName,
        string? ForestName,
        int? CoverageBelowPercent,
        bool ExpiredOnly,
        bool IncludeSamples,
        int MaxSampleRowsPerDomain,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<LapsCoverageError> Errors,
        IReadOnlyList<LapsCoverageRow> Rows,
        IReadOnlyList<LapsCoverageDetail> Details);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLapsCoverageTool"/> class.
    /// </summary>
    public AdLapsCoverageTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var coverageBelowPercent = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("coverage_below_percent"), 100);
        if (coverageBelowPercent.HasValue) {
            coverageBelowPercent = Math.Clamp(coverageBelowPercent.Value, 0, 100);
        }
        var expiredOnly = ToolArgs.GetBoolean(arguments, "expired_only", defaultValue: false);
        var includeSamples = ToolArgs.GetBoolean(arguments, "include_samples", defaultValue: false);
        var maxSampleRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_sample_rows_per_domain", 50, 1, 1000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "LAPS coverage",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return targetDomainError!;
        }

        var rows = new List<LapsCoverageRow>(targetDomains.Length);
        var details = new List<LapsCoverageDetail>(targetDomains.Length);
        var errors = new List<LapsCoverageError>();

        await RunPerTargetCollectionAsync(
                targets: targetDomains,
                collectAsync: async domain => {
                var view = await LapsCoverageService.EvaluateAsync(domain).ConfigureAwait(false);
                rows.Add(new LapsCoverageRow(
                    DomainName: view.DomainName,
                    TotalComputers: view.TotalComputers,
                    EitherLapsCoveragePercent: view.EitherLapsCoveragePercent,
                    EitherLapsExpiredCount: view.EitherLapsExpiredCount,
                    EitherLapsExpiredPercent: view.EitherLapsExpiredPercent,
                    WindowsLapsCount: view.WindowsLapsCount,
                    LegacyLapsCount: view.LegacyLapsCount,
                    DomainControllers: view.DomainControllers,
                    DsrmLapsCount: view.DsrmLapsCount,
                    DsrmLapsCoveragePercent: view.DsrmLapsCoveragePercent,
                    DsrmLapsExpiredCount: view.DsrmLapsExpiredCount,
                    AnyFinding: view.EitherLapsCoveragePercent < 100.0 || view.EitherLapsExpiredCount > 0 || view.DsrmLapsCoveragePercent < 100.0 || view.DsrmLapsExpiredCount > 0));

                if (includeSamples) {
                    details.Add(new LapsCoverageDetail(
                        DomainName: view.DomainName,
                        MissingEitherLaps: view.MissingEitherLaps.Take(maxSampleRowsPerDomain).ToArray(),
                        ExpiredWindowsLaps: view.ExpiredWindowsLaps.Take(maxSampleRowsPerDomain).ToArray(),
                        ExpiredLegacyLaps: view.ExpiredLegacyLaps.Take(maxSampleRowsPerDomain).ToArray(),
                        MissingDsrmLaps: view.MissingDsrmLaps.Take(maxSampleRowsPerDomain).ToArray(),
                        ExpiredDsrmLaps: view.ExpiredDsrmLaps.Take(maxSampleRowsPerDomain).ToArray()));
                }
            },
            errorFactory: (domain, ex) => new LapsCoverageError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var filtered = rows
            .Where(row => !coverageBelowPercent.HasValue || row.EitherLapsCoveragePercent < coverageBelowPercent.Value)
            .Where(row => !expiredOnly || row.EitherLapsExpiredCount > 0)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdLapsCoverageResult(
            DomainName: domainName,
            ForestName: forestName,
            CoverageBelowPercent: coverageBelowPercent,
            ExpiredOnly: expiredOnly,
            IncludeSamples: includeSamples,
            MaxSampleRowsPerDomain: maxSampleRowsPerDomain,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            Details: projectedDetails);

        return BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: LAPS Coverage (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                if (coverageBelowPercent.HasValue) {
                    meta.Add("coverage_below_percent", coverageBelowPercent.Value);
                }
                meta.Add("expired_only", expiredOnly);
                meta.Add("include_samples", includeSamples);
                meta.Add("max_sample_rows_per_domain", maxSampleRowsPerDomain);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            });
    }
}
