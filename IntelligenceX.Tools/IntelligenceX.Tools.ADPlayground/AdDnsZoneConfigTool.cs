using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Dns;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns DNS zone configuration rows from a DNS server (read-only).
/// </summary>
public sealed class AdDnsZoneConfigTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dns_zone_config",
        "Get DNS zone configuration (zone type, dynamic update mode, secondaries) from a DNS server via WMI (read-only).",
        ToolSchema.Object(
                ("dns_server", ToolSchema.String("DNS server name to query.")),
                ("zone_name_contains", ToolSchema.String("Optional zone-name substring filter (case-insensitive).")),
                ("dynamic_updates_only", ToolSchema.Boolean("When true, return only zones where dynamic updates are enabled (AllowUpdate > 0).")),
                ("insecure_updates_only", ToolSchema.Boolean("When true, return only zones that allow non-secure updates (AllowUpdate = 1).")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("dns_server")
            .NoAdditionalProperties());

    private sealed record DnsZoneConfigRow(
        string Server,
        string ZoneName,
        string ZoneType,
        uint? AllowUpdate,
        bool DynamicUpdatesEnabled,
        bool InsecureDynamicUpdates,
        uint? SecureSecondaries,
        int SecondaryServerCount,
        IReadOnlyList<string> SecondaryServers);

    private sealed record AdDnsZoneConfigResult(
        string DnsServer,
        bool QuerySucceeded,
        string ErrorMessage,
        string ZoneNameContains,
        bool DynamicUpdatesOnly,
        bool InsecureUpdatesOnly,
        int Scanned,
        bool Truncated,
        IReadOnlyList<DnsZoneConfigRow> Zones);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDnsZoneConfigTool"/> class.
    /// </summary>
    public AdDnsZoneConfigTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var dnsServer = ToolArgs.GetOptionalTrimmed(arguments, "dns_server");
        if (string.IsNullOrWhiteSpace(dnsServer)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "dns_server is required."));
        }

        var zoneNameContains = ToolArgs.GetOptionalTrimmed(arguments, "zone_name_contains");
        var dynamicUpdatesOnly = ToolArgs.GetBoolean(arguments, "dynamic_updates_only", defaultValue: false);
        var insecureUpdatesOnly = ToolArgs.GetBoolean(arguments, "insecure_updates_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (!TryExecute(
                action: () => DnsZoneConfigService.GetZonesResult(dnsServer),
                result: out DnsZoneConfigService.QueryResult query,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "DNS zone configuration query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var rows = query.Zones
            .Where(zone => string.IsNullOrWhiteSpace(zoneNameContains) || zone.ZoneName.Contains(zoneNameContains, StringComparison.OrdinalIgnoreCase))
            .Select(static zone => new DnsZoneConfigRow(
                Server: zone.Server,
                ZoneName: zone.ZoneName,
                ZoneType: zone.ZoneType ?? string.Empty,
                AllowUpdate: zone.AllowUpdate,
                DynamicUpdatesEnabled: zone.AllowUpdate.GetValueOrDefault() > 0,
                InsecureDynamicUpdates: zone.AllowUpdate.GetValueOrDefault() == 1,
                SecureSecondaries: zone.SecureSecondaries,
                SecondaryServerCount: zone.SecondaryServers?.Length ?? 0,
                SecondaryServers: zone.SecondaryServers ?? Array.Empty<string>()))
            .Where(row => !dynamicUpdatesOnly || row.DynamicUpdatesEnabled)
            .Where(row => !insecureUpdatesOnly || row.InsecureDynamicUpdates)
            .ToArray();

        var scanned = rows.Length;
        IReadOnlyList<DnsZoneConfigRow> projectedRows = scanned > maxResults
            ? rows.Take(maxResults).ToArray()
            : rows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDnsZoneConfigResult(
            DnsServer: dnsServer,
            QuerySucceeded: query.QuerySucceeded,
            ErrorMessage: query.ErrorMessage ?? string.Empty,
            ZoneNameContains: zoneNameContains ?? string.Empty,
            DynamicUpdatesOnly: dynamicUpdatesOnly,
            InsecureUpdatesOnly: insecureUpdatesOnly,
            Scanned: scanned,
            Truncated: truncated,
            Zones: projectedRows);

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "zones_view",
            title: "Active Directory: DNS Zone Configuration (preview)",
            baseTruncated: truncated,
            scanned: scanned,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta.Add("dns_server", dnsServer);
                meta.Add("query_succeeded", query.QuerySucceeded);
                meta.Add("dynamic_updates_only", dynamicUpdatesOnly);
                meta.Add("insecure_updates_only", insecureUpdatesOnly);
                meta.Add("max_results", maxResults);
                if (!string.IsNullOrWhiteSpace(zoneNameContains)) {
                    meta.Add("zone_name_contains", zoneNameContains);
                }
                if (!string.IsNullOrWhiteSpace(query.ErrorMessage)) {
                    meta.Add("query_error", query.ErrorMessage);
                }
            });
        return Task.FromResult(response);
    }
}


