using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.Ping;
using ADPlayground.Monitoring.Probes.DirectoryHealth;
using ADPlayground.Monitoring.Probes.Dns;
using ADPlayground.Network;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdMonitoringProbeRunTool {
    private static ToolChainContractModel BuildChainContract(
        string normalizedKind,
        string? directoryProbeKind,
        ProbeResult result,
        IReadOnlyList<string> resolvedTargets,
        string? domainName,
        string? forestName,
        bool includeTrusts,
        DirectoryDiscoveryFallback discoveryFallback) {
        var nextActions = BuildNextActions(
            normalizedKind: normalizedKind,
            directoryProbeKind: directoryProbeKind,
            resolvedTargets: resolvedTargets,
            domainName: domainName,
            forestName: forestName,
            includeTrusts: includeTrusts,
            discoveryFallback: discoveryFallback);
        var fallbackName = ToDiscoveryFallbackName(discoveryFallback);
        var confidence = nextActions.Count == 0
            ? 0d
            : result.Status switch {
                ProbeStatus.Down => 0.92d,
                ProbeStatus.Degraded => 0.88d,
                ProbeStatus.Recovering => 0.84d,
                ProbeStatus.Up => 0.78d,
                _ => 0.72d
            };

        return ToolChainingHints.Create(
            nextActions: nextActions,
            cursor: ToolChainingHints.BuildToken(
                "ad_monitoring_probe_run",
                ("probe_kind", normalizedKind),
                ("directory_probe_kind", directoryProbeKind ?? string.Empty),
                ("status", result.Status.ToString()),
                ("target", resolvedTargets.FirstOrDefault() ?? string.Empty)),
            resumeToken: ToolChainingHints.BuildToken(
                "ad_monitoring_probe_run.resume",
                ("probe_kind", normalizedKind),
                ("fallback", fallbackName),
                ("target_count", resolvedTargets.Count.ToString())),
            flowId: ToolChainingHints.BuildToken(
                "ad_monitoring_probe_follow_up",
                ("probe_kind", normalizedKind),
                ("directory_probe_kind", directoryProbeKind ?? string.Empty)),
            stepId: ToolChainingHints.BuildToken(
                "ad_monitoring_probe_follow_up.step",
                ("status", result.Status.ToString())),
            handoff: ToolChainingHints.Map(
                ("contract", "ad_monitoring_probe_run_handoff"),
                ("probe_kind", normalizedKind),
                ("directory_probe_kind", directoryProbeKind ?? string.Empty),
                ("status", result.Status.ToString()),
                ("forest_name", forestName ?? string.Empty),
                ("domain_name", domainName ?? string.Empty),
                ("discovery_fallback", fallbackName),
                ("targets_preview", string.Join(";", resolvedTargets.Take(10)))),
            checkpoint: ToolChainingHints.Map(
                ("current_tool", "ad_monitoring_probe_run"),
                ("probe_kind", normalizedKind),
                ("directory_probe_kind", directoryProbeKind ?? string.Empty),
                ("status", result.Status.ToString()),
                ("row_targets", resolvedTargets.Count),
                ("include_trusts", includeTrusts)),
            confidence: confidence);
    }

    private static IReadOnlyList<ToolNextActionModel> BuildNextActions(
        string normalizedKind,
        string? directoryProbeKind,
        IReadOnlyList<string> resolvedTargets,
        string? domainName,
        string? forestName,
        bool includeTrusts,
        DirectoryDiscoveryFallback discoveryFallback) {
        var nextActions = new List<ToolNextActionModel>();
        var primaryTarget = TryResolvePrimaryTarget(resolvedTargets);

        switch (normalizedKind) {
            case "ldap":
                nextActions.Add(BuildLdapDiagnosticsAction(primaryTarget));
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_ldap_policy_posture", "Inspect host LDAP signing and channel-binding posture on the same target."),
                    ("system_info", "Collect same-host runtime identity and OS context for LDAP follow-through."));
                break;
            case "dns":
            case "dns_service":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_network_client_posture", "Inspect DNS client and name-resolution posture on the same target."),
                    ("system_network_adapters", "Inspect same-host network adapters and addressing for DNS follow-through."));
                break;
            case "kerberos":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_time_sync", "Inspect same-host time-sync posture when Kerberos timing or KDC latency needs follow-through."),
                    ("system_metrics_summary", "Inspect same-host runtime pressure when Kerberos behavior may be impacted by load."));
                break;
            case "ntp":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_time_sync", "Inspect same-host time-sync posture after NTP skew or packet-loss findings."),
                    ("system_metrics_summary", "Inspect same-host runtime pressure when time service behavior looks unstable."));
                break;
            case "replication":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_metrics_summary", "Inspect same-host runtime pressure after replication or topology failures."),
                    ("system_logical_disks_list", "Inspect same-host disks when SYSVOL or storage-backed replication may be involved."),
                    ("system_ports_list", "Inspect same-host listening ports when replication endpoints appear unreachable."));
                break;
            case "port":
            case "adws":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_ports_list", "Inspect same-host listeners and port state for the failing endpoint."),
                    ("system_service_list", "Inspect same-host services behind the failing listener or ADWS path."));
                break;
            case "https":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_tls_posture", "Inspect same-host TLS/SChannel posture after HTTPS probe findings."),
                    ("system_certificate_posture", "Inspect same-host certificate-store posture when HTTPS trust-store follow-through is needed."));
                break;
            case "ping":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_metrics_summary", "Inspect same-host runtime pressure after latency, jitter, or loss findings."),
                    ("system_network_adapters", "Inspect same-host network adapters after ICMP reachability or latency issues."));
                break;
            case "windows_update":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_windows_update_client_status", "Inspect same-host WSUS and Windows Update client state."),
                    ("system_windows_update_telemetry", "Inspect same-host update freshness, reboot pressure, and telemetry."),
                    ("system_patch_compliance", "Correlate same-host installed updates with missing security coverage."));
                break;
            case "directory":
                AddDirectoryFollowUpActions(nextActions, directoryProbeKind, primaryTarget);
                break;
        }

        if (nextActions.Count == 0 && (!string.IsNullOrWhiteSpace(domainName) || !string.IsNullOrWhiteSpace(forestName))) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "ad_scope_discovery",
                reason: "Reconfirm AD scope before deeper follow-up when probe output lacks a concrete host pivot.",
                suggestedArguments: ToolChainingHints.Map(
                    ("forest_name", forestName ?? string.Empty),
                    ("domain_name", domainName ?? string.Empty),
                    ("discovery_fallback", ToDiscoveryFallbackName(discoveryFallback)),
                    ("include_trusts", includeTrusts)),
                mutating: false));
        }

        return nextActions;
    }

    private static void AddDirectoryFollowUpActions(
        ICollection<ToolNextActionModel> nextActions,
        string? directoryProbeKind,
        string primaryTarget) {
        var normalizedDirectoryKind = NormalizeProbeKind(directoryProbeKind ?? string.Empty);
        switch (normalizedDirectoryKind) {
            case "ldap_search":
            case "gc_readiness":
            case "root_dse":
            case "client_path":
                nextActions.Add(BuildLdapDiagnosticsAction(primaryTarget));
                break;
            case "rpc_endpoint":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_ports_list", "Inspect same-host listener inventory after directory RPC endpoint failures."),
                    ("system_service_list", "Inspect same-host services after directory RPC endpoint failures."));
                break;
            case "sysvol_gpt":
            case "netlogon_share":
            case "share_permissions":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_logical_disks_list", "Inspect same-host disks and share backing paths after directory share or SYSVOL failures."),
                    ("system_service_list", "Inspect same-host services behind SYSVOL or share availability."));
                break;
            case "dns_registration":
            case "dns_soa":
            case "srv_coverage":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_network_client_posture", "Inspect same-host DNS client posture after directory DNS findings."),
                    ("system_network_adapters", "Inspect same-host network adapters after directory DNS findings."));
                break;
            case "fsmo":
                AddSingleHostSystemActions(
                    nextActions,
                    primaryTarget,
                    ("system_info", "Inspect same-host runtime identity and OS context after FSMO follow-through."));
                break;
        }
    }

    private static ToolNextActionModel BuildLdapDiagnosticsAction(string primaryTarget) {
        var suggestedArgs = new List<(string Key, object? Value)> {
            ("verify_certificate", true),
            ("include_global_catalog", true)
        };
        if (!string.IsNullOrWhiteSpace(primaryTarget)) {
            suggestedArgs.Add(("domain_controller", primaryTarget));
        }

        return ToolChainingHints.NextAction(
            tool: "ad_ldap_diagnostics",
            reason: "Inspect LDAP/LDAPS endpoint status and certificate details on the same AD target.",
            suggestedArguments: ToolChainingHints.Map(suggestedArgs.ToArray()),
            mutating: false);
    }

    private static void AddSingleHostSystemActions(
        ICollection<ToolNextActionModel> nextActions,
        string primaryTarget,
        params (string ToolName, string Reason)[] actions) {
        if (nextActions is null || actions is null || actions.Length == 0 || string.IsNullOrWhiteSpace(primaryTarget)) {
            return;
        }

        for (var i = 0; i < actions.Length; i++) {
            var (toolName, reason) = actions[i];
            if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(reason)) {
                continue;
            }

            nextActions.Add(ToolChainingHints.NextAction(
                tool: toolName,
                reason: reason,
                suggestedArguments: ToolChainingHints.Map(("computer_name", primaryTarget)),
                mutating: false));
        }
    }

    private static string TryResolvePrimaryTarget(IReadOnlyList<string> resolvedTargets) {
        if (resolvedTargets is null || resolvedTargets.Count == 0) {
            return string.Empty;
        }

        for (var i = 0; i < resolvedTargets.Count; i++) {
            var target = (resolvedTargets[i] ?? string.Empty).Trim();
            if (target.Length > 0) {
                return target;
            }
        }

        return string.Empty;
    }

    private async Task<ProbeResult> RunDirectoryAsyncCore(
        JsonObject? arguments,
        string name,
        IReadOnlyList<string> resolvedTargets,
        string? domainName,
        string? forestName,
        IReadOnlyList<string> includeDomains,
        IReadOnlyList<string> excludeDomains,
        IReadOnlyList<string> includeDomainControllers,
        IReadOnlyList<string> excludeDomainControllers,
        bool skipRodc,
        bool includeTrusts,
        TimeSpan timeout,
        int retries,
        TimeSpan retryDelay,
        int maxConcurrency,
        CancellationToken cancellationToken) {
        var queryTimeout = ReadOptionalTimeSpanFromMilliseconds(arguments, "directory_query_timeout_ms") ?? timeout;
        var degradedAbove = ReadOptionalTimeSpanFromMilliseconds(arguments, "degraded_above_ms") ?? TimeSpan.FromSeconds(1);
        var directoryService = new OnDemandDirectoryHealthProbeService();
        return await directoryService.ExecuteAsync(
            new OnDemandDirectoryHealthProbeRequest {
                Name = name,
                DirectoryProbeKind = ToolArgs.GetOptionalTrimmed(arguments, "directory_probe_kind"),
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
                TotalBudget = ReadOptionalTimeSpanFromMilliseconds(arguments, "total_budget_ms") ?? TimeSpan.Zero,
                QueryTimeout = queryTimeout,
                CommonPort = ToolArgs.GetCappedInt32(arguments, "port", 389, 1, 65535),
                UseAnonymousBind = ToolArgs.GetBoolean(arguments, "directory_use_anonymous_bind", defaultValue: false),
                AllowAuthenticatedFallback = ToolArgs.GetBoolean(arguments, "directory_allow_authenticated_fallback", defaultValue: true),
                RequireGlobalCatalogReady = ToolArgs.GetBoolean(arguments, "directory_require_global_catalog_ready", defaultValue: false),
                RequireSynchronized = ToolArgs.GetBoolean(arguments, "directory_require_synchronized", defaultValue: false),
                DnsServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_dns_servers")).ToArray(),
                UseAllDnsServers = ToolArgs.GetBoolean(arguments, "directory_use_all_dns_servers", defaultValue: false),
                QueryName = ToolArgs.GetOptionalTrimmed(arguments, "directory_query_name"),
                Sites = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_sites")).ToArray(),
                ExcludeSites = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_exclude_sites")).ToArray(),
                UseLocalSiteFallback = ToolArgs.GetBoolean(arguments, "directory_use_local_site_fallback", defaultValue: true),
                IncludeForestRoles = ToolArgs.GetBoolean(arguments, "directory_include_forest_roles", defaultValue: true),
                IncludeLinkedOnly = ToolArgs.GetBoolean(arguments, "directory_include_linked_only", defaultValue: true),
                MaxGpos = ToolArgs.GetCappedInt32(arguments, "directory_max_gpos", 10, 1, 1000),
                IncludeGpoIds = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_include_gpo_ids")).ToArray(),
                IncludeGpoNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_include_gpo_names")).ToArray(),
                ShareName = ToolArgs.GetTrimmedOrDefault(arguments, "directory_share_name", "NETLOGON"),
                RequiredShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_required_shares")).ToArray(),
                AllowedShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_allowed_shares")).ToArray(),
                OptionalShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_optional_shares")).ToArray(),
                IgnoreDriveShares = ToolArgs.GetBoolean(arguments, "directory_ignore_drive_shares", defaultValue: true),
                Zones = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_zones")).ToArray(),
                SearchBase = ToolArgs.GetTrimmedOrDefault(arguments, "directory_search_base", string.Empty),
                Filter = ToolArgs.GetTrimmedOrDefault(arguments, "directory_filter", "(objectClass=*)"),
                Attribute = ToolArgs.GetTrimmedOrDefault(arguments, "directory_attribute", "distinguishedName"),
                UseLdaps = ToolArgs.GetBoolean(arguments, "directory_use_ldaps", defaultValue: false),
                UseStartTls = ToolArgs.GetBoolean(arguments, "directory_use_start_tls", defaultValue: false),
                BindIdentity = ToolArgs.GetOptionalTrimmed(arguments, "bind_identity"),
                BindSecret = ToolArgs.GetOptionalTrimmed(arguments, "bind_secret"),
                DegradedAbove = degradedAbove
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProbeResult> RunPingAsyncCore(
        JsonObject? arguments,
        string name,
        IReadOnlyList<string> resolvedTargets,
        int retries,
        int timeoutMs,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) {
        var pingService = new OnDemandPingProbeService();
        return await pingService.ExecuteAsync(
            new OnDemandPingProbeRequest {
                Name = name,
                Targets = resolvedTargets.ToArray(),
                Retries = retries,
                TimeoutMs = timeoutMs,
                RetryDelay = retryDelay,
                LatencyThresholdMs = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("latency_threshold_ms")),
                P95LatencyThresholdMs = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("p95_latency_threshold_ms")),
                LossThresholdPercent = arguments?.GetInt64("loss_threshold_percent")
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeProbeKind(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);

        return normalized switch {
            "dnsservice" => "dns_service",
            "windowsupdate" => "windows_update",
            _ => normalized
        };
    }

    private static TimeSpan? ReadOptionalTimeSpanFromMilliseconds(JsonObject? arguments, string key) {
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        var raw = arguments.GetInt64(key);
        if (!raw.HasValue || raw.Value <= 0) {
            return null;
        }

        var bounded = raw.Value > MaxTimeoutMs ? MaxTimeoutMs : (int)raw.Value;
        return TimeSpan.FromMilliseconds(Math.Max(1, bounded));
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
        DirectoryDiscoveryFallback fallback,
        CancellationToken cancellationToken) {
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

    private static string ToDiscoveryFallbackName(DirectoryDiscoveryFallback fallback) {
        return fallback switch {
            DirectoryDiscoveryFallback.None => "none",
            DirectoryDiscoveryFallback.CurrentForest => "current_forest",
            _ => "current_domain"
        };
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

}
