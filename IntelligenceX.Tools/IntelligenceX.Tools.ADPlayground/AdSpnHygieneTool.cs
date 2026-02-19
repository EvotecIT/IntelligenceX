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
/// Evaluates SPN hygiene signals such as invalid SPNs, unexpected classes, and privileged SPN usage (read-only).
/// </summary>
public sealed class AdSpnHygieneTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_spn_hygiene",
        "Evaluate SPN hygiene for one domain or forest scope, including invalid SPNs, class allow/block signals, and privileged SPN usage (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used to enumerate domains when domain_name is omitted.")),
                ("allowlist_classes", ToolSchema.Array(ToolSchema.String(), "Optional allowlist of service classes. Classes outside this set are flagged unexpected.")),
                ("blocklist_classes", ToolSchema.Array(ToolSchema.String(), "Optional blocklist of service classes to flag as blocked usage.")),
                ("dns_resolve_classes", ToolSchema.Array(ToolSchema.String(), "Optional service classes for DNS target resolution checks.")),
                ("top_n", ToolSchema.Integer("Number of top service classes captured per domain. Default 10.")),
                ("include_invalid_spn_sample", ToolSchema.Boolean("When true, include sampled invalid SPN entries. Default true.")),
                ("max_invalid_spn_sample", ToolSchema.Integer("Maximum invalid/unresolvable SPN sample entries per domain. Default 25.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record SpnHygieneSummaryRow(
        string DomainName,
        int TotalServiceAccounts,
        int PrivilegedWithSpnCount,
        int InvalidSpnCount,
        int UnresolvableTargetCount,
        int UnexpectedClassCount,
        int BlockedClassUsageCount,
        int MaxSpnCount,
        string TopClasses);

    private sealed record SpnHygieneDetail(
        string DomainName,
        IReadOnlyList<string> PrivilegedWithSpn,
        IReadOnlyList<SpnHygieneService.ServiceClassInfo> TopClasses,
        IReadOnlyList<SpnHygieneService.ServiceClassInfo> UnexpectedClasses,
        IReadOnlyList<SpnHygieneService.ServiceClassInfo> BlockedClassesUsed,
        IReadOnlyList<SpnHygieneService.SpnInvalidEntry> InvalidSpns,
        IReadOnlyList<SpnHygieneService.SpnInvalidEntry> UnresolvableTargets);

    private sealed record SpnHygieneError(
        string Domain,
        string Message);

    private sealed record AdSpnHygieneResult(
        string? DomainName,
        string? ForestName,
        int TopN,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<SpnHygieneError> Errors,
        IReadOnlyList<SpnHygieneSummaryRow> Domains,
        IReadOnlyList<SpnHygieneDetail> DomainDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSpnHygieneTool"/> class.
    /// </summary>
    public AdSpnHygieneTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var allowlist = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("allowlist_classes"));
        var blocklist = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("blocklist_classes"));
        var dnsResolveClasses = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("dns_resolve_classes"));
        var topN = ToolArgs.GetCappedInt32(arguments, "top_n", 10, 1, 50);
        var includeInvalidSpnSample = ToolArgs.GetBoolean(arguments, "include_invalid_spn_sample", defaultValue: true);
        var maxInvalidSpnSample = ToolArgs.GetCappedInt32(arguments, "max_invalid_spn_sample", 25, 1, 200);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "SPN hygiene",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var summaries = new List<SpnHygieneSummaryRow>(targetDomains.Length);
        var details = new List<SpnHygieneDetail>(targetDomains.Length);
        var errors = new List<SpnHygieneError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var snapshot = SpnHygieneService.Evaluate(
                    domainName: domain,
                    allowlist: allowlist,
                    blocklist: blocklist,
                    topN: topN,
                    dnsResolveClasses: dnsResolveClasses);

                summaries.Add(new SpnHygieneSummaryRow(
                    DomainName: snapshot.DomainName,
                    TotalServiceAccounts: snapshot.TotalServiceAccounts,
                    PrivilegedWithSpnCount: snapshot.PrivilegedWithSpn.Count,
                    InvalidSpnCount: snapshot.InvalidSpns.Count,
                    UnresolvableTargetCount: snapshot.UnresolvableTargets.Count,
                    UnexpectedClassCount: snapshot.UnexpectedClasses.Count,
                    BlockedClassUsageCount: snapshot.BlockedClassesUsed.Count,
                    MaxSpnCount: snapshot.MaxSpnCount,
                    TopClasses: string.Join(
                        ", ",
                        snapshot.TopClasses.Select(static x => $"{x.Name}:{x.Count}"))));

                details.Add(new SpnHygieneDetail(
                    DomainName: snapshot.DomainName,
                    PrivilegedWithSpn: snapshot.PrivilegedWithSpn,
                    TopClasses: snapshot.TopClasses,
                    UnexpectedClasses: snapshot.UnexpectedClasses,
                    BlockedClassesUsed: snapshot.BlockedClassesUsed,
                    InvalidSpns: includeInvalidSpnSample
                        ? snapshot.InvalidSpns.Take(maxInvalidSpnSample).ToArray()
                        : Array.Empty<SpnHygieneService.SpnInvalidEntry>(),
                    UnresolvableTargets: includeInvalidSpnSample
                        ? snapshot.UnresolvableTargets.Take(maxInvalidSpnSample).ToArray()
                        : Array.Empty<SpnHygieneService.SpnInvalidEntry>()));
            },
            errorFactory: (domain, ex) => new SpnHygieneError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var projectedRows = CapRows(summaries, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdSpnHygieneResult(
            DomainName: domainName,
            ForestName: forestName,
            TopN: topN,
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
            title: "Active Directory: SPN Hygiene (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("top_n", topN);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                meta.Add("allowlist_count", allowlist.Count);
                meta.Add("blocklist_count", blocklist.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}

