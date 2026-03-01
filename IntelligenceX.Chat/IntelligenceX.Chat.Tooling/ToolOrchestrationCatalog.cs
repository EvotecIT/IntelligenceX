using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

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
    /// <param name="registeredPackIdsByToolName">Optional pack id assignments captured at registration time.</param>
    /// <returns>Normalized orchestration catalog.</returns>
    public static ToolOrchestrationCatalog Build(
        IReadOnlyList<ToolDefinition> definitions,
        IReadOnlyDictionary<string, string>? registeredPackIdsByToolName = null) {
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
            if (packId.Length == 0
                && registeredPackIdsByToolName is not null
                && registeredPackIdsByToolName.TryGetValue(toolName, out var assignedPackId)) {
                packId = NormalizePackId(assignedPackId);
            }

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
            if (routes is { Count: > 0 }) {
                for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++) {
                    var route = routes[routeIndex];
                    if (route?.Bindings is null || route.Bindings.Count == 0) {
                        continue;
                    }

                    handoffBindingCount += route.Bindings.Count;
                }
            }

            var alternateEngineCount = 0;
            if (recovery?.AlternateEngineIds is { Count: > 0 }) {
                for (var engineIndex = 0; engineIndex < recovery.AlternateEngineIds.Count; engineIndex++) {
                    var engineId = NormalizeToken(recovery.AlternateEngineIds[engineIndex]);
                    if (engineId.Length > 0) {
                        alternateEngineCount++;
                    }
                }
            }

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
                IsHandoffAware = handoff?.IsHandoffAware == true,
                HandoffRouteCount = handoff?.OutboundRoutes?.Count ?? 0,
                HandoffBindingCount = handoffBindingCount,
                IsRecoveryAware = recovery?.IsRecoveryAware == true,
                SupportsTransientRetry = recovery?.SupportsTransientRetry == true,
                MaxRetryAttempts = Math.Max(0, recovery?.MaxRetryAttempts ?? 0),
                SupportsAlternateEngines = recovery?.SupportsAlternateEngines == true,
                AlternateEngineCount = alternateEngineCount
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
}
