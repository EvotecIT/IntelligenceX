using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared control tags that let tool-owned definitions declare explicit selection metadata without chat-side hardcoding.
/// </summary>
public static class ToolSelectionHintTags {
    /// <summary>Control-tag prefix for explicit scope hints.</summary>
    public const string ScopeTagPrefix = "selection_scope:";
    /// <summary>Control-tag prefix for explicit operation hints.</summary>
    public const string OperationTagPrefix = "selection_operation:";
    /// <summary>Control-tag prefix for explicit entity hints.</summary>
    public const string EntityTagPrefix = "selection_entity:";
    /// <summary>Control-tag prefix for explicit risk hints.</summary>
    public const string RiskTagPrefix = "selection_risk:";

    /// <summary>
    /// Applies tool-owned explicit routing hints to a definition while preserving existing tags and removing prior control-tag values.
    /// </summary>
    public static ToolDefinition ApplyExplicitRoutingHints(
        ToolDefinition definition,
        string? scope = null,
        string? operation = null,
        string? entity = null,
        string? risk = null,
        IReadOnlyList<string>? additionalTags = null) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var tags = new List<string>(definition.Tags.Count + (additionalTags?.Count ?? 0) + 4);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTag(string? value) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                return;
            }

            tags.Add(normalized);
        }

        for (var i = 0; i < definition.Tags.Count; i++) {
            var tag = definition.Tags[i];
            if (IsControlTag(tag)) {
                continue;
            }

            AddTag(tag);
        }

        if (additionalTags is { Count: > 0 }) {
            for (var i = 0; i < additionalTags.Count; i++) {
                AddTag(additionalTags[i]);
            }
        }

        AddControlTag(tags, seen, ScopeTagPrefix, scope);
        AddControlTag(tags, seen, OperationTagPrefix, operation);
        AddControlTag(tags, seen, EntityTagPrefix, entity);
        AddControlTag(tags, seen, RiskTagPrefix, risk);
        var routing = BuildUpdatedRoutingContract(definition.Routing, scope, operation, entity, risk);

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

    /// <summary>
    /// Returns true when the tag is an internal tool-owned selection control tag.
    /// </summary>
    public static bool IsControlTag(string? tag) {
        var normalized = (tag ?? string.Empty).Trim();
        return normalized.StartsWith(ScopeTagPrefix, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(OperationTagPrefix, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(EntityTagPrefix, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(RiskTagPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads an explicit scope hint from control tags when present.
    /// </summary>
    public static bool TryGetScope(IReadOnlyList<string>? tags, out string scope) {
        return TryGetValue(tags, ScopeTagPrefix, out scope);
    }

    /// <summary>
    /// Reads an explicit operation hint from control tags when present.
    /// </summary>
    public static bool TryGetOperation(IReadOnlyList<string>? tags, out string operation) {
        return TryGetValue(tags, OperationTagPrefix, out operation);
    }

    /// <summary>
    /// Reads an explicit entity hint from control tags when present.
    /// </summary>
    public static bool TryGetEntity(IReadOnlyList<string>? tags, out string entity) {
        return TryGetValue(tags, EntityTagPrefix, out entity);
    }

    /// <summary>
    /// Reads an explicit risk hint from control tags when present.
    /// </summary>
    public static bool TryGetRisk(IReadOnlyList<string>? tags, out string risk) {
        return TryGetValue(tags, RiskTagPrefix, out risk);
    }

    private static void AddControlTag(List<string> tags, HashSet<string> seen, string prefix, string? value) {
        var normalizedValue = (value ?? string.Empty).Trim();
        if (normalizedValue.Length == 0) {
            return;
        }

        var tag = prefix + normalizedValue;
        if (seen.Add(tag)) {
            tags.Add(tag);
        }
    }

    private static bool TryGetValue(IReadOnlyList<string>? tags, string prefix, out string value) {
        value = string.Empty;
        if (tags is null || tags.Count == 0) {
            return false;
        }

        for (var i = 0; i < tags.Count; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var candidate = tag.Length > prefix.Length ? tag.Substring(prefix.Length).Trim() : string.Empty;
            if (candidate.Length == 0) {
                continue;
            }

            value = candidate;
            return true;
        }

        return false;
    }

    private static ToolRoutingContract BuildUpdatedRoutingContract(
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
