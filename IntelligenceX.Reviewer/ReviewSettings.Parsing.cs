using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Analysis;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;


namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    private static ReviewProvider ParseProvider(string value) {
        return ReviewProviderContracts.ParseProviderOrDefault(value, ReviewProvider.OpenAI);
    }

    private static ReviewProvider? ParseProviderNullable(string value) {
        return ReviewProviderContracts.TryParseProviderAlias(value, out var provider)
            ? provider
            : null;
    }

    private static ReviewCodeHost ParseCodeHost(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "azure" or "azuredevops" or "azure-devops" or "ado" => ReviewCodeHost.AzureDevOps,
            _ => ReviewCodeHost.GitHub
        };
    }

    internal static AzureDevOpsAuthScheme ParseAzureAuthScheme(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "basic" or "pat" => AzureDevOpsAuthScheme.Basic,
            "bearer" => AzureDevOpsAuthScheme.Bearer,
            _ => throw new InvalidOperationException(
                $"Invalid azure auth scheme '{value}'. Use 'basic', 'pat', or 'bearer'.")
        };
    }

    private static OpenAITransportKind ParseTransport(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => OpenAITransportKind.AppServer
        };
    }

    private static string? GetInput(string inputName, string? envName = null, string? altEnvName = null) {
        var value = Environment.GetEnvironmentVariable($"INPUT_{inputName.ToUpperInvariant()}");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value.Trim();
        }
        if (!string.IsNullOrWhiteSpace(envName)) {
            value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }
        if (!string.IsNullOrWhiteSpace(altEnvName)) {
            value = Environment.GetEnvironmentVariable(altEnvName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ParseList(string? value, IReadOnlyList<string>? fallback = null) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback ?? Array.Empty<string>();
        }
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? fallback ?? Array.Empty<string>() : parts;
    }

    internal static IReadOnlyList<string> NormalizeAccountIdList(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            var normalized = value.Trim();
            if (normalized.Length == 0) {
                continue;
            }
            if (seen.Add(normalized)) {
                list.Add(normalized);
            }
        }
        return list;
    }

    internal static string NormalizeOpenAiAccountRotation(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "first" or "first-available" or "first_available" or "ordered" => "first-available",
            "round-robin" or "round_robin" or "rr" or "rotate" => "round-robin",
            "sticky" or "pin" or "pinned" => "sticky",
            _ => fallback
        };
    }

    internal static string NormalizeDiffRange(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "current" or "pr" or "pr-files" or "pr_files" => "current",
            "pr-base" or "pr_base" or "base" or "prbase" => "pr-base",
            "first-review" or "first_review" or "first-reviewed" or "firstreview" or "first" => "first-review",
            _ => fallback
        };
    }

    internal static string NormalizeEmbedPlacement(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "top" or "header" or "above" => "top",
            "bottom" or "footer" or "below" => "bottom",
            _ => fallback
        };
    }

    private static CopilotTransportKind ParseCopilotTransport(string? value, CopilotTransportKind fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "direct" or "api" or "http" => CopilotTransportKind.Direct,
            "cli" => CopilotTransportKind.Cli,
            _ => fallback
        };
    }

    private static bool ParseBoolean(string? value, bool fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => fallback
        };
    }

    private static int ParsePositiveInt(string? value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) {
            return parsed;
        }
        return fallback;
    }

    private static int ParseNonNegativeInt(string? value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0) {
            return parsed;
        }
        return fallback;
    }

    private static double ParsePositiveDouble(string? value, double fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 1 &&
            NumericGuards.IsFinite(parsed)) {
            return parsed;
        }
        return fallback;
    }

    private static string? NormalizeSeverity(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "low" or "medium" or "high" or "critical" => normalized,
            _ => null
        };
    }
}
