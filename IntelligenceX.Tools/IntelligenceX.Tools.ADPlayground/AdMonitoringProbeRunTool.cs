using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.Adws;
using ADPlayground.Monitoring.Probes.Dns;
using ADPlayground.Monitoring.Probes.DnsService;
using ADPlayground.Monitoring.Probes.Https;
using ADPlayground.Monitoring.Probes.Kerberos;
using ADPlayground.Monitoring.Probes.Ldap;
using ADPlayground.Monitoring.Probes.Ntp;
using ADPlayground.Monitoring.Probes.Port;
using ADPlayground.Monitoring.Probes.Replication;
using ADPlayground.Network;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using MonitoringDnsProtocol = ADPlayground.Monitoring.Probes.Dns.DnsProtocol;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Executes ADPlayground monitoring probes for all ADPlayground.Monitoring probe kinds.
/// </summary>
public sealed partial class AdMonitoringProbeRunTool : ActiveDirectoryToolBase, ITool {
    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var probeKind = ToolArgs.GetOptionalTrimmed(arguments, "probe_kind");
        if (string.IsNullOrWhiteSpace(probeKind)) {
            return Error("invalid_argument", "probe_kind is required.");
        }

        var normalizedKind = NormalizeProbeKind(probeKind);
        if (!ProbeKinds.Contains(normalizedKind, StringComparer.OrdinalIgnoreCase)) {
            return Error("invalid_argument", "probe_kind must be one of: " + string.Join(", ", ProbeKinds) + ".");
        }

        var name = ToolArgs.GetOptionalTrimmed(arguments, "name");
        if (string.IsNullOrWhiteSpace(name)) {
            name = $"ix-{normalizedKind}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
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
        var defaultDiscoveryFallback =
            string.Equals(normalizedKind, "replication", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedKind, "directory", StringComparison.OrdinalIgnoreCase)
            ? DirectoryDiscoveryFallback.CurrentForest
            : DirectoryDiscoveryFallback.CurrentDomain;
        var discoveryFallback = ToolEnumBinders.ParseOrDefault(
            value: ToolArgs.GetOptionalTrimmed(arguments, "discovery_fallback"),
            map: DiscoveryFallbackModes,
            defaultValue: defaultDiscoveryFallback);
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
            fallback: discoveryFallback,
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
                case "port":
                    result = await RunPortAsync().ConfigureAwait(false);
                    break;
                case "https":
                    result = await RunHttpsAsync().ConfigureAwait(false);
                    break;
                case "dns_service":
                    result = await RunDnsServiceAsync().ConfigureAwait(false);
                    break;
                case "adws":
                    result = await RunAdwsAsync().ConfigureAwait(false);
                    break;
                case "directory":
                    result = await RunDirectoryAsync().ConfigureAwait(false);
                    break;
                case "ping":
                    result = await RunPingAsync().ConfigureAwait(false);
                    break;
                default:
                    return Error("invalid_argument", "Unsupported probe_kind.");
            }
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Monitoring probe execution failed.");
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
                DiscoveryFallback = ToDiscoveryFallbackName(discoveryFallback),
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

        async Task<ProbeResult> RunAdwsAsync() {
            var def = new AdwsProbeDefinition {
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
                Port = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "port", defaultValue: 9389, maxInclusive: 65535),
                Path = ToolArgs.GetTrimmedOrDefault(arguments, "path", "ActiveDirectoryWebServices/Windows/Enumeration"),
                RequestTimeout = ReadOptionalTimeSpanFromMilliseconds(arguments, "request_timeout_ms") ?? timeout,
                FailureHandling = ToolEnumBinders.ParseOrDefault(
                    ToolArgs.GetOptionalTrimmed(arguments, "adws_failure_handling"),
                    IssueHandlingModes,
                    PortIssueHandling.Down),
                BindIdentity = ToolArgs.GetOptionalTrimmed(arguments, "bind_identity"),
                BindSecret = ToolArgs.GetOptionalTrimmed(arguments, "bind_secret"),
                MaxConcurrency = maxConcurrency
            };

