using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared mutability hint vocabulary used by tool contracts and chat-side safety fallbacks.
/// </summary>
public static class ToolMutabilityHintNames {
    /// <summary>
    /// Canonical argument names that typically indicate a mutating operation.
    /// </summary>
    public static IReadOnlyList<string> CanonicalMutatingActionArguments { get; } = new[] {
        "send",
        "dry_run",
        "confirm",
        "execute",
        "apply",
        "force",
        "enable",
        "disable",
        "allow_write"
    };

    /// <summary>
    /// Canonical read-only hint tokens used by tool metadata fallbacks.
    /// </summary>
    public static IReadOnlyList<string> CanonicalReadOnlyHints { get; } = new[] {
        "read_only",
        "readonly",
        "safe_read",
        "query_only",
        "inventory",
        "diagnostic"
    };

    /// <summary>
    /// Normalizes mutability hint text into a compact lowercase token form.
    /// </summary>
    public static string NormalizeHintToken(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var chars = new char[normalized.Length];
        var len = 0;
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                chars[len++] = ch;
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator) {
                continue;
            }

            chars[len++] = '_';
            previousWasSeparator = true;
        }

        if (len == 0) {
            return string.Empty;
        }

        return new string(chars, 0, len).Trim('_');
    }

    /// <summary>
    /// Returns true when a normalized metadata token implies mutating behavior.
    /// </summary>
    public static bool LooksLikeMutatingHint(string? value) {
        var normalized = NormalizeHintToken(value);
        if (normalized.Length == 0) {
            return false;
        }

        return ContainsExactHint(normalized, CanonicalMutatingActionArguments)
               || ContainsExactHint(normalized, ToolWriteGovernanceArgumentNames.CanonicalSchemaMetadataArguments)
               || normalized.IndexOf("read_write", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("readwrite", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("danger", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("mutat", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("state_change", StringComparison.Ordinal) >= 0
               || normalized.IndexOf("destruct", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Returns true when a normalized metadata token implies read-only behavior.
    /// </summary>
    public static bool LooksLikeReadOnlyHint(string? value) {
        var normalized = NormalizeHintToken(value);
        if (normalized.Length == 0) {
            return false;
        }

        return ContainsExactHint(normalized, CanonicalReadOnlyHints);
    }

    private static bool ContainsExactHint(string value, IReadOnlyList<string> hints) {
        for (var i = 0; i < hints.Count; i++) {
            if (string.Equals(value, hints[i], StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
