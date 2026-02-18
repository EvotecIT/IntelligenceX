using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.DirectoryHealth;
using ADPlayground.Monitoring.Probes.Dns;
using ADPlayground.Monitoring.Probes.Https;
using ADPlayground.Network;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdMonitoringProbeRunTool {
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
        var directoryProbeKind = ToolArgs.GetOptionalTrimmed(arguments, "directory_probe_kind");
        if (string.IsNullOrWhiteSpace(directoryProbeKind)) {
            throw new InvalidOperationException("directory_probe_kind is required when probe_kind=directory.");
        }

        if (!DirectoryProbeKindsMap.TryGetValue(directoryProbeKind!, out var kind)) {
            throw new InvalidOperationException("directory_probe_kind must be one of: " + string.Join(", ", DirectoryProbeKinds) + ".");
        }

        var queryTimeout = ReadOptionalTimeSpanFromMilliseconds(arguments, "directory_query_timeout_ms") ?? timeout;
        var commonPort = ToolArgs.GetCappedInt32(arguments, "port", 389, 1, 65535);
        var useAnonymous = ToolArgs.GetBoolean(arguments, "directory_use_anonymous_bind", defaultValue: false);
        var allowFallback = ToolArgs.GetBoolean(arguments, "directory_allow_authenticated_fallback", defaultValue: true);

        DirectoryHealthProbeDefinitionBase definition = kind switch {
            DirectoryHealthProbeKind.RootDse => new DirectoryRootDseProbeDefinition {
                RootDse = {
                    Port = commonPort,
                    UseAnonymousBind = useAnonymous,
                    AllowAuthenticatedFallback = allowFallback,
                    RequireGlobalCatalogReady = ToolArgs.GetBoolean(arguments, "directory_require_global_catalog_ready", defaultValue: false),
                    RequireSynchronized = ToolArgs.GetBoolean(arguments, "directory_require_synchronized", defaultValue: false)
                }
            },
            DirectoryHealthProbeKind.DnsRegistration => new DirectoryDnsRegistrationProbeDefinition {
                DnsRegistration = {
                    QueryTimeout = queryTimeout,
                    SrvQueryName = ToolArgs.GetTrimmedOrDefault(arguments, "directory_query_name", "_ldap._tcp.dc._msdcs.{domain}"),
                    DnsServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_dns_servers")).ToArray(),
                    UseAllDnsServers = ToolArgs.GetBoolean(arguments, "directory_use_all_dns_servers", defaultValue: false)
                }
            },
            DirectoryHealthProbeKind.SrvCoverage => new DirectorySrvCoverageProbeDefinition {
                SrvCoverage = {
                    DnsServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_dns_servers")).ToArray(),
                    Sites = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_sites")).ToArray(),
                    ExcludeSites = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_exclude_sites")).ToArray(),
                    UseLocalSiteFallback = ToolArgs.GetBoolean(arguments, "directory_use_local_site_fallback", defaultValue: true),
                    QueryTimeout = queryTimeout,
                    UseAllDnsServers = ToolArgs.GetBoolean(arguments, "directory_use_all_dns_servers", defaultValue: false)
                }
            },
            DirectoryHealthProbeKind.Fsmo => new DirectoryFsmoProbeDefinition {
                Fsmo = {
                    IncludeForestRoles = ToolArgs.GetBoolean(arguments, "directory_include_forest_roles", defaultValue: true)
                }
            },
            DirectoryHealthProbeKind.SysvolGpt => new DirectorySysvolGptProbeDefinition {
                SysvolGpt = {
                    IncludeLinkedOnly = ToolArgs.GetBoolean(arguments, "directory_include_linked_only", defaultValue: true),
                    MaxGpos = ToolArgs.GetCappedInt32(arguments, "directory_max_gpos", 10, 1, 1000),
                    IncludeGpoIds = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_include_gpo_ids")).ToArray(),
                    IncludeGpoNames = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_include_gpo_names")).ToArray()
                }
            },
            DirectoryHealthProbeKind.NetlogonShare => new DirectoryNetlogonShareProbeDefinition {
                Netlogon = {
                    ShareName = ToolArgs.GetTrimmedOrDefault(arguments, "directory_share_name", "NETLOGON"),
                    RequiredShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_required_shares")).ToArray(),
                    AllowedShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_allowed_shares")).ToArray(),
                    IgnoreDriveShares = ToolArgs.GetBoolean(arguments, "directory_ignore_drive_shares", defaultValue: true)
                }
            },
            DirectoryHealthProbeKind.DnsSoa => new DirectoryDnsSoaProbeDefinition {
                DnsSoa = {
                    DnsServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_dns_servers")).ToArray(),
                    Zones = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_zones")),
                    QueryTimeout = queryTimeout,
                    UseAllDnsServers = ToolArgs.GetBoolean(arguments, "directory_use_all_dns_servers", defaultValue: false)
                }
            },
            DirectoryHealthProbeKind.LdapSearch => new DirectoryLdapSearchProbeDefinition {
                LdapSearch = {
                    SearchBase = ToolArgs.GetTrimmedOrDefault(arguments, "directory_search_base", string.Empty),
                    Filter = ToolArgs.GetTrimmedOrDefault(arguments, "directory_filter", "(objectClass=*)"),
                    Attribute = ToolArgs.GetTrimmedOrDefault(arguments, "directory_attribute", "distinguishedName"),
                    Port = commonPort,
                    UseLdaps = ToolArgs.GetBoolean(arguments, "directory_use_ldaps", defaultValue: false),
                    UseStartTls = ToolArgs.GetBoolean(arguments, "directory_use_start_tls", defaultValue: false),
                    UseAnonymousBind = useAnonymous,
                    AllowAuthenticatedFallback = allowFallback,
                    BindIdentity = ToolArgs.GetOptionalTrimmed(arguments, "bind_identity"),
                    BindSecret = ToolArgs.GetOptionalTrimmed(arguments, "bind_secret"),
                    Timeout = queryTimeout,
                    DegradedAbove = ReadOptionalTimeSpanFromMilliseconds(arguments, "degraded_above_ms") ?? TimeSpan.FromSeconds(1)
                }
            },
            DirectoryHealthProbeKind.GcReadiness => new DirectoryGcReadinessProbeDefinition {
                GcReadiness = {
                    QueryName = ToolArgs.GetTrimmedOrDefault(arguments, "directory_query_name", "_gc._tcp.{domain}"),
                    DnsServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_dns_servers")).ToArray(),
                    UseAllDnsServers = ToolArgs.GetBoolean(arguments, "directory_use_all_dns_servers", defaultValue: false),
                    QueryTimeout = queryTimeout,
                    Port = commonPort,
                    LdapTimeout = queryTimeout,
                    UseAnonymousBind = useAnonymous,
                    AllowAuthenticatedFallback = allowFallback,
                    RequireGlobalCatalogReady = ToolArgs.GetBoolean(arguments, "directory_require_global_catalog_ready", defaultValue: true),
                    RequireSynchronized = ToolArgs.GetBoolean(arguments, "directory_require_synchronized", defaultValue: false)
                }
            },
            DirectoryHealthProbeKind.ClientPath => new DirectoryClientPathProbeDefinition {
                ClientPath = {
                    QueryName = ToolArgs.GetTrimmedOrDefault(arguments, "directory_query_name", "_ldap._tcp.dc._msdcs.{domain}"),
                    DnsServers = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_dns_servers")).ToArray(),
                    Sites = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_sites")).ToArray(),
                    ExcludeSites = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_exclude_sites")).ToArray(),
                    UseLocalSiteFallback = ToolArgs.GetBoolean(arguments, "directory_use_local_site_fallback", defaultValue: true),
                    UseAllDnsServers = ToolArgs.GetBoolean(arguments, "directory_use_all_dns_servers", defaultValue: false),
                    QueryTimeout = queryTimeout,
                    Port = commonPort,
                    UseLdaps = ToolArgs.GetBoolean(arguments, "directory_use_ldaps", defaultValue: false),
                    UseAnonymousBind = useAnonymous,
                    AllowAuthenticatedFallback = allowFallback,
                    Timeout = queryTimeout,
                    DegradedAbove = ReadOptionalTimeSpanFromMilliseconds(arguments, "degraded_above_ms") ?? TimeSpan.FromSeconds(1)
                }
            },
            DirectoryHealthProbeKind.RpcEndpoint => new DirectoryRpcEndpointProbeDefinition {
                RpcEndpoint = {
                    Port = ToolArgs.GetCappedInt32(arguments, "port", 135, 1, 65535),
                    Timeout = queryTimeout,
                    RetryCount = retries,
                    RetryDelay = retryDelay,
                    DegradedAbove = ReadOptionalTimeSpanFromMilliseconds(arguments, "degraded_above_ms") ?? TimeSpan.FromSeconds(1)
                }
            },
            DirectoryHealthProbeKind.SharePermissions => new DirectorySharePermissionsProbeDefinition {
                SharePermissions = {
                    RequiredShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_required_shares")).ToArray(),
                    OptionalShares = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("directory_optional_shares")).ToArray()
                }
            },
            _ => throw new InvalidOperationException("Unsupported directory_probe_kind.")
        };

        definition.Name = name;
        definition.Targets = resolvedTargets.ToArray();
        definition.DomainName = domainName;
        definition.ForestName = forestName;
        definition.IncludeDomains = includeDomains.ToArray();
        definition.ExcludeDomains = excludeDomains.ToArray();
        definition.IncludeDomainControllers = includeDomainControllers.ToArray();
        definition.ExcludeDomainControllers = excludeDomainControllers.ToArray();
        definition.SkipRodc = skipRodc;
        definition.IncludeTrusts = includeTrusts;
        definition.Timeout = timeout;
        definition.Retries = retries;
        definition.RetryDelay = retryDelay;
        definition.MaxConcurrency = maxConcurrency;
        definition.TotalBudget = ReadOptionalTimeSpanFromMilliseconds(arguments, "total_budget_ms") ?? TimeSpan.Zero;

        var runner = new DirectoryHealthProbeRunner();
        return await runner.ExecuteAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProbeResult> RunPingAsyncCore(
        JsonObject? arguments,
        string name,
        IReadOnlyList<string> resolvedTargets,
        int retries,
        int timeoutMs,
        TimeSpan retryDelay,
        CancellationToken cancellationToken) {
        var pingTargets = resolvedTargets
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pingTargets.Count == 0) {
            throw new InvalidOperationException("Ping probe requires at least one resolved target.");
        }

        var latencyThreshold = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("latency_threshold_ms"));
        var p95Threshold = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("p95_latency_threshold_ms"));
        var lossThresholdPercentRaw = arguments?.GetInt64("loss_threshold_percent");
        int? lossThresholdPercent = null;
        if (lossThresholdPercentRaw.HasValue) {
            if (lossThresholdPercentRaw.Value < 0 || lossThresholdPercentRaw.Value > 100) {
                throw new InvalidOperationException("loss_threshold_percent must be between 0 and 100.");
            }
            lossThresholdPercent = (int)lossThresholdPercentRaw.Value;
        }

        using var ping = new Ping();
        var children = new List<ProbeResult>(pingTargets.Count);
        foreach (var target in pingTargets) {
            var attempts = Math.Max(1, retries + 1);
            var failures = 0;
            long? rttMs = null;
            string? error = null;
            for (var attempt = 0; attempt < attempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();

                PingReply? reply = null;
                try {
                    reply = await ping.SendPingAsync(target, timeoutMs).ConfigureAwait(false);
                } catch (Exception ex) {
                    failures++;
                    error = SanitizeErrorMessage(ex.Message, "Ping attempt failed.");
                    if (attempt + 1 < attempts && retryDelay > TimeSpan.Zero) {
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }

                if (reply.Status == IPStatus.Success) {
                    rttMs = reply.RoundtripTime;
                    error = null;
                    break;
                }

                failures++;
                error = reply.Status.ToString();
                if (attempt + 1 < attempts && retryDelay > TimeSpan.Zero) {
                    await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                }
            }

            var status = rttMs.HasValue ? ProbeStatus.Up : ProbeStatus.Down;
            var lossPercent = (int)Math.Round((failures * 100.0) / attempts, MidpointRounding.AwayFromZero);
            if (status == ProbeStatus.Up && rttMs.HasValue) {
                var rttValue = rttMs.Value;
                if (latencyThreshold.HasValue && rttValue > latencyThreshold.Value) {
                    status = ProbeStatus.Degraded;
                } else if (p95Threshold.HasValue && rttValue > p95Threshold.Value) {
                    status = ProbeStatus.Degraded;
                } else if (lossThresholdPercent.HasValue && lossPercent > lossThresholdPercent.Value) {
                    status = ProbeStatus.Degraded;
                }
            }

            children.Add(new ProbeResult {
                Name = $"{name}-{target}",
                Type = ProbeType.Ping,
                Status = status,
                CompletedUtc = DateTimeOffset.UtcNow,
                Latency = rttMs.HasValue ? TimeSpan.FromMilliseconds(rttMs.Value) : null,
                Duration = rttMs.HasValue ? TimeSpan.FromMilliseconds(rttMs.Value) : null,
                Error = status == ProbeStatus.Up ? null : (error ?? "Ping failed."),
                Target = target,
                Protocol = "ICMP",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["pingAttempts"] = attempts.ToString(),
                    ["pingFailures"] = failures.ToString(),
                    ["pingLossPercent"] = lossPercent.ToString()
                }
            });
        }

        if (children.Count == 1) {
            var single = children[0];
            single.Name = name;
            return single;
        }

        return BuildAggregateParentResult(
            name: name,
            type: ProbeType.Ping,
            protocol: "ICMP",
            targetLabel: $"{children.Count} targets",
            children: children);
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

    private static HttpsProbeDefinition CloneHttpsDefinition(HttpsProbeDefinition template, string endpoint, string name) {
        return new HttpsProbeDefinition {
            Name = name,
            Timeout = template.Timeout,
            Retries = template.Retries,
            RetryDelay = template.RetryDelay,
            Port = template.Port,
            VerifyCertificate = template.VerifyCertificate,
            CertificateDegradedDays = template.CertificateDegradedDays,
            DegradedAbove = template.DegradedAbove,
            Url = endpoint
        };
    }

    private static ProbeResult BuildAggregateParentResult(
        string name,
        ProbeType type,
        string protocol,
        string targetLabel,
        IReadOnlyList<ProbeResult> children) {
        var up = 0;
        var down = 0;
        var degraded = 0;
        var recovering = 0;
        var unknown = 0;
        foreach (var child in children) {
            switch (child.Status) {
                case ProbeStatus.Up:
                    up++;
                    break;
                case ProbeStatus.Down:
                    down++;
                    break;
                case ProbeStatus.Degraded:
                    degraded++;
                    break;
                case ProbeStatus.Recovering:
                    recovering++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        var status = down > 0
            ? ProbeStatus.Down
            : degraded > 0
                ? ProbeStatus.Degraded
                : recovering > 0
                    ? ProbeStatus.Recovering
                    : up > 0
                        ? ProbeStatus.Up
                        : ProbeStatus.Unknown;

        var firstError = children.FirstOrDefault(static child =>
            child.Status == ProbeStatus.Down || child.Status == ProbeStatus.Degraded)?.Error;
        var hasLatency = children.Any(static child => child.Latency.HasValue);
        var avgLatency = hasLatency
            ? TimeSpan.FromMilliseconds(children.Where(static child => child.Latency.HasValue).Average(static child => child.Latency!.Value.TotalMilliseconds))
            : (TimeSpan?)null;

        return new ProbeResult {
            Name = name,
            Type = type,
            Status = status,
            CompletedUtc = DateTimeOffset.UtcNow,
            Latency = avgLatency,
            Duration = avgLatency,
            Error = firstError,
            Details = $"checks={children.Count} up={up} degraded={degraded} down={down} recovering={recovering} unknown={unknown}",
            Protocol = protocol,
            Target = targetLabel,
            AnswerCount = children.Count,
            Children = children.ToList()
        };
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
