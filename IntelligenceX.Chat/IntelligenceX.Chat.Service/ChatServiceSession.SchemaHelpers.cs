using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
            parts[i] = NormalizeToolDisplayToken(part);
        }

        return string.Join(' ', parts);
    }

    private static string ResolveToolDisplayName(ToolDefinition definition) {
        if (definition is null) {
            return "Tool";
        }

        var explicitDisplayName = ReadOptionalToolMetadata(definition, "DisplayName");
        if (explicitDisplayName.Length > 0) {
            return explicitDisplayName;
        }

        return FormatToolDisplayName(definition.Name);
    }

    private static string ResolveToolCategory(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        return ReadOptionalToolMetadata(definition, "Category");
    }

    private static string NormalizeToolDisplayToken(string token) {
        var normalized = (token ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return normalized switch {
            "ad" => "AD",
            "fs" => "File System",
            "gpo" => "GPO",
            "ldap" => "LDAP",
            "spn" => "SPN",
            "wsl" => "WSL",
            "evtx" => "EVTX",
            "imap" => "IMAP",
            "smtp" => "SMTP",
            "imo" => "IMO",
            "id" => "ID",
            "utc" => "UTC",
            _ => normalized.Length <= 1
                ? normalized.ToUpperInvariant()
                : char.ToUpperInvariant(normalized[0]) + normalized[1..]
        };
    }

    private static string InferToolCategory(string? explicitPackId, string? explicitCategory) {
        var normalizedCategory = NormalizeCategoryLabel(explicitCategory);
        if (normalizedCategory.Length > 0) {
            return normalizedCategory;
        }

        var packId = NormalizePackId(explicitPackId);
        return packId switch {
            "ad" => "active-directory",
            "active_directory" => "active-directory",
            "eventlog" => "event-log",
            "fs" => "file-system",
            "system" => "system",
            "powershell" => "powershell",
            "email" => "email",
            "testimox" => "testimox",
            "officeimo" => "officeimo",
            "reviewersetup" => "reviewer-setup",
            "reviewer_setup" => "reviewer-setup",
            "reviewer-setup" => "reviewer-setup",
            "filesystem" => "file-system",
            _ => "other"
        };
    }

    private static string NormalizeCategoryLabel(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.Replace("_", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "-{2,}", "-", RegexOptions.CultureInvariant);

        return normalized switch {
            "ad" => "active-directory",
            "active-directory" => "active-directory",
            "activedirectory" => "active-directory",
            "eventlog" => "event-log",
            "event-log" => "event-log",
            "fs" => "file-system",
            "file-system" => "file-system",
            "filesystem" => "file-system",
            "reviewersetup" => "reviewer-setup",
            "reviewer-setup" => "reviewer-setup",
            _ => normalized
        };
    }

    private static string ReadOptionalToolMetadata(ToolDefinition definition, string propertyName) {
        if (definition is null || string.IsNullOrWhiteSpace(propertyName)) {
            return string.Empty;
        }

        var property = definition.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || !property.CanRead || property.PropertyType != typeof(string)) {
            return string.Empty;
        }

        var value = (property.GetValue(definition) as string ?? string.Empty).Trim();
        return value;
    }

    private static async Task<string> EnsureThreadAsync(IntelligenceXClient client, string? requestThreadId, string? activeThreadId, string? model,
        CancellationToken cancellationToken) {
        var normalizedModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (!string.IsNullOrWhiteSpace(requestThreadId)) {
            var normalizedRequestThreadId = requestThreadId.Trim();
            try {
                await client.UseThreadAsync(normalizedRequestThreadId, cancellationToken).ConfigureAwait(false);
                return normalizedRequestThreadId;
            } catch (Exception ex) when (ShouldRecoverMissingTransportThread(ex)) {
                var recoveredThread = await client.StartNewThreadAsync(normalizedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
                return recoveredThread.Id;
            }
        }
        if (string.IsNullOrWhiteSpace(activeThreadId)) {
            var thread = await client.StartNewThreadAsync(normalizedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
            return thread.Id;
        }
        var normalizedActiveThreadId = activeThreadId.Trim();
        try {
            await client.UseThreadAsync(normalizedActiveThreadId, cancellationToken).ConfigureAwait(false);
            return normalizedActiveThreadId;
        } catch (Exception ex) when (ShouldRecoverMissingTransportThread(ex)) {
            var recoveredThread = await client.StartNewThreadAsync(normalizedModel, cancellationToken: cancellationToken).ConfigureAwait(false);
            return recoveredThread.Id;
        }
    }

    internal static bool ShouldRecoverMissingTransportThread(Exception ex) {
        return ChatThreadRecoveryHeuristics.IsMissingTransportThreadError(ex);
    }

}
