using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Domains;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns machine-account-quota posture for one domain or forest scope (read-only).
/// </summary>
public sealed class AdMachineAccountQuotaTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_machine_account_quota",
        "Get ms-DS-MachineAccountQuota values and threshold posture for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("threshold", ToolSchema.Integer("Threshold used to flag risky values (quota > threshold). Default 0.")),
                ("risky_only", ToolSchema.Boolean("When true, return only rows where quota exceeds threshold.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record MachineAccountQuotaRow(
        string DomainName,
        int MachineAccountQuota,
        bool IsKnown,
        bool ExceedsThreshold,
        int Threshold);

    private sealed record MachineAccountQuotaError(
        string Domain,
        string Message);

    private sealed record AdMachineAccountQuotaResult(
        string? DomainName,
        string? ForestName,
        int Threshold,
        bool RiskyOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<MachineAccountQuotaError> Errors,
        IReadOnlyList<MachineAccountQuotaRow> Domains);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMachineAccountQuotaTool"/> class.
    /// </summary>
    public AdMachineAccountQuotaTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var threshold = ToolArgs.GetCappedInt32(arguments, "threshold", 0, -1, int.MaxValue);
        var riskyOnly = ToolArgs.GetBoolean(arguments, "risky_only", defaultValue: false);
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
                "No domains resolved for machine-account-quota query. Provide domain_name or ensure forest discovery is available."));
        }

        var rows = new List<MachineAccountQuotaRow>(targetDomains.Length);
        var errors = new List<MachineAccountQuotaError>();
        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var view = MachineAccountQuotaEvaluator.Evaluate(domain);
                var isKnown = view.MachineAccountQuota >= 0;
                var exceedsThreshold = isKnown && view.MachineAccountQuota > threshold;
                rows.Add(new MachineAccountQuotaRow(
                    DomainName: view.DomainName,
                    MachineAccountQuota: view.MachineAccountQuota,
                    IsKnown: isKnown,
                    ExceedsThreshold: exceedsThreshold,
                    Threshold: threshold));
            } catch (Exception ex) {
                errors.Add(new MachineAccountQuotaError(domain, ex.Message));
            }
        }

        var filtered = rows
            .Where(row => !riskyOnly || row.ExceedsThreshold)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<MachineAccountQuotaRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdMachineAccountQuotaResult(
            DomainName: domainName,
            ForestName: forestName,
            Threshold: threshold,
            RiskyOnly: riskyOnly,
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
            title: "Active Directory: Machine Account Quota (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("threshold", threshold);
                meta.Add("risky_only", riskyOnly);
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
