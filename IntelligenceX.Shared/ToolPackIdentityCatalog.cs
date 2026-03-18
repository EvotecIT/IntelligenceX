using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared pack identity catalog used to normalize pack ids, aliases, categories, and display labels.
/// </summary>
public static class ToolPackIdentityCatalog {
    private sealed class PackIdentityDescriptor {
        public PackIdentityDescriptor(
            string canonicalPackId,
            string displayName,
            string category,
            IReadOnlyList<string>? aliases = null,
            IReadOnlyList<string>? searchTokens = null,
            IReadOnlyList<string>? toolNamePrefixes = null,
            IReadOnlyList<string>? runtimeNamespaceMarkers = null) {
            CanonicalPackId = canonicalPackId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Category = category ?? string.Empty;
            Aliases = aliases ?? Array.Empty<string>();
            SearchTokens = searchTokens ?? Array.Empty<string>();
            ToolNamePrefixes = toolNamePrefixes ?? Array.Empty<string>();
            RuntimeNamespaceMarkers = runtimeNamespaceMarkers ?? Array.Empty<string>();
        }

        public string CanonicalPackId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public IReadOnlyList<string> Aliases { get; }
        public IReadOnlyList<string> SearchTokens { get; }
        public IReadOnlyList<string> ToolNamePrefixes { get; }
        public IReadOnlyList<string> RuntimeNamespaceMarkers { get; }
    }

    private static readonly IReadOnlyList<PackIdentityDescriptor> KnownPackIdentityDescriptors = new[] {
        new PackIdentityDescriptor(
            canonicalPackId: "active_directory",
            displayName: "Active Directory",
            category: "active_directory",
            aliases: new[] { "ad", "adplayground" },
            searchTokens: new[] { "active_directory", "ad_playground" },
            toolNamePrefixes: new[] { "ad" },
            runtimeNamespaceMarkers: new[] { ".ADPlayground" }),
        new PackIdentityDescriptor(
            canonicalPackId: "active_directory_lifecycle",
            displayName: "AD Lifecycle",
            category: "active_directory",
            aliases: new[] { "ad_lifecycle", "adlifecycle", "joiner_leaver", "adplayground_lifecycle" },
            searchTokens: new[] { "identity_lifecycle", "joiner_leaver", "governed_write" }),
        new PackIdentityDescriptor(
            canonicalPackId: "eventlog",
            displayName: "Event Log",
            category: "eventlog",
            aliases: new[] { "event_log", "eventlogs", "eventviewerx" },
            searchTokens: new[] { "eventlog", "event_log", "eventviewerx", "eventviewer_x" },
            toolNamePrefixes: new[] { "eventlog" },
            runtimeNamespaceMarkers: new[] { ".EventLog" }),
        new PackIdentityDescriptor(
            canonicalPackId: "system",
            displayName: "System",
            category: "system",
            aliases: new[] { "computerx", "wsl" },
            searchTokens: new[] { "computer_x" },
            toolNamePrefixes: new[] { "computerx", "system", "wsl" },
            runtimeNamespaceMarkers: new[] { ".System" }),
        new PackIdentityDescriptor(
            canonicalPackId: "filesystem",
            displayName: "Filesystem",
            category: "filesystem",
            aliases: new[] { "fs" },
            toolNamePrefixes: new[] { "fs" },
            runtimeNamespaceMarkers: new[] { ".FileSystem" }),
        new PackIdentityDescriptor(
            canonicalPackId: "email",
            displayName: "Email",
            category: "email",
            aliases: new[] { "mailozaurr" },
            toolNamePrefixes: new[] { "email" },
            runtimeNamespaceMarkers: new[] { ".Email" }),
        new PackIdentityDescriptor(
            canonicalPackId: "powershell",
            displayName: "PowerShell",
            category: "powershell",
            aliases: new[] { "powershell_runtime" },
            toolNamePrefixes: new[] { "powershell" },
            runtimeNamespaceMarkers: new[] { ".PowerShell" }),
        new PackIdentityDescriptor(
            canonicalPackId: "testimox",
            displayName: "TestimoX",
            category: "testimox",
            aliases: new[] { "testimoxpack" },
            searchTokens: new[] { "testimo_x" },
            toolNamePrefixes: new[] { "testimox" },
            runtimeNamespaceMarkers: new[] { ".TestimoX" }),
        new PackIdentityDescriptor(
            canonicalPackId: "testimox_analytics",
            displayName: "TestimoX Analytics",
            category: "testimox",
            searchTokens: new[] { "testimox_analytics", "testimox analytics" }),
        new PackIdentityDescriptor(
            canonicalPackId: "officeimo",
            displayName: "OfficeIMO",
            category: "officeimo",
            toolNamePrefixes: new[] { "officeimo" },
            runtimeNamespaceMarkers: new[] { ".OfficeIMO" }),
        new PackIdentityDescriptor(
            canonicalPackId: "reviewer_setup",
            displayName: "Reviewer Setup",
            category: "reviewer_setup",
            aliases: new[] { "reviewersetup" },
            toolNamePrefixes: new[] { "reviewer" },
            runtimeNamespaceMarkers: new[] { ".ReviewerSetup" }),
        new PackIdentityDescriptor(
            canonicalPackId: "dnsclientx",
            displayName: "DnsClientX",
            category: "dns",
            aliases: new[] { "dns_client_x" },
            toolNamePrefixes: new[] { "dnsclientx" },
            runtimeNamespaceMarkers: new[] { ".DnsClientX" }),
        new PackIdentityDescriptor(
            canonicalPackId: "domaindetective",
            displayName: "DomainDetective",
            category: "dns",
            aliases: new[] { "domain_detective" },
            toolNamePrefixes: new[] { "domaindetective" },
            runtimeNamespaceMarkers: new[] { ".DomainDetective" })
    };

