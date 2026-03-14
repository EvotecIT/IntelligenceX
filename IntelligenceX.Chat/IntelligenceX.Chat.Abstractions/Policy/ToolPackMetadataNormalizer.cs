using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared normalization helpers for pack identifiers, display names, and source-kind mapping.
/// </summary>
public static class ToolPackMetadataNormalizer {
    /// <summary>
    /// Normalizes a pack identifier into the canonical token used across Chat contracts.
    /// </summary>
    public static string NormalizePackId(string? value) {
        return ToolPackIdentityCatalog.NormalizePackId(value);
    }

    /// <summary>
    /// Resolves the preferred display name for a pack, falling back to a human-friendly label derived from the canonical pack id.
    /// </summary>
    public static string ResolveDisplayName(string? descriptorId, string? fallbackName) {
        return ToolPackIdentityCatalog.ResolveDisplayName(descriptorId, fallbackName);
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

}
