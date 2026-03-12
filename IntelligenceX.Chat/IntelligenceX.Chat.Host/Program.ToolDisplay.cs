using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static string GetToolDisplayName(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return string.Empty;
        }

        // Keep stable tool ids (machine-friendly), but display a friendlier title for humans.
        var (prefix, suffix) = SplitPrefix(toolName.Trim());
        var group = string.Equals(prefix, suffix, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : ResolveToolGroupLabel(toolName, prefix);

        var title = ToTitle(suffix);

        return string.IsNullOrWhiteSpace(group) ? title : $"{group} / {title}";
    }

    private static string ResolveToolGroupLabel(string toolName, string prefix) {
        if (ToolSelectionMetadata.TryResolvePackId(
                toolName,
                category: null,
                tags: null,
                out var packId)
            && packId.Length > 0) {
            return ToTitle(packId);
        }

        return ToTitle(prefix);
    }

    private static (string Prefix, string Suffix) SplitPrefix(string toolName) {
        var idx = toolName.IndexOf('_');
        if (idx <= 0 || idx == toolName.Length - 1) {
            return (toolName, toolName);
        }
        return (toolName.Substring(0, idx), toolName.Substring(idx + 1));
    }

    private static string ToTitle(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < parts.Length; i++) {
            if (i > 0) {
                sb.Append(' ');
            }

            var p = parts[i];
            if (IsAcronym(p, out var acronym)) {
                sb.Append(acronym);
                continue;
            }

            if (p.Length == 1) {
                sb.Append(char.ToUpperInvariant(p[0]));
                continue;
            }

            sb.Append(char.ToUpperInvariant(p[0]));
            sb.Append(p.Substring(1));
        }
        return sb.ToString();
    }

    private static bool IsAcronym(string value, out string acronym) {
        acronym = value;
        if (value.Length == 0) {
            return false;
        }

        // Common acronyms used across tools.
        switch (value) {
            case var _ when string.Equals(value, "ldap", StringComparison.OrdinalIgnoreCase):
                acronym = "LDAP";
                return true;
            case var _ when string.Equals(value, "spn", StringComparison.OrdinalIgnoreCase):
                acronym = "SPN";
                return true;
            case var _ when string.Equals(value, "evtx", StringComparison.OrdinalIgnoreCase):
                acronym = "EVTX";
                return true;
            case var _ when string.Equals(value, "wsl", StringComparison.OrdinalIgnoreCase):
                acronym = "WSL";
                return true;
            case var _ when string.Equals(value, "utc", StringComparison.OrdinalIgnoreCase):
                acronym = "UTC";
                return true;
            default:
                return false;
        }
    }

    private static void CollectPackWarning(ICollection<string> sink, string? warning) {
        if (sink is null) {
            return;
        }

        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        if (!sink.Contains(normalized)) {
            sink.Add(normalized);
        }
    }

    internal static IReadOnlyList<string> BuildUnavailablePackAvailabilityWarnings(IReadOnlyList<ToolPackAvailabilityInfo> packAvailability) {
        if (packAvailability.Count == 0) {
            return Array.Empty<string>();
        }

        return StartupUnavailablePackWarningFormatter.BuildEntries(
                packAvailability,
                static pack => pack.Id,
                static pack => pack.Name,
                static pack => pack.Enabled,
                static pack => pack.DisabledReason)
            .OrderBy(static pack => pack.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Reason, StringComparer.OrdinalIgnoreCase)
            .Select(static pack => pack.Label + ": " + pack.Reason)
            .ToArray();
    }

    internal static IReadOnlyList<string> BuildFormattedPackWarnings(
        IReadOnlyList<string>? warnings,
        IReadOnlyList<ToolPackAvailabilityInfo>? packAvailability = null) {
        if (warnings is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var formatted = new List<string>(warnings.Count);
        for (var i = 0; i < warnings.Count; i++) {
            var line = FormatPackWarningForConsole(warnings[i], packAvailability);
            if (line.Length == 0 || formatted.Contains(line, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            formatted.Add(line);
        }

        return formatted;
    }

    internal static string FormatPackWarningForConsole(
        string? warning,
        IReadOnlyList<ToolPackAvailabilityInfo>? packAvailability = null) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var displayParts = StartupToolHealthWarningFormatter.BuildDisplayParts(
            normalized,
            normalizedPackId => ResolvePackNameFromAvailability(normalizedPackId, packAvailability));
        if (displayParts is not null) {
            return displayParts.Value.Title + ": " + displayParts.Value.Summary;
        }

        if (StartupBootstrapWarningFormatter.TryBuildStatusText(normalized, out var bootstrapStatus, out _)) {
            return bootstrapStatus;
        }

        return normalized;
    }

    private static string ResolvePackNameFromAvailability(
        string? packId,
        IReadOnlyList<ToolPackAvailabilityInfo>? packAvailability) {
        var normalizedPackId = NormalizeHostPackId(packId);
        if (normalizedPackId.Length == 0 || packAvailability is not { Count: > 0 }) {
            return string.Empty;
        }

        for (var i = 0; i < packAvailability.Count; i++) {
            var candidate = packAvailability[i];
            if (!string.Equals(NormalizeHostPackId(candidate.Id), normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return (candidate.Name ?? string.Empty).Trim();
        }

        return string.Empty;
    }

    private static string NormalizeHostPackId(string? packId) {
        return StartupToolHealthWarningFormatter.NormalizePackId(packId);
    }

    private static string ResolveHostPackDisplayLabel(string? packId, string? fallbackName) {
        return StartupToolHealthWarningFormatter.ResolvePackDisplayLabel(packId, fallbackName);
    }
}
