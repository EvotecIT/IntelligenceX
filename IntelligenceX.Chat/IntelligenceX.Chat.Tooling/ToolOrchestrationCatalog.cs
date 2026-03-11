using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Normalized outbound handoff edge derived from tool contracts.
/// </summary>
public sealed record ToolOrchestrationHandoffEdge {
    /// <summary>
    /// Target pack id for this handoff edge.
    /// </summary>
    public string TargetPackId { get; init; } = string.Empty;

    /// <summary>
    /// Optional target tool name for this handoff edge.
    /// </summary>
    public string TargetToolName { get; init; } = string.Empty;

    /// <summary>
    /// Optional target routing role for this handoff edge.
    /// </summary>
    public string TargetRole { get; init; } = string.Empty;

    /// <summary>
    /// Number of bindings declared by this route.
    /// </summary>
    public int BindingCount { get; init; }

    /// <summary>
    /// Normalized source-to-target binding pairs ("source->target").
    /// Duplicate pairs are preserved to keep declared contract multiplicity.
    /// </summary>
    public IReadOnlyList<string> BindingPairs { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Normalized orchestration entry derived from tool contracts.
/// </summary>
public sealed record ToolOrchestrationCatalogEntry {
    /// <summary>
    /// Tool name.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Normalized pack identifier.
    /// </summary>
    public string PackId { get; init; } = string.Empty;

    /// <summary>
    /// Routing role token.
    /// </summary>
    public string Role { get; init; } = ToolRoutingTaxonomy.RoleOperational;

    /// <summary>
    /// Routing source token.
    /// </summary>
    public string RoutingSource { get; init; } = ToolRoutingTaxonomy.SourceExplicit;

    /// <summary>
    /// Indicates whether tool exposes routing-aware metadata.
    /// </summary>
    public bool IsRoutingAware { get; init; }

    /// <summary>
    /// Routing scope token.
    /// </summary>
    public string Scope { get; init; } = ToolRoutingTaxonomy.ScopeGeneral;

    /// <summary>
    /// Routing operation token.
    /// </summary>
    public string Operation { get; init; } = ToolRoutingTaxonomy.OperationRead;

    /// <summary>
    /// Routing entity token.
    /// </summary>
    public string Entity { get; init; } = ToolRoutingTaxonomy.EntityResource;

    /// <summary>
    /// Routing risk token.
    /// </summary>
    public string Risk { get; init; } = ToolRoutingTaxonomy.RiskLow;

    /// <summary>
    /// Indicates whether the tool schema exposes target-scope arguments.
    /// </summary>
    public bool SupportsTargetScoping { get; init; }

    /// <summary>
    /// Canonical target-scope arguments projected from the tool schema.
    /// </summary>
    public IReadOnlyList<string> TargetScopeArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether the tool schema exposes remote-host targeting arguments.
    /// </summary>
    public bool SupportsRemoteHostTargeting { get; init; }

    /// <summary>
    /// Canonical remote-host targeting arguments projected from the tool schema.
    /// </summary>
    public IReadOnlyList<string> RemoteHostArguments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional domain intent family token.
    /// </summary>
    public string DomainIntentFamily { get; init; } = string.Empty;

    /// <summary>
    /// Optional domain intent action id token.
    /// </summary>
    public string DomainIntentActionId { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether tool is setup-aware.
    /// </summary>
    public bool IsSetupAware { get; init; }

    /// <summary>
    /// Number of distinct normalized setup requirement (<c>id</c>, <c>kind</c>) pairs.
    /// This aggregate is independent from <see cref="SetupRequirementIds"/> and
    /// <see cref="SetupRequirementKinds"/>, which are each distinct lists projected separately.
    /// </summary>
    public int SetupRequirementCount { get; init; }

    /// <summary>
    /// Optional setup helper tool name.
    /// </summary>
    public string SetupToolName { get; init; } = string.Empty;

    /// <summary>
    /// Optional setup contract identifier.
    /// </summary>
    public string SetupContractId { get; init; } = string.Empty;

    /// <summary>
    /// Normalized setup requirement identifiers.
    /// </summary>
    public IReadOnlyList<string> SetupRequirementIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized setup requirement kinds.
    /// </summary>
    public IReadOnlyList<string> SetupRequirementKinds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized setup hint keys (contract + requirement-level hints).
    /// </summary>
    public IReadOnlyList<string> SetupHintKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether tool declares outbound handoff routes.
    /// </summary>
    public bool IsHandoffAware { get; init; }

    /// <summary>
    /// Number of declared outbound handoff routes.
    /// </summary>
    public int HandoffRouteCount { get; init; }

    /// <summary>
    /// Number of declared outbound handoff bindings across all routes.
    /// Duplicate normalized binding pairs are counted when explicitly declared.
    /// </summary>
    public int HandoffBindingCount { get; init; }

    /// <summary>
    /// Optional handoff contract identifier.
    /// </summary>
    public string HandoffContractId { get; init; } = string.Empty;

    /// <summary>
    /// Normalized outbound handoff edges.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationHandoffEdge> HandoffEdges { get; init; } = Array.Empty<ToolOrchestrationHandoffEdge>();

    /// <summary>
    /// Indicates whether tool declares effective recovery behavior in normalized projection.
    /// This can remain true for flag-only contracts (for example transient retry support)
    /// even when detail lists like <see cref="RetryableErrorCodes"/> are empty.
    /// </summary>
    public bool IsRecoveryAware { get; init; }

    /// <summary>
    /// Indicates support for transient retry.
    /// </summary>
    public bool SupportsTransientRetry { get; init; }

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetryAttempts { get; init; }

    /// <summary>
    /// Indicates support for alternate internal engines.
    /// </summary>
    public bool SupportsAlternateEngines { get; init; }

    /// <summary>
    /// Number of declared alternate internal engines.
    /// </summary>
    public int AlternateEngineCount { get; init; }

    /// <summary>
    /// Optional recovery contract identifier.
    /// </summary>
    public string RecoveryContractId { get; init; } = string.Empty;

    /// <summary>
    /// Number of declared recovery helper tools.
    /// </summary>
    public int RecoveryToolCount { get; init; }

    /// <summary>
    /// Normalized retryable error codes.
    /// </summary>
    public IReadOnlyList<string> RetryableErrorCodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized alternate engine identifiers.
    /// </summary>
    public IReadOnlyList<string> AlternateEngineIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized recovery helper tool names.
    /// </summary>
    public IReadOnlyList<string> RecoveryToolNames { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Contract-first orchestration catalog built from tool definitions.
/// </summary>
public sealed class ToolOrchestrationCatalog {
    private readonly IReadOnlyDictionary<string, ToolOrchestrationCatalogEntry> _entriesByToolName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> _entriesByPackId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> _entriesByRole;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> _entriesByPackAndRole;

    private ToolOrchestrationCatalog(
        Dictionary<string, ToolOrchestrationCatalogEntry> entriesByToolName,
        Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> entriesByPackId,
        Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> entriesByRole,
        Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> entriesByPackAndRole) {
        _entriesByToolName = new ReadOnlyDictionary<string, ToolOrchestrationCatalogEntry>(
            new Dictionary<string, ToolOrchestrationCatalogEntry>(entriesByToolName, StringComparer.OrdinalIgnoreCase));
        _entriesByPackId = FreezeEntryListDictionary(entriesByPackId);
        _entriesByRole = FreezeEntryListDictionary(entriesByRole);
        _entriesByPackAndRole = FreezeEntryListDictionary(entriesByPackAndRole);
    }

    /// <summary>
    /// Total orchestration entries.
    /// </summary>
    public int Count => _entriesByToolName.Count;

    /// <summary>
    /// Entries by tool name.
    /// </summary>
    public IReadOnlyDictionary<string, ToolOrchestrationCatalogEntry> EntriesByToolName => _entriesByToolName;

    /// <summary>
    /// Builds orchestration catalog from registry definitions.
    /// </summary>
    /// <param name="definitions">Tool definitions.</param>
    /// <returns>Normalized orchestration catalog.</returns>
    public static ToolOrchestrationCatalog Build(IReadOnlyList<ToolDefinition> definitions) {
        return Build(definitions, packs: null);
    }

    /// <summary>
    /// Builds orchestration catalog from registry definitions and optional pack-owned tool catalogs.
    /// </summary>
    /// <param name="definitions">Tool definitions.</param>
    /// <param name="packs">Optional runtime packs that can self-publish tool catalogs.</param>
    /// <returns>Normalized orchestration catalog.</returns>
    public static ToolOrchestrationCatalog Build(
        IReadOnlyList<ToolDefinition> definitions,
        IEnumerable<IToolPack>? packs) {
        if (definitions is null) {
            throw new ArgumentNullException(nameof(definitions));
        }

        var packOwnedCatalogEntries = BuildPackOwnedCatalogEntryIndex(packs);
        var entriesByToolName = new Dictionary<string, ToolOrchestrationCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var toolName = NormalizeToken(definition.Name);
            if (toolName.Length == 0 || entriesByToolName.ContainsKey(toolName)) {
                continue;
            }

            packOwnedCatalogEntries.TryGetValue(toolName, out var packCatalogEntry);
            var packOwnedTraits = packCatalogEntry?.Traits;
            var packOwnedOrchestration = packCatalogEntry?.Orchestration;
            var routingInfo = ToolSelectionMetadata.ResolveRouting(definition);
            var routing = definition.Routing;
            var packId = packOwnedOrchestration is not null && packOwnedOrchestration.PackId.Length > 0
                ? NormalizePackId(packOwnedOrchestration.PackId)
                : NormalizePackId(routing?.PackId);
            var schemaTraits = ToolSchemaTraitProjection.Project(definition);

            var role = packOwnedOrchestration is not null && packOwnedOrchestration.Role.Length > 0
                ? NormalizeToken(packOwnedOrchestration.Role)
                : NormalizeToken(routing?.Role);
            if (!ToolRoutingTaxonomy.IsAllowedRole(role)) {
                role = ToolRoutingTaxonomy.RoleOperational;
            }

            var routingSource = packOwnedOrchestration is not null && packOwnedOrchestration.RoutingSource.Length > 0
                ? NormalizeToken(packOwnedOrchestration.RoutingSource)
                : NormalizeToken(routing?.RoutingSource);
            if (!ToolRoutingTaxonomy.IsAllowedSource(routingSource)) {
                routingSource = ToolRoutingTaxonomy.SourceExplicit;
            }

            var family = packOwnedOrchestration is not null && packOwnedOrchestration.DomainIntentFamily.Length > 0
                ? NormalizeToken(packOwnedOrchestration.DomainIntentFamily)
                : NormalizeToken(routing?.DomainIntentFamily);
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                normalizedFamily = string.Empty;
            }

            var actionId = packOwnedOrchestration is not null && packOwnedOrchestration.DomainIntentActionId.Length > 0
                ? NormalizeToken(packOwnedOrchestration.DomainIntentActionId)
                : NormalizeToken(routing?.DomainIntentActionId);
            var fallbackEntry = BuildEntryFromDefinition(
                toolName,
                definition,
                routingInfo.Scope,
                routingInfo.Operation,
                routingInfo.Entity,
                routingInfo.Risk,
                schemaTraits);
            var mergedEntry = MergeWithPackOwnedCatalogEntry(fallbackEntry, packOwnedTraits, packOwnedOrchestration);

            entriesByToolName[toolName] = mergedEntry with {
                PackId = packId.Length > 0 ? packId : mergedEntry.PackId,
                Role = role.Length > 0 ? role : mergedEntry.Role,
                RoutingSource = routingSource.Length > 0 ? routingSource : mergedEntry.RoutingSource,
                DomainIntentFamily = normalizedFamily,
                DomainIntentActionId = actionId
            };
        }

        var entriesByPackId = entriesByToolName.Values
            .Where(static entry => entry.PackId.Length > 0)
            .GroupBy(static entry => entry.PackId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => FreezeEntryList(group.OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        var entriesByRole = entriesByToolName.Values
            .GroupBy(static entry => entry.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => FreezeEntryList(group.OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        var entriesByPackAndRole = entriesByToolName.Values
            .Where(static entry => entry.PackId.Length > 0 && entry.Role.Length > 0)
            .GroupBy(static entry => BuildPackRoleKey(entry.PackId, entry.Role), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => FreezeEntryList(group.OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        return new ToolOrchestrationCatalog(entriesByToolName, entriesByPackId, entriesByRole, entriesByPackAndRole);
    }

    private static IReadOnlyDictionary<string, ToolPackToolCatalogEntryModel> BuildPackOwnedCatalogEntryIndex(
        IEnumerable<IToolPack>? packs) {
        if (packs is null) {
            return new ReadOnlyDictionary<string, ToolPackToolCatalogEntryModel>(
                new Dictionary<string, ToolPackToolCatalogEntryModel>(StringComparer.OrdinalIgnoreCase));
        }

        var entriesByToolName = new Dictionary<string, ToolPackToolCatalogEntryModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs) {
            if (pack is not IToolPackCatalogProvider catalogProvider) {
                continue;
            }

            foreach (var entry in catalogProvider.GetToolCatalog() ?? Array.Empty<ToolPackToolCatalogEntryModel>()) {
                if (entry is null) {
                    continue;
                }

                var normalizedToolName = NormalizeToken(entry.Name);
                if (normalizedToolName.Length == 0 || entriesByToolName.ContainsKey(normalizedToolName)) {
                    continue;
                }

                entriesByToolName[normalizedToolName] = entry;
            }
        }

        return new ReadOnlyDictionary<string, ToolPackToolCatalogEntryModel>(entriesByToolName);
    }

    private static ToolOrchestrationCatalogEntry BuildEntryFromDefinition(
        string toolName,
        ToolDefinition definition,
        string routingScope,
        string routingOperation,
        string routingEntity,
        string routingRisk,
        ToolSchemaTraits schemaTraits) {
        var routing = definition.Routing;
        var role = NormalizeToken(routing?.Role);
        if (!ToolRoutingTaxonomy.IsAllowedRole(role)) {
            role = ToolRoutingTaxonomy.RoleOperational;
        }

        var routingSource = NormalizeToken(routing?.RoutingSource);
        if (!ToolRoutingTaxonomy.IsAllowedSource(routingSource)) {
            routingSource = ToolRoutingTaxonomy.SourceExplicit;
        }

        var family = NormalizeToken(routing?.DomainIntentFamily);
        if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
            normalizedFamily = string.Empty;
        }

        var setup = definition.Setup;
        var handoff = definition.Handoff;
        var recovery = definition.Recovery;
        var handoffBindingCount = 0;
        var handoffEdges = new List<ToolOrchestrationHandoffEdge>();
        if (handoff?.OutboundRoutes is { Count: > 0 }) {
            for (var routeIndex = 0; routeIndex < handoff.OutboundRoutes.Count; routeIndex++) {
                var route = handoff.OutboundRoutes[routeIndex];
                var bindings = route?.Bindings;
                if (bindings is null || bindings.Count == 0) {
                    continue;
                }

                var bindingPairs = new List<string>(bindings.Count);
                for (var bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++) {
                    var binding = bindings[bindingIndex];
                    var source = NormalizeToken(binding?.SourceField);
                    var target = NormalizeToken(binding?.TargetArgument);
                    if (source.Length == 0 || target.Length == 0) {
                        continue;
                    }

                    bindingPairs.Add(source + "->" + target);
                }

                var normalizedBindingPairs = NormalizeTokensPreserveMultiplicity(bindingPairs);
                if (normalizedBindingPairs.Count == 0) {
                    continue;
                }

                handoffBindingCount += normalizedBindingPairs.Count;
                handoffEdges.Add(new ToolOrchestrationHandoffEdge {
                    TargetPackId = NormalizePackId(route?.TargetPackId),
                    TargetToolName = NormalizeToken(route?.TargetToolName),
                    TargetRole = NormalizeToken(route?.TargetRole),
                    BindingCount = normalizedBindingPairs.Count,
                    BindingPairs = FreezeStringList(normalizedBindingPairs)
                });
            }
        }

        var setupRequirementIds = new List<string>();
        var setupRequirementKinds = new List<string>();
        var setupRequirementPairs = new List<string>();
        var setupHintKeys = new List<string>();
        if (setup?.SetupHintKeys is { Count: > 0 }) {
            for (var hintIndex = 0; hintIndex < setup.SetupHintKeys.Count; hintIndex++) {
                setupHintKeys.Add(setup.SetupHintKeys[hintIndex]);
            }
        }

        if (setup?.Requirements is { Count: > 0 }) {
            for (var requirementIndex = 0; requirementIndex < setup.Requirements.Count; requirementIndex++) {
                var requirement = setup.Requirements[requirementIndex];
                var requirementId = requirement?.RequirementId ?? string.Empty;
                var requirementKind = requirement?.Kind ?? string.Empty;
                setupRequirementIds.Add(requirementId);
                setupRequirementKinds.Add(requirementKind);
                var normalizedRequirementId = NormalizeToken(requirementId);
                var normalizedRequirementKind = NormalizeToken(requirementKind);
                if (normalizedRequirementId.Length > 0 && normalizedRequirementKind.Length > 0) {
                    setupRequirementPairs.Add(normalizedRequirementId + "|" + normalizedRequirementKind);
                }

                if (requirement?.HintKeys is not { Count: > 0 }) {
                    continue;
                }

                for (var hintIndex = 0; hintIndex < requirement.HintKeys.Count; hintIndex++) {
                    setupHintKeys.Add(requirement.HintKeys[hintIndex]);
                }
            }
        }

        var retryableErrorCodes = NormalizeDistinctTokens(recovery?.RetryableErrorCodes);
        var alternateEngineIds = NormalizeDistinctTokens(recovery?.AlternateEngineIds);
        var recoveryToolNames = NormalizeDistinctTokens(recovery?.RecoveryToolNames);
        var normalizedSetupRequirementIds = NormalizeDistinctTokens(setupRequirementIds);
        var normalizedSetupRequirementKinds = NormalizeDistinctTokens(setupRequirementKinds);
        var normalizedSetupRequirementPairs = NormalizeDistinctTokens(setupRequirementPairs);
        var normalizedSetupHintKeys = NormalizeDistinctTokens(setupHintKeys);
        var normalizedSetupToolName = NormalizeToken(setup?.SetupToolName);
        var frozenHandoffEdges = FreezeHandoffEdges(
            handoffEdges
                .OrderBy(static edge => edge.TargetPackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static edge => edge.TargetRole, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static edge => edge.TargetToolName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var maxRetryAttempts = Math.Max(0, recovery?.MaxRetryAttempts ?? 0);
        var supportsTransientRetry = recovery?.SupportsTransientRetry == true;
        var supportsAlternateEngines = recovery?.SupportsAlternateEngines == true;

        return new ToolOrchestrationCatalogEntry {
            ToolName = toolName,
            PackId = NormalizePackId(routing?.PackId),
            Role = role,
            RoutingSource = routingSource,
            IsRoutingAware = routing?.IsRoutingAware == true,
            Scope = NormalizeToken(routingScope, ToolRoutingTaxonomy.ScopeGeneral),
            Operation = NormalizeToken(routingOperation, ToolRoutingTaxonomy.OperationRead),
            Entity = NormalizeToken(routingEntity, ToolRoutingTaxonomy.EntityResource),
            Risk = NormalizeToken(routingRisk, ToolRoutingTaxonomy.RiskLow),
            SupportsTargetScoping = schemaTraits.SupportsTargetScoping,
            TargetScopeArguments = FreezeStringList(schemaTraits.TargetScopeArguments),
            SupportsRemoteHostTargeting = schemaTraits.SupportsRemoteHostTargeting,
            RemoteHostArguments = FreezeStringList(schemaTraits.RemoteHostArguments),
            DomainIntentFamily = normalizedFamily,
            DomainIntentActionId = NormalizeToken(routing?.DomainIntentActionId),
            IsSetupAware = setup?.IsSetupAware == true
                           && (normalizedSetupRequirementPairs.Length > 0
                               || normalizedSetupHintKeys.Length > 0
                               || normalizedSetupToolName.Length > 0),
            SetupRequirementCount = normalizedSetupRequirementPairs.Length,
            SetupToolName = normalizedSetupToolName,
            SetupContractId = NormalizeToken(setup?.SetupContractId),
            SetupRequirementIds = FreezeStringList(normalizedSetupRequirementIds),
            SetupRequirementKinds = FreezeStringList(normalizedSetupRequirementKinds),
            SetupHintKeys = FreezeStringList(normalizedSetupHintKeys),
            IsHandoffAware = handoff?.IsHandoffAware == true && frozenHandoffEdges.Count > 0,
            HandoffRouteCount = frozenHandoffEdges.Count,
            HandoffBindingCount = handoffBindingCount,
            HandoffContractId = NormalizeToken(handoff?.HandoffContractId),
            HandoffEdges = frozenHandoffEdges,
            IsRecoveryAware = recovery?.IsRecoveryAware == true
                              && (NormalizeToken(recovery?.RecoveryContractId).Length > 0
                                  || retryableErrorCodes.Length > 0
                                  || alternateEngineIds.Length > 0
                                  || recoveryToolNames.Length > 0
                                  || supportsTransientRetry
                                  || supportsAlternateEngines
                                  || maxRetryAttempts > 0),
            SupportsTransientRetry = supportsTransientRetry,
            MaxRetryAttempts = maxRetryAttempts,
            SupportsAlternateEngines = supportsAlternateEngines,
            AlternateEngineCount = alternateEngineIds.Length,
            RecoveryContractId = NormalizeToken(recovery?.RecoveryContractId),
            RecoveryToolCount = recoveryToolNames.Length,
            RetryableErrorCodes = FreezeStringList(retryableErrorCodes),
            AlternateEngineIds = FreezeStringList(alternateEngineIds),
            RecoveryToolNames = FreezeStringList(recoveryToolNames)
        };
    }

    private static ToolOrchestrationCatalogEntry MergeWithPackOwnedCatalogEntry(
        ToolOrchestrationCatalogEntry fallbackEntry,
        ToolPackToolTraitsModel? traits,
        ToolPackToolOrchestrationModel? orchestration) {
        var targetScopeArguments = traits is not null && traits.TargetScopeArguments.Count > 0
            ? FreezeStringList(traits.TargetScopeArguments.Select(static argument => NormalizeToken(argument)).Where(static argument => argument.Length > 0).ToArray())
            : fallbackEntry.TargetScopeArguments;
        var remoteHostArguments = traits is not null && traits.RemoteHostArguments.Count > 0
            ? FreezeStringList(traits.RemoteHostArguments.Select(static argument => NormalizeToken(argument)).Where(static argument => argument.Length > 0).ToArray())
            : fallbackEntry.RemoteHostArguments;

        if (orchestration is null) {
            return fallbackEntry with {
                SupportsTargetScoping = traits?.SupportsTargetScoping == true || fallbackEntry.SupportsTargetScoping,
                TargetScopeArguments = targetScopeArguments,
                SupportsRemoteHostTargeting = traits?.SupportsRemoteHostTargeting == true || fallbackEntry.SupportsRemoteHostTargeting,
                RemoteHostArguments = remoteHostArguments
            };
        }

        var handoffEdges = orchestration.HandoffEdges.Count == 0
            ? fallbackEntry.HandoffEdges
            : FreezeHandoffEdges(orchestration.HandoffEdges.Select(static edge => new ToolOrchestrationHandoffEdge {
                TargetPackId = NormalizeToken(edge.TargetPackId),
                TargetToolName = NormalizeToken(edge.TargetToolName),
                TargetRole = NormalizeToken(edge.TargetRole),
                BindingCount = Math.Max(0, edge.BindingCount),
                BindingPairs = FreezeStringList(edge.BindingPairs.Select(static pair => NormalizeToken(pair)).Where(static pair => pair.Length > 0).ToArray())
            }).ToArray());

        return fallbackEntry with {
            PackId = orchestration.PackId.Length > 0 ? NormalizePackId(orchestration.PackId) : fallbackEntry.PackId,
            Role = orchestration.Role.Length > 0 ? NormalizeToken(orchestration.Role) : fallbackEntry.Role,
            RoutingSource = orchestration.RoutingSource.Length > 0 ? NormalizeToken(orchestration.RoutingSource) : fallbackEntry.RoutingSource,
            IsRoutingAware = orchestration.IsRoutingAware || fallbackEntry.IsRoutingAware,
            SupportsTargetScoping = traits?.SupportsTargetScoping == true || fallbackEntry.SupportsTargetScoping,
            TargetScopeArguments = targetScopeArguments,
            SupportsRemoteHostTargeting = traits?.SupportsRemoteHostTargeting == true || fallbackEntry.SupportsRemoteHostTargeting,
            RemoteHostArguments = remoteHostArguments,
            DomainIntentFamily = orchestration.DomainIntentFamily.Length > 0 ? NormalizeToken(orchestration.DomainIntentFamily) : fallbackEntry.DomainIntentFamily,
            DomainIntentActionId = orchestration.DomainIntentActionId.Length > 0 ? NormalizeToken(orchestration.DomainIntentActionId) : fallbackEntry.DomainIntentActionId,
            IsSetupAware = orchestration.IsSetupAware || fallbackEntry.IsSetupAware,
            SetupRequirementCount = Math.Max(orchestration.SetupRequirementCount, fallbackEntry.SetupRequirementCount),
            SetupToolName = orchestration.SetupToolName.Length > 0 ? NormalizeToken(orchestration.SetupToolName) : fallbackEntry.SetupToolName,
            SetupContractId = orchestration.SetupContractId.Length > 0 ? NormalizeToken(orchestration.SetupContractId) : fallbackEntry.SetupContractId,
            SetupRequirementIds = orchestration.SetupRequirementIds.Count > 0
                ? FreezeStringList(orchestration.SetupRequirementIds.Select(static value => NormalizeToken(value)).Where(static value => value.Length > 0).ToArray())
                : fallbackEntry.SetupRequirementIds,
            SetupRequirementKinds = orchestration.SetupRequirementKinds.Count > 0
                ? FreezeStringList(orchestration.SetupRequirementKinds.Select(static value => NormalizeToken(value)).Where(static value => value.Length > 0).ToArray())
                : fallbackEntry.SetupRequirementKinds,
            SetupHintKeys = orchestration.SetupHintKeys.Count > 0
                ? FreezeStringList(orchestration.SetupHintKeys.Select(static value => NormalizeToken(value)).Where(static value => value.Length > 0).ToArray())
                : fallbackEntry.SetupHintKeys,
            IsHandoffAware = orchestration.IsHandoffAware || fallbackEntry.IsHandoffAware,
            HandoffRouteCount = Math.Max(orchestration.HandoffRouteCount, fallbackEntry.HandoffRouteCount),
            HandoffBindingCount = Math.Max(orchestration.HandoffBindingCount, fallbackEntry.HandoffBindingCount),
            HandoffContractId = orchestration.HandoffContractId.Length > 0 ? NormalizeToken(orchestration.HandoffContractId) : fallbackEntry.HandoffContractId,
            HandoffEdges = handoffEdges,
            IsRecoveryAware = orchestration.IsRecoveryAware || fallbackEntry.IsRecoveryAware,
            SupportsTransientRetry = orchestration.SupportsTransientRetry || fallbackEntry.SupportsTransientRetry,
            MaxRetryAttempts = Math.Max(orchestration.MaxRetryAttempts, fallbackEntry.MaxRetryAttempts),
            SupportsAlternateEngines = orchestration.SupportsAlternateEngines || fallbackEntry.SupportsAlternateEngines,
            AlternateEngineCount = Math.Max(orchestration.AlternateEngineCount, fallbackEntry.AlternateEngineCount),
            RecoveryContractId = orchestration.RecoveryContractId.Length > 0 ? NormalizeToken(orchestration.RecoveryContractId) : fallbackEntry.RecoveryContractId,
            RecoveryToolCount = Math.Max(orchestration.RecoveryToolCount, fallbackEntry.RecoveryToolCount),
            RetryableErrorCodes = orchestration.RetryableErrorCodes.Count > 0
                ? FreezeStringList(orchestration.RetryableErrorCodes.Select(static value => NormalizeToken(value)).Where(static value => value.Length > 0).ToArray())
                : fallbackEntry.RetryableErrorCodes,
            AlternateEngineIds = orchestration.AlternateEngineIds.Count > 0
                ? FreezeStringList(orchestration.AlternateEngineIds.Select(static value => NormalizeToken(value)).Where(static value => value.Length > 0).ToArray())
                : fallbackEntry.AlternateEngineIds,
            RecoveryToolNames = orchestration.RecoveryToolNames.Count > 0
                ? FreezeStringList(orchestration.RecoveryToolNames.Select(static value => NormalizeToken(value)).Where(static value => value.Length > 0).ToArray())
                : fallbackEntry.RecoveryToolNames
        };
    }

    /// <summary>
    /// Returns true when an entry exists for the provided tool name.
    /// </summary>
    public bool TryGetEntry(string? toolName, out ToolOrchestrationCatalogEntry entry) {
        var normalized = NormalizeToken(toolName);
        return _entriesByToolName.TryGetValue(normalized, out entry!);
    }

    /// <summary>
    /// Returns true when a normalized pack id is available for the provided tool name.
    /// </summary>
    public bool TryGetPackId(string? toolName, out string packId) {
        packId = string.Empty;
        if (!TryGetEntry(toolName, out var entry) || entry.PackId.Length == 0) {
            return false;
        }

        packId = entry.PackId;
        return true;
    }

    /// <summary>
    /// Gets all entries for a pack id.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationCatalogEntry> GetByPackId(string? packId) {
        var normalized = NormalizePackId(packId);
        if (normalized.Length == 0 || !_entriesByPackId.TryGetValue(normalized, out var entries)) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entries;
    }

    /// <summary>
    /// Gets all entries for a routing role.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationCatalogEntry> GetByRole(string? role) {
        var normalized = NormalizeToken(role);
        if (normalized.Length == 0 || !_entriesByRole.TryGetValue(normalized, out var entries)) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entries;
    }

    /// <summary>
    /// Gets all entries for a pack/role pair.
    /// </summary>
    public IReadOnlyList<ToolOrchestrationCatalogEntry> GetByPackAndRole(string? packId, string? role) {
        var normalizedPackId = NormalizePackId(packId);
        var normalizedRole = NormalizeToken(role);
        if (normalizedPackId.Length == 0 || normalizedRole.Length == 0) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        var key = BuildPackRoleKey(normalizedPackId, normalizedRole);
        if (!_entriesByPackAndRole.TryGetValue(key, out var entries)) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entries;
    }

    private static string BuildPackRoleKey(string packId, string role) {
        return packId + "|" + role;
    }

    private static string NormalizePackId(string? value) {
        return ToolPackBootstrap.NormalizePackId(value);
    }

    private static string NormalizeToken(string? value, string fallback = "") {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string[] NormalizeDistinctTokens(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            var normalized = NormalizeToken(value);
            if (normalized.Length == 0) {
                continue;
            }

            unique.Add(normalized);
        }

        return unique.Count == 0
            ? Array.Empty<string>()
            : unique.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> NormalizeTokensPreserveMultiplicity(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>();
        foreach (var value in values) {
            var token = NormalizeToken(value);
            if (token.Length == 0) {
                continue;
            }

            normalized.Add(token);
        }

        return normalized.Count == 0
            ? Array.Empty<string>()
            : normalized;
    }

    private static IReadOnlyList<string> FreezeStringList(IReadOnlyList<string> values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<string>();
        }

        var copy = new string[values.Count];
        for (var i = 0; i < values.Count; i++) {
            copy[i] = values[i];
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<ToolOrchestrationHandoffEdge> FreezeHandoffEdges(
        IReadOnlyList<ToolOrchestrationHandoffEdge> edges) {
        if (edges is null || edges.Count == 0) {
            return Array.Empty<ToolOrchestrationHandoffEdge>();
        }

        var copy = new ToolOrchestrationHandoffEdge[edges.Count];
        for (var i = 0; i < edges.Count; i++) {
            copy[i] = edges[i];
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<ToolOrchestrationCatalogEntry> FreezeEntryList(
        IEnumerable<ToolOrchestrationCatalogEntry> entries) {
        if (entries is null) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        var copy = entries.ToArray();
        return copy.Length == 0
            ? Array.Empty<ToolOrchestrationCatalogEntry>()
            : Array.AsReadOnly(copy);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> FreezeEntryListDictionary(
        IReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>> source) {
        if (source is null || source.Count == 0) {
            return new ReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(
                new Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(StringComparer.OrdinalIgnoreCase));
        }

        var copy = new Dictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source) {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0 || copy.ContainsKey(key)) {
                continue;
            }

            copy[key] = FreezeEntryList(pair.Value);
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<ToolOrchestrationCatalogEntry>>(copy);
    }
}
