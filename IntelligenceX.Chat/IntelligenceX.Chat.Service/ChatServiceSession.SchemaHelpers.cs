using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private static ToolParameterDto[] ExtractToolParameters(string parametersJson, IReadOnlyCollection<string> requiredArguments) {
        if (string.IsNullOrWhiteSpace(parametersJson)) {
            return Array.Empty<ToolParameterDto>();
        }

        try {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) {
                return Array.Empty<ToolParameterDto>();
            }

            var required = new HashSet<string>(requiredArguments ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var list = new List<ToolParameterDto>();
            foreach (var property in properties.EnumerateObject()) {
                var parameterName = (property.Name ?? string.Empty).Trim();
                if (parameterName.Length == 0) {
                    continue;
                }

                var node = property.Value;
                var enumValues = TryReadEnumValues(node);
                var defaultJson = node.TryGetProperty("default", out var defaultValue)
                    ? NormalizeSchemaJsonSnippet(defaultValue.GetRawText())
                    : null;
                var exampleJson = node.TryGetProperty("example", out var exampleValue)
                    ? NormalizeSchemaJsonSnippet(exampleValue.GetRawText())
                    : (node.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Array && examples.GetArrayLength() > 0
                        ? NormalizeSchemaJsonSnippet(examples[0].GetRawText())
                        : null);

                list.Add(new ToolParameterDto {
                    Name = parameterName,
                    Type = ReadSchemaType(node),
                    Description = node.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                        ? description.GetString()
                        : null,
                    Required = required.Contains(parameterName),
                    EnumValues = enumValues,
                    DefaultJson = defaultJson,
                    ExampleJson = exampleJson
                });
            }

            return list.Count == 0
                ? Array.Empty<ToolParameterDto>()
                : list
                    .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        } catch {
            return Array.Empty<ToolParameterDto>();
        }
    }

    private static string ReadSchemaType(JsonElement node) {
        if (node.TryGetProperty("type", out var typeNode)) {
            if (typeNode.ValueKind == JsonValueKind.String) {
                var value = (typeNode.GetString() ?? string.Empty).Trim();
                if (value.Length > 0) {
                    return value;
                }
            }

            if (typeNode.ValueKind == JsonValueKind.Array) {
                var values = new List<string>();
                foreach (var item in typeNode.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    var value = (item.GetString() ?? string.Empty).Trim();
                    if (value.Length == 0 || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    values.Add(value);
                }

                if (values.Count > 0) {
                    return string.Join("|", values);
                }
            }
        }

        if (node.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array) {
            foreach (var candidate in anyOf.EnumerateArray()) {
                var resolved = ReadSchemaType(candidate);
                if (!string.Equals(resolved, "any", StringComparison.OrdinalIgnoreCase)) {
                    return resolved;
                }
            }
        }

        if (node.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array) {
            foreach (var candidate in oneOf.EnumerateArray()) {
                var resolved = ReadSchemaType(candidate);
                if (!string.Equals(resolved, "any", StringComparison.OrdinalIgnoreCase)) {
                    return resolved;
                }
            }
        }

        return "any";
    }

    private static string[]? TryReadEnumValues(JsonElement node) {
        if (!node.TryGetProperty("enum", out var enumNode) || enumNode.ValueKind != JsonValueKind.Array || enumNode.GetArrayLength() == 0) {
            return null;
        }

        var values = new List<string>();
        foreach (var enumValue in enumNode.EnumerateArray()) {
            var text = enumValue.ValueKind switch {
                JsonValueKind.String => enumValue.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => enumValue.GetRawText(),
                _ => enumValue.GetRawText()
            };

            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            values.Add(text.Trim());
        }

        return values.Count == 0 ? null : values.ToArray();
    }

    private static string? NormalizeSchemaJsonSnippet(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string FormatToolDisplayName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Tool";
        }

        var parts = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) {
            return normalized;
        }

        for (var i = 0; i < parts.Length; i++) {
            var part = parts[i];
            if (part.Length <= 1) {
                parts[i] = part.ToUpperInvariant();
                continue;
            }

            parts[i] = char.ToUpperInvariant(part[0]) + part[1..];
        }

        return string.Join(' ', parts);
    }

    private static string InferToolCategory(string toolName, IReadOnlyList<string> tags) {
        var packId = InferPackIdFromToolName(toolName, tags);
        return packId switch {
            "ad" => "active-directory",
            "eventlog" => "event-log",
            "fs" => "file-system",
            "system" => "system",
            "powershell" => "powershell",
            "email" => "email",
            "testimox" => "testimox",
            "reviewersetup" => "reviewer-setup",
            "reviewer-setup" => "reviewer-setup",
            _ => "other"
        };
    }

    private static string InferPackIdFromToolName(string? toolName, IReadOnlyList<string>? tags) {
        var normalized = (toolName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.StartsWith("ad_", StringComparison.Ordinal)
            || normalized.StartsWith("adplayground_", StringComparison.Ordinal)) {
            return "ad";
        }
        if (normalized.StartsWith("eventlog_", StringComparison.Ordinal)) {
            return "eventlog";
        }
        if (normalized.StartsWith("fs_", StringComparison.Ordinal)) {
            return "fs";
        }
        if (normalized.StartsWith("system_", StringComparison.Ordinal) || normalized.StartsWith("wsl_", StringComparison.Ordinal)) {
            return "system";
        }
        if (normalized.StartsWith("powershell_", StringComparison.Ordinal)) {
            return "powershell";
        }
        if (normalized.StartsWith("email_", StringComparison.Ordinal)) {
            return "email";
        }
        if (normalized.StartsWith("testimox_", StringComparison.Ordinal)) {
            return "testimox";
        }
        if (normalized.StartsWith("reviewer_setup_", StringComparison.Ordinal)) {
            return "reviewer-setup";
        }
        if (normalized.StartsWith("export_", StringComparison.Ordinal)) {
            return "export";
        }

        if (tags is { Count: > 0 }) {
            foreach (var tag in tags) {
                var normalizedTag = (tag ?? string.Empty).Trim().ToLowerInvariant();
                if (normalizedTag.Length == 0) {
                    continue;
                }

                if (normalizedTag.Contains("active-directory", StringComparison.Ordinal)
                    || normalizedTag.Equals("ad", StringComparison.Ordinal)) {
                    return "ad";
                }

                if (normalizedTag.Contains("eventlog", StringComparison.Ordinal)
                    || normalizedTag.Contains("event-log", StringComparison.Ordinal)) {
                    return "eventlog";
                }

                if (normalizedTag.Contains("filesystem", StringComparison.Ordinal)
                    || normalizedTag.Contains("file-system", StringComparison.Ordinal)
                    || normalizedTag.Equals("fs", StringComparison.Ordinal)) {
                    return "fs";
                }

                if (normalizedTag.Contains("powershell", StringComparison.Ordinal)) {
                    return "powershell";
                }
            }
        }

        return "other";
    }

    private static async Task<string> EnsureThreadAsync(IntelligenceXClient client, string? requestThreadId, string? activeThreadId, string? model,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(requestThreadId)) {
            await client.UseThreadAsync(requestThreadId!, cancellationToken).ConfigureAwait(false);
            return requestThreadId!;
        }
        if (string.IsNullOrWhiteSpace(activeThreadId)) {
            var thread = await client.StartNewThreadAsync(model, cancellationToken: cancellationToken).ConfigureAwait(false);
            return thread.Id;
        }
        await client.UseThreadAsync(activeThreadId!, cancellationToken).ConfigureAwait(false);
        return activeThreadId!;
    }

}
