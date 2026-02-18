using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Trusts;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns Azure AD Seamless SSO (AZUREADSSOACC$) posture for one domain or forest scope (read-only).
/// </summary>
public sealed class AdAzureAdSsoTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_azuread_sso",
        "Check Azure AD Seamless SSO account posture (AZUREADSSOACC$ presence, enabled state, delegation flags) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("only_present", ToolSchema.Boolean("When true, return only domains where AZUREADSSOACC$ exists.")),
                ("risky_only", ToolSchema.Boolean("When true, return only rows with risky posture (unconstrained delegation).")),
                ("include_spns", ToolSchema.Boolean("When true, include SPN list for AZUREADSSOACC$ in details payload.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AzureAdSsoRow(
        string DomainName,
        bool SeamlessSsoPresent,
        bool? AccountEnabled,
        bool? UnconstrainedDelegation,
        int SpnCount,
        bool RiskyConfiguration);

    private sealed record AzureAdSsoDetail(
        string DomainName,
        string? AccountDn,
        IReadOnlyList<string> Spns);

    private sealed record AzureAdSsoError(
        string Domain,
        string Message);

    private sealed record AdAzureAdSsoResult(
        string? DomainName,
        string? ForestName,
        bool OnlyPresent,
        bool RiskyOnly,
        bool IncludeSpns,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<AzureAdSsoError> Errors,
        IReadOnlyList<AzureAdSsoRow> Rows,
        IReadOnlyList<AzureAdSsoDetail> Details);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdAzureAdSsoTool"/> class.
    /// </summary>
    public AdAzureAdSsoTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var onlyPresent = ToolArgs.GetBoolean(arguments, "only_present", defaultValue: false);
        var riskyOnly = ToolArgs.GetBoolean(arguments, "risky_only", defaultValue: false);
        var includeSpns = ToolArgs.GetBoolean(arguments, "include_spns", defaultValue: false);
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
                "No domains resolved for Azure AD SSO posture query. Provide domain_name or ensure forest discovery is available."));
        }

        var rows = new List<AzureAdSsoRow>(targetDomains.Length);
        var details = new List<AzureAdSsoDetail>(targetDomains.Length);
        var errors = new List<AzureAdSsoError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var snapshot = AzureAdSsoService.GetSnapshot(domain);
                var risky = snapshot.Present && snapshot.UnconstrainedDelegation == true;
                rows.Add(new AzureAdSsoRow(
                    DomainName: domain,
                    SeamlessSsoPresent: snapshot.Present,
                    AccountEnabled: snapshot.Enabled,
                    UnconstrainedDelegation: snapshot.UnconstrainedDelegation,
                    SpnCount: snapshot.Spns.Length,
                    RiskyConfiguration: risky));

                details.Add(new AzureAdSsoDetail(
                    DomainName: domain,
                    AccountDn: snapshot.AccountDn,
                    Spns: includeSpns ? snapshot.Spns : Array.Empty<string>()));
            } catch (Exception ex) {
                errors.Add(new AzureAdSsoError(domain, ex.Message));
            }
        }

        var filtered = rows
            .Where(row => !onlyPresent || row.SeamlessSsoPresent)
            .Where(row => !riskyOnly || row.RiskyConfiguration)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<AzureAdSsoRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;
        var projectedDomains = projectedRows
            .Select(static row => row.DomainName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectedDetails = details
            .Where(detail => projectedDomains.Contains(detail.DomainName))
            .ToArray();

        var result = new AdAzureAdSsoResult(
            DomainName: domainName,
            ForestName: forestName,
            OnlyPresent: onlyPresent,
            RiskyOnly: riskyOnly,
            IncludeSpns: includeSpns,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            Details: projectedDetails);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Azure AD Seamless SSO Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("only_present", onlyPresent);
                meta.Add("risky_only", riskyOnly);
                meta.Add("include_spns", includeSpns);
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
