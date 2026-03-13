using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Allowed execution-locality scope tokens used by tool execution contracts.
/// </summary>
public static class ToolExecutionScopes {
    /// <summary>Tool runs only in the local runtime.</summary>
    public const string LocalOnly = "local_only";

    /// <summary>Tool runs only against remote targets or remote backends.</summary>
    public const string RemoteOnly = "remote_only";

    /// <summary>Tool can run locally or against a remote target.</summary>
    public const string LocalOrRemote = "local_or_remote";

    /// <summary>
    /// Allowed execution scope values.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedScopes = new[] {
        LocalOnly,
        RemoteOnly,
        LocalOrRemote
    };

    /// <summary>
    /// Returns true when the supplied scope token is allowed.
    /// </summary>
    public static bool IsAllowed(string? value) {
        var normalized = Normalize(value);
        return normalized.Length > 0
               && (string.Equals(normalized, LocalOnly, StringComparison.Ordinal)
                   || string.Equals(normalized, RemoteOnly, StringComparison.Ordinal)
                   || string.Equals(normalized, LocalOrRemote, StringComparison.Ordinal));
    }

    /// <summary>
    /// Normalizes the supplied execution scope token.
    /// </summary>
    public static string Normalize(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? string.Empty : normalized;
    }

    /// <summary>
    /// Returns true when the supplied scope is remote-capable.
    /// </summary>
    public static bool IsRemoteCapable(string? value) {
        var normalized = Normalize(value);
        return string.Equals(normalized, RemoteOnly, StringComparison.Ordinal)
               || string.Equals(normalized, LocalOrRemote, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a valid scope from an explicit contract value or inferred remote capability.
    /// </summary>
    public static string Resolve(string? explicitScope, bool supportsRemoteExecution) {
        var normalized = Normalize(explicitScope);
        if (IsAllowed(normalized)) {
            return normalized;
        }

        return supportsRemoteExecution ? LocalOrRemote : LocalOnly;
    }
}
