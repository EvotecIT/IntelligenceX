using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXPackContractCatalog {
    public static ToolDefinition Apply(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        var routing = CreateRouting(definition);
        var setup = CreateSetup(definition, routing);
        var handoff = CreateHandoff(definition, routing);
        var recovery = CreateRecovery(definition, routing);
        return ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
    }

    private static ToolRoutingContract CreateRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        var fallbackSelectionKeys = TestimoXRoutingCatalog.ResolveFallbackSelectionKeys(definition.Name, existing?.FallbackSelectionKeys);
        return ToolContractDefaults.CreateExplicitRoutingContract(
            existing: existing,
            packId: "testimox",
            role: TestimoXRoutingCatalog.ResolveRole(definition.Name, existing?.Role),
            domainIntentFamily: TestimoXRoutingCatalog.ResolveDomainIntentFamily(definition.Name, existing?.DomainIntentFamily),
            domainIntentActionId: TestimoXRoutingCatalog.ResolveDomainIntentActionId(definition.Name, existing?.DomainIntentActionId),
            defaultSignalTokens: TestimoXRoutingCatalog.SignalTokens,
            requiresSelectionForFallback: TestimoXRoutingCatalog.RequiresSelectionForFallback(existing?.RequiresSelectionForFallback == true, fallbackSelectionKeys),
            fallbackSelectionKeys: fallbackSelectionKeys,
            fallbackHintKeys: TestimoXRoutingCatalog.ResolveFallbackHintKeys(definition.Name, existing?.FallbackHintKeys));
    }

    private static ToolSetupContract? CreateSetup(ToolDefinition definition, ToolRoutingContract routing) {
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Setup;
        }

        return ToolContractDefaults.ResolveSetupContract(
            definition,
            current => string.Equals(current.Name, "testimox_runs_list", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(current.Name, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)
                ? TestimoXContractCatalog.CreateHintOnlySetup(
                    routing.FallbackHintKeys.Count > 0
                        ? routing.FallbackHintKeys
                        : TestimoXRoutingCatalog.SetupHintKeys)
                : TestimoXContractCatalog.CreateRulesCatalogSetup());
    }

    private static ToolRecoveryContract? CreateRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        return ToolContractDefaults.ResolveRecoveryContract(
            definition,
            static current => TestimoXContractCatalog.CreateRecovery(current.Name));
    }

    private static ToolHandoffContract? CreateHandoff(ToolDefinition definition, ToolRoutingContract routing) {
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Handoff;
        }

        return ToolContractDefaults.ResolveHandoffContract(
            definition,
            static current => TestimoXContractCatalog.CreateHandoff(current.Name));
    }
}
