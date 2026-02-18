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
/// Returns password-minimum-length posture for one domain or forest scope (read-only).
/// </summary>
public sealed class AdPasswordPolicyLengthTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_password_policy_length",
        "Get minimum password length posture against a recommended baseline for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("recommended_minimum_length", ToolSchema.Integer("Recommended baseline minimum password length. Default 12.")),
                ("below_recommended_only", ToolSchema.Boolean("When true, return only rows below the recommended baseline.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record PasswordPolicyLengthError(
        string Domain,
        string Message);

    private sealed record AdPasswordPolicyLengthResult(
        string? DomainName,
        string? ForestName,
        int RecommendedMinimumLength,
        bool BelowRecommendedOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<PasswordPolicyLengthError> Errors,
        IReadOnlyList<PasswordPolicyLengthSnapshot> Domains);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPasswordPolicyLengthTool"/> class.
    /// </summary>
    public AdPasswordPolicyLengthTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var recommendedMinimumLength = ToolArgs.GetCappedInt32(arguments, "recommended_minimum_length", 12, 1, 512);
        var belowRecommendedOnly = ToolArgs.GetBoolean(arguments, "below_recommended_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "password-policy-length",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<PasswordPolicyLengthSnapshot>(targetDomains.Length);
        var errors = new List<PasswordPolicyLengthError>();
        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                rows.Add(PasswordPolicyLengthService.GetSnapshot(
                    domainName: domain,
                    options: new PasswordPolicyLengthOptions {
                        RecommendedMinimumLength = recommendedMinimumLength
                    }));
            },
            errorFactory: (domain, ex) => new PasswordPolicyLengthError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !belowRecommendedOnly || !row.MeetsRecommendation)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<PasswordPolicyLengthSnapshot> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdPasswordPolicyLengthResult(
            DomainName: domainName,
            ForestName: forestName,
            RecommendedMinimumLength: recommendedMinimumLength,
            BelowRecommendedOnly: belowRecommendedOnly,
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
            title: "Active Directory: Password Policy Length (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("recommended_minimum_length", recommendedMinimumLength);
                meta.Add("below_recommended_only", belowRecommendedOnly);
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
