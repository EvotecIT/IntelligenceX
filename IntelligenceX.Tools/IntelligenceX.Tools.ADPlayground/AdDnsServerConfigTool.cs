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
/// Returns DNS server recursion/forwarder posture for one or more DNS servers (read-only).
/// </summary>
public sealed class AdDnsServerConfigTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_dns_server_config",
        "Get DNS server recursion and forwarder configuration for explicit servers or discovered domain controllers (read-only).",
        ToolSchema.Object(
                ("dns_servers", ToolSchema.Array(ToolSchema.String(), "Optional explicit DNS server list. When omitted, discovers domain controllers.")),
                ("domain_name", ToolSchema.String("Optional DNS domain name used for discovery when dns_servers is omitted.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used for discovery when domain_name is omitted.")),
                ("recursion_disabled_only", ToolSchema.Boolean("When true, return only servers where recursion is disabled.")),
                ("missing_forwarders_only", ToolSchema.Boolean("When true, return only servers without forwarders configured.")),
                ("max_servers", ToolSchema.Integer("Maximum discovered servers to query when dns_servers is omitted. Default 200.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DnsServerConfigRow(
        string Server,
        bool? RecursionEnabled,
        int ForwarderCount,
        IReadOnlyList<string> Forwarders,
        bool MissingForwarders);

    private sealed record DnsServerConfigError(
        string Server,
        string Message);

    private sealed record AdDnsServerConfigResult(
        string? DomainName,
        string? ForestName,
        int MaxServers,
        bool RecursionDisabledOnly,
        bool MissingForwardersOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DnsServerConfigError> Errors,
        IReadOnlyList<DnsServerConfigRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDnsServerConfigTool"/> class.
    /// </summary>
    public AdDnsServerConfigTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var explicitServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("dns_servers"));
        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var recursionDisabledOnly = ToolArgs.GetBoolean(arguments, "recursion_disabled_only", defaultValue: false);
        var missingForwardersOnly = ToolArgs.GetBoolean(arguments, "missing_forwarders_only", defaultValue: false);
        var maxServers = ToolArgs.GetCappedInt32(arguments, "max_servers", 200, 1, 5000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var servers = new List<string>(maxServers);
        if (explicitServers.Count > 0) {
            servers.AddRange(explicitServers.Take(maxServers));
        } else {
            var targetDomains = string.IsNullOrWhiteSpace(domainName)
                ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(static x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : new[] { domainName! };

            foreach (var domain in targetDomains) {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var dc in DomainHelper.EnumerateDomainControllers(domain, cancellationToken: cancellationToken)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!servers.Contains(dc, StringComparer.OrdinalIgnoreCase)) {
                        servers.Add(dc);
                    }
                    if (servers.Count >= maxServers) {
                        break;
                    }
                }
                if (servers.Count >= maxServers) {
                    break;
                }
            }
        }

        if (servers.Count == 0) {
            return Task.FromResult(ToolResponse.Error(
                "query_failed",
                "No DNS servers resolved. Provide dns_servers or domain_name/forest_name for discovery."));
        }

        var rows = new List<DnsServerConfigRow>(servers.Count);
        var errors = new List<DnsServerConfigError>();

        foreach (var server in servers) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var cfg = DnsServerConfigService.GetConfig(server);
                var forwarders = cfg.Forwarders ?? Array.Empty<string>();
                rows.Add(new DnsServerConfigRow(
                    Server: cfg.Server,
                    RecursionEnabled: cfg.RecursionEnabled,
                    ForwarderCount: forwarders.Length,
                    Forwarders: forwarders,
                    MissingForwarders: forwarders.Length == 0));
            } catch (Exception ex) {
                errors.Add(new DnsServerConfigError(server, ex.Message));
            }
        }

        var filtered = rows
            .Where(row => !recursionDisabledOnly || row.RecursionEnabled == false)
            .Where(row => !missingForwardersOnly || row.MissingForwarders)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<DnsServerConfigRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDnsServerConfigResult(
            DomainName: domainName,
            ForestName: forestName,
            MaxServers: maxServers,
            RecursionDisabledOnly: recursionDisabledOnly,
            MissingForwardersOnly: missingForwardersOnly,
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
            title: "Active Directory: DNS Server Configuration (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("max_servers", maxServers);
                meta.Add("recursion_disabled_only", recursionDisabledOnly);
                meta.Add("missing_forwarders_only", missingForwardersOnly);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
                if (explicitServers.Count > 0) {
                    meta.Add("explicit_dns_servers", explicitServers.Count);
                }
            }));
    }
}
