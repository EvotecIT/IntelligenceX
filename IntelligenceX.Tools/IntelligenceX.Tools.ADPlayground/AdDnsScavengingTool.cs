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
/// Retrieves DNS zone scavenging settings and mismatch signals for a DNS server (read-only).
/// </summary>
public sealed class AdDnsScavengingTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dns_scavenging",
        "Get DNS scavenging settings per zone for a DNS server and detect mismatch/stale-record signals (read-only).",
        ToolSchema.Object(
                ("dns_server", ToolSchema.String("DNS server name to query.")),
                ("mismatched_only", ToolSchema.Boolean("When true, returns only zones where scavenging intervals do not match server defaults.")),
                ("stale_only", ToolSchema.Boolean("When true, returns only zones with stale record count > 0.")),
                ("scavenging_enabled_only", ToolSchema.Boolean("When true, returns only zones with scavenging enabled.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .Required("dns_server")
            .NoAdditionalProperties());

    private sealed record AdDnsScavengingResult(
        string DnsServer,
        bool MismatchedOnly,
        bool StaleOnly,
        bool ScavengingEnabledOnly,
        int Scanned,
        bool Truncated,
        int TotalZones,
        int TotalStaleZones,
        int TotalMismatchZones,
        IReadOnlyList<DnsZoneScavengingInfo> Zones);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDnsScavengingTool"/> class.
    /// </summary>
    public AdDnsScavengingTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var dnsServer = ToolArgs.GetOptionalTrimmed(arguments, "dns_server");
        if (string.IsNullOrWhiteSpace(dnsServer)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "dns_server is required."));
        }

        var mismatchedOnly = ToolArgs.GetBoolean(arguments, "mismatched_only", defaultValue: false);
        var staleOnly = ToolArgs.GetBoolean(arguments, "stale_only", defaultValue: false);
        var scavengingEnabledOnly = ToolArgs.GetBoolean(arguments, "scavenging_enabled_only", defaultValue: false);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryExecute(
                action: () => new DnsScavengingAnalyzer()
                .GetScavengingSummary(dnsServer, cancellationToken)
                .ToArray(),
                result: out IReadOnlyList<DnsZoneScavengingInfo> allZones,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "DNS scavenging query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = allZones
            .Where(zone => !mismatchedOnly || !zone.MatchesServer)
            .Where(zone => !staleOnly || zone.StaleRecordCount > 0)
            .Where(zone => !scavengingEnabledOnly || zone.ScavengingEnabled)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<DnsZoneScavengingInfo> rows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > rows.Count;

        var result = new AdDnsScavengingResult(
            DnsServer: dnsServer,
            MismatchedOnly: mismatchedOnly,
            StaleOnly: staleOnly,
            ScavengingEnabledOnly: scavengingEnabledOnly,
            Scanned: scanned,
            Truncated: truncated,
            TotalZones: allZones.Count,
            TotalStaleZones: allZones.Count(static zone => zone.StaleRecordCount > 0),
            TotalMismatchZones: allZones.Count(static zone => !zone.MatchesServer),
            Zones: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "zones_view",
            title: "Active Directory: DNS Scavenging (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("dns_server", dnsServer);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("mismatched_only", mismatchedOnly);
                meta.Add("stale_only", staleOnly);
                meta.Add("scavenging_enabled_only", scavengingEnabledOnly);
            }));
    }
}

