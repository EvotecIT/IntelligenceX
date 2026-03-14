using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryPackContractCatalog {
    public static ToolDefinition Apply(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        var routing = CreateRouting(definition);
        var execution = CreateExecution(definition, routing);
        var setup = CreateSetup(definition);
        var handoff = CreateHandoff(definition);
        var recovery = CreateRecovery(definition);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            execution: execution,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
        return ActiveDirectoryRoutingCatalog.ApplySelectionMetadata(updatedDefinition);
    }

    private static ToolExecutionContract? CreateExecution(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.ResolveExecutionContractFromTraits(definition, routing);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        var fallbackSelectionKeys = ActiveDirectoryRoutingCatalog.ResolveFallbackSelectionKeys(
            definition.Name,
            existing?.FallbackSelectionKeys);
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "active_directory",
            role: ActiveDirectoryRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
            defaultSignalTokens: ActiveDirectoryRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: ActiveDirectoryRoutingCatalog.RequiresSelectionForFallback(
                existing?.RequiresSelectionForFallback == true,
                fallbackSelectionKeys),
            fallbackSelectionKeys: fallbackSelectionKeys,
            fallbackHintKeys: ActiveDirectoryRoutingCatalog.ResolveFallbackHintKeys(
                definition.Name,
                existing?.FallbackHintKeys));
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => ActiveDirectoryContractCatalog.CreateSetup(current.Name));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => ActiveDirectoryContractCatalog.CreateHandoff(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => ActiveDirectoryContractCatalog.CreateRecovery(current.Name));
    }
}
