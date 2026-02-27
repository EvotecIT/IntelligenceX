using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private string BuildToolRunMarkdown(ToolRunDto tools) {
        return ToolRunMarkdownFormatter.Format(tools, ResolveToolDisplayName);
    }

    private string BuildToolRunVisualMarkdown(ToolRunDto tools) {
        return ToolRunMarkdownFormatter.FormatVisualsOnly(tools, ResolveToolDisplayName);
    }

    internal static string BuildToolRunTranscriptMarkdown(
        ToolRunDto tools,
        bool debugMode,
        Func<string?, string> resolveToolDisplayName) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(resolveToolDisplayName);
        return debugMode
            ? ToolRunMarkdownFormatter.Format(tools, resolveToolDisplayName)
            : ToolRunMarkdownFormatter.FormatVisualsOnly(tools, resolveToolDisplayName);
    }

    private string BuildToolRunTranscriptMarkdown(ToolRunDto tools) {
        return BuildToolRunTranscriptMarkdown(tools, _debugMode, ResolveToolDisplayName);
    }

    private string ResolveToolDisplayName(string? name) {
        if (!string.IsNullOrWhiteSpace(name)) {
            var key = name.Trim();
            if (_toolDisplayNames.TryGetValue(key, out var displayName) && !string.IsNullOrWhiteSpace(displayName)) {
                return displayName;
            }
        }

        return FormatToolDisplayName(name);
    }

    private static string FormatToolDisplayName(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return "Tool";
        }

        var tokens = name.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) {
            return name;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++) {
            var token = tokens[i];
            var upper = token.ToUpperInvariant();
            var segment = upper switch {
                "AD" => "AD",
                "DN" => "DN",
                "LDAP" => "LDAP",
                "CSV" => "CSV",
                "TSV" => "TSV",
                "CPU" => "CPU",
                "ID" => "ID",
                "GUID" => "GUID",
                "DNS" => "DNS",
                "OU" => "OU",
                _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant())
            };

            if (i > 0) {
                sb.Append(' ');
            }
            sb.Append(segment);
        }

        return sb.ToString();
    }

    private static string[] NormalizeTags(string[]? tags) {
        if (tags is null || tags.Length == 0) {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }

            set.Add(tag.Trim());
        }

        if (set.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    private static string InferToolCategory(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return "general";
        }

        var idx = toolName.IndexOf('_');
        if (idx <= 0) {
            return "general";
        }

        var prefix = toolName.Substring(0, idx);
        return prefix.ToLowerInvariant() switch {
            "ad" => "active-directory",
            "eventlog" => "event-log",
            "system" => "system",
            "fs" => "file-system",
            "email" => "email",
            "wsl" => "system",
            _ => "general"
        };
    }

    private string[] BuildKnownProfiles() {
        var set = new HashSet<string>(_knownProfiles, StringComparer.OrdinalIgnoreCase) { _appProfileName };
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }
}
