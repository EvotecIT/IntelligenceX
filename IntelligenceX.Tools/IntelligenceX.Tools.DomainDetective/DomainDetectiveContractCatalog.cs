using System;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.DomainDetective;

internal static class DomainDetectiveContractCatalog {
    private const string PackInfoToolName = "domaindetective_pack_info";
    private static readonly string[] SetupHintKeys = { "domain", "checks", "dns_endpoint", "host", "timeout_ms" };
    private static readonly string[] RequirementHintKeys = { "domain", "host", "dns_endpoint", "timeout_ms" };
    private static readonly string[] DomainSummaryRetryableErrorCodes = { "timeout", "query_failed", "transport_unavailable" };
    private static readonly string[] NetworkProbeRetryableErrorCodes = { "probe_failed", "timeout", "transport_unavailable" };
    private static readonly string[] ChecksCatalogRecoveryTools = { "domaindetective_checks_catalog" };

    public static ToolSetupContract CreatePublicDnsConnectivitySetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "domaindetective_checks_catalog",
            requirementId: "public_dns_connectivity",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: SetupHintKeys,
            requirementHintKeys: RequirementHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreatePublicDnsConnectivitySetup();
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(toolName, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                DomainInvestigationFollowUpCatalog.CreateDomainSummaryToActiveDirectoryRoutes(
                    domainSourceField: "domain"));
        }

        if (string.Equals(toolName, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                DomainInvestigationFollowUpCatalog.CreateNetworkProbeToActiveDirectoryRoutes(
                    hostSourceField: "host"));
        }

        return null;
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(toolName, "domaindetective_checks_catalog", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateNoRetryRecovery();
        }

        if (string.Equals(toolName, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 2,
                retryableErrorCodes: DomainSummaryRetryableErrorCodes,
                recoveryToolNames: ChecksCatalogRecoveryTools);
        }

        if (string.Equals(toolName, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 1,
                retryableErrorCodes: NetworkProbeRetryableErrorCodes,
                recoveryToolNames: ChecksCatalogRecoveryTools);
        }

        return null;
    }
}
