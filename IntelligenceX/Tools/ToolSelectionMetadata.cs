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
    /// <summary>
    /// Domain-intent family token for internal Active Directory scope.
    /// </summary>
    public const string DomainIntentFamilyAd = "ad_domain";
    /// <summary>
    /// Domain-intent family token for public DNS/domain scope.
    /// </summary>
    public const string DomainIntentFamilyPublic = "public_domain";
    /// <summary>
    /// Default action id for selecting AD domain scope.
    /// </summary>
    public const string DomainIntentActionIdAd = "act_domain_scope_ad";
    /// <summary>
    /// Default action id for selecting public-domain scope.
    /// </summary>
    public const string DomainIntentActionIdPublic = "act_domain_scope_public";
    private const string DomainIntentFamilyTagPrefix = "domain_family:";
    private const string DomainScopeFamilyTagPrefix = "domain_scope_family:";
    private const string DomainSignalTagPrefix = "domain_signal:";
    private const string DomainSignalsTagPrefix = "domain_signals:";
    private const string PackTagPrefix = "pack:";
    private const string FallbackRequiresSelectionTag = "fallback_requires_selection";
    private const string FallbackRequiresSelectionTaxonomyTag = "fallback:requires_selection";
    private const string FallbackSelectionKeyTagPrefix = "fallback_selection_key:";
    private const string FallbackSelectionKeysTagPrefix = "fallback_selection_keys:";
    private const string FallbackHintKeyTagPrefix = "fallback_hint_key:";
    private const string FallbackHintKeysTagPrefix = "fallback_hint_keys:";
    private static readonly string[] DomainIntentAdDefaultSignalTokens = {
        "dc",
        "ldap",
        "gpo",
        "kerberos",
        "replication",
        "sysvol",
        "netlogon",
        "ntds",
        "forest",
        "trust",
        "adplayground",
        "active_directory",
        "ad_domain",
        DomainIntentActionIdAd
    };
    private static readonly string[] DomainIntentPublicDefaultSignalTokens = {
        "dns",
        "mx",
        "spf",
        "dmarc",
        "dkim",
        "ns",
        "dnssec",
        "caa",
        "whois",
        "mta_sts",
        "bimi",
        "dnsclientx",
        "dns_client_x",
        "domaindetective",
        "domain_detective",
        "public_domain",
        DomainIntentActionIdPublic
    };
    private static readonly HashSet<string> KnownCompoundPackRoutingTokenCompacts = new(StringComparer.OrdinalIgnoreCase) {
        "activedirectory",
        "adplayground",
        "computerx",
        "domaindetective",
        "dnsclientx",
        "eventlog",
        "testimox"
    };

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
            ["computerx"] = "system",
            ["eventlog"] = "eventlog",
            ["system"] = "system",
            ["wsl"] = "system",
            ["email"] = "email",
            ["fs"] = "filesystem",
            ["powershell"] = "powershell",
            ["testimox"] = "testimox",
            ["officeimo"] = "officeimo",
            ["reviewer"] = "reviewer_setup",
            ["dnsclientx"] = "dns",
            ["domaindetective"] = "dns"
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
        "host",
        "target",
        "targets"
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
        var routingContract = BuildRoutingContract(definition, category, routing, tags);

        if (string.Equals(category, definition.Category, StringComparison.Ordinal) &&
            SequenceEqual(definition.Tags, tags) &&
            RoutingContractsEqual(definition.Routing, routingContract)) {
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
            authentication: definition.Authentication,
            routing: routingContract,
            setup: definition.Setup,
            handoff: definition.Handoff,
            recovery: definition.Recovery);
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

    /// <summary>
    /// Tries to resolve an AD/public-domain routing family from normalized tool metadata.
    /// </summary>
    public static bool TryResolveDomainIntentFamily(ToolDefinition definition, out string family) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var routingFamily = (definition.Routing?.DomainIntentFamily ?? string.Empty).Trim();
        if (TryNormalizeDomainIntentFamilyToken(routingFamily, out family)) {
            return true;
        }

        return TryResolveDomainIntentFamily(
            toolName: definition.Name,
            category: definition.Category,
            tags: definition.Tags,
            out family);
    }

    /// <summary>
    /// Tries to resolve a normalized pack identifier from normalized tool metadata.
    /// </summary>
    public static bool TryResolvePackId(ToolDefinition definition, out string packId) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var routingPackId = NormalizeToken(definition.Routing?.PackId, fallback: string.Empty);
        if (routingPackId.Length > 0 && TryNormalizePackId(routingPackId, out packId)) {
            return true;
        }

        return TryResolvePackId(
            toolName: definition.Name,
            category: definition.Category,
            tags: definition.Tags,
            out packId);
    }

    /// <summary>
    /// Tries to resolve a normalized pack identifier from tool identity hints.
    /// </summary>
    public static bool TryResolvePackId(
        string? toolName,
        string? category,
        IReadOnlyList<string>? tags,
        out string packId) {
        _ = toolName;
        _ = category;
        packId = string.Empty;

        return TryResolvePackIdFromTags(tags, out packId);
    }

    /// <summary>
    /// Normalizes a pack identifier into canonical known ids, or compact fallback shape for unknown ids.
    /// </summary>
    public static string NormalizePackId(string? value) {
        return TryNormalizePackId(value, out var packId)
            ? packId
            : string.Empty;
    }

    /// <summary>
    /// Tries to resolve an AD/public-domain routing family from tool identity hints.
    /// </summary>
    public static bool TryResolveDomainIntentFamily(
        string? toolName,
        string? category,
        IReadOnlyList<string>? tags,
        out string family) {
        _ = toolName;
        _ = category;
        family = string.Empty;

        return TryResolveDomainIntentFamilyFromTags(tags, out family);
    }

    /// <summary>
    /// Returns default action id for the specified domain intent family.
    /// </summary>
    public static string GetDefaultDomainIntentActionId(string? family) {
        if (!TryNormalizeDomainIntentFamilyToken(family, out var normalizedFamily)) {
            return DomainIntentActionIdAd;
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)) {
            return DomainIntentActionIdAd;
        }

        if (string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            return DomainIntentActionIdPublic;
        }

        return $"act_domain_scope_{normalizedFamily}";
    }

    /// <summary>
    /// Tries to resolve a domain intent action id from tool metadata.
    /// </summary>
    public static bool TryResolveDomainIntentActionId(ToolDefinition definition, out string actionId) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var explicitActionId = (definition.Routing?.DomainIntentActionId ?? string.Empty).Trim();
        if (explicitActionId.Length > 0) {
            actionId = explicitActionId;
            return true;
        }

        if (TryResolveDomainIntentFamily(definition, out var family)) {
            actionId = GetDefaultDomainIntentActionId(family);
            return true;
        }

        actionId = string.Empty;
        return false;
    }

    /// <summary>
    /// Tries to normalize a domain-intent family token.
    /// </summary>
    public static bool TryNormalizeDomainIntentFamily(string? value, out string family) {
        return TryNormalizeDomainIntentFamilyToken(value, out family);
    }

    /// <summary>
    /// Returns default family-level signal tokens used for domain-intent inference.
    /// </summary>
    public static IReadOnlyList<string> GetDefaultDomainSignalTokens(string? family) {
        if (string.Equals((family ?? string.Empty).Trim(), DomainIntentFamilyAd, StringComparison.OrdinalIgnoreCase)) {
            return DomainIntentAdDefaultSignalTokens;
        }

        if (string.Equals((family ?? string.Empty).Trim(), DomainIntentFamilyPublic, StringComparison.OrdinalIgnoreCase)) {
            return DomainIntentPublicDefaultSignalTokens;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Returns tool-owned domain signal tokens parsed from metadata tags.
    /// </summary>
    public static IReadOnlyList<string> GetDomainSignalTokens(ToolDefinition definition) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        return GetDomainSignalTokens(definition.Tags);
    }

    /// <summary>
    /// Returns domain signal tokens parsed from metadata tags.
    /// </summary>
    public static IReadOnlyList<string> GetDomainSignalTokens(IReadOnlyList<string>? tags) {
        return TryGetDomainSignalTokensFromTags(tags, out var tokens)
            ? tokens
            : Array.Empty<string>();
    }

    /// <summary>
    /// Returns normalized pack-id aliases used for pack matching.
    /// </summary>
    public static IReadOnlyList<string> GetNormalizedPackAliases(string? packId) {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddAlias(string? value) {
            var normalized = NormalizePackCompactId(value);
            if (normalized.Length > 0) {
                aliases.Add(normalized);
            }
        }

        AddAlias(packId);
        switch (NormalizePackCompactId(packId)) {
            case "activedirectory":
                AddAlias("ad");
                AddAlias("adplayground");
                break;
            case "ad":
                AddAlias("active_directory");
                AddAlias("adplayground");
                break;
            case "adplayground":
                AddAlias("active_directory");
                AddAlias("ad");
                break;
            case "system":
                AddAlias("computerx");
                break;
            case "computerx":
                AddAlias("system");
                break;
            case "eventlog":
                AddAlias("event_log");
                break;
            case "domaindetective":
                AddAlias("domain_detective");
                break;
            case "dnsclientx":
                AddAlias("dns_client_x");
                break;
            case "testimox":
                AddAlias("testimo_x");
                break;
        }

        if (aliases.Count == 0) {
            return Array.Empty<string>();
        }

        var list = aliases.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Returns pack-oriented search tokens for planner/routing prompts.
    /// </summary>
    public static IReadOnlyList<string> GetPackSearchTokens(string? packId) {
        var rawPackId = (packId ?? string.Empty).Trim();
        if (rawPackId.Length == 0) {
            return Array.Empty<string>();
        }

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddToken(string? value) {
            var token = (value ?? string.Empty).Trim();
            if (token.Length > 0) {
                tokens.Add(token);
            }
        }

        AddToken(rawPackId);
        foreach (var alias in GetNormalizedPackAliases(rawPackId)) {
            AddToken(alias);
        }

        switch (NormalizePackCompactId(rawPackId)) {
            case "activedirectory":
                AddToken("active_directory");
                AddToken("ad_playground");
                break;
            case "ad":
                AddToken("active_directory");
                AddToken("ad_playground");
                break;
            case "adplayground":
                AddToken("active_directory");
                AddToken("ad_playground");
                break;
            case "system":
                AddToken("computer_x");
                break;
            case "computerx":
                AddToken("computer_x");
                break;
            case "eventlog":
                AddToken("event_log");
                break;
            case "domaindetective":
                AddToken("domain_detective");
                break;
            case "dnsclientx":
                AddToken("dns_client_x");
                break;
            case "testimox":
                AddToken("testimo_x");
                break;
        }

        if (tokens.Count == 0) {
            return Array.Empty<string>();
        }

        var list = tokens.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Indicates whether a compact token maps to a known compound pack identifier.
    /// </summary>
    public static bool IsKnownCompoundPackRoutingCompact(string? compactToken) {
        var normalized = NormalizePackCompactId(compactToken);
        return normalized.Length > 0
               && KnownCompoundPackRoutingTokenCompacts.Contains(normalized);
    }

    /// <summary>
    /// Indicates whether fallback should require selector-like arguments for the tool.
    /// </summary>
    public static bool RequiresSelectionForFallback(ToolDefinition definition) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        if (definition.Routing?.RequiresSelectionForFallback == true) {
            return true;
        }

        if (definition.Routing?.FallbackSelectionKeys is { Count: > 0 }) {
            return true;
        }

        if (TryGetFallbackSelectionKeysFromTags(definition.Tags, out var taggedKeys) && taggedKeys.Count > 0) {
            return true;
        }

        return HasFallbackRequiresSelectionTag(definition.Tags);
    }

    /// <summary>
    /// Returns selector argument names used to gate fallback execution for the tool.
    /// </summary>
    public static IReadOnlyList<string> GetFallbackSelectionKeys(ToolDefinition definition) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        if (definition.Routing?.FallbackSelectionKeys is { Count: > 0 } routingKeys) {
            return routingKeys;
        }

        if (TryGetFallbackSelectionKeysFromTags(definition.Tags, out var taggedKeys) && taggedKeys.Count > 0) {
            return taggedKeys;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Returns hint argument names used to seed fallback execution arguments for the tool.
    /// </summary>
    public static IReadOnlyList<string> GetFallbackHintKeys(ToolDefinition definition) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        if (definition.Routing?.FallbackHintKeys is { Count: > 0 } routingKeys) {
            return routingKeys;
        }

        if (TryGetFallbackHintKeysFromTags(definition.Tags, out var taggedKeys) && taggedKeys.Count > 0) {
            return taggedKeys;
        }

        return Array.Empty<string>();
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
        var normalized = toolName?.Trim() ?? string.Empty;
        if (normalized.Length > 0) {
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
        if (ns.IndexOf(".DnsClientX", StringComparison.OrdinalIgnoreCase) >= 0
            || ns.IndexOf(".DomainDetective", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "dns";
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
        if (string.Equals(category, "dns", StringComparison.OrdinalIgnoreCase)) {
            return "domain";
        }

        if (string.Equals(category, "reviewer_setup", StringComparison.OrdinalIgnoreCase)) {
            return "repository";
        }

        return ToolRoutingTaxonomy.ScopeGeneral;
    }

    private static bool TryResolveDomainIntentFamilyFromTags(IReadOnlyList<string>? tags, out string family) {
        family = string.Empty;
        if (tags is null || tags.Count == 0) {
            return false;
        }

        for (var i = 0; i < tags.Count; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0) {
                continue;
            }

            if (string.Equals(tag, DomainIntentFamilyAd, StringComparison.OrdinalIgnoreCase)) {
                family = DomainIntentFamilyAd;
                return true;
            }

            if (string.Equals(tag, DomainIntentFamilyPublic, StringComparison.OrdinalIgnoreCase)) {
                family = DomainIntentFamilyPublic;
                return true;
            }

            if (TryParseDomainIntentFamilyTagValue(tag, DomainIntentFamilyTagPrefix, out family)
                || TryParseDomainIntentFamilyTagValue(tag, DomainScopeFamilyTagPrefix, out family)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDomainIntentFamilyTagValue(string tag, string prefix, out string family) {
        family = string.Empty;
        if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var value = tag.Length > prefix.Length ? tag.Substring(prefix.Length).Trim() : string.Empty;
        return TryNormalizeDomainIntentFamilyToken(value, out family);
    }

    private static bool TryNormalizeDomainIntentFamilyToken(string? value, out string family) {
        family = string.Empty;
        var normalized = NormalizeToken(value, fallback: string.Empty);
        if (normalized.Length == 0) {
            return false;
        }

        if (string.Equals(normalized, DomainIntentFamilyAd, StringComparison.Ordinal)) {
            family = DomainIntentFamilyAd;
            return true;
        }

        if (string.Equals(normalized, DomainIntentFamilyPublic, StringComparison.Ordinal)) {
            family = DomainIntentFamilyPublic;
            return true;
        }

        if (!IsValidCustomDomainIntentFamilyToken(normalized)) {
            return false;
        }

        family = normalized;
        return true;
    }

    private static bool IsValidCustomDomainIntentFamilyToken(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length is < 3 or > 64) {
            return false;
        }

        if (normalized.StartsWith("_", StringComparison.Ordinal)
            || normalized.EndsWith("_", StringComparison.Ordinal)) {
            return false;
        }

        var previousUnderscore = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch == '_') {
                if (previousUnderscore) {
                    return false;
                }

                previousUnderscore = true;
                continue;
            }

            previousUnderscore = false;
            if (!char.IsLetterOrDigit(ch)) {
                return false;
            }
        }

        return true;
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
        if (name.EndsWith("_ping", StringComparison.OrdinalIgnoreCase) || name.IndexOf("_ping_", StringComparison.OrdinalIgnoreCase) >= 0) {
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
        if (string.Equals(category, "dns", StringComparison.OrdinalIgnoreCase)
            && (name.IndexOf("ping", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("probe", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("traceroute", StringComparison.OrdinalIgnoreCase) >= 0)) {
            return "host";
        }

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
        if (string.Equals(category, "dns", StringComparison.OrdinalIgnoreCase)) {
            return "dns";
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
        if (TryResolvePackId(definition, out var packId)) {
            AddTag($"{PackTagPrefix}{packId}");
        }
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

    private static ToolRoutingContract BuildRoutingContract(
        ToolDefinition definition,
        string category,
        ToolSelectionRoutingInfo routing,
        IReadOnlyList<string> enrichedTags) {
        var existing = definition.Routing;
        var normalizedExistingSource = NormalizeToken(existing?.RoutingSource, fallback: string.Empty);
        var hasExplicitExistingSource = string.Equals(
            normalizedExistingSource,
            ToolRoutingTaxonomy.SourceExplicit,
            StringComparison.OrdinalIgnoreCase);

        var packId = NormalizeToken(existing?.PackId, fallback: string.Empty);
        if (packId.Length == 0) {
            if (!hasExplicitExistingSource) {
                TryResolvePackId(definition.Name, category, enrichedTags, out packId);
            }
        } else {
            TryNormalizePackId(packId, out packId);
        }

        var role = hasExplicitExistingSource
            ? NormalizeToken(existing?.Role, fallback: string.Empty)
            : ResolveRoutingRole(
                toolName: definition.Name,
                existingRole: existing?.Role,
                tags: enrichedTags);
        var source = ResolveRoutingSource(
            existingSource: existing?.RoutingSource,
            tags: enrichedTags,
            routing: routing);

        var family = NormalizeToken(existing?.DomainIntentFamily, fallback: string.Empty);
        if (!TryNormalizeDomainIntentFamilyToken(family, out family)) {
            if (!hasExplicitExistingSource) {
                TryResolveDomainIntentFamily(definition.Name, category, enrichedTags, out family);
            }
        }

        var actionId = (existing?.DomainIntentActionId ?? string.Empty).Trim();
        if (actionId.Length == 0 && family.Length > 0) {
            actionId = GetDefaultDomainIntentActionId(family);
        }

        var requiresSelection = existing?.RequiresSelectionForFallback == true
                                || HasFallbackRequiresSelectionTag(enrichedTags);
        var fallbackSelectionKeys = NormalizeTokenList(
            existing?.FallbackSelectionKeys,
            fallbackWhenEmpty: TryGetFallbackSelectionKeysFromTags(enrichedTags, out var taggedSelectionKeys)
                ? taggedSelectionKeys
                : Array.Empty<string>());
        if (fallbackSelectionKeys.Count > 0) {
            requiresSelection = true;
        }

        var fallbackHintKeys = NormalizeTokenList(
            existing?.FallbackHintKeys,
            fallbackWhenEmpty: TryGetFallbackHintKeysFromTags(enrichedTags, out var taggedHintKeys)
                ? taggedHintKeys
                : Array.Empty<string>());

        var domainSignals = NormalizeTokenList(
            existing?.DomainSignalTokens,
            fallbackWhenEmpty: GetDomainSignalTokens(enrichedTags));
        if (family.Length > 0) {
            var mergedSignals = new HashSet<string>(domainSignals, StringComparer.OrdinalIgnoreCase);
            AddTokenRange(mergedSignals, GetDefaultDomainSignalTokens(family));
            domainSignals = ToSortedTokenArray(mergedSignals);
        }

        return new ToolRoutingContract {
            IsRoutingAware = existing?.IsRoutingAware ?? true,
            RoutingContractId = string.IsNullOrWhiteSpace(existing?.RoutingContractId)
                ? ToolRoutingContract.DefaultContractId
                : existing!.RoutingContractId.Trim(),
            RoutingSource = source,
            PackId = packId,
            Role = role,
            DomainIntentFamily = family,
            DomainIntentActionId = actionId,
            DomainSignalTokens = domainSignals,
            RequiresSelectionForFallback = requiresSelection,
            FallbackSelectionKeys = fallbackSelectionKeys,
            FallbackHintKeys = fallbackHintKeys
        };
    }

    private static bool RoutingContractsEqual(ToolRoutingContract? left, ToolRoutingContract? right) {
        if (ReferenceEquals(left, right)) {
            return true;
        }

        if (left is null || right is null) {
            return false;
        }

        return left.IsRoutingAware == right.IsRoutingAware
               && string.Equals(left.RoutingContractId, right.RoutingContractId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.RoutingSource, right.RoutingSource, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.PackId, right.PackId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Role, right.Role, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.DomainIntentFamily, right.DomainIntentFamily, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.DomainIntentActionId, right.DomainIntentActionId, StringComparison.OrdinalIgnoreCase)
               && SequenceEqual(left.DomainSignalTokens, right.DomainSignalTokens)
               && left.RequiresSelectionForFallback == right.RequiresSelectionForFallback
               && SequenceEqual(left.FallbackSelectionKeys, right.FallbackSelectionKeys)
               && SequenceEqual(left.FallbackHintKeys, right.FallbackHintKeys);
    }

    private static string ResolveRoutingRole(string toolName, string? existingRole, IReadOnlyList<string> tags) {
        var normalizedExistingRole = NormalizeToken(existingRole, fallback: string.Empty);
        if (normalizedExistingRole.Length > 0 && ToolRoutingTaxonomy.IsAllowedRole(normalizedExistingRole)) {
            return normalizedExistingRole;
        }

        if (ContainsTag(tags, "pack_info")) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        var normalizedName = (toolName ?? string.Empty).Trim();
        if (normalizedName.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (normalizedName.EndsWith("_environment_discover", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleEnvironmentDiscover;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }

    private static string ResolveRoutingSource(
        string? existingSource,
        IReadOnlyList<string> tags,
        ToolSelectionRoutingInfo routing) {
        var normalizedExistingSource = NormalizeToken(existingSource, fallback: string.Empty);
        if (normalizedExistingSource.Length > 0 && ToolRoutingTaxonomy.IsAllowedSource(normalizedExistingSource)) {
            return normalizedExistingSource;
        }

        if (ContainsTag(tags, $"{ToolRoutingTaxonomy.RoutingTagPrefix}{ToolRoutingTaxonomy.SourceExplicit}")) {
            return ToolRoutingTaxonomy.SourceExplicit;
        }

        if (ContainsTag(tags, $"{ToolRoutingTaxonomy.RoutingTagPrefix}{ToolRoutingTaxonomy.SourceInferred}")) {
            return ToolRoutingTaxonomy.SourceInferred;
        }

        return routing.IsExplicit
            ? ToolRoutingTaxonomy.SourceExplicit
            : ToolRoutingTaxonomy.SourceInferred;
    }

    private static bool ContainsTag(IReadOnlyList<string>? tags, string expectedTag) {
        if (tags is null || tags.Count == 0 || string.IsNullOrWhiteSpace(expectedTag)) {
            return false;
        }

        var normalizedExpectedTag = expectedTag.Trim();
        for (var i = 0; i < tags.Count; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0) {
                continue;
            }

            if (string.Equals(tag, normalizedExpectedTag, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeCategory(string? value) {
        var normalized = NormalizeToken(value, fallback: string.Empty);
        return normalized.Length == 0 ? DefaultCategory : normalized;
    }

    private static string NormalizePackCompactId(string? value) {
        return NormalizeCompactToken(value);
    }

    private static bool HasFallbackRequiresSelectionTag(IReadOnlyList<string>? tags) {
        if (tags is null || tags.Count == 0) {
            return false;
        }

        for (var i = 0; i < tags.Count; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0) {
                continue;
            }

            if (string.Equals(tag, FallbackRequiresSelectionTag, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, FallbackRequiresSelectionTaxonomyTag, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDomainSignalTokensFromTags(IReadOnlyList<string>? tags, out IReadOnlyList<string> tokens) {
        tokens = Array.Empty<string>();
        if (tags is null || tags.Count == 0) {
            return false;
        }

        var collected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tags.Count; i++) {
            var rawTag = (tags[i] ?? string.Empty).Trim();
            if (rawTag.Length == 0) {
                continue;
            }

            if (rawTag.StartsWith(DomainSignalTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                var single = rawTag.Length > DomainSignalTagPrefix.Length
                    ? rawTag.Substring(DomainSignalTagPrefix.Length).Trim()
                    : string.Empty;
                AddDomainSignalToken(single, collected, seen);
                continue;
            }

            if (!rawTag.StartsWith(DomainSignalsTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var value = rawTag.Length > DomainSignalsTagPrefix.Length
                ? rawTag.Substring(DomainSignalsTagPrefix.Length).Trim()
                : string.Empty;
            if (value.Length == 0) {
                continue;
            }

            var split = value.Split(',');
            for (var j = 0; j < split.Length; j++) {
                AddDomainSignalToken(split[j], collected, seen);
            }
        }

        if (collected.Count == 0) {
            return false;
        }

        collected.Sort(StringComparer.OrdinalIgnoreCase);
        tokens = collected.ToArray();
        return true;
    }

    private static bool TryGetFallbackSelectionKeysFromTags(IReadOnlyList<string>? tags, out IReadOnlyList<string> keys) {
        keys = Array.Empty<string>();
        if (tags is null || tags.Count == 0) {
            return false;
        }

        var collected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tags.Count; i++) {
            var rawTag = (tags[i] ?? string.Empty).Trim();
            if (rawTag.Length == 0) {
                continue;
            }

            if (rawTag.StartsWith(FallbackSelectionKeyTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                var single = rawTag.Length > FallbackSelectionKeyTagPrefix.Length
                    ? rawTag.Substring(FallbackSelectionKeyTagPrefix.Length).Trim()
                    : string.Empty;
                AddFallbackSelectionKey(single, collected, seen);
                continue;
            }

            if (!rawTag.StartsWith(FallbackSelectionKeysTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var value = rawTag.Length > FallbackSelectionKeysTagPrefix.Length
                ? rawTag.Substring(FallbackSelectionKeysTagPrefix.Length).Trim()
                : string.Empty;
            if (value.Length == 0) {
                continue;
            }

            var split = value.Split(',');
            for (var j = 0; j < split.Length; j++) {
                AddFallbackSelectionKey(split[j], collected, seen);
            }
        }

        if (collected.Count == 0) {
            return false;
        }

        collected.Sort(StringComparer.OrdinalIgnoreCase);
        keys = collected.ToArray();
        return true;
    }

    private static bool TryGetFallbackHintKeysFromTags(IReadOnlyList<string>? tags, out IReadOnlyList<string> keys) {
        keys = Array.Empty<string>();
        if (tags is null || tags.Count == 0) {
            return false;
        }

        var collected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tags.Count; i++) {
            var rawTag = (tags[i] ?? string.Empty).Trim();
            if (rawTag.Length == 0) {
                continue;
            }

            if (rawTag.StartsWith(FallbackHintKeyTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                var single = rawTag.Length > FallbackHintKeyTagPrefix.Length
                    ? rawTag.Substring(FallbackHintKeyTagPrefix.Length).Trim()
                    : string.Empty;
                AddFallbackSelectionKey(single, collected, seen);
                continue;
            }

            if (!rawTag.StartsWith(FallbackHintKeysTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var value = rawTag.Length > FallbackHintKeysTagPrefix.Length
                ? rawTag.Substring(FallbackHintKeysTagPrefix.Length).Trim()
                : string.Empty;
            if (value.Length == 0) {
                continue;
            }

            var split = value.Split(',');
            for (var j = 0; j < split.Length; j++) {
                AddFallbackSelectionKey(split[j], collected, seen);
            }
        }

        if (collected.Count == 0) {
            return false;
        }

        collected.Sort(StringComparer.OrdinalIgnoreCase);
        keys = collected.ToArray();
        return true;
    }

    private static void AddFallbackSelectionKey(string? candidate, List<string> keys, HashSet<string> seen) {
        var key = (candidate ?? string.Empty).Trim();
        if (key.Length == 0) {
            return;
        }

        if (seen.Add(key)) {
            keys.Add(key);
        }
    }

    private static void AddDomainSignalToken(string? candidate, List<string> tokens, HashSet<string> seen) {
        var normalized = NormalizeSignalToken(candidate);
        if (normalized.Length == 0) {
            return;
        }

        if (seen.Add(normalized)) {
            tokens.Add(normalized);
        }
    }

    private static IReadOnlyList<string> NormalizeTokenList(
        IReadOnlyList<string>? values,
        IReadOnlyList<string>? fallbackWhenEmpty = null) {
        var source = values is { Count: > 0 } ? values : fallbackWhenEmpty;
        if (source is null || source.Count == 0) {
            return Array.Empty<string>();
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < source.Count; i++) {
            var candidate = (source[i] ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                continue;
            }

            normalized.Add(candidate);
        }

        return ToSortedTokenArray(normalized);
    }

    private static void AddTokenRange(HashSet<string> destination, IReadOnlyList<string>? values) {
        if (destination is null || values is null || values.Count == 0) {
            return;
        }

        for (var i = 0; i < values.Count; i++) {
            var candidate = (values[i] ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                continue;
            }

            destination.Add(candidate);
        }
    }

    private static IReadOnlyList<string> ToSortedTokenArray(HashSet<string> values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<string>();
        }

        var array = values.ToArray();
        Array.Sort(array, StringComparer.OrdinalIgnoreCase);
        return array;
    }

    private static string NormalizeSignalToken(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var buffer = new char[normalized.Length];
        var length = 0;
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                buffer[length++] = char.ToLowerInvariant(ch);
                previousWasSeparator = false;
                continue;
            }

            if (ch is '_' or '-') {
                if (length > 0 && !previousWasSeparator) {
                    buffer[length++] = '_';
                    previousWasSeparator = true;
                }
            }
        }

        while (length > 0 && buffer[length - 1] == '_') {
            length--;
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static string NormalizeToken(string? value, string fallback) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return fallback;
        }

        return normalized.ToLowerInvariant();
    }

    private static string NormalizeCompactToken(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        var normalizedValue = value!;
        var buffer = new char[normalizedValue.Length];
        var length = 0;
        for (var i = 0; i < normalizedValue.Length; i++) {
            var ch = normalizedValue[i];
            if (!char.IsLetterOrDigit(ch)) {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(ch);
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static bool TryResolvePackIdFromTags(IReadOnlyList<string>? tags, out string packId) {
        packId = string.Empty;
        if (tags is null || tags.Count == 0) {
            return false;
        }

        for (var i = 0; i < tags.Count; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (!tag.StartsWith(PackTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var candidate = tag.Length > PackTagPrefix.Length
                ? tag.Substring(PackTagPrefix.Length).Trim()
                : string.Empty;
            if (TryNormalizePackId(candidate, out packId)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizePackId(string? value, out string packId) {
        packId = string.Empty;
        var normalized = NormalizePackToken(value);
        if (normalized.Length == 0) {
            return false;
        }

        var compact = NormalizeCompactToken(normalized);
        if (compact.Length == 0) {
            return false;
        }

        packId = compact switch {
            "ad" => "active_directory",
            "activedirectory" => "active_directory",
            "adplayground" => "active_directory",
            "eventlog" => "eventlog",
            "eventlogs" => "eventlog",
            "system" => "system",
            "computerx" => "system",
            "wsl" => "system",
            "filesystem" => "filesystem",
            "fs" => "filesystem",
            "email" => "email",
            "powershell" => "powershell",
            "testimox" => "testimox",
            "testimoxpack" => "testimox",
            "officeimo" => "officeimo",
            "reviewersetup" => "reviewer_setup",
            "dnsclientx" => "dnsclientx",
            "domaindetective" => "domaindetective",
            _ => normalized
        };

        return true;
    }

    private static string NormalizePackToken(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var buffer = new char[normalized.Length];
        var length = 0;
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                buffer[length++] = char.ToLowerInvariant(ch);
                previousWasSeparator = false;
                continue;
            }

            if (ch is '_' or '-' || char.IsWhiteSpace(ch)) {
                if (length > 0 && !previousWasSeparator) {
                    buffer[length++] = '_';
                    previousWasSeparator = true;
                }
            }
        }

        while (length > 0 && buffer[length - 1] == '_') {
            length--;
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
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
