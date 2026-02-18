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
/// Returns DC registration posture (DNS resolution, site container, subnet match) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdRegistrationPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_registration_posture",
        "Check domain controller registration posture (DNS resolution, site container, subnet coverage) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("dns_failed_only", ToolSchema.Boolean("When true, return only domains with DNS resolution failures.")),
                ("missing_site_only", ToolSchema.Boolean("When true, return only domains with missing site-container coverage.")),
                ("missing_subnet_only", ToolSchema.Boolean("When true, return only domains with missing subnet coverage.")),
                ("include_details", ToolSchema.Boolean("When true, include failing DC detail rows for each domain.")),
                ("max_detail_rows_per_domain", ToolSchema.Integer("Maximum failing DC rows per domain in details payload. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record RegistrationPostureRow(
        string DomainName,
        int DomainControllerCount,
        int DnsResolveFailedCount,
        int MissingSiteCount,
        int MissingSubnetCount,
        bool AnyFinding);

    private sealed record RegistrationPostureDetailRow(
        string DomainName,
        string Category,
        string DistinguishedName,
        string Host,
        string Site,
        IReadOnlyList<string> IpV4,
        bool DnsResolves,
        bool HasSiteContainer,
        bool HasMatchingSubnet);

    private sealed record RegistrationPostureError(
        string Domain,
        string Message);

    private sealed record AdRegistrationPostureResult(
        string? DomainName,
        string? ForestName,
        bool DnsFailedOnly,
        bool MissingSiteOnly,
        bool MissingSubnetOnly,
        bool IncludeDetails,
        int MaxDetailRowsPerDomain,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<RegistrationPostureError> Errors,
        IReadOnlyList<RegistrationPostureRow> Rows,
        IReadOnlyList<RegistrationPostureDetailRow> Details);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdRegistrationPostureTool"/> class.
    /// </summary>
    public AdRegistrationPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var dnsFailedOnly = ToolArgs.GetBoolean(arguments, "dns_failed_only", defaultValue: false);
        var missingSiteOnly = ToolArgs.GetBoolean(arguments, "missing_site_only", defaultValue: false);
        var missingSubnetOnly = ToolArgs.GetBoolean(arguments, "missing_subnet_only", defaultValue: false);
        var includeDetails = ToolArgs.GetBoolean(arguments, "include_details", defaultValue: false);
        var maxDetailRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_detail_rows_per_domain", 100, 1, 5000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "registration posture",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<RegistrationPostureRow>(targetDomains.Length);
        var details = new List<RegistrationPostureDetailRow>(targetDomains.Length * 3);
        var errors = new List<RegistrationPostureError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var domainDn = DomainHelper.DomainNameToDistinguishedName(domain);
                var view = RegistrationPostureService.Evaluate(domain, domainDn);

                rows.Add(new RegistrationPostureRow(
                    DomainName: view.DomainName,
                    DomainControllerCount: view.DomainControllerCount,
                    DnsResolveFailedCount: view.DnsResolveFailedCount,
                    MissingSiteCount: view.MissingSiteCount,
                    MissingSubnetCount: view.MissingSubnetCount,
                    AnyFinding: view.DnsResolveFailedCount > 0 || view.MissingSiteCount > 0 || view.MissingSubnetCount > 0));

                if (includeDetails) {
                    foreach (var item in view.DnsResolveFailed.Take(maxDetailRowsPerDomain)) {
                        details.Add(MapDetail(view.DomainName, "dns_resolve_failed", item));
                    }
                    foreach (var item in view.MissingSite.Take(maxDetailRowsPerDomain)) {
                        details.Add(MapDetail(view.DomainName, "missing_site", item));
                    }
                    foreach (var item in view.MissingSubnet.Take(maxDetailRowsPerDomain)) {
                        details.Add(MapDetail(view.DomainName, "missing_subnet", item));
                    }
                }
            },
            errorFactory: (domain, ex) => new RegistrationPostureError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !dnsFailedOnly || row.DnsResolveFailedCount > 0)
            .Where(row => !missingSiteOnly || row.MissingSiteCount > 0)
            .Where(row => !missingSubnetOnly || row.MissingSubnetCount > 0)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdRegistrationPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            DnsFailedOnly: dnsFailedOnly,
            MissingSiteOnly: missingSiteOnly,
            MissingSubnetOnly: missingSubnetOnly,
            IncludeDetails: includeDetails,
            MaxDetailRowsPerDomain: maxDetailRowsPerDomain,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            Details: projectedDetails);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Registration Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("dns_failed_only", dnsFailedOnly);
                meta.Add("missing_site_only", missingSiteOnly);
                meta.Add("missing_subnet_only", missingSubnetOnly);
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

    private static RegistrationPostureDetailRow MapDetail(string domainName, string category, RegistrationPostureService.DcRegItem item) {
        return new RegistrationPostureDetailRow(
            DomainName: domainName,
            Category: category,
            DistinguishedName: item.DistinguishedName,
            Host: item.Host ?? string.Empty,
            Site: item.Site ?? string.Empty,
            IpV4: item.IpV4,
            DnsResolves: item.DnsResolves,
            HasSiteContainer: item.HasSiteContainer,
            HasMatchingSubnet: item.HasMatchingSubnet);
    }
}
