using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helpers for creating stable pack-owned tool definitions.
/// </summary>
public static class ToolPackDefinitionFactory {
    /// <summary>
    /// Creates a pack-info tool definition with explicit routing metadata.
    /// </summary>
    public static ToolDefinition CreatePackInfoDefinition(
        string toolName,
        string description,
        string packId,
        string? category = null,
        IReadOnlyList<string>? tags = null,
        string? displayName = null,
        string? domainIntentFamily = null,
        string? domainIntentActionId = null,
        IReadOnlyList<string>? domainSignalTokens = null) {
        return CreateStructuredDefinition(
            toolName: toolName,
            description: description,
            parameters: ToolSchema.Object().NoAdditionalProperties(),
            packId: packId,
            role: ToolRoutingTaxonomy.RolePackInfo,
            category: category,
            tags: tags,
            displayName: displayName,
            domainIntentFamily: domainIntentFamily,
            domainIntentActionId: domainIntentActionId,
            domainSignalTokens: domainSignalTokens);
    }

    /// <summary>
    /// Creates an environment-discovery tool definition with explicit routing metadata.
    /// </summary>
    public static ToolDefinition CreateEnvironmentDiscoverDefinition(
        string toolName,
        string description,
        JsonObject parameters,
        string packId,
        string? category = null,
        IReadOnlyList<string>? tags = null,
        string? displayName = null,
        string? domainIntentFamily = null,
        string? domainIntentActionId = null,
        IReadOnlyList<string>? domainSignalTokens = null) {
        return CreateStructuredDefinition(
            toolName: toolName,
            description: description,
            parameters: parameters,
            packId: packId,
            role: ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            category: category,
            tags: tags,
            displayName: displayName,
            domainIntentFamily: domainIntentFamily,
            domainIntentActionId: domainIntentActionId,
            domainSignalTokens: domainSignalTokens);
    }

    private static ToolDefinition CreateStructuredDefinition(
        string toolName,
        string description,
        JsonObject parameters,
        string packId,
        string role,
        string? category = null,
        IReadOnlyList<string>? tags = null,
        string? displayName = null,
        string? domainIntentFamily = null,
        string? domainIntentActionId = null,
        IReadOnlyList<string>? domainSignalTokens = null) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            throw new ArgumentException("Tool name cannot be empty.", nameof(toolName));
        }
        if (parameters is null) {
            throw new ArgumentNullException(nameof(parameters));
        }

        var normalizedPackId = ToolSelectionMetadata.NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            throw new ArgumentException("Pack id cannot be empty.", nameof(packId));
        }

        var normalizedFamily = (domainIntentFamily ?? string.Empty).Trim();
        var normalizedActionId = (domainIntentActionId ?? string.Empty).Trim();
        if (normalizedActionId.Length > 0 && normalizedFamily.Length == 0) {
            throw new ArgumentException("Domain intent family is required when an action id is provided.", nameof(domainIntentFamily));
        }

        return new ToolDefinition(
            name: toolName,
            description: description,
            parameters: parameters,
            displayName: displayName,
            category: category,
            tags: tags,
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = normalizedPackId,
                Role = role,
                DomainIntentFamily = normalizedFamily,
                DomainIntentActionId = normalizedActionId,
                DomainSignalTokens = domainSignalTokens ?? Array.Empty<string>()
            });
    }
}
