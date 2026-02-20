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
/// Returns DNS zone ACL exposure posture for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDnsZoneSecurityTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dns_zone_security",
        "Get DNS zone ACL exposure posture (anonymous/everyone/authenticated and write-control exposure) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("exposed_only", ToolSchema.Boolean("When true, return only zones with any exposure signal.")),
                ("broad_write_min", ToolSchema.Integer("Optional minimum broad-write ACE count filter. Default 0.")),
                ("include_offending_principals", ToolSchema.Boolean("When true, include flattened offending-principal rows in details payload.")),
                ("max_offending_rows", ToolSchema.Integer("Maximum offending-principal detail rows. Default 500.")),
                ("max_results", ToolSchema.Integer("Maximum zone rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DnsZoneSecurityRow(
        string DomainName,
        string ZoneName,
        string Partition,
        bool ExposedToAnonymous,
        bool ExposedToEveryone,
        bool ExposedToAuthenticatedUsers,
        bool HasCreateChildExposure,
        bool HasWriteAclExposure,
        int BroadWriteAceCount,
        int BroadReadAceCount,
        bool AnyExposure);

    private sealed record DnsZoneSecurityOffendingRow(
        string DomainName,
        string ZoneName,
        string Principal,
        string Sid,
        string Rights,
        string OperationKind,
        string RiskLevel,
        string ObjectTypeName);

    private sealed record DnsZoneSecurityError(
        string Domain,
        string Message);

    private sealed record AdDnsZoneSecurityResult(
        string? DomainName,
        string? ForestName,
        bool ExposedOnly,
        int BroadWriteMin,
        bool IncludeOffendingPrincipals,
        int MaxOffendingRows,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DnsZoneSecurityError> Errors,
        IReadOnlyList<DnsZoneSecurityRow> Rows,
        IReadOnlyList<DnsZoneSecurityOffendingRow> OffendingPrincipals);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDnsZoneSecurityTool"/> class.
    /// </summary>
    public AdDnsZoneSecurityTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var exposedOnly = ToolArgs.GetBoolean(arguments, "exposed_only", defaultValue: false);
        var broadWriteMin = ToolArgs.GetCappedInt32(arguments, "broad_write_min", 0, 0, 100000);
        var includeOffendingPrincipals = ToolArgs.GetBoolean(arguments, "include_offending_principals", defaultValue: false);
        var maxOffendingRows = ToolArgs.GetCappedInt32(arguments, "max_offending_rows", 500, 1, 50000);
        var maxResults = ResolveMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "DNS zone security",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<DnsZoneSecurityRow>(targetDomains.Length * 10);
        var offendingRows = new List<DnsZoneSecurityOffendingRow>(maxOffendingRows);
        var errors = new List<DnsZoneSecurityError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var snapshot = DnsZoneSecurityService.GetSnapshot(domain);
                foreach (var zone in snapshot.Zones) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var anyExposure = zone.ExposedToAnonymous ||
                                      zone.ExposedToEveryone ||
                                      zone.ExposedToAuthenticatedUsers ||
                                      zone.HasCreateChildExposure ||
                                      zone.HasWriteAclExposure;
                    rows.Add(new DnsZoneSecurityRow(
                        DomainName: domain,
                        ZoneName: zone.ZoneName,
                        Partition: zone.Partition,
                        ExposedToAnonymous: zone.ExposedToAnonymous,
                        ExposedToEveryone: zone.ExposedToEveryone,
                        ExposedToAuthenticatedUsers: zone.ExposedToAuthenticatedUsers,
                        HasCreateChildExposure: zone.HasCreateChildExposure,
                        HasWriteAclExposure: zone.HasWriteAclExposure,
                        BroadWriteAceCount: zone.BroadWriteAceCount,
                        BroadReadAceCount: zone.BroadReadAceCount,
                        AnyExposure: anyExposure));

                    if (includeOffendingPrincipals && offendingRows.Count < maxOffendingRows) {
                        foreach (var principal in zone.OffendingPrincipals) {
                            if (offendingRows.Count >= maxOffendingRows) {
                                break;
                            }
                            offendingRows.Add(new DnsZoneSecurityOffendingRow(
                                DomainName: domain,
                                ZoneName: zone.ZoneName,
                                Principal: principal.Name ?? principal.Sid,
                                Sid: principal.Sid,
                                Rights: principal.Rights,
                                OperationKind: principal.OperationKind.ToString(),
                                RiskLevel: principal.RiskLevel.ToString(),
                                ObjectTypeName: principal.ObjectTypeName ?? string.Empty));
                        }
                    }
                }
            },
            errorFactory: (domain, ex) => new DnsZoneSecurityError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !exposedOnly || row.AnyExposure)
            .Where(row => row.BroadWriteAceCount >= broadWriteMin)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);

        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedZones = BuildProjectedSet(projectedRows, static row => $"{row.DomainName}|{row.ZoneName}");
        var projectedOffending = FilterByProjectedSet(
            FilterByProjectedSet(offendingRows, projectedDomains, static row => row.DomainName),
            projectedZones,
            static row => $"{row.DomainName}|{row.ZoneName}");

        var result = new AdDnsZoneSecurityResult(
            DomainName: domainName,
            ForestName: forestName,
            ExposedOnly: exposedOnly,
            BroadWriteMin: broadWriteMin,
            IncludeOffendingPrincipals: includeOffendingPrincipals,
            MaxOffendingRows: maxOffendingRows,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            OffendingPrincipals: projectedOffending);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: DNS Zone Security (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("exposed_only", exposedOnly);
                meta.Add("broad_write_min", broadWriteMin);
                meta.Add("include_offending_principals", includeOffendingPrincipals);
                meta.Add("max_offending_rows", maxOffendingRows);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestAndMaxResultsMeta(meta, domainName, forestName, maxResults);
            }));
    }
}

