using System;
using ADPlayground.Monitoring.Probes;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdScopeDiscoveryTool : ActiveDirectoryToolBase, ITool {
    private static string? NormalizeOptionalDnsName(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var normalized = value.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool TryParseDiscoveryFallback(
        string raw,
        out DirectoryDiscoveryFallback fallback,
        out string? error) {
        var normalized = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().TrimEnd('.');
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
}