    private static readonly IReadOnlyDictionary<string, PackIdentityDescriptor> KnownPackIdentitiesByCompactToken =
        BuildKnownPackIdentityMap();
    private static readonly IReadOnlyDictionary<string, PackIdentityDescriptor> KnownPackIdentitiesByToolNamePrefix =
        BuildKnownPackIdentityPrefixMap();
    private static readonly HashSet<string> KnownCompoundPackRoutingTokenCompacts =
        BuildKnownCompoundPackRoutingTokenCompacts();

    /// <summary>
    /// Normalizes a pack id or engine alias into its canonical pack id.
    /// </summary>
    public static string NormalizePackId(string? value) {
        var normalized = NormalizePackToken(value);
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var compact = NormalizeCompactToken(normalized);
        if (compact.Length == 0) {
            return string.Empty;
        }

        return TryGetKnownPackIdentityByCompact(compact, out var identity)
            ? identity.CanonicalPackId
            : normalized;
    }

    /// <summary>
    /// Returns the category associated with the pack identity when known.
    /// </summary>
    public static bool TryGetCategory(string? packId, out string category) {
        category = string.Empty;
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        category = TryGetKnownPackIdentity(normalizedPackId, out var identity)
            ? identity.Category
            : normalizedPackId;
        return category.Length > 0;
    }

    /// <summary>
    /// Returns normalized compact aliases used for pack matching.
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
        if (TryGetKnownPackIdentity(packId, out var identity)) {
            AddAlias(identity.CanonicalPackId);
            for (var i = 0; i < identity.Aliases.Count; i++) {
                AddAlias(identity.Aliases[i]);
            }
        }

        if (aliases.Count == 0) {
            return Array.Empty<string>();
        }

        var list = aliases.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Returns pack-oriented search tokens used by planner and routing prompts.
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

