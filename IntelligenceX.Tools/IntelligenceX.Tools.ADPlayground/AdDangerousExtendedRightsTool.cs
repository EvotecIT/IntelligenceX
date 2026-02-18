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
/// Detects dangerous extended/write rights on sensitive AD objects across one domain or forest scope (read-only).
/// </summary>
public sealed class AdDangerousExtendedRightsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dangerous_extended_rights",
        "Detect dangerous extended/write rights on sensitive AD objects (domain root/AdminSDHolder/krbtgt/master key) by non-privileged principals (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("include_findings", ToolSchema.Boolean("When true, include finding rows per domain. Default true.")),
                ("max_findings_per_domain", ToolSchema.Integer("Maximum findings included per domain. Default 200.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DangerousExtendedRightsSummaryRow(
        string DomainName,
        int Findings,
        int CategoryCount,
        int RightCount);

    private sealed record DangerousExtendedRightsDetail(
        string DomainName,
        IReadOnlyList<DangerousExtendedRightsService.SummaryItem> ByCategory,
        IReadOnlyList<DangerousExtendedRightsService.SummaryItem> ByRight,
        IReadOnlyList<DangerousExtendedRightsService.Finding> Findings);

    private sealed record DangerousExtendedRightsError(
        string Domain,
        string Message);

    private sealed record AdDangerousExtendedRightsResult(
        string? DomainName,
        string? ForestName,
        bool IncludeFindings,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DangerousExtendedRightsError> Errors,
        IReadOnlyList<DangerousExtendedRightsSummaryRow> Domains,
        IReadOnlyList<DangerousExtendedRightsDetail> DomainDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDangerousExtendedRightsTool"/> class.
    /// </summary>
    public AdDangerousExtendedRightsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeFindings = ToolArgs.GetBoolean(arguments, "include_findings", defaultValue: true);
        var maxFindingsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_findings_per_domain", 200, 1, Options.MaxResults);
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
                "No domains resolved for dangerous extended-rights query. Provide domain_name or ensure forest discovery is available."));
        }

        var summaries = new List<DangerousExtendedRightsSummaryRow>(targetDomains.Length);
        var details = new List<DangerousExtendedRightsDetail>(targetDomains.Length);
        var errors = new List<DangerousExtendedRightsError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var domainDn = DomainHelper.DomainNameToDistinguishedName(domain);
                var view = DangerousExtendedRightsService.Evaluate(domain, domainDn);
                summaries.Add(new DangerousExtendedRightsSummaryRow(
                    DomainName: view.DomainName,
                    Findings: view.Findings,
                    CategoryCount: view.CategoryCount,
                    RightCount: view.RightCount));
                details.Add(new DangerousExtendedRightsDetail(
                    DomainName: view.DomainName,
                    ByCategory: view.ByCategory,
                    ByRight: view.ByRight,
                    Findings: includeFindings
                        ? view.Items.Take(maxFindingsPerDomain).ToArray()
                        : Array.Empty<DangerousExtendedRightsService.Finding>()));
            } catch (Exception ex) {
                errors.Add(new DangerousExtendedRightsError(domain, ToCollectorErrorMessage(ex)));
            }
        }

        var scanned = summaries.Count;
        IReadOnlyList<DangerousExtendedRightsSummaryRow> projectedRows = scanned > maxResults
            ? summaries.Take(maxResults).ToArray()
            : summaries;
        var truncated = scanned > projectedRows.Count;
        var projectedDomains = projectedRows
            .Select(static row => row.DomainName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectedDetails = details
            .Where(detail => projectedDomains.Contains(detail.DomainName))
            .ToArray();

        var result = new AdDangerousExtendedRightsResult(
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
            title: "Active Directory: Dangerous Extended Rights (preview)",
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
