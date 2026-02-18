using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.DomainControllers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns domain controller fleet hygiene posture (inactive/old-password/ownership signals) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDcFleetPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dc_fleet_posture",
        "Get domain controller fleet hygiene posture (inactive, old password, disabled, managedBy, non-admin owner) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("include_details", ToolSchema.Boolean("When true, include per-domain detailed DC lists.")),
                ("max_detail_rows_per_domain", ToolSchema.Integer("Maximum detailed DC rows per domain bucket. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DcFleetPostureRow(
        string DomainName,
        int DomainControllerCount,
        int InactiveCount,
        int OldPasswordCount,
        int DisabledCount,
        int ManagedBySetCount,
        int NonAdministrativeOwnerCount,
        int TotalSignals);

    private sealed record DcFleetPostureDomainDetails(
        string DomainName,
        IReadOnlyList<FleetPostureService.DcItem> Inactive,
        IReadOnlyList<FleetPostureService.DcItem> OldPasswords,
        IReadOnlyList<FleetPostureService.DcItem> Disabled,
        IReadOnlyList<FleetPostureService.DcItem> ManagedBySet,
        IReadOnlyList<FleetPostureService.DcItem> NonAdministrativeOwners);

    private sealed record DcFleetPostureError(
        string Domain,
        string Message);

    private sealed record AdDcFleetPostureResult(
        string? DomainName,
        string? ForestName,
        bool IncludeDetails,
        int MaxDetailRowsPerDomain,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DcFleetPostureError> Errors,
        IReadOnlyList<DcFleetPostureRow> Domains,
        IReadOnlyList<DcFleetPostureDomainDetails> Details);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDcFleetPostureTool"/> class.
    /// </summary>
    public AdDcFleetPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeDetails = ToolArgs.GetBoolean(arguments, "include_details", defaultValue: false);
        var maxDetailRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_detail_rows_per_domain", 100, 1, 2000);
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
                "No domains resolved for DC fleet posture query. Provide domain_name or ensure forest discovery is available."));
        }

        var summaryRows = new List<DcFleetPostureRow>(targetDomains.Length);
        var detailRows = new List<DcFleetPostureDomainDetails>(targetDomains.Length);
        var errors = new List<DcFleetPostureError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var view = FleetPostureService.Evaluate(domain, cancellationToken: cancellationToken);
                summaryRows.Add(new DcFleetPostureRow(
                    DomainName: view.DomainName,
                    DomainControllerCount: view.DomainControllerCount,
                    InactiveCount: view.InactiveCount,
                    OldPasswordCount: view.OldPasswordCount,
                    DisabledCount: view.DisabledCount,
                    ManagedBySetCount: view.ManagedBySetCount,
                    NonAdministrativeOwnerCount: view.NonAdministrativeOwnerCount,
                    TotalSignals: view.InactiveCount + view.OldPasswordCount + view.DisabledCount + view.ManagedBySetCount + view.NonAdministrativeOwnerCount));

                if (includeDetails) {
                    detailRows.Add(new DcFleetPostureDomainDetails(
                        DomainName: view.DomainName,
                        Inactive: view.Inactive.Take(maxDetailRowsPerDomain).ToArray(),
                        OldPasswords: view.OldPasswords.Take(maxDetailRowsPerDomain).ToArray(),
                        Disabled: view.Disabled.Take(maxDetailRowsPerDomain).ToArray(),
                        ManagedBySet: view.ManagedBySet.Take(maxDetailRowsPerDomain).ToArray(),
                        NonAdministrativeOwners: view.NonAdministrativeOwners.Take(maxDetailRowsPerDomain).ToArray()));
                }
            } catch (Exception ex) {
                errors.Add(new DcFleetPostureError(domain, ex.Message));
            }
        }

        var scanned = summaryRows.Count;
        IReadOnlyList<DcFleetPostureRow> projectedRows = scanned > maxResults
            ? summaryRows.Take(maxResults).ToArray()
            : summaryRows;
        var truncated = scanned > projectedRows.Count;
        var projectedDomains = projectedRows
            .Select(static row => row.DomainName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projectedDetails = detailRows
            .Where(detail => projectedDomains.Contains(detail.DomainName))
            .ToArray();

        var result = new AdDcFleetPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            IncludeDetails: includeDetails,
            MaxDetailRowsPerDomain: maxDetailRowsPerDomain,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows,
            Details: projectedDetails);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: DC Fleet Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_details", includeDetails);
                meta.Add("max_detail_rows_per_domain", maxDetailRowsPerDomain);
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
