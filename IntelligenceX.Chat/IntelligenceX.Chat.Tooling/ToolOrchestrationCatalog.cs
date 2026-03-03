using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;

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
    /// Number of declared setup requirements.
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
    /// Indicates whether tool declares recovery behavior.
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
    /// Normalized retryable error codes.
    /// </summary>
    public IReadOnlyList<string> RetryableErrorCodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized alternate engine identifiers.
    /// </summary>
    public IReadOnlyList<string> AlternateEngineIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Contract-first orchestration catalog built from tool definitions.
/// </summary>
public sealed class ToolOrchestrationCatalog {
    private readonly Dictionary<string, ToolOrchestrationCatalogEntry> _entriesByToolName;
    private readonly Dictionary<string, ToolOrchestrationCatalogEntry[]> _entriesByPackId;
    private readonly Dictionary<string, ToolOrchestrationCatalogEntry[]> _entriesByRole;
    private readonly Dictionary<string, ToolOrchestrationCatalogEntry[]> _entriesByPackAndRole;

    private ToolOrchestrationCatalog(
        Dictionary<string, ToolOrchestrationCatalogEntry> entriesByToolName,
        Dictionary<string, ToolOrchestrationCatalogEntry[]> entriesByPackId,
        Dictionary<string, ToolOrchestrationCatalogEntry[]> entriesByRole,
        Dictionary<string, ToolOrchestrationCatalogEntry[]> entriesByPackAndRole) {
        _entriesByToolName = entriesByToolName;
        _entriesByPackId = entriesByPackId;
        _entriesByRole = entriesByRole;
        _entriesByPackAndRole = entriesByPackAndRole;
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

                    handoffBindingCount += bindings.Count;
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

                    handoffEdges.Add(new ToolOrchestrationHandoffEdge {
                        TargetPackId = NormalizePackId(route?.TargetPackId),
                        TargetToolName = NormalizeToken(route?.TargetToolName),
                        TargetRole = NormalizeToken(route?.TargetRole),
                        BindingCount = bindings.Count,
                        BindingPairs = NormalizeDistinctTokens(bindingPairs)
                    });
                }
            }

            var setupRequirementIds = new List<string>();
            var setupRequirementKinds = new List<string>();
            var setupHintKeys = new List<string>();
            if (setup?.SetupHintKeys is { Count: > 0 }) {
                for (var hintIndex = 0; hintIndex < setup.SetupHintKeys.Count; hintIndex++) {
                    setupHintKeys.Add(setup.SetupHintKeys[hintIndex]);
                }
            }
            if (setup?.Requirements is { Count: > 0 }) {
                for (var requirementIndex = 0; requirementIndex < setup.Requirements.Count; requirementIndex++) {
                    var requirement = setup.Requirements[requirementIndex];
                    setupRequirementIds.Add(requirement?.RequirementId ?? string.Empty);
                    setupRequirementKinds.Add(requirement?.Kind ?? string.Empty);
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
            var alternateEngineCount = alternateEngineIds.Length;

            entriesByToolName[toolName] = new ToolOrchestrationCatalogEntry {
                ToolName = toolName,
                PackId = packId,
                Role = role,
                RoutingSource = routingSource,
                IsRoutingAware = routing?.IsRoutingAware == true,
                Scope = NormalizeToken(routingInfo.Scope, ToolRoutingTaxonomy.ScopeGeneral),
                Operation = NormalizeToken(routingInfo.Operation, ToolRoutingTaxonomy.OperationRead),
                Entity = NormalizeToken(routingInfo.Entity, ToolRoutingTaxonomy.EntityResource),
                Risk = NormalizeToken(routingInfo.Risk, ToolRoutingTaxonomy.RiskLow),
                DomainIntentFamily = normalizedFamily,
                DomainIntentActionId = actionId,
                IsSetupAware = setup?.IsSetupAware == true,
                SetupRequirementCount = setup?.Requirements?.Count ?? 0,
                SetupToolName = NormalizeToken(setup?.SetupToolName),
                SetupContractId = NormalizeToken(setup?.SetupContractId),
                SetupRequirementIds = NormalizeDistinctTokens(setupRequirementIds),
                SetupRequirementKinds = NormalizeDistinctTokens(setupRequirementKinds),
                SetupHintKeys = NormalizeDistinctTokens(setupHintKeys),
                IsHandoffAware = handoff?.IsHandoffAware == true,
                HandoffRouteCount = handoff?.OutboundRoutes?.Count ?? 0,
                HandoffBindingCount = handoffBindingCount,
                HandoffContractId = NormalizeToken(handoff?.HandoffContractId),
                HandoffEdges = handoffEdges
                    .OrderBy(static edge => edge.TargetPackId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.TargetRole, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static edge => edge.TargetToolName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                IsRecoveryAware = recovery?.IsRecoveryAware == true,
                SupportsTransientRetry = recovery?.SupportsTransientRetry == true,
                MaxRetryAttempts = Math.Max(0, recovery?.MaxRetryAttempts ?? 0),
                SupportsAlternateEngines = recovery?.SupportsAlternateEngines == true,
                AlternateEngineCount = alternateEngineCount,
                RecoveryContractId = NormalizeToken(recovery?.RecoveryContractId),
                RetryableErrorCodes = retryableErrorCodes,
                AlternateEngineIds = alternateEngineIds
            };
        }

        var entriesByPackId = entriesByToolName.Values
            .Where(static entry => entry.PackId.Length > 0)
            .GroupBy(static entry => entry.PackId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var entriesByRole = entriesByToolName.Values
            .GroupBy(static entry => entry.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var entriesByPackAndRole = entriesByToolName.Values
            .Where(static entry => entry.PackId.Length > 0 && entry.Role.Length > 0)
            .GroupBy(static entry => BuildPackRoleKey(entry.PackId, entry.Role), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return new ToolOrchestrationCatalog(entriesByToolName, entriesByPackId, entriesByRole, entriesByPackAndRole);
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
}
