using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared activation-state tokens for pack/plugin capability snapshots.
/// </summary>
public static class ToolActivationStates {
    /// <summary>
    /// The pack or plugin is live and callable in the current runtime.
    /// </summary>
    public const string Active = "active";
    /// <summary>
    /// The pack or plugin is known from descriptors and can be activated on demand.
    /// </summary>
    public const string Deferred = "deferred";
    /// <summary>
    /// The pack or plugin is unavailable in the current runtime.
    /// </summary>
    public const string Disabled = "disabled";

    /// <summary>
    /// Resolves a normalized activation-state token from enabled/descriptor-only inputs.
    /// </summary>
    public static string Resolve(bool enabled, bool descriptorOnly) {
        if (!enabled) {
            return Disabled;
        }

        return descriptorOnly ? Deferred : Active;
    }

    /// <summary>
    /// Returns the normalized activation state when the token is recognized; otherwise returns an enabled-based fallback.
    /// </summary>
    public static string NormalizeOrDefault(string? activationState, bool enabledFallback) {
        var normalized = Normalize(activationState);
        if (normalized.Length > 0) {
            return normalized;
        }

        return enabledFallback ? Active : Disabled;
    }

    /// <summary>
    /// Returns the normalized activation-state token when recognized; otherwise returns an empty string.
    /// </summary>
    public static string Normalize(string? activationState) {
        var normalized = (activationState ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            Active => Active,
            Deferred => Deferred,
            Disabled => Disabled,
            _ => string.Empty
        };
    }

    /// <summary>
    /// Indicates whether the activation state represents descriptor-only deferred activation.
    /// </summary>
    public static bool IsDeferred(string? activationState) {
        return string.Equals(Normalize(activationState), Deferred, StringComparison.Ordinal);
    }
}
