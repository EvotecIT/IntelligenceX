using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared helper for applying explicit tool-owned selection metadata without chat-side hardcoding.
/// </summary>
public static class ToolExplicitSelectionMetadata {
    /// <summary>
    /// Applies explicit routing taxonomy and additional tags to a tool definition.
    /// </summary>
    public static ToolDefinition Apply(
        ToolDefinition definition,
        string? scope = null,
        string? operation = null,
        string? entity = null,
        string? risk = null,
        IReadOnlyList<string>? additionalTags = null) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var tags = MergeAdditionalTags(definition.Tags, additionalTags ?? Array.Empty<string>());
        var routing = ApplyRouting(definition.Routing, scope, operation, entity, risk);

        return new ToolDefinition(
            name: definition.Name,
            description: definition.Description,
            parameters: definition.Parameters,
            displayName: definition.DisplayName,
            category: definition.Category,
            tags: tags,
            writeGovernance: definition.WriteGovernance,
            aliases: definition.Aliases,
            aliasOf: definition.AliasOf,
            authentication: definition.Authentication,
            routing: routing,
            setup: definition.Setup,
            handoff: definition.Handoff,
            recovery: definition.Recovery,
            execution: definition.Execution);
    }

    private static IReadOnlyList<string> MergeAdditionalTags(
        IReadOnlyList<string> existingTags,
        IReadOnlyList<string> additionalTags) {
        if (existingTags.Count == 0 && additionalTags.Count == 0) {
            return Array.Empty<string>();
        }

        var tags = new List<string>(existingTags.Count + additionalTags.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddTag(List<string> buffer, HashSet<string> seenTags, string? value) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seenTags.Add(normalized)) {
                return;
            }

            buffer.Add(normalized);
        }

        for (var i = 0; i < existingTags.Count; i++) {
            AddTag(tags, seen, existingTags[i]);
        }

        for (var i = 0; i < additionalTags.Count; i++) {
            AddTag(tags, seen, additionalTags[i]);
        }

        return tags.Count == 0 ? Array.Empty<string>() : tags;
    }

    private static ToolRoutingContract ApplyRouting(
        ToolRoutingContract? existing,
        string? scope,
        string? operation,
        string? entity,
        string? risk) {
        var normalizedScope = (scope ?? string.Empty).Trim();
        var normalizedOperation = (operation ?? string.Empty).Trim();
        var normalizedEntity = (entity ?? string.Empty).Trim();
        var normalizedRisk = (risk ?? string.Empty).Trim();

        var routing = existing is null
            ? new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit
            }
            : new ToolRoutingContract {
                IsRoutingAware = existing.IsRoutingAware,
                RoutingContractId = existing.RoutingContractId,
                RoutingSource = existing.RoutingSource,
                PackId = existing.PackId,
                Role = existing.Role,
                Scope = existing.Scope,
                Operation = existing.Operation,
                Entity = existing.Entity,
                Risk = existing.Risk,
                DomainIntentFamily = existing.DomainIntentFamily,
                DomainIntentActionId = existing.DomainIntentActionId,
                DomainIntentFamilyDisplayName = existing.DomainIntentFamilyDisplayName,
                DomainIntentFamilyReplyExample = existing.DomainIntentFamilyReplyExample,
                DomainIntentFamilyChoiceDescription = existing.DomainIntentFamilyChoiceDescription,
                DomainSignalTokens = existing.DomainSignalTokens,
                RequiresSelectionForFallback = existing.RequiresSelectionForFallback,
                FallbackSelectionKeys = existing.FallbackSelectionKeys,
                FallbackHintKeys = existing.FallbackHintKeys
            };

        if (string.IsNullOrWhiteSpace(routing.RoutingContractId)) {
            routing.RoutingContractId = ToolRoutingContract.DefaultContractId;
        }

        if (string.IsNullOrWhiteSpace(routing.RoutingSource)) {
            routing.RoutingSource = ToolRoutingTaxonomy.SourceExplicit;
        }

        if (normalizedScope.Length > 0) {
            routing.Scope = normalizedScope;
        }

        if (normalizedOperation.Length > 0) {
            routing.Operation = normalizedOperation;
        }

        if (normalizedEntity.Length > 0) {
            routing.Entity = normalizedEntity;
        }

        if (normalizedRisk.Length > 0) {
            routing.Risk = normalizedRisk;
        }

        return routing;
    }
}
