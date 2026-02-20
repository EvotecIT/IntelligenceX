using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Provides centralized tool-selection metadata enrichment for routing hints.
/// </summary>
public static class ToolSelectionMetadata {
    private const string DefaultCategory = "general";

    private sealed class ExplicitSelectionOverride {
        public ExplicitSelectionOverride(
            string? category,
            string? scope,
            string? operation,
            string? entity,
            string? risk,
            IReadOnlyList<string>? tags,
            bool isHighTraffic,
            bool isHighRisk) {
            Category = category;
            Scope = scope;
            Operation = operation;
            Entity = entity;
            Risk = risk;
            Tags = tags ?? Array.Empty<string>();
            IsHighTraffic = isHighTraffic;
            IsHighRisk = isHighRisk;
        }

        public string? Category { get; }
        public string? Scope { get; }
        public string? Operation { get; }
        public string? Entity { get; }
        public string? Risk { get; }
        public IReadOnlyList<string> Tags { get; }
        public bool IsHighTraffic { get; }
        public bool IsHighRisk { get; }
    }

    /// <summary>
    /// Structured routing taxonomy resolved for a tool.
    /// </summary>
    public sealed class ToolSelectionRoutingInfo {
        /// <summary>
        /// Initializes a new routing taxonomy descriptor.
        /// </summary>
        public ToolSelectionRoutingInfo(string scope, string operation, string entity, string risk, bool isExplicit) {
            Scope = NormalizeToken(scope, ToolRoutingTaxonomy.ScopeGeneral);
            Operation = NormalizeToken(operation, ToolRoutingTaxonomy.OperationRead);
            Entity = NormalizeToken(entity, ToolRoutingTaxonomy.EntityResource);
            Risk = NormalizeToken(risk, ToolRoutingTaxonomy.RiskLow);
            IsExplicit = isExplicit;
        }

        /// <summary>
        /// Primary scope where the tool operates.
        /// </summary>
        public string Scope { get; }
        /// <summary>
        /// Primary operation kind (query/search/list/read/write/etc).
        /// </summary>
        public string Operation { get; }
        /// <summary>
        /// Primary entity class handled by the tool.
        /// </summary>
        public string Entity { get; }
        /// <summary>
        /// Relative risk profile (low/medium/high).
        /// </summary>
        public string Risk { get; }
        /// <summary>
        /// True when routing values came from an explicit override.
        /// </summary>
        public bool IsExplicit { get; }
    }

