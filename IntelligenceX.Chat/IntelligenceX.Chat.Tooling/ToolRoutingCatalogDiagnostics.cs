using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Shared;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Per-family routing action summary.
/// </summary>
public sealed record ToolRoutingFamilyActionSummary {
    /// <summary>
    /// Normalized domain intent family token.
    /// </summary>
    public required string Family { get; init; }

    /// <summary>
    /// Action id declared for the family.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Number of tools mapped to this family/action pair.
    /// </summary>
    public required int ToolCount { get; init; }

    /// <summary>
    /// Human-friendly family label inferred from the registered capability surface.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Short natural-language reply example for clarification prompts.
    /// </summary>
    public string? ReplyExample { get; init; }

    /// <summary>
    /// User-facing clarification description for this family.
    /// </summary>
    public string? ChoiceDescription { get; init; }

    /// <summary>
    /// Representative pack ids contributing tools to this family/action pair.
    /// </summary>
    public string[] RepresentativePackIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Routing catalog diagnostics snapshot.
/// </summary>
public sealed record ToolRoutingCatalogDiagnostics {
    /// <summary>
    /// Total tool definitions observed.
    /// </summary>
    public required int TotalTools { get; init; }

    /// <summary>
    /// Tools that explicitly participate in routing metadata.
    /// </summary>
    public required int RoutingAwareTools { get; init; }

    /// <summary>
    /// Routing-aware tools that declare explicit routing source.
    /// </summary>
    public int ExplicitRoutingTools { get; init; }

    /// <summary>
    /// Routing-aware tools that still rely on inferred routing source.
    /// </summary>
    public int InferredRoutingTools { get; init; }

    /// <summary>
    /// Tools without routing contracts.
    /// </summary>
    public required int MissingRoutingContractTools { get; init; }

    /// <summary>
    /// Routing-aware tools that miss explicit pack id.
    /// </summary>
    public int MissingPackIdTools { get; init; }

    /// <summary>
    /// Routing-aware tools that miss explicit role.
    /// </summary>
    public int MissingRoleTools { get; init; }

    /// <summary>
    /// Tools with setup-aware contracts.
    /// </summary>
    public int SetupAwareTools { get; init; }
    /// <summary>
    /// Tools with explicit environment-discovery/bootstrap role.
    /// </summary>
    public int EnvironmentDiscoverTools { get; init; }

    /// <summary>
    /// Tools with handoff-aware contracts.
    /// </summary>
    public int HandoffAwareTools { get; init; }

    /// <summary>
    /// Tools with recovery-aware contracts.
    /// </summary>
    public int RecoveryAwareTools { get; init; }

    /// <summary>
    /// Tools whose schemas support remote host targeting.
    /// </summary>
    public int RemoteCapableTools { get; init; }

    /// <summary>
    /// Tools whose handoff contracts pivot into a different pack.
    /// </summary>
    public int CrossPackHandoffTools { get; init; }

    /// <summary>
    /// Tools that declare a non-empty domain intent family.
    /// </summary>
    public required int DomainFamilyTools { get; init; }

    /// <summary>
    /// Tools where domain-family is inferred from identity hints but routing contract omits it.
    /// </summary>
    public required int ExpectedDomainFamilyMissingTools { get; init; }

    /// <summary>
    /// Tools that declare family but miss action id.
    /// </summary>
    public required int DomainFamilyMissingActionTools { get; init; }

    /// <summary>
    /// Tools that declare action id but no family.
    /// </summary>
    public required int ActionWithoutFamilyTools { get; init; }

    /// <summary>
    /// Count of domain families that map to multiple action ids.
    /// </summary>
    public required int FamilyActionConflictFamilies { get; init; }

    /// <summary>
    /// Family/action distribution.
    /// </summary>
    public required IReadOnlyList<ToolRoutingFamilyActionSummary> FamilyActions { get; init; }

    /// <summary>
    /// True when no catalog inconsistencies were detected.
    /// </summary>
    public bool IsHealthy =>
        MissingRoutingContractTools == 0
        && MissingPackIdTools == 0
        && MissingRoleTools == 0
        && ExpectedDomainFamilyMissingTools == 0
        && DomainFamilyMissingActionTools == 0
        && ActionWithoutFamilyTools == 0
        && FamilyActionConflictFamilies == 0;

