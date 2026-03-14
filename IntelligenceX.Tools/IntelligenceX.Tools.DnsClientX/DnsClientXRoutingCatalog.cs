using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

internal static class DnsClientXRoutingCatalog {
    private static readonly string[] PingFallbackSelectionKeys = { "target", "targets" };
    private static readonly string[] PingFallbackHintKeys = { "buffer_size", "dont_fragment", "max_targets", "target", "targets", "timeout_ms" };

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["dnsclientx_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["dnsclientx_ping"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
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
        "dns_client_x"
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "DnsClientX");
    }

    public static IReadOnlyList<string> ResolveFallbackSelectionKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return string.Equals(toolName, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)
            ? PingFallbackSelectionKeys
            : Array.Empty<string>();
    }

    public static IReadOnlyList<string> ResolveFallbackHintKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return string.Equals(toolName, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)
            ? PingFallbackHintKeys
            : Array.Empty<string>();
    }

    public static bool RequiresSelectionForFallback(bool explicitRequiresSelection, IReadOnlyList<string> fallbackSelectionKeys) {
        return explicitRequiresSelection || fallbackSelectionKeys.Count > 0;
    }
}
