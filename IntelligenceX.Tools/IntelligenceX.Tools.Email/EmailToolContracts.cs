using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

internal static class EmailToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["email_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["email_imap_search"] = ToolRoutingTaxonomy.RoleResolver,
            ["email_imap_get"] = ToolRoutingTaxonomy.RoleOperational,
            ["email_smtp_probe"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["email_smtp_send"] = ToolRoutingTaxonomy.RoleOperational
        };

    private static readonly string[] SetupHintKeys = {
        "folder",
        "query",
        "from",
        "to",
        "subject",
        "auth_probe_id"
    };

    private static readonly string[] EmailSignalTokens = {
        "email",
        "imap",
        "smtp",
        "mailbox",
        "message"
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
            PackId = "email",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : EmailSignalTokens,
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
                setupToolName: "email_pack_info",
                requirementId: "email_account_authentication",
                requirementKind: ToolSetupRequirementKinds.Authentication,
                setupHintKeys: SetupHintKeys));
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => {
                if (definition.WriteGovernance?.IsWriteCapable == true) {
                    return ToolContractDefaults.CreateNoRetryRecovery();
                }

                var supportsRetry = string.Equals(definition.Name, "email_imap_search", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(definition.Name, "email_imap_get", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(definition.Name, "email_smtp_probe", StringComparison.OrdinalIgnoreCase);

                return ToolContractDefaults.CreateRecovery(
                    supportsTransientRetry: supportsRetry,
                    maxRetryAttempts: supportsRetry ? 1 : 0,
                    retryableErrorCodes: supportsRetry ? new[] { "timeout", "query_failed", "connection_failed" } : Array.Empty<string>());
            });
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "Email");
    }
}
