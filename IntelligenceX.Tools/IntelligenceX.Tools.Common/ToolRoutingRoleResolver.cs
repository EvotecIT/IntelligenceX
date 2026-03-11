using System;
using System.Collections.Generic;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helpers for resolving explicit routing roles with validated pack-level fallbacks.
/// </summary>
public static class ToolRoutingRoleResolver {
    /// <summary>
    /// Returns the explicit role when provided; otherwise validates and returns the pack fallback role.
    /// </summary>
    public static string ResolveExplicitOrFallback(string? explicitRole, string fallbackRole, string packDisplayName) {
        var normalizedExplicitRole = NormalizeOptionalRole(explicitRole, packDisplayName);
        if (normalizedExplicitRole.Length > 0) {
            return normalizedExplicitRole;
        }

        return NormalizeRequiredRole(fallbackRole, packDisplayName);
    }

    /// <summary>
    /// Returns the explicit role when provided; otherwise resolves a declared tool-name role and fails when missing.
    /// </summary>
    public static string ResolveExplicitOrDeclared(
        string? explicitRole,
        string toolName,
        IReadOnlyDictionary<string, string> declaredRolesByToolName,
        string packDisplayName) {
        var normalizedExplicitRole = NormalizeOptionalRole(explicitRole, packDisplayName);
        if (normalizedExplicitRole.Length > 0) {
            return normalizedExplicitRole;
        }

        if (declaredRolesByToolName is null) {
            throw new ArgumentNullException(nameof(declaredRolesByToolName));
        }

        if (declaredRolesByToolName.TryGetValue(toolName, out var declaredRole)) {
            return NormalizeRequiredRole(declaredRole, packDisplayName);
        }

        throw new InvalidOperationException(
            $"{packDisplayName} tool '{toolName}' must declare an explicit routing role in pack contracts or ToolDefinition.Routing.Role.");
    }

    private static string NormalizeOptionalRole(string? role, string packDisplayName) {
        var normalized = (role ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return NormalizeRequiredRole(normalized, packDisplayName);
    }

    private static string NormalizeRequiredRole(string role, string packDisplayName) {
        var normalized = role.Trim().ToLowerInvariant();
        if (!ToolRoutingTaxonomy.IsAllowedRole(normalized)) {
            throw new InvalidOperationException(
                $"{packDisplayName} tool routing role '{role}' is invalid. Allowed values: {string.Join(", ", ToolRoutingTaxonomy.AllowedRoles)}.");
        }

        return normalized;
    }
}
