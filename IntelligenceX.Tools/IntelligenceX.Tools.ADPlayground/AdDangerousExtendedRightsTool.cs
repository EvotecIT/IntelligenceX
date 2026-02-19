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

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var includeFindings = ToolArgs.GetBoolean(arguments, "include_findings", defaultValue: true);
        var maxFindingsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_findings_per_domain", 200, 1, Options.MaxResults);
        var maxResults = ResolveMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "dangerous extended-rights",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var summaries = new List<DangerousExtendedRightsSummaryRow>(targetDomains.Length);
        var details = new List<DangerousExtendedRightsDetail>(targetDomains.Length);
        var errors = new List<DangerousExtendedRightsError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
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
            },
            errorFactory: (domain, ex) => new DangerousExtendedRightsError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var projectedRows = CapRows(summaries, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

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
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}

