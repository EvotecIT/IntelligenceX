using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryLifecyclePackContractCatalog {
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
        return ActiveDirectoryLifecycleRoutingCatalog.ApplySelectionMetadata(updatedDefinition);
    }

    private static ToolExecutionContract? CreateExecution(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.ResolveExecutionContractFromTraits(definition, routing);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "active_directory",
            role: ActiveDirectoryLifecycleRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
            domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
            defaultSignalTokens: ActiveDirectoryLifecycleRoutingCatalog.SignalTokens);
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => ActiveDirectoryLifecycleContractCatalog.CreateSetup(current.Name));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => ActiveDirectoryLifecycleContractCatalog.CreateHandoff(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => ActiveDirectoryLifecycleContractCatalog.CreateRecovery(
                current.Name,
                current.WriteGovernance?.IsWriteCapable == true));
    }
}
