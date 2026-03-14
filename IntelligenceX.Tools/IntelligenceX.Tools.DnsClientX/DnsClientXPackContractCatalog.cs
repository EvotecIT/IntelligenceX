using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

internal static class DnsClientXPackContractCatalog {
    public static ToolDefinition Apply(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        var routing = CreateRouting(definition);
        var setup = CreateSetup(definition);
        var handoff = CreateHandoff(definition);
        var recovery = CreateRecovery(definition);
        return ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        var fallbackSelectionKeys = DnsClientXRoutingCatalog.ResolveFallbackSelectionKeys(definition.Name, existing?.FallbackSelectionKeys);
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "dnsclientx",
            role: DnsClientXRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyPublic,
            domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdPublic,
            defaultSignalTokens: DnsClientXRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: DnsClientXRoutingCatalog.RequiresSelectionForFallback(existing?.RequiresSelectionForFallback == true, fallbackSelectionKeys),
            fallbackSelectionKeys: fallbackSelectionKeys,
            fallbackHintKeys: DnsClientXRoutingCatalog.ResolveFallbackHintKeys(definition.Name, existing?.FallbackHintKeys));
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => DnsClientXContractCatalog.CreateSetup(current.Name));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => DnsClientXContractCatalog.CreateHandoff(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => DnsClientXContractCatalog.CreateRecovery(current.Name));
    }
}
