using System;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared formatting helpers for structured startup tool-health warnings and pack labels.
/// </summary>
public static class StartupToolHealthWarningFormatter {
    private static readonly Regex StructuredToolHealthWarningPattern = new(
        @"^\[(?<kind>tool health(?: notice)?)\]\[(?<source>[^\]]+)\]\[(?<pack>[^\]]+)\]\s+(?<tool>\S+)\s+failed\s+\((?<code>[^)]+)\):\s*(?<message>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Normalizes a startup warning pack identifier into the canonical Chat contract id when possible.
    /// </summary>
    public static string NormalizePackId(string? packId) {
        var normalized = ToolPackMetadataNormalizer.NormalizePackId(packId);
        if (normalized.Length > 0) {
            return normalized;
        }

        return (packId ?? string.Empty).Trim();
    }

    /// <summary>
    /// Resolves a human-friendly pack label for operator-facing warning surfaces.
    /// </summary>
    public static string ResolvePackDisplayLabel(string? packId, string? fallbackName) {
        var explicitName = (fallbackName ?? string.Empty).Trim();
        if (explicitName.Length > 0) {
            return explicitName;
        }

        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return "Unknown pack";
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
            _ => HumanizePackId(normalizedPackId)
        };
    }

    /// <summary>
    /// Resolves a human-facing source label for structured startup tool-health warnings.
    /// </summary>
    public static string ResolveSourceLabel(string? source) {
        var normalized = (source ?? string.Empty).Trim();
        return normalized.ToLowerInvariant() switch {
            "builtin" => "Core",
            "open_source" => "Open",
            "closed_source" => "Private",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Builds the human-facing issue summary for a structured startup tool-health warning.
    /// </summary>
    public static string BuildIssueSummary(string? toolName, string? errorCode, string? message) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        var normalizedErrorCode = (errorCode ?? string.Empty).Trim();
        var normalizedMessage = (message ?? string.Empty).Trim();
        if (normalizedMessage.Length == 0) {
            normalizedMessage = "Probe failed.";
        }

        if (string.Equals(normalizedErrorCode, "smoke_invalid_argument", StringComparison.OrdinalIgnoreCase)) {
            return "startup smoke check needs input selection: " + normalizedMessage;
        }

        if (string.Equals(normalizedErrorCode, "smoke_not_configured", StringComparison.OrdinalIgnoreCase)) {
            return "startup smoke check is not configured: " + normalizedMessage;
        }

        var toolLabel = normalizedToolName.Length == 0 ? "startup probe" : normalizedToolName + " startup probe";
        if (normalizedErrorCode.Length == 0) {
            return toolLabel + " failed: " + normalizedMessage;
        }

        return toolLabel + " failed (" + normalizedErrorCode + "): " + normalizedMessage;
    }

    /// <summary>
    /// Parses a structured startup tool-health warning and resolves display-ready parts.
    /// </summary>
    public static StartupToolHealthWarningDisplayParts? BuildDisplayParts(
        string? warning,
        Func<string, string?>? resolvePackName = null) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        var match = StructuredToolHealthWarningPattern.Match(normalized);
        if (!match.Success) {
            return null;
        }

        var normalizedPackId = NormalizePackId(match.Groups["pack"].Value);
        var packLabel = ResolvePackDisplayLabel(
            normalizedPackId,
            resolvePackName?.Invoke(normalizedPackId));
        var sourceLabel = ResolveSourceLabel(match.Groups["source"].Value);
        var title = sourceLabel.Length == 0
            ? packLabel
            : packLabel + " (" + sourceLabel + ")";
        var summary = BuildIssueSummary(
            match.Groups["tool"].Value,
            match.Groups["code"].Value,
            match.Groups["message"].Value);
        return new StartupToolHealthWarningDisplayParts(title, summary);
    }

    private static string HumanizePackId(string normalizedPackId) {
        var parts = normalizedPackId.Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) {
            return normalizedPackId;
        }

        var buffer = new StringBuilder(normalizedPackId.Length + parts.Length);
        for (var i = 0; i < parts.Length; i++) {
            if (i > 0) {
                buffer.Append(' ');
            }

            var part = parts[i];
            if (part.Length == 1) {
                buffer.Append(char.ToUpperInvariant(part[0]));
                continue;
            }

            buffer.Append(char.ToUpperInvariant(part[0]));
            buffer.Append(part[1..]);
        }

        return buffer.ToString();
    }
}

/// <summary>
/// Display-ready title and summary for a structured startup tool-health warning.
/// </summary>
public readonly record struct StartupToolHealthWarningDisplayParts(string Title, string Summary);
