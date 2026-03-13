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
    /// Indicates whether this tool is an orientation/pack-info tool.
    /// </summary>
    public bool IsPackInfoTool { get; init; }

    /// <summary>
    /// Indicates whether this tool is an environment-discovery tool.
    /// </summary>
    public bool IsEnvironmentDiscoverTool { get; init; }

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
    /// Human-readable execution locality token projected from tool schema.
    /// </summary>
    public string ExecutionScope { get; init; } = "local_only";
    /// <summary>
    /// Indicates whether tool declares a structured execution contract.
    /// </summary>
    public bool IsExecutionAware { get; init; }
    /// <summary>
    /// Optional stable execution contract identifier.
    /// </summary>
    public string ExecutionContractId { get; init; } = string.Empty;
    /// <summary>
    /// Indicates whether the tool can execute in the local runtime.
    /// </summary>
    public bool SupportsLocalExecution { get; init; } = true;
    /// <summary>
    /// Indicates whether the tool can execute against remote targets or remote backends.
    /// </summary>
    public bool SupportsRemoteExecution { get; init; }

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
    private sealed record PackCatalogToolMetadata(
        string PackId,
        ToolPackToolCatalogEntryModel Entry);

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
        return Build(definitions, packCatalogEntriesByToolName: null);
    }

    private static ToolOrchestrationCatalog Build(
        IReadOnlyList<ToolDefinition> definitions,
        IReadOnlyDictionary<string, PackCatalogToolMetadata>? packCatalogEntriesByToolName) {
        if (definitions is null) {
            throw new ArgumentNullException(nameof(definitions));
        }

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

            var routingInfo = ToolSelectionMetadata.ResolveRouting(definition);
            var routing = definition.Routing;
            var packId = NormalizePackId(routing?.PackId);
            var schemaTraits = ToolSchemaTraitProjection.Project(definition);
            var execution = definition.Execution;
            var normalizedExecutionContractId = NormalizeToken(execution?.ExecutionContractId);
            var executionScope = ToolExecutionScopes.Resolve(schemaTraits.ExecutionScope, schemaTraits.SupportsRemoteHostTargeting);

            var role = NormalizeToken(routing?.Role);
            if (!ToolRoutingTaxonomy.IsAllowedRole(role)) {
                role = ToolRoutingTaxonomy.RoleOperational;
            }
            var isPackInfoTool = string.Equals(role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase);
            var isEnvironmentDiscoverTool = string.Equals(role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);

            var routingSource = NormalizeToken(routing?.RoutingSource);
            if (!ToolRoutingTaxonomy.IsAllowedSource(routingSource)) {
                routingSource = ToolRoutingTaxonomy.SourceExplicit;
            }

            var family = NormalizeToken(routing?.DomainIntentFamily);
            if (!ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                normalizedFamily = string.Empty;
            }

            var actionId = NormalizeToken(routing?.DomainIntentActionId);
            var setup = definition.Setup;
            var handoff = definition.Handoff;
            var recovery = definition.Recovery;
            var handoffBindingCount = 0;
            var routes = handoff?.OutboundRoutes;
            var handoffEdges = new List<ToolOrchestrationHandoffEdge>();
            if (routes is { Count: > 0 }) {
                for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++) {
                    var route = routes[routeIndex];
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
            var alternateEngineCount = alternateEngineIds.Length;
            var normalizedSetupRequirementIds = NormalizeDistinctTokens(setupRequirementIds);
            var normalizedSetupRequirementKinds = NormalizeDistinctTokens(setupRequirementKinds);
            var normalizedSetupRequirementPairs = NormalizeDistinctTokens(setupRequirementPairs);
            var normalizedSetupHintKeys = NormalizeDistinctTokens(setupHintKeys);
            var normalizedSetupToolName = NormalizeToken(setup?.SetupToolName);
            var isSetupAware = setup?.IsSetupAware == true
                               && (normalizedSetupRequirementPairs.Length > 0
                                   || normalizedSetupHintKeys.Length > 0
                                   || normalizedSetupToolName.Length > 0);
            var normalizedHandoffEdges = handoffEdges
                .OrderBy(static edge => edge.TargetPackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static edge => edge.TargetRole, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static edge => edge.TargetToolName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var frozenHandoffEdges = FreezeHandoffEdges(normalizedHandoffEdges);
            var normalizedHandoffContractId = NormalizeToken(handoff?.HandoffContractId);
            var isHandoffAware = handoff?.IsHandoffAware == true && frozenHandoffEdges.Count > 0;
            var normalizedRecoveryContractId = NormalizeToken(recovery?.RecoveryContractId);
            var maxRetryAttempts = Math.Max(0, recovery?.MaxRetryAttempts ?? 0);
            var supportsTransientRetry = recovery?.SupportsTransientRetry == true;
            var supportsAlternateEngines = recovery?.SupportsAlternateEngines == true;
            var isRecoveryAware = recovery?.IsRecoveryAware == true
                                  && (normalizedRecoveryContractId.Length > 0
                                      || retryableErrorCodes.Length > 0
                                      || alternateEngineCount > 0
                                      || recoveryToolNames.Length > 0
                                      || supportsTransientRetry
                                      || supportsAlternateEngines
                                      || maxRetryAttempts > 0);

            var entry = new ToolOrchestrationCatalogEntry {
                ToolName = toolName,
                PackId = packId,
                Role = role,
                IsPackInfoTool = isPackInfoTool,
                IsEnvironmentDiscoverTool = isEnvironmentDiscoverTool,
                RoutingSource = routingSource,
                IsRoutingAware = routing?.IsRoutingAware == true,
                Scope = NormalizeToken(routingInfo.Scope, ToolRoutingTaxonomy.ScopeGeneral),
                Operation = NormalizeToken(routingInfo.Operation, ToolRoutingTaxonomy.OperationRead),
                Entity = NormalizeToken(routingInfo.Entity, ToolRoutingTaxonomy.EntityResource),
                Risk = NormalizeToken(routingInfo.Risk, ToolRoutingTaxonomy.RiskLow),
                ExecutionScope = executionScope,
                IsExecutionAware = execution?.IsExecutionAware == true,
                ExecutionContractId = normalizedExecutionContractId,
                SupportsLocalExecution = !string.Equals(executionScope, ToolExecutionScopes.RemoteOnly, StringComparison.OrdinalIgnoreCase),
                SupportsRemoteExecution = ToolExecutionScopes.IsRemoteCapable(executionScope),
                SupportsTargetScoping = schemaTraits.SupportsTargetScoping,
                TargetScopeArguments = FreezeStringList(schemaTraits.TargetScopeArguments),
                SupportsRemoteHostTargeting = schemaTraits.SupportsRemoteHostTargeting,
                RemoteHostArguments = FreezeStringList(schemaTraits.RemoteHostArguments),
                DomainIntentFamily = normalizedFamily,
                DomainIntentActionId = actionId,
                IsSetupAware = isSetupAware,
                SetupRequirementCount = normalizedSetupRequirementPairs.Length,
                SetupToolName = normalizedSetupToolName,
                SetupContractId = NormalizeToken(setup?.SetupContractId),
                SetupRequirementIds = FreezeStringList(normalizedSetupRequirementIds),
                SetupRequirementKinds = FreezeStringList(normalizedSetupRequirementKinds),
                SetupHintKeys = FreezeStringList(normalizedSetupHintKeys),
                IsHandoffAware = isHandoffAware,
                HandoffRouteCount = frozenHandoffEdges.Count,
                HandoffBindingCount = handoffBindingCount,
                HandoffContractId = normalizedHandoffContractId,
                HandoffEdges = frozenHandoffEdges,
                IsRecoveryAware = isRecoveryAware,
                SupportsTransientRetry = supportsTransientRetry,
                MaxRetryAttempts = maxRetryAttempts,
                SupportsAlternateEngines = supportsAlternateEngines,
                AlternateEngineCount = alternateEngineCount,
                RecoveryContractId = normalizedRecoveryContractId,
                RecoveryToolCount = recoveryToolNames.Length,
                RetryableErrorCodes = FreezeStringList(retryableErrorCodes),
                AlternateEngineIds = FreezeStringList(alternateEngineIds),
                RecoveryToolNames = FreezeStringList(recoveryToolNames)
            };
            if (packCatalogEntriesByToolName is not null
                && packCatalogEntriesByToolName.TryGetValue(toolName, out var packCatalogMetadata)) {
                entry = ApplyPackCatalogOverlay(entry, packCatalogMetadata);
            }

            entriesByToolName[toolName] = entry;
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

    /// <summary>
    /// Builds orchestration catalog from registry definitions and runtime packs.
    /// </summary>
    public static ToolOrchestrationCatalog Build(
        IReadOnlyList<ToolDefinition> definitions,
        IEnumerable<IToolPack>? packs) {
        if (definitions is null) {
            throw new ArgumentNullException(nameof(definitions));
        }

        var packCatalogEntriesByToolName = BuildPackCatalogEntryIndex(packs);
        return Build(definitions, packCatalogEntriesByToolName);
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

    private static IReadOnlyDictionary<string, PackCatalogToolMetadata>? BuildPackCatalogEntryIndex(IEnumerable<IToolPack>? packs) {
        if (packs is null) {
            return null;
        }

        var index = new Dictionary<string, PackCatalogToolMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs) {
            if (pack is not IToolPackCatalogProvider catalogProvider) {
                continue;
            }

            var normalizedPackId = NormalizePackId(pack.Descriptor.Id);
            var catalogEntries = catalogProvider.GetToolCatalog();
            if (catalogEntries is null || catalogEntries.Count == 0) {
                continue;
            }

            for (var i = 0; i < catalogEntries.Count; i++) {
                var catalogEntry = catalogEntries[i];
                if (catalogEntry is null) {
                    continue;
                }

                var toolName = NormalizeToken(catalogEntry.Name);
                if (toolName.Length == 0 || index.ContainsKey(toolName)) {
                    continue;
                }

                index[toolName] = new PackCatalogToolMetadata(normalizedPackId, catalogEntry);
            }
        }

        return index.Count == 0 ? null : index;
    }

    private static ToolOrchestrationCatalogEntry ApplyPackCatalogOverlay(
        ToolOrchestrationCatalogEntry entry,
        PackCatalogToolMetadata overlay) {
        var overlayEntry = overlay.Entry;
        var normalizedPackId = NormalizeOverlayPackId(overlayEntry.Routing?.PackId, entry.PackId, overlay.PackId);
        var normalizedRole = NormalizeOverlayRole(overlayEntry.Routing?.Role, entry.Role);
        var normalizedScope = NormalizeToken(overlayEntry.Routing?.Scope, entry.Scope);
        var normalizedOperation = NormalizeToken(overlayEntry.Routing?.Operation, entry.Operation);
        var normalizedEntity = NormalizeToken(overlayEntry.Routing?.Entity, entry.Entity);
        var normalizedRisk = NormalizeOverlayRisk(overlayEntry.Routing?.Risk, entry.Risk);
        var normalizedRoutingSource = NormalizeOverlayRoutingSource(overlayEntry.Routing?.Source, entry.RoutingSource);
        var isPackInfoTool = overlayEntry.IsPackInfoTool
                             || entry.IsPackInfoTool
                             || string.Equals(normalizedRole, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase);
        var isEnvironmentDiscoverTool = overlayEntry.IsEnvironmentDiscoverTool
                                        || entry.IsEnvironmentDiscoverTool
                                        || string.Equals(normalizedRole, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);
        var normalizedDomainIntentFamily = NormalizeOverlayDomainIntentFamily(
            overlayEntry.Routing?.DomainIntentFamily,
            entry.DomainIntentFamily);
        var normalizedDomainIntentActionId = NormalizeOverlayDomainIntentActionId(
            overlayEntry.Routing?.DomainIntentActionId,
            normalizedDomainIntentFamily,
            entry.DomainIntentFamily,
            entry.DomainIntentActionId);

        var targetScopeArguments = NormalizeDistinctTokens(overlayEntry.Traits?.TargetScopeArguments);
        var remoteHostArguments = NormalizeDistinctTokens(overlayEntry.Traits?.RemoteHostArguments);
        var supportsTargetScoping = overlayEntry.Traits?.SupportsTargetScoping == true || targetScopeArguments.Length > 0;
        var supportsRemoteHostTargeting = overlayEntry.Traits?.SupportsRemoteHostTargeting == true || remoteHostArguments.Length > 0;
        var executionScope = NormalizeOverlayExecutionScope(
            overlayEntry.Traits?.ExecutionScope,
            entry.ExecutionScope,
            supportsRemoteHostTargeting);

        var setupToolName = NormalizeToken(overlayEntry.Setup?.SetupToolName);
        var setupRequirementIds = NormalizeDistinctTokens(overlayEntry.Setup?.RequirementIds);
        var setupHintKeys = NormalizeDistinctTokens(overlayEntry.Setup?.HintKeys);
        var isSetupAware = overlayEntry.Setup?.IsSetupAware == true
                           || setupToolName.Length > 0
                           || setupRequirementIds.Length > 0
                           || setupHintKeys.Length > 0;

        var handoffEdges = BuildOverlayHandoffEdges(overlayEntry.Handoff);
        var handoffBindingCount = handoffEdges.Sum(static edge => edge.BindingCount);
        var isHandoffAware = overlayEntry.Handoff?.IsHandoffAware == true || handoffEdges.Count > 0;

        var retryableErrorCodes = NormalizeDistinctTokens(overlayEntry.Recovery?.RetryableErrorCodes);
        var recoveryToolNames = NormalizeDistinctTokens(overlayEntry.Recovery?.RecoveryToolNames);
        var supportsTransientRetry = overlayEntry.Recovery?.SupportsTransientRetry == true;
        var maxRetryAttempts = Math.Max(0, overlayEntry.Recovery?.MaxRetryAttempts ?? 0);
        var isRecoveryAware = overlayEntry.Recovery?.IsRecoveryAware == true
                              || retryableErrorCodes.Length > 0
                              || recoveryToolNames.Length > 0
                              || supportsTransientRetry
                              || maxRetryAttempts > 0;

        return entry with {
            PackId = normalizedPackId,
            Role = normalizedRole,
            IsPackInfoTool = isPackInfoTool,
            IsEnvironmentDiscoverTool = isEnvironmentDiscoverTool,
            Scope = normalizedScope,
            Operation = normalizedOperation,
            Entity = normalizedEntity,
            Risk = normalizedRisk,
            RoutingSource = normalizedRoutingSource,
            IsRoutingAware = true,
            ExecutionScope = executionScope,
            SupportsTargetScoping = supportsTargetScoping,
            TargetScopeArguments = FreezeStringList(targetScopeArguments),
            SupportsRemoteHostTargeting = supportsRemoteHostTargeting,
            RemoteHostArguments = FreezeStringList(remoteHostArguments),
            DomainIntentFamily = normalizedDomainIntentFamily,
            DomainIntentActionId = normalizedDomainIntentActionId,
            IsSetupAware = isSetupAware,
            SetupRequirementCount = isSetupAware
                ? Math.Max(entry.SetupRequirementCount, setupRequirementIds.Length)
                : 0,
            SetupToolName = setupToolName,
            SetupRequirementIds = FreezeStringList(setupRequirementIds),
            SetupRequirementKinds = isSetupAware ? entry.SetupRequirementKinds : Array.Empty<string>(),
            SetupHintKeys = FreezeStringList(setupHintKeys),
            IsHandoffAware = isHandoffAware,
            HandoffRouteCount = handoffEdges.Count,
            HandoffBindingCount = handoffBindingCount,
            HandoffEdges = handoffEdges,
            IsRecoveryAware = isRecoveryAware,
            SupportsTransientRetry = supportsTransientRetry,
            MaxRetryAttempts = maxRetryAttempts,
            SupportsAlternateEngines = entry.SupportsAlternateEngines,
            AlternateEngineCount = entry.AlternateEngineCount,
            RecoveryToolCount = recoveryToolNames.Length,
            RetryableErrorCodes = FreezeStringList(retryableErrorCodes),
            AlternateEngineIds = entry.AlternateEngineIds,
            RecoveryToolNames = FreezeStringList(recoveryToolNames)
        };
    }

    private static IReadOnlyList<ToolOrchestrationHandoffEdge> BuildOverlayHandoffEdges(ToolPackToolHandoffModel? handoff) {
        if (handoff?.Routes is not { Count: > 0 }) {
            return Array.Empty<ToolOrchestrationHandoffEdge>();
        }

        var edges = new List<ToolOrchestrationHandoffEdge>();
        for (var i = 0; i < handoff.Routes.Count; i++) {
            var route = handoff.Routes[i];
            var bindingPairs = NormalizeTokensPreserveMultiplicity(route?.BindingPairs);
            if (bindingPairs.Count == 0) {
                continue;
            }

            edges.Add(new ToolOrchestrationHandoffEdge {
                TargetPackId = NormalizePackId(route?.TargetPackId),
                TargetToolName = NormalizeToken(route?.TargetToolName),
                TargetRole = NormalizeToken(route?.TargetRole),
                BindingCount = bindingPairs.Count,
                BindingPairs = FreezeStringList(bindingPairs)
            });
        }

        return edges.Count == 0
            ? Array.Empty<ToolOrchestrationHandoffEdge>()
            : FreezeHandoffEdges(
                edges
                    .OrderBy(static edge => edge.TargetPackId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.TargetRole, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.TargetToolName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
    }

    private static string NormalizeOverlayRisk(string? value, string fallback) {
        var normalized = NormalizeToken(value, fallback);
        return ToolRoutingTaxonomy.IsAllowedRisk(normalized) ? normalized : fallback;
    }

    private static string NormalizeOverlayRoutingSource(string? value, string fallback) {
        var normalized = NormalizeToken(value, fallback);
        return ToolRoutingTaxonomy.IsAllowedSource(normalized) ? normalized : fallback;
    }

    private static string NormalizeOverlayPackId(string? value, string fallback, string overlayFallback) {
        var normalized = NormalizePackId(value);
        if (normalized.Length > 0) {
            return normalized;
        }

        var fallbackPackId = NormalizePackId(fallback);
        if (fallbackPackId.Length > 0) {
            return fallbackPackId;
        }

        return NormalizePackId(overlayFallback);
    }

    private static string NormalizeOverlayRole(string? value, string fallback) {
        var normalized = NormalizeToken(value);
        return ToolRoutingTaxonomy.IsAllowedRole(normalized) ? normalized : fallback;
    }

    private static string NormalizeOverlayDomainIntentFamily(string? value, string fallback) {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0) {
            return fallback;
        }

        return ToolSelectionMetadata.TryNormalizeDomainIntentFamily(normalized, out var family)
            ? family
            : fallback;
    }

    private static string NormalizeOverlayDomainIntentActionId(
        string? value,
        string normalizedFamily,
        string fallbackFamily,
        string fallbackActionId) {
        var normalizedActionId = NormalizeToken(value);
        if (normalizedActionId.Length > 0) {
            return normalizedActionId;
        }

        if (normalizedFamily.Length > 0
            && !string.Equals(normalizedFamily, fallbackFamily, StringComparison.OrdinalIgnoreCase)) {
            return ToolSelectionMetadata.GetDefaultDomainIntentActionId(normalizedFamily);
        }

        return fallbackActionId;
    }

    private static string NormalizeOverlayExecutionScope(string? value, string fallback, bool supportsRemoteHostTargeting) {
        var normalized = NormalizeToken(value);
        if (string.Equals(normalized, "local_only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "local_or_remote", StringComparison.OrdinalIgnoreCase)) {
            return normalized;
        }

        if (supportsRemoteHostTargeting) {
            return "local_or_remote";
        }

        return NormalizeToken(fallback, "local_only");
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
