using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Kerberos;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns KRBTGT rotation and orphaned-RODC posture across one domain or forest scope (read-only).
/// </summary>
public sealed class AdKrbtgtHealthTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_krbtgt_health",
        "Get KRBTGT password age/KVNO health with orphaned RODC indicators for a domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used to enumerate domains when domain_name is omitted.")),
                ("age_threshold_days", ToolSchema.Integer("Age threshold in days for stale KRBTGT password signal. Default 180.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record KrbtgtHealthError(
        string Domain,
        string Message);

    private sealed record AdKrbtgtHealthResult(
        string? DomainName,
        string? ForestName,
        int AgeThresholdDays,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<KrbtgtHealthError> Errors,
        IReadOnlyList<KrbtgtHealthSnapshot> Domains);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdKrbtgtHealthTool"/> class.
    /// </summary>
    public AdKrbtgtHealthTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var (domainName, forestName, maxResults) = ResolveDomainAndForestScopeWithMaxResults(arguments);
        var ageThresholdDays = ToolArgs.GetCappedInt32(arguments, "age_threshold_days", 180, 1, 3650);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "KRBTGT health",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<KrbtgtHealthSnapshot>(targetDomains.Length);
        var errors = new List<KrbtgtHealthError>();
        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                rows.Add(KrbtgtHealthService.GetSnapshot(domain, ageThresholdDays));
            },
            errorFactory: (domain, ex) => new KrbtgtHealthError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var projectedRows = CapRows(rows, maxResults, out var scanned, out var truncated);

        var result = new AdKrbtgtHealthResult(
            DomainName: domainName,
            ForestName: forestName,
            AgeThresholdDays: ageThresholdDays,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: KRBTGT Health (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("age_threshold_days", ageThresholdDays);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
            }));
    }
}
