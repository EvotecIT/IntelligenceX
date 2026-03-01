using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXToolContracts {
    private static readonly string[] SetupHintKeys = {
        "search_text",
        "categories",
        "tags",
        "source_types",
        "rule_origin"
    };

    private static readonly string[] TestimoXSignalTokens = {
        "testimox",
        "testimo",
        "baseline",
        "security",
        "posture"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            recovery: recovery);
        return ToolDefinitionOverlay.WithDefinition(tool, updatedDefinition);
    }

    private static ToolRoutingContract BuildRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = string.IsNullOrWhiteSpace(existing?.RoutingContractId)
                ? ToolRoutingContract.DefaultContractId
                : existing!.RoutingContractId,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = "testimox",
            Role = ResolveRole(definition.Name),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : TestimoXSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Setup;
        }

        if (definition.Setup is { IsSetupAware: true }) {
            return definition.Setup;
        }

        return new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = "testimox_rules_list",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "testimox_rules_catalog",
                    Kind = ToolSetupRequirementKinds.Capability,
                    IsRequired = true,
                    HintKeys = SetupHintKeys
                }
            },
            SetupHintKeys = SetupHintKeys
        };
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        var supportsRetry = string.Equals(definition.Name, "testimox_rules_run", StringComparison.OrdinalIgnoreCase);
        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = supportsRetry,
            MaxRetryAttempts = supportsRetry ? 1 : 0,
            RetryableErrorCodes = supportsRetry
                ? new[] { "execution_failed", "timeout", "transport_unavailable" }
                : Array.Empty<string>()
        };
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "testimox_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (string.Equals(toolName, "testimox_rules_list", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
