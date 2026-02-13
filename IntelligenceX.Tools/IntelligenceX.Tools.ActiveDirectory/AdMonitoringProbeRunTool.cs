using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.Dns;
using ADPlayground.Monitoring.Probes.Kerberos;
using ADPlayground.Monitoring.Probes.Ldap;
using ADPlayground.Monitoring.Probes.Ntp;
using ADPlayground.Monitoring.Probes.Replication;
using ADPlayground.Network;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using MonitoringDnsProtocol = ADPlayground.Monitoring.Probes.Dns.DnsProtocol;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Executes ADPlayground monitoring probes for LDAP/DNS/Kerberos/NTP/Replication.
/// </summary>
public sealed class AdMonitoringProbeRunTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultTimeoutMs = 5000;
    private const int MaxTimeoutMs = 300_000;
    private const int DefaultMaxConcurrency = 4;
    private const int MaxConcurrency = 128;
    private const int DefaultRetries = 0;
    private const int MaxRetries = 10;
    private const int DefaultRetryDelayMs = 250;
    private const int MaxRetryDelayMs = 10_000;
    private const int MaxViewTop = 5000;

    private static readonly string[] ProbeKinds = { "ldap", "dns", "kerberos", "ntp", "replication" };
    private static readonly IReadOnlyDictionary<string, MonitoringDnsProtocol> DnsProtocols =
        new Dictionary<string, MonitoringDnsProtocol>(StringComparer.OrdinalIgnoreCase) {
            ["udp"] = MonitoringDnsProtocol.Udp,
            ["tcp"] = MonitoringDnsProtocol.Tcp,
            ["both"] = MonitoringDnsProtocol.Both
        };
    private static readonly IReadOnlyDictionary<string, KerberosTransport> KerberosProtocols =
        new Dictionary<string, KerberosTransport>(StringComparer.OrdinalIgnoreCase) {
            ["udp"] = KerberosTransport.Udp,
            ["tcp"] = KerberosTransport.Tcp,
            ["both"] = KerberosTransport.Both
        };
    private static readonly IReadOnlyDictionary<string, ReplicationQueryMode> ReplicationModes =
        new Dictionary<string, ReplicationQueryMode>(StringComparer.OrdinalIgnoreCase) {
            ["auto"] = ReplicationQueryMode.Auto,
            ["drsr"] = ReplicationQueryMode.Drsr,
            ["sda"] = ReplicationQueryMode.Sda
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_probe_run",
        "Run an AD monitoring probe through ADPlayground.Monitoring (ldap/dns/kerberos/ntp/replication) with optional domain/forest/DC scoping.",
        ToolSchema.Object(
                ("probe_kind", ToolSchema.String("Probe kind to execute.").Enum("ldap", "dns", "kerberos", "ntp", "replication")),
                ("name", ToolSchema.String("Optional probe execution name. If omitted, a generated name is used.")),
                ("targets", ToolSchema.Array(ToolSchema.String(), "Optional explicit target hosts. When omitted, AD discovery can be used via domain/forest/include filters.")),
                ("domain_controller", ToolSchema.String("Optional single DC host shortcut. If set and targets are omitted, this DC is used as target.")),
                ("domain_name", ToolSchema.String("Optional DNS domain scope used for discovery and probe defaults.")),
                ("forest_name", ToolSchema.String("Optional forest scope used for discovery.")),
                ("include_domains", ToolSchema.Array(ToolSchema.String(), "Optional include-domain filter for discovery.")),
                ("exclude_domains", ToolSchema.Array(ToolSchema.String(), "Optional exclude-domain filter for discovery.")),
                ("include_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional include-DC filter for discovery.")),
                ("exclude_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional exclude-DC filter for discovery.")),
                ("skip_rodc", ToolSchema.Boolean("When true, excludes RODCs from discovered targets.")),
                ("include_trusts", ToolSchema.Boolean("When true, includes trusted domains in discovery.")),
                ("timeout_ms", ToolSchema.Integer("Probe timeout in milliseconds. Default 5000.")),
                ("retries", ToolSchema.Integer("Retry count. Default 0.")),
                ("retry_delay_ms", ToolSchema.Integer("Retry delay in milliseconds. Default 250.")),
                ("max_concurrency", ToolSchema.Integer("Maximum target concurrency. Default 4.")),
                ("protocol", ToolSchema.String("Transport/profile for applicable probes. dns: udp/tcp/both. kerberos: udp/tcp/both.")),
                ("split_protocol_results", ToolSchema.Boolean("When true (where supported), returns separate protocol child rows.")),
                ("dns_queries", ToolSchema.Array(
                    ToolSchema.Object(
                            ("name", ToolSchema.String("DNS query name/FQDN.")),
                            ("type", ToolSchema.String("DNS type (A, AAAA, SRV, CNAME, PTR, TXT, MX, NS, SOA, etc.).")))
                        .Required("name", "type")
                        .NoAdditionalProperties(),
                    "Optional DNS queries; if omitted and domain_name is available, defaults are derived.")),
                ("verify_certificate", ToolSchema.Boolean("LDAP only: verify LDAPS certificates. Default true.")),
                ("include_global_catalog", ToolSchema.Boolean("LDAP only: include GC ports 3268/3269. Default true.")),
                ("include_facts", ToolSchema.Boolean("LDAP only: include quickly retrievable DC facts. Default true.")),
                ("identity", ToolSchema.String("LDAP only: optional identity to validate (samAccountName/UPN/DN/GUID/SID).")),
                ("stale_threshold_hours", ToolSchema.Integer("Replication only: stale threshold in hours. Default 12.")),
                ("include_sysvol", ToolSchema.Boolean("Replication only: include SYSVOL/DFSR snapshot.")),
                ("test_sysvol_shares", ToolSchema.Boolean("Replication only: validate SYSVOL/NETLOGON share access.")),
                ("test_ports", ToolSchema.Boolean("Replication only: include TCP connectivity precheck details.")),
                ("test_ping", ToolSchema.Boolean("Replication only: include ICMP ping check details.")),
                ("query_mode", ToolSchema.String("Replication only: data source mode.").Enum("auto", "drsr", "sda")),
                ("include_children", ToolSchema.Boolean("When false, omits nested child results in raw probe_result while keeping parent status.")))
            .WithTableViewOptions(
                columnsDescription: "Optional columns projected from flattened probe rows (parent + children + metadata).",
                sortByDescription: "Optional sort column for flattened probe rows.",
                topDescription: "Optional top-N limit for flattened probe rows.")
            .Required("probe_kind")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringProbeRunTool"/> class.
    /// </summary>
    public AdMonitoringProbeRunTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var probeKind = ToolArgs.GetOptionalTrimmed(arguments, "probe_kind");
        if (string.IsNullOrWhiteSpace(probeKind)) {
            return Error("invalid_argument", "probe_kind is required.");
        }

        var normalizedKind = probeKind.Trim().ToLowerInvariant();
        if (!ProbeKinds.Contains(normalizedKind, StringComparer.OrdinalIgnoreCase)) {
            return Error("invalid_argument", "probe_kind must be one of: ldap, dns, kerberos, ntp, replication.");
        }

        var name = ToolArgs.GetOptionalTrimmed(arguments, "name");
        if (string.IsNullOrWhiteSpace(name)) {
            name = $"ix-{normalizedKind}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var domainController = ToolArgs.GetOptionalTrimmed(arguments, "domain_controller");

        var includeDomains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("include_domains"));
        var excludeDomains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("exclude_domains"));
        var includeDomainControllers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("include_domain_controllers"));
        var excludeDomainControllers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("exclude_domain_controllers"));
        var targets = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("targets"));
        if (!string.IsNullOrWhiteSpace(domainController)) {
            includeDomainControllers.Add(domainController!);
            if (targets.Count == 0) {
                targets.Add(domainController!);
            }
        }

        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", DefaultTimeoutMs, 200, MaxTimeoutMs);
        var retries = ToolArgs.GetCappedInt32(arguments, "retries", DefaultRetries, 0, MaxRetries);
        var retryDelayMs = ToolArgs.GetCappedInt32(arguments, "retry_delay_ms", DefaultRetryDelayMs, 0, MaxRetryDelayMs);
        var maxConcurrency = ToolArgs.GetCappedInt32(arguments, "max_concurrency", DefaultMaxConcurrency, 1, MaxConcurrency);
        var includeChildren = ToolArgs.GetBoolean(arguments, "include_children", defaultValue: true);
        var skipRodc = ToolArgs.GetBoolean(arguments, "skip_rodc", defaultValue: false);
        var includeTrusts = ToolArgs.GetBoolean(arguments, "include_trusts", defaultValue: false);
        var splitProtocolResults = ToolArgs.GetBoolean(arguments, "split_protocol_results", defaultValue: false);
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var retryDelay = TimeSpan.FromMilliseconds(retryDelayMs);

        var resolvedTargets = ResolveDirectoryTargets(
            explicitTargets: targets,
            forestName: forestName,
            domainName: domainName,
            includeDomains: includeDomains,
            excludeDomains: excludeDomains,
            includeDomainControllers: includeDomainControllers,
            excludeDomainControllers: excludeDomainControllers,
            skipRodc: skipRodc,
            includeTrusts: includeTrusts,
            cancellationToken: cancellationToken);

        ProbeResult result;
        try {
            switch (normalizedKind) {
                case "ldap":
                    result = await RunLdapAsync().ConfigureAwait(false);
                    break;
                case "dns":
                    result = await RunDnsAsync().ConfigureAwait(false);
                    break;
                case "kerberos":
                    result = await RunKerberosAsync().ConfigureAwait(false);
                    break;
                case "ntp":
                    result = await RunNtpAsync().ConfigureAwait(false);
                    break;
                case "replication":
                    result = await RunReplicationAsync().ConfigureAwait(false);
                    break;
                default:
                    return Error("invalid_argument", "Unsupported probe_kind.");
            }
        } catch (InvalidOperationException ex) {
            return Error("invalid_argument", ex.Message);
        }

        if (!includeChildren) {
            result.Children = null;
        }

        var rows = FlattenProbeRows(result, includeChildren);
        var model = new {
            ProbeKind = normalizedKind,
            ProbeResult = result,
            ResultRows = rows,
            NormalizedRequest = new {
                Name = name,
                DomainName = domainName,
                ForestName = forestName,
                DomainController = domainController,
                Targets = resolvedTargets,
                IncludeDomains = includeDomains,
                ExcludeDomains = excludeDomains,
                IncludeDomainControllers = includeDomainControllers,
                ExcludeDomainControllers = excludeDomainControllers,
                SkipRodc = skipRodc,
                IncludeTrusts = includeTrusts,
                TimeoutMs = timeoutMs,
                Retries = retries,
                RetryDelayMs = retryDelayMs,
                MaxConcurrency = maxConcurrency,
                SplitProtocolResults = splitProtocolResults,
                IncludeChildren = includeChildren
            }
        };

        ToolDynamicTableViewEnvelope.TryBuildModelResponseFromBags(
            arguments: arguments,
            model: model,
            rows: rows,
            title: $"Active Directory Monitoring: {normalizedKind} probe rows",
            rowsPath: "results_view",
            baseTruncated: false,
            response: out var response,
            scanned: rows.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                meta.Add("probe_kind", normalizedKind);
                meta.Add("row_count", rows.Count);
            });
        return response;

        async Task<ProbeResult> RunLdapAsync() {
            var def = new LdapProbeDefinition {
                Name = name!,
                Targets = resolvedTargets.ToArray(),
                DomainName = domainName,
                ForestName = forestName,
                IncludeDomains = includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.ToArray(),
                SkipRodc = skipRodc,
                IncludeTrusts = includeTrusts,
                Timeout = timeout,
                Retries = retries,
                RetryDelay = retryDelay,
                MaxConcurrency = maxConcurrency,
                VerifyCertificate = ToolArgs.GetBoolean(arguments, "verify_certificate", defaultValue: true),
                SkipGlobalCatalog = !ToolArgs.GetBoolean(arguments, "include_global_catalog", defaultValue: true),
                IncludeFacts = ToolArgs.GetBoolean(arguments, "include_facts", defaultValue: true),
                Identity = ToolArgs.GetOptionalTrimmed(arguments, "identity")
            };

            var runner = new LdapProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunDnsAsync() {
            var queries = ReadDnsQueries(arguments?.GetArray("dns_queries"));
            if (queries.Count == 0) {
                var effectiveScope = domainName;
                if (string.IsNullOrWhiteSpace(effectiveScope)) {
                    effectiveScope = DomainHelper.RootDomainName;
                }

                if (!string.IsNullOrWhiteSpace(effectiveScope)) {
                    queries.Add(new DnsQueryItem {
                        Name = $"_ldap._tcp.dc._msdcs.{effectiveScope}",
                        Type = "SRV"
                    });
                    queries.Add(new DnsQueryItem {
                        Name = effectiveScope!,
                        Type = "A"
                    });
                }
            }

            if (queries.Count == 0) {
                throw new InvalidOperationException("DNS probe requires dns_queries or a resolvable domain_name scope.");
            }
            if (resolvedTargets.Count == 0) {
                throw new InvalidOperationException("DNS probe requires at least one target DNS server (targets/domain_controller/include_domain_controllers).");
            }

            var protocol = ToolEnumBinders.ParseOrDefault(
                value: ToolArgs.GetOptionalTrimmed(arguments, "protocol"),
                map: DnsProtocols,
                defaultValue: MonitoringDnsProtocol.Both);

            var def = new DnsProbeDefinition {
                Name = name!,
                Targets = resolvedTargets.ToList(),
                DomainName = domainName,
                ForestName = forestName,
                IncludeDomains = includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.ToArray(),
                SkipRodc = skipRodc,
                IncludeTrusts = includeTrusts,
                Timeout = timeout,
                Retries = retries,
                RetryDelay = retryDelay,
                MaxConcurrency = maxConcurrency,
                Protocol = protocol,
                SplitProtocolResults = splitProtocolResults,
                PerQueryTimeout = timeout,
                Queries = queries
            };

            var runner = new DnsProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunKerberosAsync() {
            var protocol = ToolEnumBinders.ParseOrDefault(
                value: ToolArgs.GetOptionalTrimmed(arguments, "protocol"),
                map: KerberosProtocols,
                defaultValue: KerberosTransport.Both);

            var def = new KerberosProbeDefinition {
                Name = name!,
                Targets = resolvedTargets.ToArray(),
                DomainName = domainName,
                ForestName = forestName,
                IncludeDomains = includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.ToArray(),
                SkipRodc = skipRodc,
                IncludeTrusts = includeTrusts,
                Timeout = timeout,
                Retries = retries,
                RetryDelay = retryDelay,
                MaxConcurrency = maxConcurrency,
                Transport = protocol,
                SplitProtocolResults = splitProtocolResults
            };

            var runner = new KerberosProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunNtpAsync() {
            var def = new NtpProbeDefinition {
                Name = name!,
                Targets = resolvedTargets.ToArray(),
                DomainName = domainName,
                ForestName = forestName,
                IncludeDomains = includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.ToArray(),
                SkipRodc = skipRodc,
                IncludeTrusts = includeTrusts,
                Timeout = timeout,
                Retries = retries,
                RetryDelay = retryDelay,
                MaxConcurrency = maxConcurrency,
                RequestTimeout = timeout
            };

            var runner = new NtpProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunReplicationAsync() {
            var queryMode = ToolEnumBinders.ParseOrDefault(
                value: ToolArgs.GetOptionalTrimmed(arguments, "query_mode"),
                map: ReplicationModes,
                defaultValue: ReplicationQueryMode.Auto);

            var staleThresholdHours = ToolArgs.GetCappedInt32(arguments, "stale_threshold_hours", 12, 1, 24 * 30);
            var def = new ReplicationProbeDefinition {
                Name = name!,
                DomainName = domainName,
                ForestName = forestName,
                IncludeDomains = includeDomains.ToArray(),
                ExcludeDomains = excludeDomains.ToArray(),
                IncludeDomainControllers = includeDomainControllers.ToArray(),
                ExcludeDomainControllers = excludeDomainControllers.ToArray(),
                SkipRodc = skipRodc,
                IncludeTrusts = includeTrusts,
                Timeout = timeout,
                QueryTimeout = timeout,
                Retries = retries,
                RetryDelay = retryDelay,
                DomainControllers = resolvedTargets.ToList(),
                StaleThreshold = TimeSpan.FromHours(staleThresholdHours),
                IncludeSysvol = ToolArgs.GetBoolean(arguments, "include_sysvol", defaultValue: true),
                TestSysvolShares = ToolArgs.GetBoolean(arguments, "test_sysvol_shares", defaultValue: false),
                TestPorts = ToolArgs.GetBoolean(arguments, "test_ports", defaultValue: false),
                TestPing = ToolArgs.GetBoolean(arguments, "test_ping", defaultValue: false),
                QueryMode = queryMode
            };

            var runner = new ReplicationProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<string> ResolveDirectoryTargets(
        IReadOnlyList<string> explicitTargets,
        string? forestName,
        string? domainName,
        IReadOnlyList<string> includeDomains,
        IReadOnlyList<string> excludeDomains,
        IReadOnlyList<string> includeDomainControllers,
        IReadOnlyList<string> excludeDomainControllers,
        bool skipRodc,
        bool includeTrusts,
        CancellationToken cancellationToken) {
        var fallback = DirectoryDiscoveryFallback.CurrentDomain;
        return DirectoryTargetResolver.ResolveTargets(
            explicitTargets: explicitTargets,
            forestName: forestName,
            domainName: domainName,
            includeDomains: includeDomains,
            excludeDomains: excludeDomains,
            includeDomainControllers: includeDomainControllers,
            excludeDomainControllers: excludeDomainControllers,
            skipRodc: skipRodc,
            includeTrusts: includeTrusts,
            fallback: fallback,
            cancellationToken: cancellationToken);
    }

    private static List<DnsQueryItem> ReadDnsQueries(JsonArray? array) {
        var queries = new List<DnsQueryItem>();
        if (array is null || array.Count == 0) {
            return queries;
        }

        for (var i = 0; i < array.Count; i++) {
            var queryObject = array[i].AsObject();
            if (queryObject is null) {
                continue;
            }

            var name = queryObject.GetString("name")?.Trim();
            var type = queryObject.GetString("type")?.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) {
                continue;
            }

            queries.Add(new DnsQueryItem {
                Name = name!,
                Type = type!
            });
        }

        return queries;
    }

    private static List<IReadOnlyDictionary<string, object?>> FlattenProbeRows(ProbeResult result, bool includeChildren) {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        AddRow(result, depth: 0, parentName: null);
        return rows;

        void AddRow(ProbeResult current, int depth, string? parentName) {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                ["name"] = current.Name,
                ["type"] = current.Type.ToString(),
                ["status"] = current.Status.ToString(),
                ["is_maintenance"] = current.IsMaintenance,
                ["completed_utc"] = current.CompletedUtc.UtcDateTime,
                ["latency_ms"] = current.Latency?.TotalMilliseconds,
                ["duration_ms"] = current.Duration?.TotalMilliseconds,
                ["error"] = current.Error,
                ["details"] = current.Details,
                ["agent"] = current.Agent,
                ["zone"] = current.Zone,
                ["target"] = current.Target,
                ["protocol"] = current.Protocol,
                ["answer_count"] = current.AnswerCount,
                ["answer_sample"] = current.AnswerSample,
                ["resolved_target"] = current.ResolvedTarget,
                ["root_probe"] = current.RootProbe,
                ["depth"] = depth,
                ["parent_name"] = parentName ?? string.Empty,
                ["children_count"] = current.Children?.Count ?? 0
            };

            if (current.Metadata is not null) {
                foreach (var pair in current.Metadata) {
                    if (string.IsNullOrWhiteSpace(pair.Key)) {
                        continue;
                    }

                    var normalizedKey = JsonNamingPolicy.SnakeCaseLower.ConvertName(pair.Key.Trim());
                    row[$"meta_{normalizedKey}"] = pair.Value;
                }
            }

            rows.Add(row);

            if (!includeChildren || current.Children is null || current.Children.Count == 0) {
                return;
            }

            for (var i = 0; i < current.Children.Count; i++) {
                var child = current.Children[i];
                if (child is null) {
                    continue;
                }

                AddRow(child, depth + 1, current.Name);
            }
        }
    }
}
