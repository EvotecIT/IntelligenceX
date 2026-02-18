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
/// Returns smart-card posture signals across one domain or forest scope (read-only).
/// </summary>
public sealed class AdSmartCardPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_smartcard_posture",
        "Evaluate smart-card posture for privileged users and smart-card-required password age indicators (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("include_details", ToolSchema.Boolean("When true, include detail rows per domain. Default true.")),
                ("max_privileged_rows_per_domain", ToolSchema.Integer("Maximum privileged-user detail rows included per domain. Default 100.")),
                ("max_finding_rows_per_domain", ToolSchema.Integer("Maximum finding rows included per domain list (missing/old). Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record SmartCardPostureSummaryRow(
        string DomainName,
        int PrivilegedUsers,
        int PrivilegedRequireSmartCard,
        int PrivilegedMissingSmartCard,
        int SmartCardUsers,
        int SmartCardPwdOld90);

    private sealed record SmartCardPostureDetail(
        string DomainName,
        IReadOnlyList<SmartCardPostureService.PrivUser> Privileged,
        IReadOnlyList<SmartCardPostureService.Finding> PrivilegedMissing,
        IReadOnlyList<SmartCardPostureService.Finding> SmartCardPwdOld);

    private sealed record SmartCardPostureError(
        string Domain,
        string Message);

    private sealed record AdSmartCardPostureResult(
        string? DomainName,
        string? ForestName,
        bool IncludeDetails,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<SmartCardPostureError> Errors,
        IReadOnlyList<SmartCardPostureSummaryRow> Domains,
        IReadOnlyList<SmartCardPostureDetail> DomainDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSmartCardPostureTool"/> class.
    /// </summary>
    public AdSmartCardPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeDetails = ToolArgs.GetBoolean(arguments, "include_details", defaultValue: true);
        var maxPrivilegedRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_privileged_rows_per_domain", 100, 1, Options.MaxResults);
        var maxFindingRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_finding_rows_per_domain", 100, 1, Options.MaxResults);
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
                "No domains resolved for smart-card posture query. Provide domain_name or ensure forest discovery is available."));
        }

        var summaries = new List<SmartCardPostureSummaryRow>(targetDomains.Length);
        var details = new List<SmartCardPostureDetail>(targetDomains.Length);
        var errors = new List<SmartCardPostureError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var domainDn = DomainHelper.DomainNameToDistinguishedName(domain);
                var view = SmartCardPostureService.Evaluate(domain, domainDn);
                summaries.Add(new SmartCardPostureSummaryRow(
                    DomainName: view.DomainName,
                    PrivilegedUsers: view.PrivilegedUsers,
                    PrivilegedRequireSmartCard: view.PrivilegedRequireSmartCard,
                    PrivilegedMissingSmartCard: view.PrivilegedMissingSmartCard,
                    SmartCardUsers: view.SmartCardUsers,
                    SmartCardPwdOld90: view.SmartCardPwdOld90));
                details.Add(new SmartCardPostureDetail(
                    DomainName: view.DomainName,
                    Privileged: includeDetails
                        ? view.Privileged.Take(maxPrivilegedRowsPerDomain).ToArray()
                        : Array.Empty<SmartCardPostureService.PrivUser>(),
                    PrivilegedMissing: includeDetails
                        ? view.PrivilegedMissing.Take(maxFindingRowsPerDomain).ToArray()
                        : Array.Empty<SmartCardPostureService.Finding>(),
                    SmartCardPwdOld: includeDetails
                        ? view.SmartCardPwdOld.Take(maxFindingRowsPerDomain).ToArray()
                        : Array.Empty<SmartCardPostureService.Finding>()));
            } catch (Exception ex) {
                errors.Add(new SmartCardPostureError(domain, ex.Message));
            }
        }

        var scanned = summaries.Count;
        IReadOnlyList<SmartCardPostureSummaryRow> projectedRows = scanned > maxResults
            ? summaries.Take(maxResults).ToArray()
            : summaries;
        var truncated = scanned > projectedRows.Count;
        var projectedDomains = projectedRows
            .Select(static row => row.DomainName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectedDetails = details
            .Where(detail => projectedDomains.Contains(detail.DomainName))
            .ToArray();

        var result = new AdSmartCardPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            IncludeDetails: includeDetails,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows,
            DomainDetails: projectedDetails);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: Smart Card Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_details", includeDetails);
                meta.Add("max_privileged_rows_per_domain", maxPrivilegedRowsPerDomain);
                meta.Add("max_finding_rows_per_domain", maxFindingRowsPerDomain);
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
