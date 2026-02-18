using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Domains;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns default and optional fine-grained password policies across selected domains (read-only).
/// </summary>
public sealed class AdPasswordPolicyTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_password_policy",
        "Get default domain password policy and optional fine-grained policies (PSO/FGPP) for one domain or a forest (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name. When omitted, uses current forest.")),
                ("domain_name", ToolSchema.String("Optional DNS domain name. When omitted, all domains in forest scope are queried.")),
                ("include_fine_grained", ToolSchema.Boolean("When true, includes fine-grained password policies (FGPP/PSO).")),
                ("max_results", ToolSchema.Integer("Maximum policy rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record PasswordPolicyError(
        string Domain,
        string Stage,
        string Message);

    private sealed record AdPasswordPolicyResult(
        string? ForestName,
        string? DomainName,
        bool IncludeFineGrained,
        IReadOnlyList<string> Domains,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<PasswordPolicyError> Errors,
        IReadOnlyList<PasswordPolicyInfo> Policies);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPasswordPolicyTool"/> class.
    /// </summary>
    public AdPasswordPolicyTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var includeFineGrained = ToolArgs.GetBoolean(arguments, "include_fine_grained", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var domains = LdapQueryHelper.GetTargetDomains(domainName, forestName)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (domains.Count == 0) {
            return Task.FromResult(ToolResponse.Error(
                "query_failed",
                "No domains resolved for password policy query. Provide domain_name or ensure forest discovery is available."));
        }

        var reader = new PasswordPolicyReader();
        var rows = new List<PasswordPolicyInfo>(Math.Min(maxResults, 128));
        var errors = new List<PasswordPolicyError>();

        foreach (var domain in domains) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                rows.Add(reader.GetDomainPolicy(domain));
            } catch (Exception ex) {
                errors.Add(new PasswordPolicyError(
                    Domain: domain,
                    Stage: "default_policy",
                    Message: ex.Message));
            }

            if (!includeFineGrained) {
                continue;
            }

            try {
                foreach (var policy in reader.GetFineGrainedPolicies(domain)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (policy is not null) {
                        rows.Add(policy);
                    }
                }
            } catch (Exception ex) {
                errors.Add(new PasswordPolicyError(
                    Domain: domain,
                    Stage: "fine_grained_policy",
                    Message: ex.Message));
            }
        }

        var scanned = rows.Count;
        IReadOnlyList<PasswordPolicyInfo> projectedRows = scanned > maxResults
            ? rows.Take(maxResults).ToArray()
            : rows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdPasswordPolicyResult(
            ForestName: forestName,
            DomainName: domainName,
            IncludeFineGrained: includeFineGrained,
            Domains: domains,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Policies: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "policies_view",
            title: "Active Directory: Password Policies (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("max_results", maxResults);
                meta.Add("include_fine_grained", includeFineGrained);
                meta.Add("domains_count", domains.Count);
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
