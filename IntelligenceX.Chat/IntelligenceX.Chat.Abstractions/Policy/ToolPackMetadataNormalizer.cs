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
    /// Resolves the preferred display name for a pack, falling back to a human-friendly label derived from the canonical pack id.
    /// </summary>
    public static string ResolveDisplayName(string? descriptorId, string? fallbackName) {
        if (!string.IsNullOrWhiteSpace(fallbackName)) {
            return fallbackName.Trim();
        }

        return HumanizePackId(NormalizePackId(descriptorId));
    }

    /// <summary>
    /// Maps a raw source-kind token into the protocol enum used by Chat surfaces.
    /// </summary>
    public static ToolPackSourceKind ResolveSourceKind(string? sourceKind) {
        var normalizedSourceKind = NormalizeSourceKind(sourceKind);
        return normalizedSourceKind switch {
            "builtin" => ToolPackSourceKind.Builtin,
            "closed_source" => ToolPackSourceKind.ClosedSource,
            _ => ToolPackSourceKind.OpenSource
        };
    }

    /// <summary>
    /// Normalizes a raw source-kind token into a stable machine-friendly string.
    /// </summary>
    public static string NormalizeSourceKind(string? sourceKind) {
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

    /// <summary>
    /// Normalizes engine ids and capability tags without applying pack-id alias mapping.
    /// </summary>
    public static string NormalizeDescriptorToken(string? value) {
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

            if (length > 0 && !previousWasSeparator) {
                buffer[length++] = '_';
                previousWasSeparator = true;
            }
        }

        while (length > 0 && buffer[length - 1] == '_') {
            length--;
        }

        return length == 0 ? string.Empty : new string(buffer, 0, length);
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

    private static string HumanizePackId(string normalizedPackId) {
        if (string.IsNullOrWhiteSpace(normalizedPackId)) {
            return string.Empty;
        }

        return normalizedPackId switch {
            "active_directory" => "Active Directory",
            "eventlog" => "Event Log",
            "system" => "System",
            "filesystem" => "Filesystem",
            "email" => "Email",
            "powershell" => "PowerShell",
            "testimox" => "TestimoX",
            "officeimo" => "OfficeIMO",
            "reviewer_setup" => "Reviewer Setup",
            "dnsclientx" => "DnsClientX",
            "domaindetective" => "DomainDetective",
            _ => HumanizeFallbackPackId(normalizedPackId)
        };
    }

    private static string HumanizeFallbackPackId(string normalizedPackId) {
        var parts = normalizedPackId.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) {
            return string.Empty;
        }

        for (var i = 0; i < parts.Length; i++) {
            var part = parts[i];
            if (part.Length == 0) {
                continue;
            }

            parts[i] = part.Length == 1
                ? part.ToUpperInvariant()
                : char.ToUpperInvariant(part[0]) + part[1..];
        }

        return string.Join(" ", parts);
    }
}
