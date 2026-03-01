using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

internal static class EmailToolContracts {
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
            Role = ResolveRole(definition.Name),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : EmailSignalTokens,
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
            SetupToolName = "email_pack_info",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "email_account_authentication",
                    Kind = ToolSetupRequirementKinds.Authentication,
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

        if (definition.WriteGovernance?.IsWriteCapable == true) {
            return new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = false,
                MaxRetryAttempts = 0
            };
        }

        var supportsRetry = string.Equals(definition.Name, "email_imap_search", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(definition.Name, "email_imap_get", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(definition.Name, "email_smtp_probe", StringComparison.OrdinalIgnoreCase);

        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = supportsRetry,
            MaxRetryAttempts = supportsRetry ? 1 : 0,
            RetryableErrorCodes = supportsRetry ? new[] { "timeout", "query_failed", "connection_failed" } : Array.Empty<string>()
        };
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "email_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (string.Equals(toolName, "email_smtp_probe", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (string.Equals(toolName, "email_imap_search", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleResolver;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
