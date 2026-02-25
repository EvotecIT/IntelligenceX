using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using ADPlayground.Monitoring.Probes;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdScopeDiscoveryTool : ActiveDirectoryToolBase, ITool {
    private static NamingContextsModel ReadNamingContexts(DomainInfoQueryResult? info) {
        var attrs = info?.RootDse?.Attributes;
        if (attrs is null) {
            return new NamingContextsModel(null, null, null, null);
        }

        return new NamingContextsModel(
            DefaultNamingContext: ReadAttributeString(attrs, "defaultNamingContext"),
            ConfigurationNamingContext: ReadAttributeString(attrs, "configurationNamingContext"),
            SchemaNamingContext: ReadAttributeString(attrs, "schemaNamingContext"),
            RootDomainNamingContext: ReadAttributeString(attrs, "rootDomainNamingContext"));
    }

    private static string? ReadAttributeString(IReadOnlyDictionary<string, object?> attributes, string key) {
        if (!attributes.TryGetValue(key, out var value) || value is null) {
            return null;
        }

        if (value is string text) {
            return NormalizeOptional(text);
        }

        if (value is IEnumerable enumerable) {
            foreach (var item in enumerable) {
                var normalized = NormalizeOptional(item?.ToString());
                if (normalized is not null) {
                    return normalized;
                }
            }
        }

        return NormalizeOptional(value.ToString());
    }

    private static bool TryParseDiscoveryFallback(
        string raw,
        out DirectoryDiscoveryFallback fallback,
        out string? error) {
        var normalized = NormalizeHostOrName(raw);
        if (DiscoveryFallbackModes.TryGetValue(normalized, out fallback)) {
            error = null;
            return true;
        }

        error = "discovery_fallback must be one of: none, current_domain, current_forest.";
        fallback = DirectoryDiscoveryFallback.None;
        return false;
    }

    private static string ToDiscoveryFallbackName(DirectoryDiscoveryFallback fallback) {
        return fallback switch {
            DirectoryDiscoveryFallback.None => "none",
            DirectoryDiscoveryFallback.CurrentForest => "current_forest",
            _ => "current_domain"
        };
    }

    private static string? ResolveFallbackForest(DirectoryDiscoveryFallback fallback) {
        if (fallback != DirectoryDiscoveryFallback.CurrentForest) {
            return null;
        }

        return NormalizeOptional(DomainHelper.RootDomainName);
    }

    private static string? ResolveFallbackDomain(DirectoryDiscoveryFallback fallback) {
        if (fallback != DirectoryDiscoveryFallback.CurrentDomain) {
            return null;
        }

        return DomainHelper.TryGetCurrentDomainName(out var domain)
            ? NormalizeOptional(domain)
            : null;
    }

    private static HashSet<string> BuildSet(IEnumerable<string>? items) {
        if (items is null) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return items
            .Select(NormalizeHostOrName)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddCandidate(HashSet<string> store, string? value) {
        var normalized = NormalizeHostOrName(value);
        if (!string.IsNullOrWhiteSpace(normalized)) {
            store.Add(normalized);
        }
    }

    private static void AddCandidates(HashSet<string> store, IEnumerable<string>? values) {
        if (values is null) {
            return;
        }

        foreach (var value in values) {
            AddCandidate(store, value);
        }
    }

    private static string NormalizeHostOrName(string? input) {
        if (string.IsNullOrWhiteSpace(input)) {
            return string.Empty;
        }

        return input.Trim().TrimEnd('.');
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = NormalizeHostOrName(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsRodcBestEffort(string host) {
        if (string.IsNullOrWhiteSpace(host)) {
            return false;
        }

        try {
            return DomainHelper.IsReadOnlyDc(host);
        } catch {
            var normalized = NormalizeHostOrName(host);
            var separator = normalized.IndexOf('.');
            var label = separator >= 0 ? normalized[..separator] : normalized;
            return label.StartsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
                   label.EndsWith("rodc", StringComparison.OrdinalIgnoreCase) ||
                   label.Contains("-rodc", StringComparison.OrdinalIgnoreCase) ||
                   label.Contains("rodc-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
