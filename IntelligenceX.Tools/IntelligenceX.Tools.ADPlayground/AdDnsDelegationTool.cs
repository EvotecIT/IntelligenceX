using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Dns;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns DNS delegation ACL posture for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDnsDelegationTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dns_delegation",
        "Get DNS delegation ACL posture (non-privileged trustee rights on delegated zones) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("zone_name_contains", ToolSchema.String("Optional delegated zone-name substring filter (case-insensitive).")),
                ("identity_contains", ToolSchema.String("Optional trustee identity substring filter (case-insensitive).")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DnsDelegationRow(
        string DomainName,
        string ZoneName,
        string DistinguishedName,
        string Identity,
        string Sid,
        string Rights);

    private sealed record DnsDelegationError(
        string Domain,
        string Message);

    private sealed record AdDnsDelegationResult(
        string? DomainName,
        string? ForestName,
        string ZoneNameContains,
        string IdentityContains,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DnsDelegationError> Errors,
        IReadOnlyList<DnsDelegationRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDnsDelegationTool"/> class.
    /// </summary>
    public AdDnsDelegationTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var zoneNameContains = ToolArgs.GetOptionalTrimmed(arguments, "zone_name_contains");
        var identityContains = ToolArgs.GetOptionalTrimmed(arguments, "identity_contains");
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
                "No domains resolved for DNS delegation query. Provide domain_name or ensure forest discovery is available."));
        }

        var rows = new List<DnsDelegationRow>(targetDomains.Length * 20);
        var errors = new List<DnsDelegationError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var snapshot = DnsDelegationService.GetSnapshot(domain);
                foreach (var record in snapshot.Delegations) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(zoneNameContains) &&
                        !record.ZoneName.Contains(zoneNameContains, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(identityContains) &&
                        !record.Identity.Contains(identityContains, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    rows.Add(new DnsDelegationRow(
                        DomainName: domain,
                        ZoneName: record.ZoneName,
                        DistinguishedName: record.DistinguishedName,
                        Identity: record.Identity,
                        Sid: record.Sid,
                        Rights: record.Rights.ToString()));
                }
            } catch (Exception ex) {
                errors.Add(new DnsDelegationError(domain, ex.Message));
            }
        }

        var scanned = rows.Count;
        IReadOnlyList<DnsDelegationRow> projectedRows = scanned > maxResults
            ? rows.Take(maxResults).ToArray()
            : rows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDnsDelegationResult(
            DomainName: domainName,
            ForestName: forestName,
            ZoneNameContains: zoneNameContains ?? string.Empty,
            IdentityContains: identityContains ?? string.Empty,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: DNS Delegation ACLs (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(zoneNameContains)) {
                    meta.Add("zone_name_contains", zoneNameContains);
                }
                if (!string.IsNullOrWhiteSpace(identityContains)) {
                    meta.Add("identity_contains", identityContains);
                }
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            }));
    }
}
