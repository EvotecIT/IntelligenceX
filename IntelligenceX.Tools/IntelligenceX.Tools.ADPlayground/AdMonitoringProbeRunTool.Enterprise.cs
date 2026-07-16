using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.Adfs;
using ADPlayground.Monitoring.Probes.EntraConnect;
using ADPlayground.Monitoring.Probes.SqlServer;
using ADPlayground.Monitoring.Probes.WindowsBackup;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdMonitoringProbeRunTool {
    private static bool IsEngineOwnedTargetResolutionProbe(string normalizedKind) {
        return normalizedKind is AdfsProbeKind or EntraConnectProbeKind or SqlServerProbeKind or WindowsBackupProbeKind;
    }

    private static int ResolveDefaultTimeoutMs(string normalizedKind, int fallback) {
        return normalizedKind switch {
            AdfsProbeKind => 20_000,
            EntraConnectProbeKind => 15_000,
            SqlServerProbeKind => 10_000,
            WindowsBackupProbeKind => 15_000,
            _ => fallback
        };
    }

    private static int ResolveDefaultMaxConcurrency(string normalizedKind, int fallback) {
        return normalizedKind switch {
            AdfsProbeKind => 2,
            EntraConnectProbeKind => 2,
            WindowsBackupProbeKind => 2,
            _ => fallback
        };
    }

    private static Task<ProbeResult> RunEnterpriseProbeAsync(
        string normalizedKind,
        JsonObject? arguments,
        string name,
        IReadOnlyList<string> resolvedTargets,
        string? domainName,
        string? forestName,
        IReadOnlyList<string> includeDomains,
        IReadOnlyList<string> excludeDomains,
        bool includeTrusts,
        string? domainController,
        TimeSpan timeout,
        int retries,
        TimeSpan retryDelay,
        int maxConcurrency,
        CancellationToken cancellationToken) {
        var definition = BuildEnterpriseProbeDefinition(
            normalizedKind: normalizedKind,
            arguments: arguments,
            name: name,
            resolvedTargets: resolvedTargets,
            domainName: domainName,
            forestName: forestName,
            includeDomains: includeDomains,
            excludeDomains: excludeDomains,
            includeTrusts: includeTrusts,
            domainController: domainController,
            timeout: timeout,
            retries: retries,
            retryDelay: retryDelay,
            maxConcurrency: maxConcurrency);
        var runner = ProbeRunnerCatalog.Create(definition.Type);
        return runner.ExecuteAsync(definition, cancellationToken);
    }

    internal static ProbeDefinition BuildEnterpriseProbeDefinition(
        string normalizedKind,
        JsonObject? arguments,
        string name,
        IReadOnlyList<string> resolvedTargets,
        string? domainName,
        string? forestName,
        IReadOnlyList<string> includeDomains,
        IReadOnlyList<string> excludeDomains,
        bool includeTrusts,
        string? domainController,
        TimeSpan timeout,
        int retries,
        TimeSpan retryDelay,
        int maxConcurrency) {
        ProbeDefinition definition = normalizedKind switch {
            AdfsProbeKind => new AdfsProbeDefinition {
                Targets = CopyTargets(resolvedTargets),
                MonitorWebApplicationProxy = ToolArgs.GetBoolean(arguments, "adfs_monitor_web_application_proxy", defaultValue: false),
                FederationServiceHost = ToolArgs.GetOptionalTrimmed(arguments, "adfs_federation_service_host"),
                CheckFederationMetadata = ToolArgs.GetBoolean(arguments, "adfs_check_federation_metadata", defaultValue: true),
                CheckTlsCertificate = ToolArgs.GetBoolean(arguments, "verify_certificate", defaultValue: true),
                HttpsPort = ToolArgs.GetPositiveCappedInt32OrDefault(arguments, "port", defaultValue: 443, maxInclusive: 65535),
                QueryTimeout = timeout,
                MaxConcurrency = maxConcurrency
            },
            EntraConnectProbeKind => new EntraConnectProbeDefinition {
                Targets = CopyTargets(resolvedTargets),
                QueryTimeout = timeout,
                MaxConcurrency = maxConcurrency
            },
            SqlServerProbeKind => new SqlServerProbeDefinition {
                Targets = CopyTargets(resolvedTargets),
                DiscoverFromActiveDirectory = ToolArgs.GetBoolean(
                    arguments,
                    "sql_discover_from_active_directory",
                    defaultValue: ShouldDiscoverSqlServers(
                        resolvedTargets,
                        domainName,
                        forestName,
                        includeDomains,
                        includeTrusts,
                        domainController)),
                DomainName = domainName,
                ForestName = forestName,
                IncludeDomains = CopyTargets(includeDomains),
                ExcludeDomains = CopyTargets(excludeDomains),
                IncludeTrusts = includeTrusts,
                DomainController = domainController,
                DiscoveryBindIdentity = ToolArgs.GetOptionalTrimmed(arguments, "bind_identity"),
                DiscoveryBindSecret = ToolArgs.GetOptionalTrimmed(arguments, "bind_secret"),
                Database = ToolArgs.GetOptionalTrimmed(arguments, "sql_database") ?? "master",
                IntegratedSecurity = ToolArgs.GetBoolean(arguments, "sql_integrated_security", defaultValue: true),
                Username = ToolArgs.GetOptionalTrimmed(arguments, "sql_username"),
                PasswordSecret = ToolArgs.GetOptionalTrimmed(arguments, "sql_password_secret"),
                Port = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("port"), maxInclusive: 65535),
                TrustServerCertificate = ToolArgs.GetBoolean(arguments, "sql_trust_server_certificate", defaultValue: false),
                CheckServices = ToolArgs.GetBoolean(arguments, "sql_check_services", defaultValue: true),
                IncludeSystemDatabases = ToolArgs.GetBoolean(arguments, "sql_include_system_databases", defaultValue: false),
                IncludeAgentJobs = ToolArgs.GetBoolean(arguments, "sql_include_agent_jobs", defaultValue: true),
                IncludeWaitStatistics = ToolArgs.GetBoolean(arguments, "sql_include_wait_statistics", defaultValue: false),
                IncludeAvailabilityGroups = ToolArgs.GetBoolean(arguments, "sql_include_availability_groups", defaultValue: false),
                MaxConcurrency = maxConcurrency
            },
            WindowsBackupProbeKind => new WindowsBackupProbeDefinition {
                Targets = CopyTargets(resolvedTargets),
                QueryTimeout = timeout,
                MaxConcurrency = maxConcurrency
            },
            _ => throw new ArgumentOutOfRangeException(nameof(normalizedKind), normalizedKind, "Unsupported enterprise probe kind.")
        };

        definition.Name = name;
        definition.Timeout = timeout;
        definition.PerTargetTimeout = timeout;
        definition.Retries = retries;
        definition.RetryDelay = retryDelay;
        return definition;
    }

    private static bool ShouldDiscoverSqlServers(
        IReadOnlyList<string> resolvedTargets,
        string? domainName,
        string? forestName,
        IReadOnlyList<string> includeDomains,
        bool includeTrusts,
        string? domainController) {
        return resolvedTargets.Count == 0
               && (!string.IsNullOrWhiteSpace(domainName)
                   || !string.IsNullOrWhiteSpace(forestName)
                   || includeDomains.Count > 0
                   || includeTrusts
                   || !string.IsNullOrWhiteSpace(domainController));
    }

    private static string[] CopyTargets(IReadOnlyList<string> values) {
        if (values.Count == 0) {
            return Array.Empty<string>();
        }

        var result = new string[values.Count];
        for (var i = 0; i < values.Count; i++) {
            result[i] = values[i];
        }

        return result;
    }
}