        if (TryGetKnownPackIdentity(rawPackId, out var identity)) {
            AddToken(identity.CanonicalPackId);
            for (var i = 0; i < identity.Aliases.Count; i++) {
                AddToken(identity.Aliases[i]);
            }
            for (var i = 0; i < identity.SearchTokens.Count; i++) {
                AddToken(identity.SearchTokens[i]);
            }
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
    /// Resolves a category from a tool-name prefix when it matches a known pack identity.
    /// </summary>
    public static bool TryResolveCategoryFromToolName(string? toolName, out string category) {
        category = string.Empty;
        var prefix = ExtractToolNamePrefix(toolName);
        if (prefix.Length == 0
            || !KnownPackIdentitiesByToolNamePrefix.TryGetValue(prefix, out var identity)
            || identity.Category.Length == 0) {
            return false;
        }

        category = identity.Category;
        return true;
    }

    /// <summary>
    /// Resolves a category from a runtime namespace marker when it matches a known pack identity.
    /// </summary>
    public static bool TryResolveCategoryFromRuntimeNamespace(string? runtimeNamespace, out string category) {
        category = string.Empty;
        var normalizedNamespace = (runtimeNamespace ?? string.Empty).Trim();
        if (normalizedNamespace.Length == 0) {
            return false;
        }

        for (var i = 0; i < KnownPackIdentityDescriptors.Count; i++) {
            var descriptor = KnownPackIdentityDescriptors[i];
            for (var markerIndex = 0; markerIndex < descriptor.RuntimeNamespaceMarkers.Count; markerIndex++) {
                var marker = descriptor.RuntimeNamespaceMarkers[markerIndex];
                if (marker.Length == 0
                    || normalizedNamespace.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                if (descriptor.Category.Length == 0) {
                    return false;
                }

                category = descriptor.Category;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the preferred display name for a pack, falling back to a human-friendly label derived from the canonical pack id.
    /// </summary>
    public static string ResolveDisplayName(string? descriptorId, string? fallbackName) {
        if (!string.IsNullOrWhiteSpace(fallbackName)) {
            return fallbackName!.Trim();
        }

        var normalizedPackId = NormalizePackId(descriptorId);
        if (TryGetKnownPackIdentity(normalizedPackId, out var identity) && identity.DisplayName.Length > 0) {
            return identity.DisplayName;
        }

        return HumanizeFallbackPackId(normalizedPackId);
    }

    /// <summary>
    /// Normalizes a token into a compact alphanumeric pack/engine alias.
    /// </summary>
    public static string NormalizePackCompactId(string? value) {
        return NormalizeCompactToken(value);
    }

    /// <summary>
    /// Indicates whether the value matches a known pack identity token or alias.
    /// </summary>
    public static bool IsKnownPackIdentityToken(string? value) {
        return TryGetKnownPackIdentity(value, out _);
    }

    private static bool TryGetKnownPackIdentity(string? value, out PackIdentityDescriptor identity) {
        var compact = NormalizePackCompactId(value);
        return TryGetKnownPackIdentityByCompact(compact, out identity);
    }

    private static bool TryGetKnownPackIdentityByCompact(string compactToken, out PackIdentityDescriptor identity) {
        if (compactToken.Length > 0
            && KnownPackIdentitiesByCompactToken.TryGetValue(compactToken, out identity!)) {
            return true;
        }

        identity = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, PackIdentityDescriptor> BuildKnownPackIdentityMap() {
        var map = new Dictionary<string, PackIdentityDescriptor>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < KnownPackIdentityDescriptors.Count; i++) {
            var descriptor = KnownPackIdentityDescriptors[i];
            AddPackIdentityToken(map, descriptor.CanonicalPackId, descriptor);
            for (var aliasIndex = 0; aliasIndex < descriptor.Aliases.Count; aliasIndex++) {
                AddPackIdentityToken(map, descriptor.Aliases[aliasIndex], descriptor);
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<string, PackIdentityDescriptor> BuildKnownPackIdentityPrefixMap() {
        var map = new Dictionary<string, PackIdentityDescriptor>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < KnownPackIdentityDescriptors.Count; i++) {
            var descriptor = KnownPackIdentityDescriptors[i];
            for (var prefixIndex = 0; prefixIndex < descriptor.ToolNamePrefixes.Count; prefixIndex++) {
                var prefix = NormalizePackToken(descriptor.ToolNamePrefixes[prefixIndex]);
                if (prefix.Length == 0 || map.ContainsKey(prefix)) {
                    continue;
                }

                map[prefix] = descriptor;
            }
        }

        return map;
    }

    private static HashSet<string> BuildKnownCompoundPackRoutingTokenCompacts() {
        var compacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < KnownPackIdentityDescriptors.Count; i++) {
            var descriptor = KnownPackIdentityDescriptors[i];
            AddKnownCompoundCompact(compacts, descriptor.CanonicalPackId);
            for (var aliasIndex = 0; aliasIndex < descriptor.Aliases.Count; aliasIndex++) {
                AddKnownCompoundCompact(compacts, descriptor.Aliases[aliasIndex]);
            }
        }

        return compacts;
    }

    private static void AddPackIdentityToken(
        Dictionary<string, PackIdentityDescriptor> map,
        string? value,
        PackIdentityDescriptor descriptor) {
        var compact = NormalizePackCompactId(value);
        if (compact.Length == 0 || map.ContainsKey(compact)) {
            return;
        }

        map[compact] = descriptor;
    }

    private static void AddKnownCompoundCompact(HashSet<string> compacts, string? value) {
        var compact = NormalizePackCompactId(value);
        if (compact.Length > 0) {
            compacts.Add(compact);
        }
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

    private static string NormalizeCompactToken(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var buffer = new char[normalized.Length];
        var length = 0;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                buffer[length++] = char.ToLowerInvariant(ch);
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }

    private static string ExtractToolNamePrefix(string? toolName) {
        var normalized = NormalizePackToken(toolName);
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var separator = normalized.IndexOf('_');
        if (separator <= 0) {
            return string.Empty;
        }

        return normalized.Substring(0, separator);
    }

    private static string HumanizeFallbackPackId(string normalizedPackId) {
        if (string.IsNullOrWhiteSpace(normalizedPackId)) {
            return string.Empty;
        }

        var parts = normalizedPackId.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) {
            return string.Empty;
        }

        for (var i = 0; i < parts.Length; i++) {
            var part = (parts[i] ?? string.Empty).Trim();
            if (part.Length == 0) {
                continue;
            }

            parts[i] = part.Length == 1
                ? part.ToUpperInvariant()
                : char.ToUpperInvariant(part[0]) + part.Substring(1);
        }

        return string.Join(" ", parts);
    }
}
