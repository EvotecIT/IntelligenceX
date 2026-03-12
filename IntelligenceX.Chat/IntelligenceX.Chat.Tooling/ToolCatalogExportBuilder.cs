using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using JsonValueKind = System.Text.Json.JsonValueKind;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared projection helpers for lightweight tool catalog exports used by Chat host and service surfaces.
/// </summary>
public static class ToolCatalogExportBuilder {
    /// <summary>
    /// Builds client-facing tool definition DTOs from runtime tool definitions and orchestration metadata.
    /// </summary>
    public static ToolDefinitionDto[] BuildToolDefinitionDtos(
        IReadOnlyList<ToolDefinition> definitions,
        ToolOrchestrationCatalog orchestrationCatalog,
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability) {
        if (definitions is null || definitions.Count == 0) {
            return Array.Empty<ToolDefinitionDto>();
        }

        var packLookup = BuildPackAvailabilityLookup(packAvailability);
        var tools = new ToolDefinitionDto[definitions.Count];
        for (var i = 0; i < definitions.Count; i++) {
            tools[i] = BuildToolDefinitionDto(definitions[i], orchestrationCatalog, packLookup);
        }

        return tools;
    }

    /// <summary>
    /// Builds pack DTOs enriched with autonomy summaries from availability metadata and orchestration contracts.
    /// </summary>
    public static ToolPackInfoDto[] BuildPackInfoDtos(
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        var list = new List<ToolPackInfoDto>();
        foreach (var pack in packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()) {
            list.Add(new ToolPackInfoDto {
                Id = pack.Id,
                Name = ToolPackMetadataNormalizer.ResolveDisplayName(pack.Id, pack.Name),
                Description = string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim(),
                Tier = MapTier(pack.Tier),
                Enabled = pack.Enabled,
                DisabledReason = pack.Enabled || string.IsNullOrWhiteSpace(pack.DisabledReason) ? null : pack.DisabledReason.Trim(),
                IsDangerous = pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite,
                SourceKind = ToolPackMetadataNormalizer.ResolveSourceKind(pack.SourceKind, pack.Id),
                AutonomySummary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary(pack.Id, orchestrationCatalog)
            });
        }

        return list
            .OrderBy(static pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Projects routing diagnostics into the protocol DTO consumed by lightweight tooling/bootstrap surfaces.
    /// </summary>
    public static SessionRoutingCatalogDiagnosticsDto? BuildRoutingCatalogDiagnosticsDto(ToolRoutingCatalogDiagnostics? diagnostics) {
        if (diagnostics is null) {
            return null;
        }

        var familyActions = diagnostics.FamilyActions.Count == 0
            ? Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            : diagnostics.FamilyActions
                .Select(static item => new SessionRoutingFamilyActionSummaryDto {
                    Family = item.Family,
                    ActionId = item.ActionId,
                    ToolCount = Math.Max(0, item.ToolCount)
                })
                .ToArray();
        var autonomyReadinessHighlights = ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(diagnostics, maxItems: 6);

        return new SessionRoutingCatalogDiagnosticsDto {
            TotalTools = Math.Max(0, diagnostics.TotalTools),
            RoutingAwareTools = Math.Max(0, diagnostics.RoutingAwareTools),
            ExplicitRoutingTools = Math.Max(0, diagnostics.ExplicitRoutingTools),
            InferredRoutingTools = Math.Max(0, diagnostics.InferredRoutingTools),
            MissingRoutingContractTools = Math.Max(0, diagnostics.MissingRoutingContractTools),
            MissingPackIdTools = Math.Max(0, diagnostics.MissingPackIdTools),
            MissingRoleTools = Math.Max(0, diagnostics.MissingRoleTools),
            SetupAwareTools = Math.Max(0, diagnostics.SetupAwareTools),
            HandoffAwareTools = Math.Max(0, diagnostics.HandoffAwareTools),
            RecoveryAwareTools = Math.Max(0, diagnostics.RecoveryAwareTools),
            RemoteCapableTools = Math.Max(0, diagnostics.RemoteCapableTools),
            CrossPackHandoffTools = Math.Max(0, diagnostics.CrossPackHandoffTools),
            DomainFamilyTools = Math.Max(0, diagnostics.DomainFamilyTools),
            ExpectedDomainFamilyMissingTools = Math.Max(0, diagnostics.ExpectedDomainFamilyMissingTools),
            DomainFamilyMissingActionTools = Math.Max(0, diagnostics.DomainFamilyMissingActionTools),
            ActionWithoutFamilyTools = Math.Max(0, diagnostics.ActionWithoutFamilyTools),
            FamilyActionConflictFamilies = Math.Max(0, diagnostics.FamilyActionConflictFamilies),
            IsHealthy = diagnostics.IsHealthy,
            IsExplicitRoutingReady = diagnostics.IsExplicitRoutingReady,
            FamilyActions = familyActions,
            AutonomyReadinessHighlights = autonomyReadinessHighlights.Count == 0
                ? Array.Empty<string>()
                : autonomyReadinessHighlights.ToArray()
        };
    }

    /// <summary>
    /// Normalizes a tool category token for external tool-list payloads.
    /// </summary>
    public static string ResolveToolListCategory(string? explicitCategory) {
        var normalized = (explicitCategory ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);
        while (normalized.Contains("--", StringComparison.Ordinal)) {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        return normalized.Length == 0 ? "other" : normalized;
    }

    private static Dictionary<string, ToolPackAvailabilityInfo> BuildPackAvailabilityLookup(IEnumerable<ToolPackAvailabilityInfo>? packAvailability) {
        return (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack is not null)
            .GroupBy(static pack => ToolPackMetadataNormalizer.NormalizePackId(pack.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static ToolDefinitionDto BuildToolDefinitionDto(
        ToolDefinition definition,
        ToolOrchestrationCatalog orchestrationCatalog,
        IReadOnlyDictionary<string, ToolPackAvailabilityInfo> packLookup) {
        var parametersJson = definition.Parameters is null ? "{}" : JsonLite.Serialize(definition.Parameters);
        var requiredArguments = ExtractRequiredArguments(parametersJson);
        var parameters = ExtractToolParameters(parametersJson, requiredArguments);
        ToolOrchestrationCatalogEntry? orchestrationEntry = null;
        string? packId = null;
        string? packName = null;
        string? packDescription = null;
        ToolPackSourceKind? packSourceKind = null;
        if (orchestrationCatalog.TryGetEntry(definition.Name, out var resolvedEntry)) {
            orchestrationEntry = resolvedEntry;
            if (resolvedEntry.PackId.Length > 0) {
                packId = resolvedEntry.PackId;
                if (packLookup.TryGetValue(resolvedEntry.PackId, out var pack)) {
                    packName = ToolPackMetadataNormalizer.ResolveDisplayName(pack.Id, pack.Name);
                    packDescription = string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim();
                    packSourceKind = ToolPackMetadataNormalizer.ResolveSourceKind(pack.SourceKind, pack.Id);
                }
            }
        }

        return new ToolDefinitionDto {
            Name = definition.Name,
            Description = definition.Description ?? string.Empty,
            DisplayName = ResolveToolDisplayName(definition),
            Category = ResolveToolListCategory(definition.Category),
            Tags = definition.Tags.Count == 0 ? null : definition.Tags.ToArray(),
            PackId = string.IsNullOrWhiteSpace(packId) ? null : packId,
            PackName = string.IsNullOrWhiteSpace(packName) ? null : packName,
            PackDescription = string.IsNullOrWhiteSpace(packDescription) ? null : packDescription,
            PackSourceKind = packSourceKind,
            IsWriteCapable = definition.WriteGovernance?.IsWriteCapable == true,
            ExecutionScope = orchestrationEntry?.ExecutionScope ?? "local_only",
            SupportsTargetScoping = orchestrationEntry?.SupportsTargetScoping == true,
            TargetScopeArguments = orchestrationEntry?.TargetScopeArguments?.ToArray() ?? Array.Empty<string>(),
            SupportsRemoteHostTargeting = orchestrationEntry?.SupportsRemoteHostTargeting == true,
            RemoteHostArguments = orchestrationEntry?.RemoteHostArguments?.ToArray() ?? Array.Empty<string>(),
            IsSetupAware = orchestrationEntry?.IsSetupAware == true,
            SetupToolName = string.IsNullOrWhiteSpace(orchestrationEntry?.SetupToolName) ? null : orchestrationEntry!.SetupToolName,
            IsHandoffAware = orchestrationEntry?.IsHandoffAware == true,
            HandoffTargetPackIds = orchestrationEntry?.HandoffEdges
                .Select(static edge => edge.TargetPackId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>(),
            HandoffTargetToolNames = orchestrationEntry?.HandoffEdges
                .Select(static edge => edge.TargetToolName)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>(),
            IsRecoveryAware = orchestrationEntry?.IsRecoveryAware == true,
            SupportsTransientRetry = orchestrationEntry?.SupportsTransientRetry == true,
            MaxRetryAttempts = orchestrationEntry?.MaxRetryAttempts ?? 0,
            RecoveryToolNames = orchestrationEntry?.RecoveryToolNames?.ToArray() ?? Array.Empty<string>(),
            ParametersJson = parametersJson,
            RequiredArguments = requiredArguments,
            Parameters = parameters
        };
    }

    private static string ResolveToolDisplayName(ToolDefinition definition) {
        var explicitDisplayName = (definition.DisplayName ?? string.Empty).Trim();
        if (explicitDisplayName.Length > 0) {
            return explicitDisplayName;
        }

        return FormatToolDisplayName(definition.Name);
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

    private static string[] ExtractRequiredArguments(string parametersJson) {
        if (string.IsNullOrWhiteSpace(parametersJson)) {
            return Array.Empty<string>();
        }

        try {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty("required", out var requiredNode) || requiredNode.ValueKind != JsonValueKind.Array) {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var item in requiredNode.EnumerateArray()) {
                if (item.ValueKind != JsonValueKind.String) {
                    continue;
                }

                var value = (item.GetString() ?? string.Empty).Trim();
                if (value.Length == 0) {
                    continue;
                }

                list.Add(value);
            }

            return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
        } catch {
            return Array.Empty<string>();
        }
    }

    private static ToolParameterDto[] ExtractToolParameters(string parametersJson, IReadOnlyCollection<string> requiredArguments) {
        if (string.IsNullOrWhiteSpace(parametersJson)) {
            return Array.Empty<ToolParameterDto>();
        }

        try {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty("properties", out var propertiesNode) || propertiesNode.ValueKind != JsonValueKind.Object) {
                return Array.Empty<ToolParameterDto>();
            }

            var required = new HashSet<string>(requiredArguments ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var list = new List<ToolParameterDto>();
            foreach (var property in propertiesNode.EnumerateObject()) {
                var parameterName = (property.Name ?? string.Empty).Trim();
                if (parameterName.Length == 0) {
                    continue;
                }

                var node = property.Value;
                var defaultJson = node.TryGetProperty("default", out var defaultValue)
                    ? NormalizeSchemaJsonSnippet(defaultValue.GetRawText())
                    : null;
                var exampleJson = node.TryGetProperty("example", out var exampleValue)
                    ? NormalizeSchemaJsonSnippet(exampleValue.GetRawText())
                    : (node.TryGetProperty("examples", out var examplesNode) && examplesNode.ValueKind == JsonValueKind.Array && examplesNode.GetArrayLength() > 0
                        ? NormalizeSchemaJsonSnippet(examplesNode[0].GetRawText())
                        : null);
                list.Add(new ToolParameterDto {
                    Name = parameterName,
                    Type = ReadSchemaType(node),
                    Description = node.TryGetProperty("description", out var descriptionNode) && descriptionNode.ValueKind == JsonValueKind.String
                        ? descriptionNode.GetString()
                        : null,
                    Required = required.Contains(parameterName),
                    EnumValues = TryReadEnumValues(node),
                    DefaultJson = defaultJson,
                    ExampleJson = exampleJson
                });
            }

            return list.Count == 0
                ? Array.Empty<ToolParameterDto>()
                : list.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
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

        if (node.TryGetProperty("anyOf", out var anyOfNode) && anyOfNode.ValueKind == JsonValueKind.Array) {
            foreach (var candidate in anyOfNode.EnumerateArray()) {
                var resolved = ReadSchemaType(candidate);
                if (!string.Equals(resolved, "any", StringComparison.OrdinalIgnoreCase)) {
                    return resolved;
                }
            }
        }

        if (node.TryGetProperty("oneOf", out var oneOfNode) && oneOfNode.ValueKind == JsonValueKind.Array) {
            foreach (var candidate in oneOfNode.EnumerateArray()) {
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
            var value = enumValue.ValueKind switch {
                JsonValueKind.String => enumValue.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => enumValue.GetRawText(),
                _ => enumValue.GetRawText()
            };
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            values.Add(value.Trim());
        }

        return values.Count == 0 ? null : values.ToArray();
    }

    private static string? NormalizeSchemaJsonSnippet(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static CapabilityTier MapTier(ToolCapabilityTier tier) {
        return tier switch {
            ToolCapabilityTier.ReadOnly => CapabilityTier.ReadOnly,
            ToolCapabilityTier.SensitiveRead => CapabilityTier.SensitiveRead,
            ToolCapabilityTier.DangerousWrite => CapabilityTier.DangerousWrite,
            _ => CapabilityTier.SensitiveRead
        };
    }
}
