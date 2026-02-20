using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.DomainControllers;
using ADPlayground.Domains;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns domain functional-level and object-count snapshots for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDomainStatisticsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_statistics",
        "Get domain functional-level, DC inventory, and object-count statistics for one domain or a forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used to enumerate domains when domain_name is omitted.")),
                ("include_domain_controllers", ToolSchema.Boolean("When true, include per-DC detail objects in snapshot payload. Default false.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DomainStatisticsSummaryRow(
        string DomainName,
        string DomainFunctionalLevelLabel,
        string ForestFunctionalLevelLabel,
        int DomainControllerCount,
        long UserCount,
        long ComputerCount,
        long GroupCount,
        bool IsComplete,
        string RecommendedFunctionalLevelLabel,
        int FunctionalLevelGap);

    private sealed record DomainStatisticsError(
        string Domain,
        string Message);

    private sealed record AdDomainStatisticsResult(
        string? DomainName,
        string? ForestName,
        bool IncludeDomainControllers,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DomainStatisticsError> Errors,
        IReadOnlyList<DomainStatisticsSummaryRow> Domains,
        IReadOnlyList<DomainStatisticsSnapshot> DomainSnapshots);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainStatisticsTool"/> class.
    /// </summary>
    public AdDomainStatisticsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var (domainName, forestName, maxResults) = ResolveDomainAndForestScopeWithMaxResults(arguments);
        var includeDomainControllers = ToolArgs.GetBoolean(arguments, "include_domain_controllers", defaultValue: false);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "domain statistics",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var snapshots = new List<DomainStatisticsSnapshot>(targetDomains.Length);
        var summaries = new List<DomainStatisticsSummaryRow>(targetDomains.Length);
        var errors = new List<DomainStatisticsError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var snapshot = DomainStatisticsService.GetSnapshot(domain);
                snapshots.Add(snapshot);
                summaries.Add(new DomainStatisticsSummaryRow(
                    DomainName: snapshot.DomainName,
                    DomainFunctionalLevelLabel: snapshot.DomainFunctionalLevelLabel,
                    ForestFunctionalLevelLabel: snapshot.ForestFunctionalLevelLabel,
                    DomainControllerCount: snapshot.DomainControllerCount,
                    UserCount: snapshot.UserCount,
                    ComputerCount: snapshot.ComputerCount,
                    GroupCount: snapshot.GroupCount,
                    IsComplete: snapshot.IsComplete,
                    RecommendedFunctionalLevelLabel: snapshot.RecommendedFunctionalLevelLabel,
                    FunctionalLevelGap: snapshot.FunctionalLevelGap));
            },
            errorFactory: (domain, ex) => new DomainStatisticsError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var projectedRows = CapRows(summaries, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);

        var projectedSnapshots = snapshots
            .Where(snapshot => projectedDomains.Contains(snapshot.DomainName))
            .Select(snapshot => includeDomainControllers
                ? snapshot
                : new DomainStatisticsSnapshot {
                    DomainName = snapshot.DomainName,
                    DomainFunctionalLevel = snapshot.DomainFunctionalLevel,
                    DomainFunctionalLevelRaw = snapshot.DomainFunctionalLevelRaw,
                    DomainFunctionalLevelLabel = snapshot.DomainFunctionalLevelLabel,
                    ForestFunctionalLevel = snapshot.ForestFunctionalLevel,
                    ForestFunctionalLevelRaw = snapshot.ForestFunctionalLevelRaw,
                    ForestFunctionalLevelLabel = snapshot.ForestFunctionalLevelLabel,
                    DomainControllerCount = snapshot.DomainControllerCount,
                    UserCount = snapshot.UserCount,
                    UserCountComplete = snapshot.UserCountComplete,
                    ComputerCount = snapshot.ComputerCount,
                    ComputerCountComplete = snapshot.ComputerCountComplete,
                    GroupCount = snapshot.GroupCount,
                    GroupCountComplete = snapshot.GroupCountComplete,
                    DomainControllers = Array.Empty<DomainControllerInfo>(),
                    MaximumSupportedDomainFunctionalLevelRaw = snapshot.MaximumSupportedDomainFunctionalLevelRaw,
                    MaximumSupportedDomainFunctionalLevelLabel = snapshot.MaximumSupportedDomainFunctionalLevelLabel,
                    RecommendedFunctionalLevelRaw = snapshot.RecommendedFunctionalLevelRaw,
                    RecommendedFunctionalLevelLabel = snapshot.RecommendedFunctionalLevelLabel,
                    FunctionalLevelGap = snapshot.FunctionalLevelGap
                })
            .ToArray();

        var result = new AdDomainStatisticsResult(
            DomainName: domainName,
            ForestName: forestName,
            IncludeDomainControllers: includeDomainControllers,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows,
            DomainSnapshots: projectedSnapshots);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: Domain Statistics (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_domain_controllers", includeDomainControllers);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
            }));
    }
}