    private static readonly IReadOnlyDictionary<string, string> CategoryByPrefix =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ad"] = "active_directory",
            ["eventlog"] = "eventlog",
            ["system"] = "system",
            ["wsl"] = "system",
            ["email"] = "email",
            ["fs"] = "filesystem",
            ["powershell"] = "powershell",
            ["testimox"] = "testimox",
            ["officeimo"] = "officeimo",
            ["reviewer"] = "reviewer_setup"
        };

    private static readonly IReadOnlyDictionary<string, ExplicitSelectionOverride> ExplicitOverrides =
        new Dictionary<string, ExplicitSelectionOverride>(StringComparer.OrdinalIgnoreCase) {
            ["system_info"] = new ExplicitSelectionOverride(
                category: "system",
                scope: "host",
                operation: ToolRoutingTaxonomy.OperationRead,
                entity: "host",
                risk: ToolRoutingTaxonomy.RiskLow,
                tags: new[] { "inventory", "baseline" },
                isHighTraffic: true,
                isHighRisk: false),
            ["eventlog_named_events_query"] = new ExplicitSelectionOverride(
                category: "eventlog",
                scope: "host",
                operation: "query",
                entity: "event",
                risk: ToolRoutingTaxonomy.RiskLow,
                tags: new[] { "named_events", "correlation" },
                isHighTraffic: true,
                isHighRisk: false),
            ["eventlog_timeline_query"] = new ExplicitSelectionOverride(
                category: "eventlog",
                scope: "host",
                operation: "query",
                entity: "event",
                risk: ToolRoutingTaxonomy.RiskLow,
                tags: new[] { "timeline", "correlation" },
                isHighTraffic: true,
                isHighRisk: false),
            ["ad_search"] = new ExplicitSelectionOverride(
                category: "active_directory",
                scope: "domain",
                operation: "search",
                entity: "directory_object",
                risk: "medium",
                tags: new[] { "identity", "handoff_consumer" },
                isHighTraffic: true,
                isHighRisk: false),
            ["ad_object_resolve"] = new ExplicitSelectionOverride(
                category: "active_directory",
                scope: "domain",
                operation: "resolve",
                entity: "directory_object",
                risk: "medium",
                tags: new[] { "identity", "handoff_consumer" },
                isHighTraffic: true,
                isHighRisk: false),
            ["ad_handoff_prepare"] = new ExplicitSelectionOverride(
                category: "active_directory",
                scope: "domain",
                operation: "transform",
                entity: "identity",
                risk: ToolRoutingTaxonomy.RiskLow,
                tags: new[] { "handoff", "normalization" },
                isHighTraffic: true,
                isHighRisk: false),
            ["powershell_run"] = new ExplicitSelectionOverride(
                category: "powershell",
                scope: "host",
                operation: "execute_write",
                entity: "command",
                risk: "high",
                tags: new[] { "execution", "mutating" },
                isHighTraffic: false,
                isHighRisk: true),
            ["email_smtp_send"] = new ExplicitSelectionOverride(
                category: "email",
                scope: "message",
                operation: "write",
                entity: "message",
                risk: "high",
                tags: new[] { "smtp", "send" },
                isHighTraffic: false,
                isHighRisk: true)
        };

    private static readonly string[] RequiredExplicitOverrideToolNames =
        ExplicitOverrides
            .Where(static kv => kv.Value.IsHighRisk || kv.Value.IsHighTraffic)
            .Select(static kv => kv.Key)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static readonly string[] TimeRangeArgumentNames = {
        "start_time_utc",
        "end_time_utc",
        "time_period",
        "since_utc"
    };

    private static readonly string[] TableViewArgumentNames = {
        "columns",
        "sort_by",
        "sort_direction",
        "top"
    };

    private static readonly string[] PagingArgumentNames = {
        "cursor",
        "page_size",
        "max_results"
    };

    private static readonly string[] TargetScopeArgumentNames = {
        "domain_name",
        "domain_controller",
        "machine_name",
        "machine_names",
        "search_base_dn",
        "computer_name"
    };

    private static readonly string[] DomainScopeArgumentNames = {
        "domain_name",
        "domain_controller",
        "search_base_dn",
        "forest_name"
    };

    private static readonly string[] HostScopeArgumentNames = {
        "machine_name",
        "machine_names",
        "computer_name",
        "server",
        "host"
    };

    private static readonly string[] FileScopeArgumentNames = {
        "path",
        "folder",
        "file_path",
        "evtx_path",
        "source_path"
    };

    /// <summary>
    /// Returns the same definition with inferred category/tags when missing.
    /// </summary>
    public static ToolDefinition Enrich(ToolDefinition definition, Type? toolType = null) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var explicitOverride = GetExplicitOverride(definition);
        var category = NormalizeCategory(string.IsNullOrWhiteSpace(definition.Category)
            ? (explicitOverride?.Category ?? InferCategory(definition.Name, toolType))
            : definition.Category);
        var routing = ResolveRouting(definition, category, explicitOverride);
        var tags = BuildSelectionTags(definition, category, routing, explicitOverride);

        if (string.Equals(category, definition.Category, StringComparison.Ordinal) &&
            SequenceEqual(definition.Tags, tags)) {
            return definition;
        }

        return new ToolDefinition(
            name: definition.Name,
            description: definition.Description,
            parameters: definition.Parameters,
            displayName: definition.DisplayName,
            category: category,
            tags: tags,
            writeGovernance: definition.WriteGovernance,
            aliases: definition.Aliases,
            aliasOf: definition.AliasOf,
            authentication: definition.Authentication);
    }

    /// <summary>
    /// Resolves structured routing taxonomy for a tool definition.
    /// </summary>
    public static ToolSelectionRoutingInfo ResolveRouting(ToolDefinition definition, Type? toolType = null) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var explicitOverride = GetExplicitOverride(definition);
        var category = NormalizeCategory(string.IsNullOrWhiteSpace(definition.Category)
            ? (explicitOverride?.Category ?? InferCategory(definition.Name, toolType))
            : definition.Category);
        return ResolveRouting(definition, category, explicitOverride);
    }

    /// <summary>
    /// Indicates whether a tool has an explicit selection-metadata override.
    /// </summary>
    public static bool HasExplicitOverride(string? toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return ExplicitOverrides.ContainsKey(normalized);
    }

    /// <summary>
    /// Returns high-priority tools that must keep explicit routing metadata overrides.
    /// </summary>
    public static IReadOnlyList<string> GetRequiredExplicitOverrideToolNames() {
        return RequiredExplicitOverrideToolNames;
    }

    private static ToolSelectionRoutingInfo ResolveRouting(
        ToolDefinition definition,
        string? category,
        ExplicitSelectionOverride? explicitOverride) {
        var operation = string.IsNullOrWhiteSpace(explicitOverride?.Operation)
            ? InferOperation(definition.Name, definition.WriteGovernance?.IsWriteCapable == true)
            : explicitOverride!.Operation!;

        var scope = string.IsNullOrWhiteSpace(explicitOverride?.Scope)
            ? InferScope(definition, category, operation)
            : explicitOverride!.Scope!;

        var entity = string.IsNullOrWhiteSpace(explicitOverride?.Entity)
            ? InferEntity(definition.Name, category)
            : explicitOverride!.Entity!;

        var risk = string.IsNullOrWhiteSpace(explicitOverride?.Risk)
            ? InferRisk(definition, operation)
            : explicitOverride!.Risk!;

        return new ToolSelectionRoutingInfo(
            scope: scope,
            operation: operation,
            entity: entity,
            risk: risk,
            isExplicit: explicitOverride is not null);
    }

    private static ExplicitSelectionOverride? GetExplicitOverride(ToolDefinition definition) {
        if (definition is null) {
            return null;
        }

        var canonical = string.IsNullOrWhiteSpace(definition.CanonicalName)
            ? definition.Name
            : definition.CanonicalName;
        if (!string.IsNullOrWhiteSpace(canonical) && ExplicitOverrides.TryGetValue(canonical.Trim(), out var hit)) {
            return hit;
        }

        var name = definition.Name;
        if (!string.IsNullOrWhiteSpace(name) && ExplicitOverrides.TryGetValue(name.Trim(), out hit)) {
            return hit;
        }

        return null;
    }

    private static string? InferCategory(string? toolName, Type? toolType) {
        if (!string.IsNullOrWhiteSpace(toolName)) {
            var normalized = toolName.Trim();
            var separator = normalized.IndexOf('_');
            if (separator > 0) {
                var prefix = normalized.Substring(0, separator);
                if (CategoryByPrefix.TryGetValue(prefix, out var prefixedCategory)) {
                    return prefixedCategory;
                }
            }
        }

        var ns = toolType?.Namespace ?? string.Empty;
        if (ns.IndexOf(".ADPlayground", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "active_directory";
        }
        if (ns.IndexOf(".EventLog", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "eventlog";
        }
        if (ns.IndexOf(".System", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "system";
        }
        if (ns.IndexOf(".Email", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "email";
        }
        if (ns.IndexOf(".FileSystem", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "filesystem";
        }
        if (ns.IndexOf(".PowerShell", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "powershell";
        }
        if (ns.IndexOf(".TestimoX", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "testimox";
        }
        if (ns.IndexOf(".OfficeIMO", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "officeimo";
        }
        if (ns.IndexOf(".ReviewerSetup", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "reviewer_setup";
        }

        return null;
    }

    private static string InferScope(ToolDefinition definition, string? category, string operation) {
        if (string.Equals(operation, "guide", StringComparison.OrdinalIgnoreCase)) {
            return "pack";
        }

        if (HasAnyProperty(definition.Parameters, DomainScopeArgumentNames) ||
            string.Equals(category, "active_directory", StringComparison.OrdinalIgnoreCase)) {
            return "domain";
        }

        if (HasAnyProperty(definition.Parameters, FileScopeArgumentNames) ||
            string.Equals(category, "filesystem", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "officeimo", StringComparison.OrdinalIgnoreCase)) {
            return "file";
        }

        if (HasAnyProperty(definition.Parameters, HostScopeArgumentNames) ||
            string.Equals(category, "system", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "eventlog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "powershell", StringComparison.OrdinalIgnoreCase)) {
            return "host";
        }

        if (string.Equals(category, "email", StringComparison.OrdinalIgnoreCase)) {
            return "message";
        }

        if (string.Equals(category, "reviewer_setup", StringComparison.OrdinalIgnoreCase)) {
            return "repository";
        }

        return ToolRoutingTaxonomy.ScopeGeneral;
    }

    private static string InferOperation(string? toolName, bool isWriteCapable) {
        var name = (toolName ?? string.Empty).Trim();
        if (name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return "guide";
        }

        if (name.IndexOf("_discover", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "discover";
        }
        if (name.EndsWith("_list", StringComparison.OrdinalIgnoreCase) || name.IndexOf("_list_", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "list";
        }
        if (name.EndsWith("_search", StringComparison.OrdinalIgnoreCase) || name.IndexOf("_search_", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "search";
        }
        if (name.EndsWith("_query", StringComparison.OrdinalIgnoreCase) || name.IndexOf("_query_", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "query";
        }
        if (name.EndsWith("_resolve", StringComparison.OrdinalIgnoreCase) || name.IndexOf("_resolve_", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "resolve";
        }
        if (name.EndsWith("_stats", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_summary", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_health", StringComparison.OrdinalIgnoreCase)) {
            return "summarize";
        }
        if (name.EndsWith("_probe", StringComparison.OrdinalIgnoreCase)) {
            return "probe";
        }
        if (name.EndsWith("_send", StringComparison.OrdinalIgnoreCase)) {
            return "write";
        }
        if (name.EndsWith("_run", StringComparison.OrdinalIgnoreCase)) {
            return isWriteCapable ? "execute_write" : "execute";
        }
        if (name.EndsWith("_get", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.OperationRead;
        }

        return isWriteCapable ? "write" : ToolRoutingTaxonomy.OperationRead;
    }

    private static string InferEntity(string? toolName, string? category) {
        var name = (toolName ?? string.Empty).Trim();
        if (name.IndexOf("user", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "user";
        }
        if (name.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "group";
        }
        if (name.IndexOf("gpo", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "gpo";
        }
        if (name.IndexOf("event", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("eventlog", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "event";
        }
        if (name.IndexOf("dns", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "dns";
        }
        if (name.IndexOf("ldap", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("object", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "directory_object";
        }
        if (name.IndexOf("mail", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "message";
        }

        if (string.Equals(category, "active_directory", StringComparison.OrdinalIgnoreCase)) {
            return "directory_object";
        }
        if (string.Equals(category, "eventlog", StringComparison.OrdinalIgnoreCase)) {
            return "event";
        }
        if (string.Equals(category, "system", StringComparison.OrdinalIgnoreCase)) {
            return "host";
        }
        if (string.Equals(category, "powershell", StringComparison.OrdinalIgnoreCase)) {
            return "command";
        }
        if (string.Equals(category, "filesystem", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "officeimo", StringComparison.OrdinalIgnoreCase)) {
            return "file";
        }
        if (string.Equals(category, "email", StringComparison.OrdinalIgnoreCase)) {
            return "message";
        }

        return ToolRoutingTaxonomy.EntityResource;
    }

    private static string InferRisk(ToolDefinition definition, string operation) {
        if (definition.WriteGovernance?.IsWriteCapable == true ||
            string.Equals(operation, "write", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation, "execute_write", StringComparison.OrdinalIgnoreCase)) {
            return "high";
        }

        if (definition.Authentication?.RequiresAuthentication == true ||
            definition.Name.IndexOf("security", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "medium";
        }

        return ToolRoutingTaxonomy.RiskLow;
    }

    private static IReadOnlyList<string> BuildSelectionTags(
        ToolDefinition definition,
        string? category,
        ToolSelectionRoutingInfo routing,
        ExplicitSelectionOverride? explicitOverride) {
        var tags = new List<string>(definition.Tags.Count + 16);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTag(string? value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            var normalized = NormalizeToken(value, fallback: string.Empty);
            if (normalized.Length == 0) {
                return;
            }

            if (seen.Add(normalized)) {
                tags.Add(normalized);
            }
        }

        for (var i = 0; i < definition.Tags.Count; i++) {
            var tag = definition.Tags[i];
            if (ToolRoutingTaxonomy.IsTaxonomyTag(tag)) {
                continue;
            }

            AddTag(tag);
        }

        AddTag(category);
        AddTag($"scope:{routing.Scope}");
        AddTag($"operation:{routing.Operation}");
        AddTag($"entity:{routing.Entity}");
        AddTag($"risk:{routing.Risk}");
        AddTag(
            routing.IsExplicit
                ? $"routing:{ToolRoutingTaxonomy.SourceExplicit}"
                : $"routing:{ToolRoutingTaxonomy.SourceInferred}");

        foreach (var tag in explicitOverride?.Tags ?? Array.Empty<string>()) {
            if (ToolRoutingTaxonomy.IsTaxonomyTag(tag)) {
                continue;
            }

            AddTag(tag);
        }

        if (explicitOverride?.IsHighTraffic == true) {
            AddTag("traffic:high");
        }
        if (explicitOverride?.IsHighRisk == true) {
            AddTag("priority:high_risk");
        }

        if (definition.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase)) {
            AddTag("pack_info");
        }

        if (HasAnyProperty(definition.Parameters, TimeRangeArgumentNames)) {
            AddTag("time_range");
        }
        if (HasAnyProperty(definition.Parameters, TableViewArgumentNames)) {
            AddTag("table_view");
        }
        if (HasAnyProperty(definition.Parameters, PagingArgumentNames)) {
            AddTag("paging");
        }
        if (HasAnyProperty(definition.Parameters, TargetScopeArgumentNames)) {
            AddTag("target_scope");
        }

        if (definition.WriteGovernance?.IsWriteCapable == true) {
            AddTag("write");
            if (definition.WriteGovernance.RequiresGovernanceAuthorization) {
                AddTag("write_governed");
            }
        }

        if (definition.Authentication?.IsAuthenticationAware == true) {
            AddTag("auth");
            if (definition.Authentication.RequiresAuthentication) {
                AddTag("auth_required");
            }
        }

        if (tags.Count == 0) {
            return Array.Empty<string>();
        }

        tags.Sort(StringComparer.OrdinalIgnoreCase);
        return tags.ToArray();
    }

    private static string NormalizeCategory(string? value) {
        var normalized = NormalizeToken(value, fallback: string.Empty);
        return normalized.Length == 0 ? DefaultCategory : normalized;
    }

    private static string NormalizeToken(string? value, string fallback) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return fallback;
        }

        return normalized.ToLowerInvariant();
    }

    private static bool HasAnyProperty(JsonObject? schema, IReadOnlyList<string> propertyNames) {
        if (schema is null || propertyNames is null || propertyNames.Count == 0) {
            return false;
        }

        var properties = schema.GetObject("properties");
        if (properties is null) {
            return false;
        }

        for (var i = 0; i < propertyNames.Count; i++) {
            var name = propertyNames[i];
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            if (properties.GetObject(name) is not null) {
                return true;
            }
        }

        return false;
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (ReferenceEquals(left, right)) {
            return true;
        }
        if (left is null || right is null || left.Count != right.Count) {
            return false;
        }

        for (var i = 0; i < left.Count; i++) {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) {
                return false;
            }
        }

        return true;
    }
}
