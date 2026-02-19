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
/// Returns rollup posture for domain password policy and fine-grained PSOs (read-only).
/// </summary>
public sealed class AdPasswordPolicyRollupTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_password_policy_rollup",
        "Get domain password policy rollup with fine-grained PSO weakness counts and attribution signals (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used to enumerate domains when domain_name is omitted.")),
                ("pso_min_length", ToolSchema.Integer("Minimum expected password length baseline used for weak PSO detection. Default 14.")),
                ("pso_history_min", ToolSchema.Integer("Minimum expected password history baseline used for weak PSO detection. Default 24.")),
                ("include_pso_details", ToolSchema.Boolean("When true, include PSO detail rows per domain. Default true.")),
                ("max_pso_rows_per_domain", ToolSchema.Integer("Maximum PSO detail rows included per domain. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record PasswordPolicyRollupSummaryRow(
        string DomainName,
        int TotalPsos,
        int WeakPsos,
        bool HasDomainPolicy,
        string? DomainPolicyAttributionTopWriters);

    private sealed record PasswordPolicyRollupDetail(
        string DomainName,
        PasswordPolicyInfo? DomainPolicy,
        IReadOnlyList<global::ADPlayground.Gpo.PolicyAttribution> DomainPolicyAttribution,
        IReadOnlyList<PasswordPolicyInfo> Psos);

    private sealed record PasswordPolicyRollupError(
        string Domain,
        string Message);

    private sealed record AdPasswordPolicyRollupResult(
        string? DomainName,
        string? ForestName,
        int PsoMinLength,
        int PsoHistoryMin,
        bool IncludePsoDetails,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<PasswordPolicyRollupError> Errors,
        IReadOnlyList<PasswordPolicyRollupSummaryRow> Domains,
        IReadOnlyList<PasswordPolicyRollupDetail> DomainDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPasswordPolicyRollupTool"/> class.
    /// </summary>
    public AdPasswordPolicyRollupTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var psoMinLength = ToolArgs.GetCappedInt32(arguments, "pso_min_length", 14, 1, 128);
        var psoHistoryMin = ToolArgs.GetCappedInt32(arguments, "pso_history_min", 24, 0, 1024);
        var includePsoDetails = ToolArgs.GetBoolean(arguments, "include_pso_details", defaultValue: true);
        var maxPsoRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_pso_rows_per_domain", 100, 1, Options.MaxResults);
        var maxResults = ResolveMaxResultsClampToOne(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "password policy rollup",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var summaries = new List<PasswordPolicyRollupSummaryRow>(targetDomains.Length);
        var details = new List<PasswordPolicyRollupDetail>(targetDomains.Length);
        var errors = new List<PasswordPolicyRollupError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var rollup = PasswordPolicyRollupService.Evaluate(domain, psoMinLength, psoHistoryMin);
                summaries.Add(new PasswordPolicyRollupSummaryRow(
                    DomainName: rollup.DomainName,
                    TotalPsos: rollup.TotalPsos,
                    WeakPsos: rollup.WeakPsos,
                    HasDomainPolicy: rollup.DomainPolicy is not null,
                    DomainPolicyAttributionTopWriters: rollup.DomainPolicyAttributionTopWriters));

                details.Add(new PasswordPolicyRollupDetail(
                    DomainName: rollup.DomainName,
                    DomainPolicy: rollup.DomainPolicy,
                    DomainPolicyAttribution: rollup.DomainPolicyAttribution,
                    Psos: includePsoDetails
                        ? rollup.Psos.Take(maxPsoRowsPerDomain).ToArray()
                        : Array.Empty<PasswordPolicyInfo>()));
            },
            errorFactory: (domain, ex) => new PasswordPolicyRollupError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var projectedRows = CapRows(summaries, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdPasswordPolicyRollupResult(
            DomainName: domainName,
            ForestName: forestName,
            PsoMinLength: psoMinLength,
            PsoHistoryMin: psoHistoryMin,
            IncludePsoDetails: includePsoDetails,
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
            title: "Active Directory: Password Policy Rollup (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("pso_min_length", psoMinLength);
                meta.Add("pso_history_min", psoHistoryMin);
                meta.Add("include_pso_details", includePsoDetails);
                meta.Add("max_pso_rows_per_domain", maxPsoRowsPerDomain);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}
