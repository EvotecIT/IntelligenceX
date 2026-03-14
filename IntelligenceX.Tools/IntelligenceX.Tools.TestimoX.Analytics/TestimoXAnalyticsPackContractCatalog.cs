using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsPackContractCatalog {
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
        var fallbackSelectionKeys = TestimoXAnalyticsRoutingCatalog.ResolveFallbackSelectionKeys(definition.Name, existing?.FallbackSelectionKeys);
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "testimox_analytics",
            role: TestimoXAnalyticsRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: TestimoXAnalyticsRoutingCatalog.ResolveDomainIntentFamily(existing?.DomainIntentFamily),
            domainIntentActionId: TestimoXAnalyticsRoutingCatalog.ResolveDomainIntentActionId(existing?.DomainIntentActionId),
            defaultSignalTokens: TestimoXAnalyticsRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: TestimoXAnalyticsRoutingCatalog.RequiresSelectionForFallback(existing?.RequiresSelectionForFallback == true, fallbackSelectionKeys),
            fallbackSelectionKeys: fallbackSelectionKeys,
            fallbackHintKeys: TestimoXAnalyticsRoutingCatalog.ResolveFallbackHintKeys(definition.Name, existing?.FallbackHintKeys));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition) {
        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => TestimoXAnalyticsContractCatalog.CreateHandoff(current.Name));
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition) {
        return ToolContractDefaults.ResolveSetupContract(
            definition,
            static current => TestimoXAnalyticsContractCatalog.CreateSetup(current.Name));
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition) {
        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => TestimoXAnalyticsContractCatalog.CreateRecovery(current.Name));
    }
}
