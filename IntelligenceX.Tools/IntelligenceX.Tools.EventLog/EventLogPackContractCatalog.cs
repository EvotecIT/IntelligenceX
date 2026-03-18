using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogPackContractCatalog {
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
        return EventLogRoutingCatalog.ApplySelectionMetadata(updatedDefinition);
    }

    private static ToolExecutionContract? CreateExecution(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.ResolveExecutionContractFromTraits(definition, routing);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "eventlog",
            role: EventLogRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: string.IsNullOrWhiteSpace(existing?.DomainIntentFamily)
                ? ToolSelectionMetadata.DomainIntentFamilyAd
                : existing!.DomainIntentFamily,
            domainIntentActionId: string.IsNullOrWhiteSpace(existing?.DomainIntentActionId)
                ? ToolSelectionMetadata.DomainIntentActionIdAd
                : existing!.DomainIntentActionId,
            defaultSignalTokens: EventLogRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: existing?.RequiresSelectionForFallback ?? false,
            fallbackSelectionKeys: existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            fallbackHintKeys: existing?.FallbackHintKeys ?? Array.Empty<string>());
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => EventLogContractCatalog.CreateSetup(current.Name));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => EventLogContractCatalog.CreateHandoff(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => EventLogContractCatalog.CreateRecovery(current.Name));
    }
}