    /// <summary>
    /// True when catalog is ready for strict explicit routing enforcement.
    /// </summary>
    public bool IsExplicitRoutingReady =>
        MissingRoutingContractTools == 0
        && MissingPackIdTools == 0
        && MissingRoleTools == 0
        && InferredRoutingTools == 0;
}

/// <summary>
/// Shared helpers for startup routing catalog diagnostics in host and service.
/// </summary>
public static class ToolRoutingCatalogDiagnosticsBuilder {
    /// <summary>
    /// Builds diagnostics for the current registry state.
    /// </summary>
    public static ToolRoutingCatalogDiagnostics Build(ToolRegistry registry) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }

        return Build(registry.GetDefinitions());
    }

    /// <summary>
    /// Builds diagnostics for the supplied tool definitions.
    /// </summary>
    public static ToolRoutingCatalogDiagnostics Build(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions is null) {
            throw new ArgumentNullException(nameof(definitions));
        }

        var totalTools = definitions.Count;
        var routingAwareTools = 0;
        var explicitRoutingTools = 0;
        var inferredRoutingTools = 0;
        var missingRoutingContractTools = 0;
        var missingPackIdTools = 0;
        var missingRoleTools = 0;
        var setupAwareTools = 0;
        var environmentDiscoverTools = 0;
        var handoffAwareTools = 0;
        var recoveryAwareTools = 0;
        var remoteCapableTools = 0;
        var crossPackHandoffTools = 0;
        var domainFamilyTools = 0;
        var expectedDomainFamilyMissingTools = 0;
        var domainFamilyMissingActionTools = 0;
        var actionWithoutFamilyTools = 0;

        var familyActionCounts = new Dictionary<string, Dictionary<string, FamilyActionAggregate>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var routing = definition.Routing;
            var schemaTraits = ToolSchemaTraitProjection.Project(definition);
            var family = NormalizeToken(routing?.DomainIntentFamily);
            var actionId = NormalizeToken(routing?.DomainIntentActionId);
            var packId = NormalizeToken(routing?.PackId);
            var role = NormalizeToken(routing?.Role);
            var routingSource = NormalizeToken(routing?.RoutingSource);

            if (definition.Setup?.IsSetupAware == true) {
                setupAwareTools++;
            }
            if (string.Equals(role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)) {
                environmentDiscoverTools++;
            }
            if (definition.Handoff?.IsHandoffAware == true) {
                handoffAwareTools++;
            }
            if (definition.Recovery?.IsRecoveryAware == true) {
                recoveryAwareTools++;
            }
            if (ToolExecutionScopes.IsRemoteCapable(schemaTraits.ExecutionScope)) {
                remoteCapableTools++;
            }
            if (HasCrossPackHandoff(definition, packId)) {
                crossPackHandoffTools++;
            }

            if (routing is null) {
                missingRoutingContractTools++;
                if (family.Length == 0
                    && ToolSelectionMetadata.TryResolveDomainIntentFamily(
                        toolName: definition.Name,
                        category: definition.Category,
                        tags: definition.Tags,
                        out _)) {
                    expectedDomainFamilyMissingTools++;
                }
                continue;
            }

            if (routing.IsRoutingAware) {
                routingAwareTools++;
            }

            if (routingSource.Length == 0
                || string.Equals(routingSource, ToolRoutingTaxonomy.SourceExplicit, StringComparison.OrdinalIgnoreCase)) {
                explicitRoutingTools++;
            } else if (string.Equals(routingSource, ToolRoutingTaxonomy.SourceInferred, StringComparison.OrdinalIgnoreCase)) {
                inferredRoutingTools++;
            } else {
                inferredRoutingTools++;
            }

            if (packId.Length == 0) {
                missingPackIdTools++;
            }

            if (role.Length == 0) {
                missingRoleTools++;
            }

            if (family.Length == 0) {
                if (ToolSelectionMetadata.TryResolveDomainIntentFamily(
                        toolName: definition.Name,
                        category: definition.Category,
                        tags: definition.Tags,
                        out _)) {
                    expectedDomainFamilyMissingTools++;
                }
            } else {
                domainFamilyTools++;
                if (actionId.Length == 0) {
                    domainFamilyMissingActionTools++;
                } else {
                    if (!familyActionCounts.TryGetValue(family, out var actionCounts)) {
                        actionCounts = new Dictionary<string, FamilyActionAggregate>(StringComparer.OrdinalIgnoreCase);
                        familyActionCounts[family] = actionCounts;
                    }

                    if (!actionCounts.TryGetValue(actionId, out var aggregate)) {
                        aggregate = new FamilyActionAggregate();
                        actionCounts[actionId] = aggregate;
                    }

                    aggregate.ToolCount++;
                    if (packId.Length > 0) {
                        aggregate.AddPackId(packId);
                    }
                    aggregate.AddPresentation(
                        routing?.DomainIntentFamilyDisplayName,
                        routing?.DomainIntentFamilyReplyExample,
                        routing?.DomainIntentFamilyChoiceDescription);
                }
            }

            if (family.Length == 0 && actionId.Length > 0) {
                actionWithoutFamilyTools++;
            }
        }

        var familyActions = new List<ToolRoutingFamilyActionSummary>();
        var familyActionConflictFamilies = 0;

        foreach (var familyPair in familyActionCounts.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
            var actionCountById = familyPair.Value;
            if (actionCountById.Count > 1) {
                familyActionConflictFamilies++;
            }

            foreach (var actionPair in actionCountById.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
                var representativePackIds = actionPair.Value.GetRepresentativePackIds();
                var explicitDisplayName = actionPair.Value.GetPreferredDisplayName();
                var explicitReplyExample = actionPair.Value.GetPreferredReplyExample();
                var explicitChoiceDescription = actionPair.Value.GetPreferredChoiceDescription();
                var presentation = DomainIntentFamilyPresentationCatalog.Resolve(
                    familyPair.Key,
                    representativePackIds,
                    explicitDisplayName,
                    explicitReplyExample,
                    explicitChoiceDescription);
                familyActions.Add(new ToolRoutingFamilyActionSummary {
                    Family = familyPair.Key,
                    ActionId = actionPair.Key,
                    ToolCount = Math.Max(0, actionPair.Value.ToolCount),
                    DisplayName = presentation.DisplayName,
                    ReplyExample = presentation.ReplyExample,
                    ChoiceDescription = presentation.ChoiceDescription,
                    RepresentativePackIds = representativePackIds
                });
            }
        }

        return new ToolRoutingCatalogDiagnostics {
            TotalTools = totalTools,
            RoutingAwareTools = routingAwareTools,
            ExplicitRoutingTools = explicitRoutingTools,
            InferredRoutingTools = inferredRoutingTools,
            MissingRoutingContractTools = missingRoutingContractTools,
            MissingPackIdTools = missingPackIdTools,
            MissingRoleTools = missingRoleTools,
            SetupAwareTools = setupAwareTools,
            EnvironmentDiscoverTools = environmentDiscoverTools,
            HandoffAwareTools = handoffAwareTools,
            RecoveryAwareTools = recoveryAwareTools,
            RemoteCapableTools = remoteCapableTools,
            CrossPackHandoffTools = crossPackHandoffTools,
            DomainFamilyTools = domainFamilyTools,
            ExpectedDomainFamilyMissingTools = expectedDomainFamilyMissingTools,
            DomainFamilyMissingActionTools = domainFamilyMissingActionTools,
            ActionWithoutFamilyTools = actionWithoutFamilyTools,
            FamilyActionConflictFamilies = familyActionConflictFamilies,
            FamilyActions = familyActions.Count == 0 ? Array.Empty<ToolRoutingFamilyActionSummary>() : familyActions
        };
    }

    /// <summary>
    /// Formats a one-line diagnostics summary for startup status banners.
    /// </summary>
    public static string FormatSummary(ToolRoutingCatalogDiagnostics diagnostics) {
        if (diagnostics is null) {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        return
            $"tools={diagnostics.TotalTools}, " +
            $"routing_aware={diagnostics.RoutingAwareTools}, " +
            $"routing_explicit={diagnostics.ExplicitRoutingTools}, " +
            $"routing_inferred={diagnostics.InferredRoutingTools}, " +
            $"missing_contract={diagnostics.MissingRoutingContractTools}, " +
            $"missing_pack={diagnostics.MissingPackIdTools}, " +
            $"missing_role={diagnostics.MissingRoleTools}, " +
            $"setup_aware={diagnostics.SetupAwareTools}, " +
            $"environment_discover={diagnostics.EnvironmentDiscoverTools}, " +
            $"handoff_aware={diagnostics.HandoffAwareTools}, " +
            $"recovery_aware={diagnostics.RecoveryAwareTools}, " +
            $"remote_capable={diagnostics.RemoteCapableTools}, " +
            $"cross_pack_handoffs={diagnostics.CrossPackHandoffTools}, " +
            $"domain_families={diagnostics.DomainFamilyTools}, " +
            $"expected_family_missing={diagnostics.ExpectedDomainFamilyMissingTools}, " +
            $"missing_action={diagnostics.DomainFamilyMissingActionTools}, " +
            $"action_without_family={diagnostics.ActionWithoutFamilyTools}, " +
            $"conflicts={diagnostics.FamilyActionConflictFamilies}";
    }

    /// <summary>
    /// Formats family/action entries for startup diagnostics banners.
    /// </summary>
    public static IReadOnlyList<string> FormatFamilySummaries(ToolRoutingCatalogDiagnostics diagnostics, int maxItems = 8) {
        if (diagnostics is null) {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        if (maxItems <= 0 || diagnostics.FamilyActions.Count == 0) {
            return Array.Empty<string>();
        }

        var byFamily = diagnostics.FamilyActions
            .GroupBy(static item => item.Family, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (byFamily.Length == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, byFamily.Length));
        for (var i = 0; i < byFamily.Length && lines.Count < maxItems; i++) {
            var group = byFamily[i];
            var actionParts = group
                .OrderBy(static item => item.ActionId, StringComparer.OrdinalIgnoreCase)
                .Select(static item => $"{item.ActionId} ({item.ToolCount})");
            lines.Add($"{group.Key}: {string.Join(", ", actionParts)}");
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    /// <summary>
    /// Builds issue-oriented warnings when diagnostics detect degraded routing catalog state.
    /// </summary>
    public static IReadOnlyList<string> BuildWarnings(ToolRoutingCatalogDiagnostics diagnostics, int maxWarnings = 8) {
        if (diagnostics is null) {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        if (maxWarnings <= 0) {
            return Array.Empty<string>();
        }

        var warnings = new List<string>();
        AddWarningIfPositive(warnings, diagnostics.MissingRoutingContractTools, "tool(s) are missing routing contracts.");
        AddWarningIfPositive(warnings, diagnostics.InferredRoutingTools, "tool(s) still use inferred routing metadata.");
        AddWarningIfPositive(warnings, diagnostics.MissingPackIdTools, "tool(s) are missing routing pack id.");
        AddWarningIfPositive(warnings, diagnostics.MissingRoleTools, "tool(s) are missing routing role.");
        AddWarningIfPositive(warnings, diagnostics.ExpectedDomainFamilyMissingTools,
            "tool(s) are missing domain intent family despite inferred scope.");
        AddWarningIfPositive(warnings, diagnostics.DomainFamilyMissingActionTools,
            "tool(s) declare a domain intent family but miss action id.");
        AddWarningIfPositive(warnings, diagnostics.ActionWithoutFamilyTools,
            "tool(s) declare an action id without a domain intent family.");
        AddWarningIfPositive(warnings, diagnostics.FamilyActionConflictFamilies,
            "domain intent family/families map to multiple action ids.");

        if (warnings.Count >= maxWarnings) {
            return warnings.Take(maxWarnings).ToArray();
        }

        var conflictingFamilies = diagnostics.FamilyActions
            .GroupBy(static item => item.Family, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(item => item.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(item => item.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))}")
            .ToArray();
        for (var i = 0; i < conflictingFamilies.Length && warnings.Count < maxWarnings; i++) {
            warnings.Add("conflict " + conflictingFamilies[i]);
        }

        return warnings.Count == 0 ? Array.Empty<string>() : warnings.ToArray();
    }

    /// <summary>
    /// Builds concise autonomy/readiness notes for operators and planner-facing diagnostics.
    /// </summary>
    public static IReadOnlyList<string> BuildAutonomyReadinessHighlights(ToolRoutingCatalogDiagnostics diagnostics, int maxItems = 6) {
        if (diagnostics is null) {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        if (maxItems <= 0) {
            return Array.Empty<string>();
        }

        var highlights = new List<string>();
        AddHighlightIfPositive(
            highlights,
            diagnostics.RemoteCapableTools,
            "remote host-targeting is ready for ",
            " tool(s).",
            maxItems);
        AddHighlightIfPositive(
            highlights,
            diagnostics.CrossPackHandoffTools,
            "cross-pack continuation is ready for ",
            " tool(s).",
            maxItems);
        AddHighlightIfPositive(
            highlights,
            diagnostics.SetupAwareTools,
            "setup helpers are available for ",
            " tool(s).",
            maxItems);
        AddHighlightIfPositive(
            highlights,
            diagnostics.EnvironmentDiscoverTools,
            "environment discovery bootstrap is available for ",
            " tool(s).",
            maxItems);
        AddHighlightIfPositive(
            highlights,
            diagnostics.RecoveryAwareTools,
            "recovery helpers are available for ",
            " tool(s).",
            maxItems);

        if (highlights.Count < maxItems && diagnostics.IsExplicitRoutingReady) {
            highlights.Add("explicit routing metadata is ready for strict enforcement.");
        } else if (highlights.Count < maxItems && diagnostics.InferredRoutingTools > 0) {
            highlights.Add("explicit routing still depends on inferred metadata for " + diagnostics.InferredRoutingTools + " tool(s).");
        }

        if (highlights.Count < maxItems
            && diagnostics.MissingRoutingContractTools == 0
            && diagnostics.MissingPackIdTools == 0
            && diagnostics.MissingRoleTools == 0) {
            highlights.Add("routing contracts, pack ids, and roles are fully populated.");
        }

        return highlights.Count == 0 ? Array.Empty<string>() : highlights.Take(maxItems).ToArray();
    }

    private static void AddWarningIfPositive(List<string> warnings, int count, string suffix) {
        if (count <= 0) {
            return;
        }

        warnings.Add($"{count} {suffix}");
    }

    private static void AddHighlightIfPositive(
        List<string> highlights,
        int count,
        string prefix,
        string suffix,
        int maxItems) {
        if (count <= 0 || highlights.Count >= maxItems) {
            return;
        }

        highlights.Add(prefix + count + suffix);
    }

    private sealed class FamilyActionAggregate {
        private readonly Dictionary<string, int> _packCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _displayNameCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _replyExampleCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _choiceDescriptionCounts = new(StringComparer.Ordinal);

        internal int ToolCount { get; set; }

        internal void AddPackId(string packId) {
            var normalizedPackId = ToolPackIdentityCatalog.NormalizePackId(packId);
            if (normalizedPackId.Length == 0) {
                return;
            }

            _packCounts.TryGetValue(normalizedPackId, out var currentCount);
            _packCounts[normalizedPackId] = currentCount + 1;
        }

        internal string[] GetRepresentativePackIds(int maxItems = 3) {
            return _packCounts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .Select(static pair => pair.Key)
                .ToArray();
        }

        internal void AddPresentation(string? displayName, string? replyExample, string? choiceDescription) {
            AddValue(_displayNameCounts, displayName);
            AddValue(_replyExampleCounts, replyExample);
            AddValue(_choiceDescriptionCounts, choiceDescription);
        }

        internal string GetPreferredDisplayName() {
            return GetPreferredValue(_displayNameCounts);
        }

        internal string GetPreferredReplyExample() {
            return GetPreferredValue(_replyExampleCounts);
        }

        internal string GetPreferredChoiceDescription() {
            return GetPreferredValue(_choiceDescriptionCounts);
        }

        private static void AddValue(IDictionary<string, int> counts, string? value) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                return;
            }

            counts.TryGetValue(normalized, out var currentCount);
            counts[normalized] = currentCount + 1;
        }

        private static string GetPreferredValue(IEnumerable<KeyValuePair<string, int>> counts) {
            return counts
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key)
                .FirstOrDefault() ?? string.Empty;
        }
    }

    private static bool HasCrossPackHandoff(ToolDefinition definition, string sourcePackId) {
        var routes = definition.Handoff?.OutboundRoutes;
        if (routes is null || routes.Count == 0) {
            return false;
        }

        for (var i = 0; i < routes.Count; i++) {
            var targetPackId = NormalizeToken(routes[i]?.TargetPackId);
            if (targetPackId.Length == 0) {
                continue;
            }

            if (!string.Equals(targetPackId, sourcePackId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeToken(string? value) {
        return (value ?? string.Empty).Trim();
    }
}
