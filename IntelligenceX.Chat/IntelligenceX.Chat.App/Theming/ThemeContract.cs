using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Theming;

/// <summary>
/// Canonical theme contract used by UI, normalization, and model prompts.
/// </summary>
internal static class ThemeContract {
    public const string DefaultPreset = "default";

    private static readonly IReadOnlyDictionary<string, string> AliasMap;

    static ThemeContract() {
        var canonical = new List<string> { DefaultPreset };
        canonical.AddRange(ThemeRegistry.PresetNames
            .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase));
        CanonicalPresetNames = canonical;

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in CanonicalPresetNames) {
            aliases[name] = name;
        }
        aliases["blue"] = "cobalt";
        aliases["gray"] = "graphite";
        aliases["grey"] = "graphite";
        AliasMap = aliases;

        AcceptedPresetTokens = aliases.Keys
            .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ThemePresetSchema = string.Join("|", CanonicalPresetNames);
        ThemeValueRegexAlternation = string.Join("|", AcceptedPresetTokens.Select(Regex.Escape));
    }

    /// <summary>
    /// Canonical theme values (includes <c>default</c>).
    /// </summary>
    public static IReadOnlyList<string> CanonicalPresetNames { get; }

    /// <summary>
    /// Accepted user-facing tokens (canonical values + aliases).
    /// </summary>
    public static IReadOnlyList<string> AcceptedPresetTokens { get; }

    /// <summary>
    /// Canonical schema value list for prompt contracts.
    /// </summary>
    public static string ThemePresetSchema { get; }

    /// <summary>
    /// Regex alternation fragment matching accepted tokens.
    /// </summary>
    public static string ThemeValueRegexAlternation { get; }

    /// <summary>
    /// Normalizes a user-provided theme value to canonical form.
    /// </summary>
    public static string? Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        var token = value.Trim();
        return AliasMap.TryGetValue(token, out var canonical) ? canonical : null;
    }

    /// <summary>
    /// Returns true if text includes any known theme token.
    /// </summary>
    public static bool ContainsKnownToken(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var normalized = text.Trim();
        foreach (var token in AcceptedPresetTokens) {
            if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds option-tag markup for the theme selector.
    /// </summary>
    public static string BuildThemeOptionTagsHtml() {
        var sb = new StringBuilder();
        foreach (var preset in CanonicalPresetNames) {
            var value = WebUtility.HtmlEncode(preset);
            var displayName = WebUtility.HtmlEncode(ToDisplayName(preset));
            sb.Append("            <option value=\"")
              .Append(value)
              .Append("\">")
              .Append(displayName)
              .AppendLine("</option>");
        }

        return sb.ToString();
    }

    private static string ToDisplayName(string preset) {
        if (string.IsNullOrWhiteSpace(preset)) {
            return DefaultPreset;
        }

        if (preset.Length == 1) {
            return preset.ToUpperInvariant();
        }

        return char.ToUpperInvariant(preset[0]) + preset[1..];
    }
}
