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
/// Detects shadow-credentials ACL exposure across one domain or forest scope (read-only).
/// </summary>
public sealed class AdShadowCredentialsRiskTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_shadow_credentials_risk",
        "Detect principals with write access to msDS-KeyCredentialLink on privileged users/DC objects (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("include_findings", ToolSchema.Boolean("When true, include finding rows per domain. Default true.")),
                ("max_findings_per_domain", ToolSchema.Integer("Maximum findings included per domain. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record ShadowCredentialsRiskSummaryRow(
        string DomainName,
        int FindingCount);

    private sealed record ShadowCredentialsRiskDetail(
        string DomainName,
        IReadOnlyList<ShadowCredentialsRiskService.Finding> Findings);

    private sealed record ShadowCredentialsRiskError(
        string Domain,
        string Message);

    private sealed record AdShadowCredentialsRiskResult(
        string? DomainName,
        string? ForestName,
        bool IncludeFindings,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<ShadowCredentialsRiskError> Errors,
        IReadOnlyList<ShadowCredentialsRiskSummaryRow> Domains,
        IReadOnlyList<ShadowCredentialsRiskDetail> DomainDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdShadowCredentialsRiskTool"/> class.
    /// </summary>
    public AdShadowCredentialsRiskTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var (domainName, forestName, maxResults) = ResolveDomainAndForestScopeWithMaxResults(arguments);
        var includeFindings = ToolArgs.GetBoolean(arguments, "include_findings", defaultValue: true);
        var maxFindingsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_findings_per_domain", 100, 1, Options.MaxResults);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "shadow credentials risk",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var summaries = new List<ShadowCredentialsRiskSummaryRow>(targetDomains.Length);
        var details = new List<ShadowCredentialsRiskDetail>(targetDomains.Length);
        var errors = new List<ShadowCredentialsRiskError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var view = ShadowCredentialsRiskService.Evaluate(domain);
                summaries.Add(new ShadowCredentialsRiskSummaryRow(
                    DomainName: view.DomainName,
                    FindingCount: view.FindingCount));
                details.Add(new ShadowCredentialsRiskDetail(
                    DomainName: view.DomainName,
                    Findings: includeFindings
                        ? view.Findings.Take(maxFindingsPerDomain).ToArray()
                        : Array.Empty<ShadowCredentialsRiskService.Finding>()));
            },
            errorFactory: (domain, ex) => new ShadowCredentialsRiskError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var projectedRows = CapRows(summaries, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdShadowCredentialsRiskResult(
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
            title: "Active Directory: Shadow Credentials Risk (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_findings", includeFindings);
                meta.Add("max_findings_per_domain", maxFindingsPerDomain);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
            }));
    }
}
