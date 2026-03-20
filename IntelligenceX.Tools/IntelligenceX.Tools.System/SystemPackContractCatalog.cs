using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

internal static class SystemPackContractCatalog {
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
        return SystemRoutingCatalog.ApplySelectionMetadata(updatedDefinition);
    }

    private static ToolExecutionContract? CreateExecution(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.ResolveExecutionContractFromTraits(definition, routing);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "system",
            role: SystemRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: existing?.DomainIntentFamily,
            domainIntentActionId: existing?.DomainIntentActionId,
            defaultSignalTokens: SystemRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: existing?.RequiresSelectionForFallback ?? false,
            fallbackSelectionKeys: existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            fallbackHintKeys: existing?.FallbackHintKeys ?? Array.Empty<string>());
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => SystemContractCatalog.CreateSetup(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => SystemContractCatalog.CreateRecovery(
                current.Name,
                current.Parameters,
                isWriteCapable: current.WriteGovernance?.IsWriteCapable == true));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => SystemContractCatalog.CreateHandoff(current.Name, current.Parameters));
    }
}
