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

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var ageThresholdDays = ToolArgs.GetCappedInt32(arguments, "age_threshold_days", 180, 1, 3650);
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
                "No domains resolved for KRBTGT health query. Provide domain_name or ensure forest discovery is available."));
        }

        var rows = new List<KrbtgtHealthSnapshot>(targetDomains.Length);
        var errors = new List<KrbtgtHealthError>();
        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                rows.Add(KrbtgtHealthService.GetSnapshot(domain, ageThresholdDays));
            } catch (Exception ex) {
                errors.Add(new KrbtgtHealthError(domain, ex.Message));
            }
        }

        var scanned = rows.Count;
        IReadOnlyList<KrbtgtHealthSnapshot> projectedRows = scanned > maxResults
            ? rows.Take(maxResults).ToArray()
            : rows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdKrbtgtHealthResult(
            DomainName: domainName,
            ForestName: forestName,
            AgeThresholdDays: ageThresholdDays,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: KRBTGT Health (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("age_threshold_days", ageThresholdDays);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            });
        return Task.FromResult(response);
    }
}
