using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

internal static class ReviewerSetupPackContractCatalog {
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
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "reviewer_setup",
            role: ReviewerSetupRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: existing?.DomainIntentFamily,
            domainIntentActionId: existing?.DomainIntentActionId,
            defaultSignalTokens: ReviewerSetupRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: existing?.RequiresSelectionForFallback ?? false,
            fallbackSelectionKeys: existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            fallbackHintKeys: existing?.FallbackHintKeys ?? Array.Empty<string>());
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => ReviewerSetupContractCatalog.CreateSetup(current.Name));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => ReviewerSetupContractCatalog.CreateHandoff(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => ReviewerSetupContractCatalog.CreateRecovery(current.Name));
    }
}
