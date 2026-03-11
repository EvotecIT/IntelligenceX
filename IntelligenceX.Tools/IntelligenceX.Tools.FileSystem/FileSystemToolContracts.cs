using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

internal static class FileSystemToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["fs_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["fs_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["fs_read"] = ToolRoutingTaxonomy.RoleOperational,
            ["fs_search"] = ToolRoutingTaxonomy.RoleResolver
        };

    private static readonly string[] SetupHintKeys = {
        "path",
        "folder",
        "recurse",
        "pattern"
    };

    private static readonly string[] FileSystemSignalTokens = {
        "filesystem",
        "file",
        "folder",
        "path",
        "content"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            handoff: handoff,
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
            PackId = "filesystem",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : FileSystemSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateRequiredSetup(
                setupToolName: "fs_list",
                requirementId: "filesystem_path_access",
                requirementKind: ToolSetupRequirementKinds.Capability,
                setupHintKeys: SetupHintKeys));
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "fs_list", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "filesystem",
                    targetToolName: "fs_read",
                    reason: "Promote listed file entries into direct local content inspection.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("entries[].path", "path", isRequired: false)
                    })
            });
        }

        if (string.Equals(definition.Name, "fs_search", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "filesystem",
                    targetToolName: "fs_read",
                    reason: "Promote content search matches into direct local file inspection.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("matches[].path", "path")
                    })
            });
        }

        return definition.Handoff;
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 1,
                retryableErrorCodes: new[] { "io_error", "access_denied", "timeout", "query_failed" }));
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "FileSystem");
    }
}
