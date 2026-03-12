namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared normalization helpers for pack identifiers, display names, and source-kind mapping.
/// </summary>
public static class ToolPackMetadataNormalizer {
    /// <summary>
    /// Normalizes a pack identifier into the canonical token used across Chat contracts.
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

        return compact switch {
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
    }

    /// <summary>
    /// Resolves the preferred display name for a pack, falling back to the canonical normalized pack id.
    /// </summary>
    public static string ResolveDisplayName(string? descriptorId, string? fallbackName) {
        var normalizedPackId = NormalizePackId(descriptorId);
        if (!string.IsNullOrWhiteSpace(fallbackName)) {
            return fallbackName.Trim();
        }

        return normalizedPackId;
    }

    /// <summary>
    /// Maps a raw source-kind token into the protocol enum used by Chat surfaces.
    /// </summary>
    public static ToolPackSourceKind ResolveSourceKind(string? sourceKind, string? descriptorId) {
        var normalizedSourceKind = NormalizeSourceKind(sourceKind, descriptorId);
        return normalizedSourceKind switch {
            "builtin" => ToolPackSourceKind.Builtin,
            "closed_source" => ToolPackSourceKind.ClosedSource,
            _ => ToolPackSourceKind.OpenSource
        };
    }

    /// <summary>
    /// Normalizes a raw source-kind token into a stable machine-friendly string.
    /// </summary>
    public static string NormalizeSourceKind(string? sourceKind, string? descriptorId) {
        _ = descriptorId;
        var raw = (sourceKind ?? string.Empty).Trim().ToLowerInvariant();
        return raw switch {
            "builtin" => "builtin",
            "open" => "open_source",
            "opensource" => "open_source",
            "open_source" => "open_source",
            "public" => "open_source",
            "closed" => "closed_source",
            "closedsource" => "closed_source",
            "closed_source" => "closed_source",
            "private" => "closed_source",
            "internal" => "closed_source",
            _ => "open_source"
        };
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

    private static string NormalizeCompactToken(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var length = 0;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch)) {
                buffer[length++] = char.ToLowerInvariant(ch);
            }
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
    }
}
