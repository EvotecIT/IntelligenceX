using System;
using System.Collections.Generic;
using ADPlayground.Helpers;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Shared helpers for mapping typed ADPlayground LDAP query results into tool envelopes.
/// </summary>
internal static class AdQueryResultHelpers {
    /// <summary>
    /// Maps a typed LDAP query failure to a standard tool error envelope.
    /// </summary>
    internal static string MapQueryFailure(LdapToolQueryFailure? failure) {
        var code = ToolFailureMapper.MapCode(failure?.Kind.ToString(), fallbackErrorCode: "exception");
        var message = string.IsNullOrWhiteSpace(failure?.Message) ? "LDAP query failed." : failure!.Message;
        var hints = BuildAdAutodiscoveryHints(message);
        var isTransient = string.Equals(code, "timeout", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(code, "query_failed", StringComparison.OrdinalIgnoreCase);

        return ToolResponse.Error(code, message, hints, isTransient);
    }

    private static IReadOnlyList<string>? BuildAdAutodiscoveryHints(string message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return null;
        }

        if (!LooksLikeSearchBaseResolutionFailure(message)) {
            return null;
        }

        return new[] {
            "Call ad_environment_discover first to auto-resolve effective domain_controller and search_base_dn.",
            "If discovery still fails, pass domain_controller (FQDN) and search_base_dn (DN) explicitly."
        };
    }

    private static bool LooksLikeSearchBaseResolutionFailure(string message) {
        var text = message.Trim();

        return text.Contains("defaultNamingContext", StringComparison.OrdinalIgnoreCase)
               || text.Contains("RootDSE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("base DN", StringComparison.OrdinalIgnoreCase)
               || text.Contains("search_base_dn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a best-effort string value from LDAP row attributes.
    /// </summary>
    internal static string ReadStringValue(Dictionary<string, object?> attrs, string key) {
        return attrs.GetString(key);
    }

    /// <summary>
    /// Reads best-effort string values from LDAP row attributes.
    /// </summary>
    internal static IReadOnlyList<string> ReadStringValues(Dictionary<string, object?> attrs, string key) {
        return attrs.GetStringValues(key);
    }

    /// <summary>
    /// Reads a best-effort integer value from LDAP row attributes.
    /// </summary>
    internal static int? ReadIntValue(Dictionary<string, object?> attrs, string key) {
        return attrs.GetInt32(key);
    }

}
