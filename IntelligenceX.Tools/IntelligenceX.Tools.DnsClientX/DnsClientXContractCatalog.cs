using System;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.DnsClientX;

internal static class DnsClientXContractCatalog {
    private const string PackInfoToolName = "dnsclientx_pack_info";
    private static readonly string[] SetupHintKeys = { "target", "targets", "name", "type", "endpoint", "timeout_ms" };
    private static readonly string[] RequirementHintKeys = { "endpoint", "timeout_ms" };
    private static readonly string[] QueryRetryableErrorCodes = { "timeout", "query_failed", "transport_unavailable" };

    public static ToolSetupContract CreateResolverConnectivitySetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "dnsclientx_ping",
            requirementId: "dns_resolver_connectivity",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: SetupHintKeys,
            requirementHintKeys: RequirementHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateResolverConnectivitySetup();
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(toolName, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                DomainInvestigationFollowUpCatalog.CreateDnsQueryToDomainSummaryRoutes(
                    domainSourceField: "query/name",
                    dnsEndpointSourceField: "query/endpoint"));
        }

        if (string.Equals(toolName, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                DomainInvestigationFollowUpCatalog.CreateDnsPingToNetworkProbeRoutes(
                    hostSourceField: "probed_targets/0",
                    timeoutSourceField: "timeout_ms"));
        }

        return null;
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(toolName, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 2,
                retryableErrorCodes: QueryRetryableErrorCodes);
        }

        if (string.Equals(toolName, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateNoRetryRecovery();
        }

        return null;
    }
}
