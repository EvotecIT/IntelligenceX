using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using IntelligenceX.Analysis;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using JsonArrayNode = System.Text.Json.Nodes.JsonArray;
using JsonNodeBase = System.Text.Json.Nodes.JsonNode;
using JsonNodeObject = System.Text.Json.Nodes.JsonObject;
using JsonValueNode = System.Text.Json.Nodes.JsonValue;


namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    private static ReviewProvider ParseProvider(string value) {
        return ReviewProviderContracts.ParseProviderOrThrow(value, "review provider");
    }

    private static ReviewProvider? ParseProviderNullable(string value) {
        return ReviewProviderContracts.ParseProviderOrThrow(value, "review provider fallback");
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

    internal static IReadOnlyList<string> NormalizeMergeBlockerSections(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            var normalized = NormalizeWhitespace(value.Trim());
            if (normalized.Length == 0) {
                continue;
            }
            if (seen.Add(normalized)) {
                list.Add(normalized);
            }
        }
        return list;
    }

    private static string NormalizeWhitespace(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var chars = new List<char>(value.Length);
        var previousWasWhitespace = false;
        foreach (var ch in value) {
            if (char.IsWhiteSpace(ch)) {
                if (previousWasWhitespace) {
                    continue;
                }
                chars.Add(' ');
                previousWasWhitespace = true;
                continue;
            }

            chars.Add(ch);
            previousWasWhitespace = false;
        }

        return new string(chars.ToArray()).Trim();
    }

    internal static ReviewNarrativeMode NormalizeNarrativeMode(string? value, ReviewNarrativeMode fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "structured" or "strict" or "template" => ReviewNarrativeMode.Structured,
            "freedom" or "free" or "flexible" or "natural" => ReviewNarrativeMode.Freedom,
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

    internal static string NormalizeCiContextFailureSnippets(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "off" or "false" or "none" => "off",
            "auto" or "smart" => "auto",
            "always" or "on" or "true" => "always",
            _ => fallback
        };
    }

    internal static IReadOnlyList<string> NormalizeSwarmReviewers(IEnumerable<string>? values, IReadOnlyList<string>? fallback = null) {
        if (values is null) {
            return fallback ?? Array.Empty<string>();
        }

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length == 0) {
                continue;
            }
            if (seen.Add(normalized)) {
                list.Add(normalized);
            }
        }

        return list.Count == 0 ? fallback ?? Array.Empty<string>() : list;
    }

    internal static IReadOnlyList<ReviewSwarmReviewerSettings> NormalizeSwarmReviewerSettings(
        IEnumerable<ReviewSwarmReviewerSettings>? values, IReadOnlyList<ReviewSwarmReviewerSettings>? fallback = null) {
        if (values is null) {
            return fallback ?? Array.Empty<ReviewSwarmReviewerSettings>();
        }

        var list = new List<ReviewSwarmReviewerSettings>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            var normalizedId = value.Id?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedId)) {
                continue;
            }
            if (!seen.Add(normalizedId)) {
                continue;
            }

            list.Add(new ReviewSwarmReviewerSettings {
                Id = normalizedId,
                AgentProfile = string.IsNullOrWhiteSpace(value.AgentProfile) ? null : value.AgentProfile.Trim(),
                Provider = value.Provider,
                Model = string.IsNullOrWhiteSpace(value.Model) ? null : value.Model.Trim(),
                ReasoningEffort = value.ReasoningEffort
            });
        }

        return list.Count == 0 ? fallback ?? Array.Empty<ReviewSwarmReviewerSettings>() : list;
    }

    internal static IReadOnlyList<ReviewSwarmReviewerSettings> BuildSwarmReviewerSettings(
        IEnumerable<string>? values, IReadOnlyList<ReviewSwarmReviewerSettings>? fallback = null) {
        if (values is null) {
            return fallback ?? Array.Empty<ReviewSwarmReviewerSettings>();
        }

        var normalizedIds = NormalizeSwarmReviewers(values);
        if (normalizedIds.Count == 0) {
            return fallback ?? Array.Empty<ReviewSwarmReviewerSettings>();
        }

        var list = new List<ReviewSwarmReviewerSettings>(normalizedIds.Count);
        foreach (var id in normalizedIds) {
            list.Add(new ReviewSwarmReviewerSettings {
                Id = id
            });
        }
        return list;
    }

    internal static IReadOnlyList<ReviewSwarmReviewerSettings> ParseSwarmReviewerSettingsInput(
        string? value, IReadOnlyList<ReviewSwarmReviewerSettings>? fallback = null) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback ?? Array.Empty<ReviewSwarmReviewerSettings>();
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal) ||
            trimmed.StartsWith("{", StringComparison.Ordinal)) {
            try {
                var node = JsonNodeBase.Parse(trimmed);
                var parsed = ParseSwarmReviewerSettingsNode(node);
                return NormalizeSwarmReviewerSettings(parsed, fallback);
            } catch (JsonException ex) {
                throw new InvalidOperationException(
                    "Invalid swarm_reviewers JSON. Use a CSV list or a JSON reviewer object/array.", ex);
            }
        }

        var reviewerIds = NormalizeSwarmReviewers(ParseList(trimmed));
        return BuildSwarmReviewerSettings(reviewerIds, fallback);
    }

    private static IReadOnlyList<ReviewSwarmReviewerSettings> ParseSwarmReviewerSettingsNode(JsonNodeBase? node) {
        var list = new List<ReviewSwarmReviewerSettings>();
        if (node is JsonArrayNode array) {
            foreach (var item in array) {
                var reviewer = ParseSwarmReviewerSettingNode(item);
                if (reviewer is not null) {
                    list.Add(reviewer);
                }
            }
            return list;
        }

        var single = ParseSwarmReviewerSettingNode(node);
        if (single is not null) {
            list.Add(single);
        }
        return list;
    }

    private static ReviewSwarmReviewerSettings? ParseSwarmReviewerSettingNode(JsonNodeBase? node) {
        if (node is JsonValueNode valueNode && valueNode.TryGetValue<string>(out var idFromValue)) {
            return string.IsNullOrWhiteSpace(idFromValue)
                ? null
                : new ReviewSwarmReviewerSettings { Id = idFromValue };
        }

        if (node is not JsonNodeObject obj) {
            return null;
        }

        var id = GetJsonString(obj, "id");
        if (string.IsNullOrWhiteSpace(id)) {
            return null;
        }

        return new ReviewSwarmReviewerSettings {
            Id = id!,
            AgentProfile = GetJsonString(obj, "agentProfile") ?? GetJsonString(obj, "modelProfile"),
            Provider = ParseOptionalSwarmProviderInput(GetJsonString(obj, "provider")),
            Model = GetJsonString(obj, "model"),
            ReasoningEffort = ParseOptionalReasoningEffortInput(GetJsonString(obj, "reasoningEffort"))
        };
    }

    private static string? GetJsonString(JsonNodeObject obj, string propertyName) {
        var node = obj[propertyName];
        return node is null ? null : node.GetValue<string>();
    }

    private static ReviewProvider? ParseOptionalSwarmProviderInput(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return ReviewProviderContracts.ParseProviderOrThrow(value, "swarm_reviewers.provider");
    }

    private static ReasoningEffort? ParseOptionalReasoningEffortInput(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return ChatEnumParser.ParseReasoningEffort(value);
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

    internal static string NormalizeCopilotLauncher(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "binary" or "cli" or "copilot" => "binary",
            "gh" or "github" or "github-cli" or "github_cli" => "gh",
            "auto" => "auto",
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
