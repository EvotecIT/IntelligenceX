using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Security;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Detects DC-shadow replication-right indicators across one domain or forest scope (read-only).
/// </summary>
public sealed class AdDcShadowIndicatorsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dc_shadow_indicators",
        "Detect non-default principals with replication extended rights (DC-shadow indicators) on critical AD containers (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("include_findings", ToolSchema.Boolean("When true, include finding rows per domain. Default true.")),
                ("max_findings_per_domain", ToolSchema.Integer("Maximum findings included per domain. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DcShadowIndicatorsSummaryRow(
        string DomainName,
        int FindingCount);

    private sealed record DcShadowIndicatorsDetail(
        string DomainName,
        IReadOnlyList<DcShadowIndicatorService.Finding> Findings);

    private sealed record DcShadowIndicatorsError(
        string Domain,
        string Message);

    private sealed record AdDcShadowIndicatorsResult(
        string? DomainName,
        string? ForestName,
        bool IncludeFindings,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DcShadowIndicatorsError> Errors,
        IReadOnlyList<DcShadowIndicatorsSummaryRow> Domains,
        IReadOnlyList<DcShadowIndicatorsDetail> DomainDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDcShadowIndicatorsTool"/> class.
    /// </summary>
    public AdDcShadowIndicatorsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeFindings = ToolArgs.GetBoolean(arguments, "include_findings", defaultValue: true);
        var maxFindingsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_findings_per_domain", 100, 1, Options.MaxResults);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var targetDomains = string.IsNullOrWhiteSpace(domainName)
            ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[] { domainName! };

        if (targetDomains.Length == 0) {
            return Task.FromResult(ToolResponse.Error(
                "query_failed",
                "No domains resolved for DC-shadow indicators query. Provide domain_name or ensure forest discovery is available."));
        }

        var summaries = new List<DcShadowIndicatorsSummaryRow>(targetDomains.Length);
        var details = new List<DcShadowIndicatorsDetail>(targetDomains.Length);
        var errors = new List<DcShadowIndicatorsError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var view = DcShadowIndicatorService.Evaluate(domain);
                summaries.Add(new DcShadowIndicatorsSummaryRow(
                    DomainName: view.DomainName,
                    FindingCount: view.FindingCount));
                details.Add(new DcShadowIndicatorsDetail(
                    DomainName: view.DomainName,
                    Findings: includeFindings
                        ? view.Findings.Take(maxFindingsPerDomain).ToArray()
                        : Array.Empty<DcShadowIndicatorService.Finding>()));
            } catch (Exception ex) {
                errors.Add(new DcShadowIndicatorsError(domain, ex.Message));
            }
        }

        var scanned = summaries.Count;
        IReadOnlyList<DcShadowIndicatorsSummaryRow> projectedRows = scanned > maxResults
            ? summaries.Take(maxResults).ToArray()
            : summaries;
        var truncated = scanned > projectedRows.Count;
        var projectedDomains = projectedRows
            .Select(static row => row.DomainName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectedDetails = details
            .Where(detail => projectedDomains.Contains(detail.DomainName))
            .ToArray();

        var result = new AdDcShadowIndicatorsResult(
            DomainName: domainName,
            ForestName: forestName,
            IncludeFindings: includeFindings,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows,
            DomainDetails: projectedDetails);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: DC-Shadow Indicators (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_findings", includeFindings);
                meta.Add("max_findings_per_domain", maxFindingsPerDomain);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            }));
    }
}