            var runner = new AdwsProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunPortAsync() {
            var tcpPorts = ToolArgs.ReadPositiveInt32ArrayCapped(arguments?.GetArray("tcp_ports"), 65535)
                .Distinct()
                .OrderBy(static x => x)
                .ToArray();
            var udpPorts = ToolArgs.ReadPositiveInt32ArrayCapped(arguments?.GetArray("udp_ports"), 65535)
                .Distinct()
                .OrderBy(static x => x)
                .ToArray();

            var def = new PortProbeDefinition {
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
                Ports = tcpPorts,
                UdpPorts = udpPorts,
                IncludeUdp = ToolArgs.GetBoolean(arguments, "include_udp", defaultValue: false),
                UseAdCoreProfile = ToolArgs.GetBoolean(arguments, "use_ad_core_profile", defaultValue: true),
                RetryCount = retries
            };

            var runner = new PortProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunDnsServiceAsync() {
            var protocol = ToolEnumBinders.ParseOrDefault(
                value: ToolArgs.GetOptionalTrimmed(arguments, "protocol"),
                map: DnsProtocols,
                defaultValue: MonitoringDnsProtocol.Udp);

            var def = new DnsServiceProbeDefinition {
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
                Protocol = protocol,
                QueryName = ToolArgs.GetTrimmedOrDefault(arguments, "dns_service_query_name", "_ldap._tcp.dc._msdcs.{domain}"),
                RecordType = ToolArgs.GetTrimmedOrDefault(arguments, "dns_service_record_type", "SRV"),
                RequireAnswers = ToolArgs.GetBoolean(arguments, "dns_service_require_answers", defaultValue: true),
                QueryTimeout = ReadOptionalTimeSpanFromMilliseconds(arguments, "dns_service_query_timeout_ms") ?? timeout,
                MaxConcurrency = maxConcurrency
            };

            var runner = new DnsServiceProbeRunner();
            return await runner.ExecuteAsync(def, cancellationToken).ConfigureAwait(false);
        }

        async Task<ProbeResult> RunHttpsAsync() {
            var endpoints = new List<string>();
            var url = ToolArgs.GetOptionalTrimmed(arguments, "url");
            if (!string.IsNullOrWhiteSpace(url)) {
                endpoints.Add(url!);
            }
            if (targets.Count > 0) {
                endpoints.AddRange(targets);
            }
            if (endpoints.Count == 0) {
                endpoints.AddRange(resolvedTargets);
            }

            endpoints = endpoints
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (endpoints.Count == 0) {
                throw new InvalidOperationException("HTTPS probe requires url, targets, or resolvable scope.");
            }

            var template = new HttpsProbeDefinition {
                Name = name!,
                Timeout = timeout,
                Retries = retries,
                RetryDelay = retryDelay,
                Port = ToolArgs.GetCappedInt32(arguments, "port", 443, 1, 65535),
                VerifyCertificate = ToolArgs.GetBoolean(arguments, "verify_certificate", defaultValue: true),
                CertificateDegradedDays = ToolArgs.GetCappedInt32(arguments, "certificate_degraded_days", 30, 0, 3650),
                DegradedAbove = ReadOptionalTimeSpanFromMilliseconds(arguments, "degraded_above_ms") ?? TimeSpan.FromSeconds(2)
            };

            var runner = new HttpsProbeRunner();
            if (endpoints.Count == 1) {
                var single = CloneHttpsDefinition(template, endpoints[0], name!);
                return await runner.ExecuteAsync(single, cancellationToken).ConfigureAwait(false);
            }

            var children = new List<ProbeResult>(endpoints.Count);
            for (var i = 0; i < endpoints.Count; i++) {
                var endpoint = endpoints[i];
                var child = CloneHttpsDefinition(template, endpoint, $"{name}-{i + 1}");
                children.Add(await runner.ExecuteAsync(child, cancellationToken).ConfigureAwait(false));
            }

            return BuildAggregateParentResult(
                name: name!,
                type: ProbeType.Https,
                protocol: "HTTPS",
                targetLabel: $"{children.Count} targets",
                children: children);
        }

        Task<ProbeResult> RunDirectoryAsync() {
            return RunDirectoryAsyncCore(
                arguments: arguments,
                name: name!,
                resolvedTargets: resolvedTargets,
                domainName: domainName,
                forestName: forestName,
                includeDomains: includeDomains,
                excludeDomains: excludeDomains,
                includeDomainControllers: includeDomainControllers,
                excludeDomainControllers: excludeDomainControllers,
                skipRodc: skipRodc,
                includeTrusts: includeTrusts,
                timeout: timeout,
                retries: retries,
                retryDelay: retryDelay,
                maxConcurrency: maxConcurrency,
                cancellationToken: cancellationToken);
        }

        Task<ProbeResult> RunPingAsync() {
            return RunPingAsyncCore(
                arguments: arguments,
                name: name!,
                resolvedTargets: resolvedTargets,
                retries: retries,
                timeoutMs: timeoutMs,
                retryDelay: retryDelay,
                cancellationToken: cancellationToken);
        }
    }
}
