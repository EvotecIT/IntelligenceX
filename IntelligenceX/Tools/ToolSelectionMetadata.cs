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
        "eventlog",
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

    /// <summary>
    /// Returns the same definition with inferred category/tags when missing.
    /// </summary>
    public static ToolDefinition Enrich(ToolDefinition definition, Type? toolType = null) {
        if (definition is null) {
            throw new ArgumentNullException(nameof(definition));
        }

        var explicitOverride = GetExplicitOverride(definition);
        var category = NormalizeCategory(string.IsNullOrWhiteSpace(definition.Category)
            ? (explicitOverride?.Category ?? InferCategory(definition, toolType))
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
            recovery: definition.Recovery,
            execution: definition.Execution);
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
            ? (explicitOverride?.Category ?? InferCategory(definition, toolType))
            : definition.Category);
        return ResolveRouting(definition, category, explicitOverride);
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
        return ToolPackIdentityCatalog.GetNormalizedPackAliases(packId);
    }

    /// <summary>
    /// Returns pack-oriented search tokens for planner/routing prompts.
    /// </summary>
    public static IReadOnlyList<string> GetPackSearchTokens(string? packId) {
        return ToolPackIdentityCatalog.GetPackSearchTokens(packId);
    }

    /// <summary>
    /// Indicates whether a compact token maps to a known compound pack identifier.
    /// </summary>
    public static bool IsKnownCompoundPackRoutingCompact(string? compactToken) {
        return ToolPackIdentityCatalog.IsKnownCompoundPackRoutingCompact(compactToken);
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

        return false;
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

        return Array.Empty<string>();
    }

    private static ToolSelectionRoutingInfo ResolveRouting(
        ToolDefinition definition,
        string? category,
        ExplicitSelectionOverride? explicitOverride) {
        var operation = string.IsNullOrWhiteSpace(explicitOverride?.Operation)
            ? ToolNameRoutingSemantics.InferOperation(definition.Name, definition.WriteGovernance?.IsWriteCapable == true)
            : explicitOverride!.Operation!;

        var scope = string.IsNullOrWhiteSpace(explicitOverride?.Scope)
            ? InferScope(definition, category, operation)
            : explicitOverride!.Scope!;

        var entity = string.IsNullOrWhiteSpace(explicitOverride?.Entity)
            ? ToolNameRoutingSemantics.InferEntity(definition.Name, category)
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

        if (TryGetRoutingContractSelectionOverride(definition.Routing, out var routingOverride)) {
            return routingOverride;
        }

        if (TryGetToolOwnedSelectionOverride(definition.Tags, out var taggedOverride)) {
            return taggedOverride;
        }

        return null;
    }

    private static bool TryGetRoutingContractSelectionOverride(
        ToolRoutingContract? routing,
        out ExplicitSelectionOverride explicitOverride) {
        explicitOverride = null!;
        if (routing is null || !routing.IsRoutingAware) {
            return false;
        }

        if (!string.Equals(
                NormalizeToken(routing.RoutingSource, fallback: string.Empty),
                ToolRoutingTaxonomy.SourceExplicit,
                StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var scope = NormalizeToken(routing.Scope, fallback: string.Empty);
        var operation = NormalizeToken(routing.Operation, fallback: string.Empty);
        var entity = NormalizeToken(routing.Entity, fallback: string.Empty);
        var risk = NormalizeRoutingToken(routing.Risk, ToolRoutingTaxonomy.IsAllowedRisk);
        if (scope.Length == 0 && operation.Length == 0 && entity.Length == 0 && risk.Length == 0) {
            return false;
        }

        explicitOverride = new ExplicitSelectionOverride(
            category: null,
            scope: scope.Length > 0 ? scope : null,
            operation: operation.Length > 0 ? operation : null,
            entity: entity.Length > 0 ? entity : null,
            risk: risk.Length > 0 ? risk : null,
            tags: Array.Empty<string>(),
            isHighTraffic: false,
            isHighRisk: string.Equals(risk, ToolRoutingTaxonomy.RiskHigh, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static bool TryGetToolOwnedSelectionOverride(IReadOnlyList<string>? tags, out ExplicitSelectionOverride explicitOverride) {
        explicitOverride = null!;

        var hasScope = ToolSelectionHintTags.TryGetScope(tags, out var scope);
        var hasOperation = ToolSelectionHintTags.TryGetOperation(tags, out var operation);
        var hasEntity = ToolSelectionHintTags.TryGetEntity(tags, out var entity);
        var hasRisk = ToolSelectionHintTags.TryGetRisk(tags, out var risk);

        if (!hasScope && !hasOperation && !hasEntity && !hasRisk) {
            return false;
        }

        explicitOverride = new ExplicitSelectionOverride(
            category: null,
            scope: hasScope ? scope : null,
            operation: hasOperation ? operation : null,
            entity: hasEntity ? entity : null,
            risk: hasRisk ? risk : null,
            tags: Array.Empty<string>(),
            isHighTraffic: false,
            isHighRisk: false);
        return true;
    }

    private static string? InferCategory(ToolDefinition definition, Type? toolType) {
        if (definition is null) {
            return null;
        }

        if (TryResolvePackId(definition, out var packId) && TryMapPackIdToCategory(packId, out var packCategory)) {
            return packCategory;
        }

        if (ToolPackIdentityCatalog.TryResolveCategoryFromToolName(definition.Name, out var toolNameCategory)) {
            return toolNameCategory;
        }

        if (ToolPackIdentityCatalog.TryResolveCategoryFromRuntimeNamespace(toolType?.Namespace, out var runtimeCategory)) {
            return runtimeCategory;
        }

        return null;
    }

    private static bool TryMapPackIdToCategory(string? packId, out string category) {
        return ToolPackIdentityCatalog.TryGetCategory(packId, out category);
    }

    private static string InferScope(ToolDefinition definition, string? category, string operation) {
        if (string.Equals(operation, "guide", StringComparison.OrdinalIgnoreCase)) {
            return "pack";
        }

        if (HasAnyProperty(definition.Parameters, ToolScopeArgumentNames.DomainScopeArguments)) {
            return "domain";
        }

        if (HasAnyProperty(definition.Parameters, ToolScopeArgumentNames.FileScopeArguments)) {
            return "file";
        }

        if (HasAnyProperty(definition.Parameters, ToolScopeArgumentNames.HostScopeArguments)) {
            return "host";
        }

        if (ToolCategoryRoutingSemantics.TryGetDefaultScope(category, out var defaultScope)) {
            return defaultScope;
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
            if (ToolRoutingTaxonomy.IsTaxonomyTag(tag) || ToolSelectionHintTags.IsControlTag(tag)) {
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
        if (HasAnyProperty(definition.Parameters, ToolScopeArgumentNames.TargetScopeArguments)
            || HasAnyProperty(definition.Parameters, ToolScopeArgumentNames.HostTargetInputArguments)) {
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

        var requiresSelection = existing?.RequiresSelectionForFallback == true;
        var fallbackSelectionKeys = NormalizeTokenList(
            existing?.FallbackSelectionKeys,
            fallbackWhenEmpty: Array.Empty<string>());
        if (fallbackSelectionKeys.Count > 0) {
            requiresSelection = true;
        }

        var fallbackHintKeys = NormalizeTokenList(
            existing?.FallbackHintKeys,
            fallbackWhenEmpty: Array.Empty<string>());

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
            Scope = NormalizeToken(existing?.Scope, routing.Scope),
            Operation = NormalizeToken(existing?.Operation, routing.Operation),
            Entity = NormalizeToken(existing?.Entity, routing.Entity),
            Risk = NormalizeRoutingToken(existing?.Risk, ToolRoutingTaxonomy.IsAllowedRisk, routing.Risk),
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
               && string.Equals(left.Scope, right.Scope, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Operation, right.Operation, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Entity, right.Entity, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Risk, right.Risk, StringComparison.OrdinalIgnoreCase)
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

    private static string NormalizeRoutingToken(
        string? value,
        Func<string?, bool> validator,
        string fallback = "") {
        var normalized = NormalizeToken(value, fallback: string.Empty);
        if (normalized.Length == 0) {
            return fallback;
        }

        return validator(normalized) ? normalized : fallback;
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
        packId = ToolPackIdentityCatalog.NormalizePackId(value);
        return packId.Length > 0;
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
