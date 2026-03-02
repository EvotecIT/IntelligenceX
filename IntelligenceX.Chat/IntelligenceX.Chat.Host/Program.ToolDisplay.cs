using System;
using System.Collections.Generic;
using System.Text;
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
}
