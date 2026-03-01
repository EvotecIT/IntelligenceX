using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

internal static class OfficeImoToolContracts {
    private static readonly string[] SetupHintKeys = {
        "path",
        "recurse",
        "extensions",
        "max_files",
        "max_total_bytes",
        "max_input_bytes"
    };

    private static readonly string[] OfficeImoSignalTokens = {
        "officeimo",
        "document",
        "word",
        "excel",
        "powerpoint",
        "pdf",
        "markdown",
        "file"
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
            PackId = "officeimo",
            Role = ResolveRole(definition.Name),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : OfficeImoSignalTokens,
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
            SetupToolName = "officeimo_pack_info",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "officeimo_path_access",
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

        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 1,
            RetryableErrorCodes = new[] { "io_error", "access_denied", "timeout", "parse_failed" }
        };
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "officeimo_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
