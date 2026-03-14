using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

internal static class PowerShellPackContractCatalog {
    public static ToolDefinition Apply(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        var routing = CreateRouting(definition);
        var setup = CreateSetup(definition);
        var recovery = CreateRecovery(definition);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            recovery: recovery);
        return PowerShellRoutingCatalog.ApplySelectionMetadata(updatedDefinition);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "powershell",
            role: PowerShellRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: existing?.DomainIntentFamily,
            domainIntentActionId: existing?.DomainIntentActionId,
            defaultSignalTokens: PowerShellRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: existing?.RequiresSelectionForFallback ?? false,
            fallbackSelectionKeys: existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            fallbackHintKeys: existing?.FallbackHintKeys ?? Array.Empty<string>());
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => PowerShellContractCatalog.CreateSetup(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => PowerShellContractCatalog.CreateRecovery(
                toolName: current.Name,
                isWriteCapable: current.WriteGovernance?.IsWriteCapable == true));
    }
}
