using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared name-shape routing heuristics used when tools do not declare explicit operation/entity metadata.
/// </summary>
public static class ToolNameRoutingSemantics {
    private sealed class OperationRule {
        public OperationRule(Func<string, bool> matches, string operation) {
            Matches = matches ?? throw new ArgumentNullException(nameof(matches));
            Operation = (operation ?? string.Empty).Trim();
        }

        public Func<string, bool> Matches { get; }
        public string Operation { get; }
    }

    private sealed class EntityRule {
        public EntityRule(Func<string, string?, bool> matches, string entity) {
            Matches = matches ?? throw new ArgumentNullException(nameof(matches));
            Entity = (entity ?? string.Empty).Trim();
        }

        public Func<string, string?, bool> Matches { get; }
        public string Entity { get; }
    }

    private static readonly IReadOnlyList<OperationRule> OperationRules = new[] {
        new OperationRule(static name => name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase), "guide"),
        new OperationRule(static name => ContainsNameShape(name, "_discover"), "discover"),
        new OperationRule(static name => HasNameShape(name, "_list"), "list"),
        new OperationRule(static name => HasNameShape(name, "_search"), "search"),
        new OperationRule(static name => HasNameShape(name, "_query"), "query"),
        new OperationRule(static name => HasNameShape(name, "_resolve"), "resolve"),
        new OperationRule(
            static name => name.EndsWith("_stats", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith("_summary", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith("_health", StringComparison.OrdinalIgnoreCase),
            "summarize"),
        new OperationRule(static name => name.EndsWith("_probe", StringComparison.OrdinalIgnoreCase), "probe"),
        new OperationRule(static name => HasNameShape(name, "_ping"), "probe"),
        new OperationRule(static name => name.EndsWith("_send", StringComparison.OrdinalIgnoreCase), "write"),
        new OperationRule(static name => name.EndsWith("_get", StringComparison.OrdinalIgnoreCase), ToolRoutingTaxonomy.OperationRead)
    };

    private static readonly IReadOnlyList<EntityRule> EntityRules = new[] {
        new EntityRule(
            static (name, category) => string.Equals(category, "dns", StringComparison.OrdinalIgnoreCase)
                                       && (ContainsNameToken(name, "ping")
                                           || ContainsNameToken(name, "probe")
                                           || ContainsNameToken(name, "traceroute")),
            "host"),
        new EntityRule(static (name, _) => ContainsNameToken(name, "user"), "user"),
        new EntityRule(static (name, _) => ContainsNameToken(name, "group"), "group"),
        new EntityRule(static (name, _) => ContainsNameToken(name, "gpo"), "gpo"),
        new EntityRule(
            static (name, _) => ContainsNameToken(name, "event") || ContainsNameToken(name, "eventlog"),
            "event"),
        new EntityRule(static (name, _) => ContainsNameToken(name, "dns"), "dns"),
        new EntityRule(
            static (name, _) => ContainsNameToken(name, "ldap") || ContainsNameToken(name, "object"),
            "directory_object"),
        new EntityRule(
            static (name, _) => ContainsNameToken(name, "mail") || ContainsNameToken(name, "email"),
            "message")
    };

    /// <summary>
    /// Infers the routing operation from tool name shape when no explicit contract exists.
    /// </summary>
    public static string InferOperation(string? toolName, bool isWriteCapable) {
        var name = (toolName ?? string.Empty).Trim();
        for (var i = 0; i < OperationRules.Count; i++) {
            var rule = OperationRules[i];
            if (rule.Matches(name)) {
                return rule.Operation;
            }
        }

        if (name.EndsWith("_run", StringComparison.OrdinalIgnoreCase)) {
            return isWriteCapable ? "execute_write" : "execute";
        }

        return isWriteCapable ? "write" : ToolRoutingTaxonomy.OperationRead;
    }

    /// <summary>
    /// Infers the routing entity from tool name shape and category when no explicit contract exists.
    /// </summary>
    public static string InferEntity(string? toolName, string? category) {
        var name = (toolName ?? string.Empty).Trim();
        for (var i = 0; i < EntityRules.Count; i++) {
            var rule = EntityRules[i];
            if (rule.Matches(name, category)) {
                return rule.Entity;
            }
        }

        if (ToolCategoryRoutingSemantics.TryGetDefaultEntity(category, out var defaultEntity)) {
            return defaultEntity;
        }

        return ToolRoutingTaxonomy.EntityResource;
    }

    private static bool HasNameShape(string name, string token) {
        return name.EndsWith(token, StringComparison.OrdinalIgnoreCase)
               || ContainsNameShape(name, token);
    }

    private static bool ContainsNameShape(string name, string token) {
        return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsNameToken(string name, string token) {
        return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
