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
    private const int ToolCatalogOrderPriorityOperational = 0;
    private const int ToolCatalogOrderPriorityResolver = 1;
    private const int ToolCatalogOrderPriorityDiagnostic = 2;
    private const int ToolCatalogOrderPriorityGeneral = 3;
    private const int ToolCatalogOrderPriorityEnvironmentDiscover = 4;
    private const int ToolCatalogOrderPriorityHelper = 5;
    private const int ToolCatalogOrderPriorityPackInfo = 6;
    private const int ToolCatalogOrderPriorityFallback = 7;

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

        return OrderToolDefinitionDtosForCatalog(tools);
    }

    /// <summary>
    /// Builds pack DTOs enriched with autonomy summaries from availability metadata and orchestration contracts.
    /// </summary>
    public static ToolPackInfoDto[] BuildPackInfoDtos(
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        var list = new List<ToolPackInfoDto>();
        var seenPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()) {
            var normalizedPackId = ToolPackMetadataNormalizer.NormalizePackId(pack.Id);
            if (normalizedPackId.Length > 0) {
                seenPackIds.Add(normalizedPackId);
            }

            list.Add(BuildPackInfoDto(pack, orchestrationCatalog));
        }

        if (orchestrationCatalog is not null) {
            foreach (var packId in orchestrationCatalog.GetKnownPackIds()) {
                if (!seenPackIds.Add(packId)
                    || !orchestrationCatalog.TryGetPackMetadata(packId, out var packMetadata)) {
                    continue;
                }

                list.Add(BuildPackInfoDtoFromMetadata(packMetadata, orchestrationCatalog));
            }
        }

        return list
            .OrderBy(static pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Builds plugin DTOs from runtime plugin availability, falling back to exported pack summaries when plugin metadata is unavailable.
    /// </summary>
    public static PluginInfoDto[] BuildPluginInfoDtos(
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        IReadOnlyList<ToolPackInfoDto>? packs,
        IEnumerable<ToolPluginCatalogInfo>? pluginCatalog = null) {
        var pluginList = new List<PluginInfoDto>();
        var normalizedPlugins = (pluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>())
            .Where(static plugin => plugin is not null)
            .ToArray();
        var normalizedCatalog = (pluginCatalog ?? Array.Empty<ToolPluginCatalogInfo>())
            .Where(static plugin => plugin is not null)
            .ToArray();
        var packLookup = BuildPackInfoLookup(packs);
        var availabilityLookup = BuildPluginAvailabilityLookup(normalizedPlugins);
        var seenPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (normalizedPlugins.Length == 0 && normalizedCatalog.Length == 0) {
            foreach (var pack in packs ?? Array.Empty<ToolPackInfoDto>()) {
                pluginList.Add(new PluginInfoDto {
                    Id = pack.Id,
                    Name = pack.Name,
                    Origin = ResolveSyntheticPluginOrigin(pack.SourceKind),
                    SourceKind = pack.SourceKind,
                    DefaultEnabled = pack.Enabled,
                    Enabled = pack.Enabled,
                    ActivationState = ToolActivationStates.Resolve(pack.Enabled, ToolActivationStates.IsDeferred(pack.ActivationState)),
                    CanActivateOnDemand = pack.CanActivateOnDemand,
                    DisabledReason = pack.Enabled ? null : pack.DisabledReason,
                    IsDangerous = pack.IsDangerous || pack.Tier == CapabilityTier.DangerousWrite,
                    PackIds = string.IsNullOrWhiteSpace(pack.Id) ? Array.Empty<string>() : new[] { ToolPackMetadataNormalizer.NormalizePackId(pack.Id) },
                    SkillDirectories = Array.Empty<string>(),
                    SkillIds = Array.Empty<string>()
                });
            }
        } else {
            foreach (var catalogEntry in normalizedCatalog) {
                var normalizedPluginId = ToolPackMetadataNormalizer.NormalizePackId(catalogEntry.Id);
                if (normalizedPluginId.Length == 0 || !seenPluginIds.Add(normalizedPluginId)) {
                    continue;
                }

                availabilityLookup.TryGetValue(normalizedPluginId, out var availability);
                pluginList.Add(BuildPluginInfoDto(catalogEntry, availability, packLookup));
            }

            foreach (var plugin in normalizedPlugins) {
                var normalizedPluginId = ToolPackMetadataNormalizer.NormalizePackId(plugin.Id);
                if (normalizedPluginId.Length > 0 && !seenPluginIds.Add(normalizedPluginId)) {
                    continue;
                }

                pluginList.Add(BuildPluginInfoDto(plugin, packLookup));
            }
        }

        return pluginList
            .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Builds a nested runtime tooling snapshot from normalized pack/plugin export DTOs.
    /// </summary>
    public static SessionCapabilityToolingSnapshotDto? BuildCapabilityToolingSnapshotDto(
        IReadOnlyList<ToolPackInfoDto>? packs,
        IReadOnlyList<PluginInfoDto>? plugins,
        string? source = null) {
        var normalizedPacks = (packs ?? Array.Empty<ToolPackInfoDto>())
            .Where(static pack => pack is not null)
            .ToArray();
        var normalizedPlugins = (plugins ?? Array.Empty<PluginInfoDto>())
            .Where(static plugin => plugin is not null)
            .ToArray();
        if (normalizedPacks.Length == 0 && normalizedPlugins.Length == 0) {
            return null;
        }

        return new SessionCapabilityToolingSnapshotDto {
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            Packs = normalizedPacks,
            Plugins = normalizedPlugins
        };
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
                    ToolCount = Math.Max(0, item.ToolCount),
                    DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? null : item.DisplayName.Trim(),
                    ReplyExample = string.IsNullOrWhiteSpace(item.ReplyExample) ? null : item.ReplyExample.Trim(),
                    ChoiceDescription = string.IsNullOrWhiteSpace(item.ChoiceDescription) ? null : item.ChoiceDescription.Trim(),
                    RepresentativePackIds = item.RepresentativePackIds is { Length: > 0 } ? item.RepresentativePackIds : null
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
            EnvironmentDiscoverTools = Math.Max(0, diagnostics.EnvironmentDiscoverTools),
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

    /// <summary>
    /// Applies a request-independent, contract-aware default ordering for exported tool catalogs.
    /// </summary>
    public static ToolDefinitionDto[] OrderToolDefinitionDtosForCatalog(IReadOnlyList<ToolDefinitionDto>? definitions) {
        if (definitions is not { Count: > 0 }) {
            return Array.Empty<ToolDefinitionDto>();
        }

        if (definitions.Count == 1) {
            return definitions[0] is null ? Array.Empty<ToolDefinitionDto>() : new[] { definitions[0] };
        }

        var helperTargetToolNames = BuildHelperTargetToolNameSet(definitions);
        return definitions
            .Select(static (definition, index) => new CatalogOrderedToolDefinition(definition, index))
            .Where(static entry => entry.Definition is not null)
            .OrderBy(entry => GetCatalogToolPriority(entry.Definition!, helperTargetToolNames))
            .ThenBy(static entry => HasRepresentativeExamples(entry.Definition!) ? 0 : 1)
            .ThenBy(static entry => entry.Definition!.IsWriteCapable ? 1 : 0)
            .ThenBy(static entry => NormalizeCatalogToken(entry.Definition!.PackId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => NormalizeCatalogToken(entry.Definition!.PackName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => NormalizeCatalogToken(entry.Definition!.DisplayName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => NormalizeCatalogToken(entry.Definition!.Name), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Index)
            .Select(static entry => entry.Definition!)
            .ToArray();
    }

    private static Dictionary<string, ToolPackAvailabilityInfo> BuildPackAvailabilityLookup(IEnumerable<ToolPackAvailabilityInfo>? packAvailability) {
        return (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack is not null)
            .GroupBy(static pack => ToolPackMetadataNormalizer.NormalizePackId(pack.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildHelperTargetToolNameSet(IReadOnlyList<ToolDefinitionDto> definitions) {
        var helperTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            AddHelperTargetName(helperTargets, definition.ProbeToolName);
            AddHelperTargetName(helperTargets, definition.SetupToolName);
            AddHelperTargetNames(helperTargets, definition.RecoveryToolNames);
        }

        return helperTargets;
    }

    private static void AddHelperTargetNames(HashSet<string> targets, IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return;
        }

        for (var i = 0; i < values.Count; i++) {
            AddHelperTargetName(targets, values[i]);
        }
    }

    private static void AddHelperTargetName(HashSet<string> targets, string? value) {
        var normalized = NormalizeCatalogToken(value);
        if (normalized.Length > 0) {
            targets.Add(normalized);
        }
    }

    private static int GetCatalogToolPriority(ToolDefinitionDto definition, IReadOnlySet<string> helperTargetToolNames) {
        var toolName = NormalizeCatalogToken(definition.Name);
        var isHelperTarget = toolName.Length > 0 && helperTargetToolNames.Contains(toolName);
        if (definition.IsPackInfoTool
            || string.Equals(definition.RoutingRole, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return ToolCatalogOrderPriorityPackInfo;
        }

        if (definition.IsEnvironmentDiscoverTool
            || string.Equals(definition.RoutingRole, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)) {
            return ToolCatalogOrderPriorityEnvironmentDiscover;
        }

        if (isHelperTarget) {
            return ToolCatalogOrderPriorityHelper;
        }

        if (string.Equals(definition.RoutingRole, ToolRoutingTaxonomy.RoleOperational, StringComparison.OrdinalIgnoreCase)) {
            return ToolCatalogOrderPriorityOperational;
        }

        if (string.Equals(definition.RoutingRole, ToolRoutingTaxonomy.RoleResolver, StringComparison.OrdinalIgnoreCase)) {
            return ToolCatalogOrderPriorityResolver;
        }

        if (string.Equals(definition.RoutingRole, ToolRoutingTaxonomy.RoleDiagnostic, StringComparison.OrdinalIgnoreCase)) {
            return ToolCatalogOrderPriorityDiagnostic;
        }

        if (!string.IsNullOrWhiteSpace(definition.Name)) {
            return ToolCatalogOrderPriorityGeneral;
        }

        return ToolCatalogOrderPriorityFallback;
    }

    private static bool HasRepresentativeExamples(ToolDefinitionDto definition) {
        return definition.RepresentativeExamples is { Length: > 0 };
    }

    private static string NormalizeCatalogToken(string? value) {
        return (value ?? string.Empty).Trim();
    }

    private sealed record CatalogOrderedToolDefinition(ToolDefinitionDto? Definition, int Index);

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
                    packSourceKind = ToolPackMetadataNormalizer.ResolveSourceKind(pack.SourceKind);
                }
            }
        }

        return new ToolDefinitionDto {
            Name = definition.Name,
            Description = definition.Description ?? string.Empty,
            DisplayName = ResolveToolDisplayName(definition),
            Category = ResolveExportToolCategory(definition, orchestrationEntry, packLookup),
            Tags = definition.Tags.Count == 0 ? null : definition.Tags.ToArray(),
            PackId = string.IsNullOrWhiteSpace(packId) ? null : packId,
            RoutingRole = string.IsNullOrWhiteSpace(orchestrationEntry?.Role) ? null : orchestrationEntry!.Role,
            RoutingScope = string.IsNullOrWhiteSpace(orchestrationEntry?.Scope) ? null : orchestrationEntry!.Scope,
            RoutingOperation = string.IsNullOrWhiteSpace(orchestrationEntry?.Operation) ? null : orchestrationEntry!.Operation,
            RoutingEntity = string.IsNullOrWhiteSpace(orchestrationEntry?.Entity) ? null : orchestrationEntry!.Entity,
            RoutingRisk = string.IsNullOrWhiteSpace(orchestrationEntry?.Risk) ? null : orchestrationEntry!.Risk,
            RoutingSource = string.IsNullOrWhiteSpace(orchestrationEntry?.RoutingSource) ? null : orchestrationEntry!.RoutingSource,
            DomainIntentFamily = string.IsNullOrWhiteSpace(orchestrationEntry?.DomainIntentFamily) ? null : orchestrationEntry!.DomainIntentFamily,
            DomainIntentActionId = string.IsNullOrWhiteSpace(orchestrationEntry?.DomainIntentActionId) ? null : orchestrationEntry!.DomainIntentActionId,
            DomainIntentFamilyDisplayName = string.IsNullOrWhiteSpace(orchestrationEntry?.DomainIntentFamilyDisplayName) ? null : orchestrationEntry!.DomainIntentFamilyDisplayName,
            DomainIntentFamilyReplyExample = string.IsNullOrWhiteSpace(orchestrationEntry?.DomainIntentFamilyReplyExample) ? null : orchestrationEntry!.DomainIntentFamilyReplyExample,
            DomainIntentFamilyChoiceDescription = string.IsNullOrWhiteSpace(orchestrationEntry?.DomainIntentFamilyChoiceDescription) ? null : orchestrationEntry!.DomainIntentFamilyChoiceDescription,
            PackName = string.IsNullOrWhiteSpace(packName) ? null : packName,
            PackDescription = string.IsNullOrWhiteSpace(packDescription) ? null : packDescription,
            PackSourceKind = packSourceKind,
            IsPackInfoTool = orchestrationEntry?.IsPackInfoTool == true,
            IsEnvironmentDiscoverTool = orchestrationEntry?.IsEnvironmentDiscoverTool == true,
            IsWriteCapable = orchestrationEntry?.IsWriteCapable ?? definition.WriteGovernance?.IsWriteCapable == true,
            RequiresWriteGovernance = orchestrationEntry?.RequiresWriteGovernance ?? definition.WriteGovernance?.RequiresGovernanceAuthorization == true,
            WriteGovernanceContractId = string.IsNullOrWhiteSpace(orchestrationEntry?.WriteGovernanceContractId)
                ? (string.IsNullOrWhiteSpace(definition.WriteGovernance?.GovernanceContractId) ? null : definition.WriteGovernance!.GovernanceContractId)
                : orchestrationEntry!.WriteGovernanceContractId,
            RequiresAuthentication = orchestrationEntry?.RequiresAuthentication == true,
            AuthenticationContractId = string.IsNullOrWhiteSpace(orchestrationEntry?.AuthenticationContractId) ? null : orchestrationEntry!.AuthenticationContractId,
            AuthenticationArguments = orchestrationEntry?.AuthenticationArguments?.ToArray() ?? Array.Empty<string>(),
            SupportsConnectivityProbe = orchestrationEntry?.SupportsConnectivityProbe == true,
            ProbeToolName = string.IsNullOrWhiteSpace(orchestrationEntry?.ProbeToolName) ? null : orchestrationEntry!.ProbeToolName,
            IsExecutionAware = orchestrationEntry?.IsExecutionAware == true,
            ExecutionContractId = string.IsNullOrWhiteSpace(orchestrationEntry?.ExecutionContractId) ? null : orchestrationEntry!.ExecutionContractId,
            ExecutionScope = orchestrationEntry?.ExecutionScope ?? ToolExecutionScopes.LocalOnly,
            SupportsLocalExecution = orchestrationEntry?.SupportsLocalExecution ?? true,
            SupportsRemoteExecution = orchestrationEntry?.SupportsRemoteExecution == true,
            SupportsTargetScoping = orchestrationEntry?.SupportsTargetScoping == true,
            TargetScopeArguments = orchestrationEntry?.TargetScopeArguments?.ToArray() ?? Array.Empty<string>(),
            SupportsRemoteHostTargeting = orchestrationEntry?.SupportsRemoteHostTargeting == true,
            RemoteHostArguments = orchestrationEntry?.RemoteHostArguments?.ToArray() ?? Array.Empty<string>(),
            RepresentativeExamples = orchestrationEntry?.RepresentativeExamples?.ToArray() ?? Array.Empty<string>(),
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

    private static string ResolveExportToolCategory(
        ToolDefinition definition,
        ToolOrchestrationCatalogEntry? orchestrationEntry,
        IReadOnlyDictionary<string, ToolPackAvailabilityInfo> packLookup) {
        var explicitCategory = (definition.Category ?? string.Empty).Trim();
        if (explicitCategory.Length > 0) {
            return ResolveToolListCategory(explicitCategory);
        }

        var packId = ToolPackBootstrap.NormalizePackId(orchestrationEntry?.PackId);
        if (packId.Length == 0 && ToolSelectionMetadata.TryResolvePackId(definition, out var inferredPackId)) {
            packId = ToolPackBootstrap.NormalizePackId(inferredPackId);
        }

        if (packId.Length > 0
            && packLookup.TryGetValue(packId, out var pack)
            && !string.IsNullOrWhiteSpace(pack.Category)) {
            return ResolveToolListCategory(pack.Category);
        }

        var enrichedCategory = (ToolSelectionMetadata.Enrich(definition, toolType: null).Category ?? string.Empty).Trim();
        if (enrichedCategory.Length > 0
            && !string.Equals(enrichedCategory, "general", StringComparison.OrdinalIgnoreCase)) {
            return ResolveToolListCategory(enrichedCategory);
        }

        if (enrichedCategory.Length > 0) {
            return ResolveToolListCategory(enrichedCategory);
        }

        return ResolveToolListCategory(explicitCategory);
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

    private static ToolPackInfoDto BuildPackInfoDto(
        ToolPackAvailabilityInfo pack,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        var autonomySummary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary(pack.Id, orchestrationCatalog);
        var exposesWriteCapability = ExposesWriteCapability(autonomySummary);
        var packMetadata = TryResolvePackMetadata(pack.Id, orchestrationCatalog);
        var displayName = !string.IsNullOrWhiteSpace(pack.Name)
            ? ToolPackMetadataNormalizer.ResolveDisplayName(pack.Id, pack.Name)
            : ResolvePackDisplayName(pack.Id, packMetadata);
        var sourceKind = !string.IsNullOrWhiteSpace(pack.SourceKind)
            ? ToolPackMetadataNormalizer.ResolveSourceKind(pack.SourceKind)
            : ResolvePackSourceKind(packMetadata);
        var category = !string.IsNullOrWhiteSpace(pack.Category)
            ? pack.Category.Trim()
            : ResolvePackCategory(packMetadata);
        var engineId = !string.IsNullOrWhiteSpace(pack.EngineId)
            ? pack.EngineId.Trim()
            : ResolvePackEngineId(packMetadata);
        var capabilityTags = NormalizeDistinctNonEmptyStrings(pack.CapabilityTags);
        if (capabilityTags.Length == 0) {
            capabilityTags = ResolvePackCapabilityTags(packMetadata);
        }

        return new ToolPackInfoDto {
            Id = pack.Id,
            Name = displayName,
            Description = string.IsNullOrWhiteSpace(pack.Description) ? null : pack.Description.Trim(),
            Tier = MapTier(pack.Tier),
            Enabled = pack.Enabled,
            ActivationState = ToolActivationStates.Resolve(pack.Enabled, pack.DescriptorOnly),
            CanActivateOnDemand = pack.Enabled && pack.DescriptorOnly,
            DisabledReason = pack.Enabled || string.IsNullOrWhiteSpace(pack.DisabledReason) ? null : pack.DisabledReason.Trim(),
            IsDangerous = pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite || exposesWriteCapability,
            SourceKind = sourceKind,
            Category = category,
            EngineId = engineId,
            Aliases = NormalizeDistinctNonEmptyStrings(pack.Aliases),
            CapabilityTags = capabilityTags,
            SearchTokens = NormalizeDistinctNonEmptyStrings(pack.SearchTokens),
            AutonomySummary = autonomySummary
        };
    }

    private static ToolPackInfoDto BuildPackInfoDtoFromMetadata(
        ToolOrchestrationPackMetadata packMetadata,
        ToolOrchestrationCatalog orchestrationCatalog) {
        var autonomySummary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary(packMetadata.PackId, orchestrationCatalog);
        var exposesWriteCapability = ExposesWriteCapability(autonomySummary);
        var isDangerous = packMetadata.IsDangerous || exposesWriteCapability;
        return new ToolPackInfoDto {
            Id = packMetadata.PackId,
            Name = ResolvePackDisplayName(packMetadata.PackId, packMetadata),
            Tier = MapTier(packMetadata.Tier),
            Enabled = true,
            ActivationState = ToolActivationStates.Active,
            CanActivateOnDemand = false,
            IsDangerous = isDangerous,
            SourceKind = ResolvePackSourceKind(packMetadata),
            Category = ResolvePackCategory(packMetadata),
            EngineId = ResolvePackEngineId(packMetadata),
            Aliases = Array.Empty<string>(),
            CapabilityTags = ResolvePackCapabilityTags(packMetadata),
            SearchTokens = Array.Empty<string>(),
            AutonomySummary = autonomySummary
        };
    }

    private static ToolOrchestrationPackMetadata? TryResolvePackMetadata(
        string? packId,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        if (orchestrationCatalog is null || !orchestrationCatalog.TryGetPackMetadata(packId, out var metadata)) {
            return null;
        }

        return metadata;
    }

    private static bool ExposesWriteCapability(ToolPackAutonomySummaryDto? autonomySummary) {
        return autonomySummary is not null
               && (autonomySummary.WriteCapableTools > 0 || autonomySummary.GovernedWriteTools > 0);
    }

    private static string ResolvePackDisplayName(string? packId, ToolOrchestrationPackMetadata? packMetadata) {
        if (packMetadata is not null && !string.IsNullOrWhiteSpace(packMetadata.DisplayName)) {
            return packMetadata.DisplayName.Trim();
        }

        return ToolPackMetadataNormalizer.ResolveDisplayName(packId, fallbackName: null);
    }

    private static ToolPackSourceKind ResolvePackSourceKind(ToolOrchestrationPackMetadata? packMetadata) {
        return ToolPackMetadataNormalizer.ResolveSourceKind(packMetadata?.SourceKind);
    }

    private static string? ResolvePackCategory(ToolOrchestrationPackMetadata? packMetadata) {
        return string.IsNullOrWhiteSpace(packMetadata?.Category)
            ? null
            : packMetadata.Category.Trim();
    }

    private static string? ResolvePackEngineId(ToolOrchestrationPackMetadata? packMetadata) {
        return string.IsNullOrWhiteSpace(packMetadata?.EngineId)
            ? null
            : packMetadata.EngineId.Trim();
    }

    private static string[] ResolvePackCapabilityTags(ToolOrchestrationPackMetadata? packMetadata) {
        return NormalizeDistinctNonEmptyStrings(packMetadata?.CapabilityTags);
    }

    private static string ResolveSyntheticPluginOrigin(ToolPackSourceKind sourceKind) {
        return sourceKind switch {
            ToolPackSourceKind.Builtin => "builtin",
            ToolPackSourceKind.ClosedSource => "closed_source",
            _ => "open_source"
        };
    }

    private static PluginInfoDto BuildPluginInfoDto(
        ToolPluginCatalogInfo catalog,
        ToolPluginAvailabilityInfo? availability,
        IReadOnlyDictionary<string, ToolPackInfoDto> packLookup) {
        var normalizedCatalogPluginId = ToolPackMetadataNormalizer.NormalizePackId(catalog.Id);
        var resolvedPackIds = ResolvePluginPackIds(availability, catalog, packLookup);
        var resolvedPacks = ResolvePluginPacks(resolvedPackIds, packLookup);
        var enabled = availability?.Enabled
            ?? resolvedPacks.Any(static pack => pack.Enabled)
            || catalog.DefaultEnabled;
        var origin = !string.IsNullOrWhiteSpace(availability?.Origin)
            ? availability!.Origin.Trim()
            : !string.IsNullOrWhiteSpace(catalog.Origin)
                ? catalog.Origin.Trim()
                : (resolvedPacks.Length > 0 ? ResolveSyntheticPluginOrigin(resolvedPacks[0].SourceKind) : "unknown");
        var resolvedDangerous = (availability?.IsDangerous ?? false)
            || catalog.IsDangerous
            || resolvedPacks.Any(static pack => pack.IsDangerous || pack.Tier == CapabilityTier.DangerousWrite);

        return new PluginInfoDto {
            Id = normalizedCatalogPluginId.Length == 0 ? catalog.Id : normalizedCatalogPluginId,
            Name = ResolvePluginName(availability, catalog, resolvedPacks),
            Version = ResolvePluginVersion(availability?.Version, catalog.Version),
            Origin = origin,
            SourceKind = ResolvePluginSourceKind(availability?.SourceKind, catalog.SourceKind, resolvedPacks),
            DefaultEnabled = availability?.DefaultEnabled ?? catalog.DefaultEnabled,
            Enabled = enabled,
            ActivationState = ResolvePluginActivationState(availability, resolvedPacks, enabled),
            CanActivateOnDemand = ResolvePluginCanActivateOnDemand(availability, resolvedPacks, enabled),
            DisabledReason = enabled ? null : availability?.DisabledReason,
            IsDangerous = resolvedDangerous,
            PackIds = resolvedPackIds,
            RootPath = ResolvePluginRootPath(availability?.RootPath, catalog.RootPath),
            SkillDirectories = NormalizeDistinctNonEmptyStrings(
                (availability?.SkillDirectories ?? Array.Empty<string>())
                .Concat(catalog.SkillDirectories ?? Array.Empty<string>())),
            SkillIds = NormalizePluginSkillIds(
                (availability?.SkillIds ?? Array.Empty<string>())
                .Concat(catalog.SkillIds ?? Array.Empty<string>()))
        };
    }

    private static PluginInfoDto BuildPluginInfoDto(
        ToolPluginAvailabilityInfo plugin,
        IReadOnlyDictionary<string, ToolPackInfoDto> packLookup) {
        var resolvedPackIds = ResolvePluginPackIds(plugin, packLookup);
        var resolvedPacks = ResolvePluginPacks(resolvedPackIds, packLookup);
        var resolvedDangerous = plugin.IsDangerous || resolvedPacks.Any(static pack => pack.IsDangerous || pack.Tier == CapabilityTier.DangerousWrite);
        return new PluginInfoDto {
            Id = plugin.Id,
            Name = ResolvePluginName(plugin, resolvedPacks),
            Version = string.IsNullOrWhiteSpace(plugin.Version) ? null : plugin.Version.Trim(),
            Origin = string.IsNullOrWhiteSpace(plugin.Origin) ? "unknown" : plugin.Origin.Trim(),
            SourceKind = ResolvePluginSourceKind(plugin, resolvedPacks),
            DefaultEnabled = plugin.DefaultEnabled,
            Enabled = plugin.Enabled,
            ActivationState = ToolActivationStates.Resolve(plugin.Enabled, plugin.DescriptorOnly),
            CanActivateOnDemand = plugin.Enabled && plugin.DescriptorOnly,
            DisabledReason = plugin.Enabled ? null : plugin.DisabledReason,
            IsDangerous = resolvedDangerous,
            PackIds = resolvedPackIds,
            RootPath = string.IsNullOrWhiteSpace(plugin.RootPath) ? null : plugin.RootPath.Trim(),
            SkillDirectories = NormalizeDistinctNonEmptyStrings(plugin.SkillDirectories),
            SkillIds = NormalizePluginSkillIds(plugin.SkillIds)
        };
    }

    private static Dictionary<string, ToolPackInfoDto> BuildPackInfoLookup(IReadOnlyList<ToolPackInfoDto>? packs) {
        return (packs ?? Array.Empty<ToolPackInfoDto>())
            .Where(static pack => pack is not null)
            .Select(static pack => new KeyValuePair<string, ToolPackInfoDto>(ToolPackMetadataNormalizer.NormalizePackId(pack.Id), pack))
            .Where(static pair => pair.Key.Length > 0)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolvePluginActivationState(
        ToolPluginAvailabilityInfo? availability,
        IReadOnlyList<ToolPackInfoDto> resolvedPacks,
        bool enabled) {
        if (availability is not null) {
            return ToolActivationStates.Resolve(availability.Enabled, availability.DescriptorOnly);
        }

        if (!enabled) {
            return ToolActivationStates.Disabled;
        }

        if (resolvedPacks.Count > 0 && resolvedPacks.All(static pack => ToolActivationStates.IsDeferred(pack.ActivationState))) {
            return ToolActivationStates.Deferred;
        }

        return ToolActivationStates.Active;
    }

    private static bool ResolvePluginCanActivateOnDemand(
        ToolPluginAvailabilityInfo? availability,
        IReadOnlyList<ToolPackInfoDto> resolvedPacks,
        bool enabled) {
        if (availability is not null) {
            return availability.Enabled && availability.DescriptorOnly;
        }

        return enabled && resolvedPacks.Any(static pack => pack.CanActivateOnDemand);
    }

    private static Dictionary<string, ToolPluginAvailabilityInfo> BuildPluginAvailabilityLookup(
        IEnumerable<ToolPluginAvailabilityInfo>? plugins) {
        return (plugins ?? Array.Empty<ToolPluginAvailabilityInfo>())
            .Where(static plugin => plugin is not null)
            .Select(static plugin => new KeyValuePair<string, ToolPluginAvailabilityInfo>(
                ToolPackMetadataNormalizer.NormalizePackId(plugin.Id),
                plugin))
            .Where(static pair => pair.Key.Length > 0)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ResolvePluginPackIds(
        ToolPluginAvailabilityInfo plugin,
        IReadOnlyDictionary<string, ToolPackInfoDto> packLookup) {
        var normalizedPackIds = NormalizePluginPackIds(plugin.PackIds);
        if (normalizedPackIds.Length > 0) {
            return normalizedPackIds;
        }

        var normalizedPluginId = ToolPackMetadataNormalizer.NormalizePackId(plugin.Id);
        if (normalizedPluginId.Length > 0 && packLookup.ContainsKey(normalizedPluginId)) {
            return new[] { normalizedPluginId };
        }

        return Array.Empty<string>();
    }

    private static string[] ResolvePluginPackIds(
        ToolPluginAvailabilityInfo? availability,
        ToolPluginCatalogInfo catalog,
        IReadOnlyDictionary<string, ToolPackInfoDto> packLookup) {
        var normalizedPackIds = NormalizePluginPackIds(availability?.PackIds);
        if (normalizedPackIds.Length > 0) {
            return normalizedPackIds;
        }

        normalizedPackIds = NormalizePluginPackIds(catalog.PackIds);
        if (normalizedPackIds.Length > 0) {
            return normalizedPackIds;
        }

        var normalizedPluginId = ToolPackMetadataNormalizer.NormalizePackId(availability?.Id ?? catalog.Id);
        if (normalizedPluginId.Length > 0 && packLookup.ContainsKey(normalizedPluginId)) {
            return new[] { normalizedPluginId };
        }

        return Array.Empty<string>();
    }

    private static ToolPackInfoDto[] ResolvePluginPacks(
        IReadOnlyList<string> packIds,
        IReadOnlyDictionary<string, ToolPackInfoDto> packLookup) {
        if (packIds.Count == 0) {
            return Array.Empty<ToolPackInfoDto>();
        }

        var resolved = new List<ToolPackInfoDto>(packIds.Count);
        for (var i = 0; i < packIds.Count; i++) {
            if (packLookup.TryGetValue(packIds[i], out var pack)) {
                resolved.Add(pack);
            }
        }

        return resolved.Count == 0 ? Array.Empty<ToolPackInfoDto>() : resolved.ToArray();
    }

    private static string ResolvePluginName(ToolPluginAvailabilityInfo plugin, IReadOnlyList<ToolPackInfoDto> resolvedPacks) {
        if (!string.IsNullOrWhiteSpace(plugin.Name)) {
            return plugin.Name.Trim();
        }

        if (resolvedPacks.Count == 1) {
            return resolvedPacks[0].Name;
        }

        var normalizedPluginId = ToolPackMetadataNormalizer.NormalizePackId(plugin.Id);
        return ToolPackMetadataNormalizer.ResolveDisplayName(plugin.Id, normalizedPluginId.Length == 0 ? plugin.Id : normalizedPluginId);
    }

    private static string ResolvePluginName(
        ToolPluginAvailabilityInfo? availability,
        ToolPluginCatalogInfo catalog,
        IReadOnlyList<ToolPackInfoDto> resolvedPacks) {
        if (!string.IsNullOrWhiteSpace(availability?.Name)) {
            return availability!.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(catalog.Name)) {
            return catalog.Name.Trim();
        }

        if (resolvedPacks.Count == 1) {
            return resolvedPacks[0].Name;
        }

        var rawPluginId = availability?.Id ?? catalog.Id;
        var normalizedPluginId = ToolPackMetadataNormalizer.NormalizePackId(rawPluginId);
        return ToolPackMetadataNormalizer.ResolveDisplayName(rawPluginId, normalizedPluginId.Length == 0 ? rawPluginId : normalizedPluginId);
    }

    private static ToolPackSourceKind ResolvePluginSourceKind(ToolPluginAvailabilityInfo plugin, IReadOnlyList<ToolPackInfoDto> resolvedPacks) {
        if (!string.IsNullOrWhiteSpace(plugin.SourceKind)) {
            return ToolPackMetadataNormalizer.ResolveSourceKind(plugin.SourceKind);
        }

        if (resolvedPacks.Count > 0) {
            return resolvedPacks[0].SourceKind;
        }

        return ToolPackSourceKind.OpenSource;
    }

    private static ToolPackSourceKind ResolvePluginSourceKind(
        string? availabilitySourceKind,
        string? catalogSourceKind,
        IReadOnlyList<ToolPackInfoDto> resolvedPacks) {
        if (!string.IsNullOrWhiteSpace(availabilitySourceKind)) {
            return ToolPackMetadataNormalizer.ResolveSourceKind(availabilitySourceKind);
        }

        if (!string.IsNullOrWhiteSpace(catalogSourceKind)) {
            return ToolPackMetadataNormalizer.ResolveSourceKind(catalogSourceKind);
        }

        if (resolvedPacks.Count > 0) {
            return resolvedPacks[0].SourceKind;
        }

        return ToolPackSourceKind.OpenSource;
    }

    private static string? ResolvePluginVersion(string? availabilityVersion, string? catalogVersion) {
        if (!string.IsNullOrWhiteSpace(availabilityVersion)) {
            return availabilityVersion.Trim();
        }

        return string.IsNullOrWhiteSpace(catalogVersion) ? null : catalogVersion.Trim();
    }

    private static string? ResolvePluginRootPath(string? availabilityRootPath, string? catalogRootPath) {
        if (!string.IsNullOrWhiteSpace(availabilityRootPath)) {
            return availabilityRootPath.Trim();
        }

        return string.IsNullOrWhiteSpace(catalogRootPath) ? null : catalogRootPath.Trim();
    }

    private static string[] NormalizePluginPackIds(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        return values
            .Select(static value => ToolPackMetadataNormalizer.NormalizePackId(value))
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizePluginSkillIds(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        return values
            .Select(static value => NormalizePluginSkillId(value))
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePluginSkillId(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Length > 128) {
            return string.Empty;
        }

        foreach (var ch in normalized) {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) {
                return string.Empty;
            }
        }

        return normalized;
    }

    private static string[] NormalizeDistinctNonEmptyStrings(IEnumerable<string>? values) {
        if (values is null) {
            return Array.Empty<string>();
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
