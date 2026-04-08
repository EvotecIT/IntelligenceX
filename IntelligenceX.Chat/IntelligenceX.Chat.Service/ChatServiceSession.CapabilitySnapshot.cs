using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string CapabilitySnapshotMarker = "ix:capability-snapshot:v1";
    private const string SkillsSnapshotMarker = "ix:skills:v1";
    private const string DeferredDescriptorPreviewToolingSnapshotSource = "deferred_descriptor_preview";
    private const int MaxCapabilitySnapshotPackIds = 8;
    private const int MaxCapabilitySnapshotPluginIds = 8;
    private const int MaxCapabilitySnapshotEngineIds = 8;
    private const int MaxCapabilitySnapshotCapabilityTags = 12;
    private const int MaxCapabilitySnapshotFamilies = 6;
    private const int MaxCapabilitySnapshotSkills = 8;
    private const int MaxCapabilitySnapshotRepresentativeExamples = 4;
    private const int MaxCapabilitySnapshotDeferredWorkAffordances = 6;
    private const int MaxCapabilitySnapshotCrossPackTargetDisplays = 4;
    private const int MaxCapabilitySnapshotHealthyTools = 12;
    private const int MaxCapabilitySnapshotParityAttention = 4;
    private const int MaxCapabilitySnapshotParityDetail = 4;
    private const int MaxCapabilitySnapshotToolingPackDetails = 4;
    private const int MaxCapabilitySnapshotToolingPluginDetails = 4;
    private const int MaxCapabilitySnapshotDescriptorPreviewTools = 6;
    private const int MaxCapabilitySnapshotDescriptorPreviewExamples = 4;

    private SessionCapabilitySnapshotDto BuildRuntimeCapabilitySnapshot() {
        if (_servingPersistedToolingBootstrapPreview && _persistedPreviewCapabilitySnapshot is not null) {
            return _persistedPreviewCapabilitySnapshot;
        }

        if (TryGetDeferredDescriptorPreviewCapabilitySnapshot(out var deferredDescriptorPreviewSnapshot)) {
            return deferredDescriptorPreviewSnapshot;
        }

        return BuildCapabilitySnapshot(
            _options,
            _registry.GetDefinitions(),
            _packAvailability,
            _pluginAvailability,
            _routingCatalogDiagnostics,
            _toolOrchestrationCatalog,
            connectedRuntimeSkills: _connectedRuntimeSkillInventory,
            healthyToolNames: ResolveWorkingMemoryCapabilityHealthyToolNames(
                Array.Empty<string>(),
                Array.Empty<string>()),
            remoteReachabilityMode: ResolveHelloRemoteReachabilityMode(),
            backgroundScheduler: BuildBackgroundSchedulerSummary(),
            pluginCatalog: _pluginCatalog);
    }

    private bool TryGetDeferredDescriptorPreviewCapabilitySnapshot(out SessionCapabilitySnapshotDto snapshot) {
        snapshot = null!;
        var startupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
        if (startupToolingBootstrapTask is not null
            || _servingPersistedToolingBootstrapPreview
            || _cachedToolDefinitions.Length > 0
            || _packAvailability.Length > 0
            || _pluginAvailability.Length > 0
            || _pluginCatalog.Length > 0
            || _registry.GetDefinitions().Count > 0
            || _toolOrchestrationCatalog.EntriesByToolName.Count > 0
            || _toolOrchestrationCatalog.GetKnownPackIds().Count > 0) {
            return false;
        }

        if (_deferredDescriptorPreviewCapabilitySnapshot is not null) {
            snapshot = _deferredDescriptorPreviewCapabilitySnapshot;
            return true;
        }

        if (Interlocked.CompareExchange(ref _deferredDescriptorPreviewCapabilitySnapshotBuildInProgress, 1, 0) != 0) {
            return false;
        }

        try {
        var descriptorPreview = ToolPackBootstrap.CreateDeferredDescriptorPreview(new ToolPackBootstrapOptions {
            EnableBuiltInPackLoading = _options.EnableBuiltInPackLoading,
            EnableDefaultPluginPaths = _options.EnableDefaultPluginPaths,
            PluginPaths = _options.GetEffectivePluginPaths().ToArray(),
            DisabledPackIds = _options.DisabledPackIds.ToArray(),
            EnabledPackIds = _options.EnabledPackIds.ToArray()
        });
        if (descriptorPreview.PackAvailability.Count == 0 && descriptorPreview.PluginCatalog.Count == 0) {
            return false;
        }

        // Seed descriptor-only tool definitions before building the capability snapshot so
        // background scheduler summaries can resolve deferred tool names without re-entering
        // deferred preview construction.
        _deferredDescriptorPreviewToolDefinitions = descriptorPreview.ToolDefinitions.ToArray();

        _deferredDescriptorPreviewCapabilitySnapshot = BuildCapabilitySnapshot(
            _options,
            toolDefinitions: Array.Empty<ToolDefinition>(),
            descriptorPreview.PackAvailability,
            descriptorPreview.PluginAvailability,
            _routingCatalogDiagnostics,
            orchestrationCatalog: null,
            connectedRuntimeSkills: Array.Empty<string>(),
            healthyToolNames: Array.Empty<string>(),
            remoteReachabilityMode: ResolveHelloRemoteReachabilityMode(),
            backgroundScheduler: BuildBackgroundSchedulerSummary(),
            pluginCatalog: descriptorPreview.PluginCatalog,
            toolingSnapshotSource: DeferredDescriptorPreviewToolingSnapshotSource);
        snapshot = _deferredDescriptorPreviewCapabilitySnapshot;
        return true;
        } finally {
            Volatile.Write(ref _deferredDescriptorPreviewCapabilitySnapshotBuildInProgress, 0);
        }
    }

    internal static SessionCapabilitySnapshotDto BuildCapabilitySnapshot(
        ServiceOptions options,
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        IEnumerable<ToolPackAvailabilityInfo> packAvailability,
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        ToolRoutingCatalogDiagnostics? routingCatalog,
        ToolOrchestrationCatalog? orchestrationCatalog = null,
        IEnumerable<string>? connectedRuntimeSkills = null,
        IEnumerable<string>? healthyToolNames = null,
        string? remoteReachabilityMode = null,
        SessionCapabilityBackgroundSchedulerDto? backgroundScheduler = null,
        IEnumerable<ToolPluginCatalogInfo>? pluginCatalog = null,
        string? toolingSnapshotSource = "service_runtime") {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedPackAvailability = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()).ToArray();
        var packList = ToolCatalogExportBuilder.BuildPackInfoDtos(normalizedPackAvailability, orchestrationCatalog);
        var pluginList = ToolCatalogExportBuilder.BuildPluginInfoDtos(pluginAvailability, packList, pluginCatalog);
        var toolingSnapshot = ToolCatalogExportBuilder.BuildCapabilityToolingSnapshotDto(packList, pluginList, toolingSnapshotSource);
        var enabledPackIds = NormalizeCapabilitySnapshotEnabledPackIds(
            normalizedPackAvailability
            .Where(static pack => pack.Enabled)
            .Select(static pack => pack.Id));
        if (enabledPackIds.Length == 0) {
            enabledPackIds = NormalizeCapabilitySnapshotEnabledPackIds(
                EnumerateCapabilitySnapshotKnownPackIds(normalizedPackAvailability, orchestrationCatalog));
        }
        var allPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
            pluginList.Select(static plugin => plugin.Id),
            maxItems: 0);
        if (allPluginIds.Length == 0) {
            allPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
                normalizedPackAvailability
                .Select(static pack => pack.Id),
                maxItems: 0);
        }

        var enabledPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
            pluginList
            .Where(static plugin => plugin.Enabled)
            .Select(static plugin => plugin.Id),
            MaxCapabilitySnapshotPluginIds);
        if (enabledPluginIds.Length == 0) {
            enabledPluginIds = NormalizeCapabilitySnapshotEnabledPluginIds(
                normalizedPackAvailability
                .Where(static pack => pack.Enabled)
                .Select(static pack => pack.Id),
                MaxCapabilitySnapshotPluginIds);
        }
        var enabledPackEngineIds = NormalizeCapabilitySnapshotDescriptorTokens(
            normalizedPackAvailability
            .Where(static pack => pack.Enabled)
            .Select(static pack => pack.EngineId),
            MaxCapabilitySnapshotEngineIds);
        if (enabledPackEngineIds.Length == 0) {
            enabledPackEngineIds = NormalizeCapabilitySnapshotDescriptorTokens(
                EnumerateCapabilitySnapshotKnownPackEngineIds(normalizedPackAvailability, orchestrationCatalog),
                MaxCapabilitySnapshotEngineIds);
        }
        var enabledCapabilityTags = NormalizeCapabilitySnapshotDescriptorTokens(
            normalizedPackAvailability
            .Where(static pack => pack.Enabled)
            .SelectMany(static pack => pack.CapabilityTags ?? Array.Empty<string>()),
            MaxCapabilitySnapshotCapabilityTags);
        if (enabledCapabilityTags.Length == 0) {
            enabledCapabilityTags = NormalizeCapabilitySnapshotDescriptorTokens(
                EnumerateCapabilitySnapshotKnownPackCapabilityTags(normalizedPackAvailability, orchestrationCatalog),
                MaxCapabilitySnapshotCapabilityTags);
        }

        var familyActions = MapCapabilityFamilyActions(routingCatalog);
        var routingFamilies = NormalizeCapabilitySnapshotRoutingFamilies(
            familyActions.Select(static summary => summary.Family));
        var orchestrationEntries = EnumerateCapabilitySnapshotOrchestrationEntries(orchestrationCatalog);
        var representativeExamples = NormalizeCapabilitySnapshotRepresentativeExamples(
            ToolContractPromptExamples.BuildRepresentativeExamples(orchestrationEntries));
        var deferredWorkAffordances = DeferredWorkAffordanceCatalog.Build(
            packAvailability,
            orchestrationCatalog,
            backgroundScheduler,
            MaxCapabilitySnapshotDeferredWorkAffordances);
        var crossPackTargetPackDisplayNames = NormalizeCapabilitySnapshotCrossPackTargetPackDisplayNames(
            ToolContractPromptExamples.BuildCrossPackTargetPackDisplayNames(orchestrationEntries));
        var skills = ResolveCapabilitySnapshotSkills(pluginList, routingCatalog, connectedRuntimeSkills);
        var healthyTools = NormalizeCapabilitySnapshotHealthyToolNames(healthyToolNames ?? Array.Empty<string>());
        var registeredTools = Math.Max(0, routingCatalog?.TotalTools ?? 0);
        var allowedRootCount = Math.Max(0, options.AllowedRoots.Count);
        var autonomy = ToolAutonomySummaryBuilder.BuildCapabilityAutonomySummary(
            normalizedPackAvailability,
            orchestrationCatalog);
        var dangerousPackIds = NormalizeCapabilitySnapshotDangerousPackIds(normalizedPackAvailability, autonomy);
        var parityEntries = ToolCapabilityParityInventoryBuilder.Build(toolDefinitions, normalizedPackAvailability);
        var parityAttentionCount = parityEntries.Count(static entry =>
            !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.HealthyStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.PackUnavailableStatus, StringComparison.OrdinalIgnoreCase));
        var parityMissingCapabilityCount = parityEntries.Sum(static entry => Math.Max(0, entry.MissingCapabilityCount));

        return new SessionCapabilitySnapshotDto {
            RegisteredTools = registeredTools,
            EnabledPackCount = enabledPackIds.Length,
            PluginCount = allPluginIds.Length,
            EnabledPluginCount = enabledPluginIds.Length,
            ToolingAvailable = enabledPackIds.Length > 0 || registeredTools > 0,
            AllowedRootCount = allowedRootCount,
            EnabledPackIds = enabledPackIds,
            EnabledPluginIds = enabledPluginIds,
            DangerousToolsEnabled = dangerousPackIds.Length > 0,
            DangerousPackIds = dangerousPackIds,
            EnabledPackEngineIds = enabledPackEngineIds,
            EnabledCapabilityTags = enabledCapabilityTags,
            RoutingFamilies = routingFamilies,
            FamilyActions = familyActions,
            Skills = skills,
            RepresentativeExamples = representativeExamples,
            DeferredWorkAffordances = deferredWorkAffordances,
            ToolingSnapshot = toolingSnapshot,
            CrossPackTargetPackDisplayNames = crossPackTargetPackDisplayNames,
            HealthyTools = healthyTools,
            RemoteReachabilityMode = NormalizeCapabilitySnapshotRemoteReachabilityMode(remoteReachabilityMode),
            Autonomy = autonomy,
            BackgroundScheduler = backgroundScheduler,
            ParityEntries = parityEntries,
            ParityAttentionCount = Math.Max(0, parityAttentionCount),
            ParityMissingCapabilityCount = Math.Max(0, parityMissingCapabilityCount)
        };
    }

    private static SessionRoutingFamilyActionSummaryDto[] MapCapabilityFamilyActions(ToolRoutingCatalogDiagnostics? diagnostics) {
        var familyActions = diagnostics?.FamilyActions;
        if (familyActions is null || familyActions.Count == 0) {
            return Array.Empty<SessionRoutingFamilyActionSummaryDto>();
        }

        return familyActions
            .Where(static summary =>
                !string.IsNullOrWhiteSpace(summary.Family)
                && !string.IsNullOrWhiteSpace(summary.ActionId))
            .OrderByDescending(static summary => Math.Max(0, summary.ToolCount))
            .ThenBy(static summary => summary.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static summary => summary.ActionId, StringComparer.OrdinalIgnoreCase)
            .Select(static summary => new SessionRoutingFamilyActionSummaryDto {
                Family = summary.Family.Trim(),
                ActionId = summary.ActionId.Trim(),
                ToolCount = Math.Max(0, summary.ToolCount),
                DisplayName = string.IsNullOrWhiteSpace(summary.DisplayName) ? null : summary.DisplayName.Trim(),
                ReplyExample = string.IsNullOrWhiteSpace(summary.ReplyExample) ? null : summary.ReplyExample.Trim(),
                ChoiceDescription = string.IsNullOrWhiteSpace(summary.ChoiceDescription) ? null : summary.ChoiceDescription.Trim(),
                RepresentativePackIds = summary.RepresentativePackIds is { Length: > 0 } ? summary.RepresentativePackIds : null
            })
            .ToArray();
    }

    private static string[] NormalizeCapabilitySnapshotEnabledPackIds(IEnumerable<string> packIds) {
        return NormalizeDistinctStrings(
            (packIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0),
            MaxCapabilitySnapshotPackIds);
    }

    private static string[] NormalizeCapabilitySnapshotEnabledPluginIds(IEnumerable<string> pluginIds, int maxItems) {
        return NormalizeDistinctStrings(
            (pluginIds ?? Array.Empty<string>())
            .Select(static pluginId => NormalizePackId(pluginId))
            .Where(static pluginId => pluginId.Length > 0),
            maxItems);
    }

    private static string[] NormalizeCapabilitySnapshotDescriptorTokens(IEnumerable<string?> values, int maxItems) {
        return NormalizeDistinctStrings(
            (values ?? Array.Empty<string>())
            .Select(static value => ToolPackMetadataNormalizer.NormalizeDescriptorToken(value))
            .Where(static value => value.Length > 0),
            maxItems);
    }

    private static string[] NormalizeCapabilitySnapshotDangerousPackIds(
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability,
        SessionCapabilityAutonomySummaryDto? autonomy) {
        return NormalizeCapabilitySnapshotEnabledPackIds(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled && (pack.IsDangerous || pack.Tier == ToolCapabilityTier.DangerousWrite))
            .Select(static pack => pack.Id)
            .Concat(autonomy?.WriteCapablePackIds ?? Array.Empty<string>()));
    }

    private static IEnumerable<string> EnumerateCapabilitySnapshotKnownPackIds(
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        if (orchestrationCatalog is null) {
            return Array.Empty<string>();
        }

        return orchestrationCatalog
            .GetKnownPackIds()
            .Where(packId => IsCapabilitySnapshotPackEnabledOrUnknown(packId, packAvailability));
    }

    private static IEnumerable<string> EnumerateCapabilitySnapshotKnownPackEngineIds(
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        if (orchestrationCatalog is null) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        var knownPackIds = orchestrationCatalog.GetKnownPackIds();
        for (var i = 0; i < knownPackIds.Count; i++) {
            var packId = knownPackIds[i];
            if (!IsCapabilitySnapshotPackEnabledOrUnknown(packId, packAvailability)
                || !orchestrationCatalog.TryGetPackMetadata(packId, out var metadata)
                || string.IsNullOrWhiteSpace(metadata.EngineId)) {
                continue;
            }

            values.Add(metadata.EngineId);
        }

        return values;
    }

    private static IEnumerable<string> EnumerateCapabilitySnapshotKnownPackCapabilityTags(
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        ToolOrchestrationCatalog? orchestrationCatalog) {
        if (orchestrationCatalog is null) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        var knownPackIds = orchestrationCatalog.GetKnownPackIds();
        for (var i = 0; i < knownPackIds.Count; i++) {
            var packId = knownPackIds[i];
            if (!IsCapabilitySnapshotPackEnabledOrUnknown(packId, packAvailability)
                || !orchestrationCatalog.TryGetPackCapabilityTags(packId, out var capabilityTags)
                || capabilityTags.Count == 0) {
                continue;
            }

            values.AddRange(capabilityTags);
        }

        return values;
    }

    private static bool IsCapabilitySnapshotPackEnabledOrUnknown(
        string? normalizedPackId,
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability) {
        normalizedPackId = NormalizePackId(normalizedPackId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        var sawAvailability = false;
        for (var i = 0; i < packAvailability.Count; i++) {
            var pack = packAvailability[i];
            if (!string.Equals(NormalizePackId(pack.Id), normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            sawAvailability = true;
            if (pack.Enabled) {
                return true;
            }
        }

        return !sawAvailability;
    }

    private static string[] NormalizeCapabilitySnapshotRoutingFamilies(IEnumerable<string> routingFamilies) {
        if (routingFamilies is null) {
            return Array.Empty<string>();
        }

        var normalizedFamilies = new List<string>();
        foreach (var family in routingFamilies) {
            if (!TryNormalizeDomainIntentFamily(family, out var normalizedFamily)) {
                continue;
            }

            normalizedFamilies.Add(normalizedFamily);
        }

        return NormalizeDistinctStrings(normalizedFamilies, MaxCapabilitySnapshotFamilies);
    }

    private static string[] NormalizeCapabilitySnapshotSkills(IEnumerable<string> skills) {
        return NormalizeSkillInventoryValues(skills, MaxCapabilitySnapshotSkills);
    }

    private static IReadOnlyList<ToolOrchestrationCatalogEntry> EnumerateCapabilitySnapshotOrchestrationEntries(ToolOrchestrationCatalog? orchestrationCatalog) {
        if (orchestrationCatalog?.EntriesByToolName is not { Count: > 0 } entriesByToolName) {
            return Array.Empty<ToolOrchestrationCatalogEntry>();
        }

        return entriesByToolName.Values
            .OrderBy(static entry => entry.PackId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeCapabilitySnapshotRepresentativeExamples(IEnumerable<string> examples) {
        return NormalizeDistinctStrings(
            (examples ?? Array.Empty<string>())
            .Select(static example => (example ?? string.Empty).Trim())
            .Where(static example => example.Length > 0),
            MaxCapabilitySnapshotRepresentativeExamples);
    }

    private static string[] NormalizeCapabilitySnapshotCrossPackTargetPackDisplayNames(IEnumerable<string> displayNames) {
        return NormalizeDistinctStrings(
            (displayNames ?? Array.Empty<string>())
            .Select(static displayName => (displayName ?? string.Empty).Trim())
            .Where(static displayName => displayName.Length > 0),
            MaxCapabilitySnapshotCrossPackTargetDisplays);
    }

    private static string[] NormalizeSkillInventoryValues(IEnumerable<string> skills, int maxItems) {
        return NormalizeDistinctStrings(
            (skills ?? Array.Empty<string>())
            .Select(static skill => NormalizeSkillSnapshotValue(skill))
            .Where(static skill => skill.Length > 0),
            maxItems);
    }

    private static string[] NormalizeCapabilitySnapshotHealthyToolNames(IEnumerable<string> healthyToolNames) {
        return NormalizeDistinctStrings(
            (healthyToolNames ?? Array.Empty<string>())
            .Select(static toolName => (toolName ?? string.Empty).Trim())
            .Where(static toolName => toolName.Length > 0),
            MaxCapabilitySnapshotHealthyTools);
    }

    private static string? NormalizeCapabilitySnapshotRemoteReachabilityMode(string? remoteReachabilityMode) {
        var normalized = (remoteReachabilityMode ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static void AppendCapabilitySnapshotPromptBlock(
        StringBuilder runtimeIdentity,
        SessionCapabilitySnapshotDto snapshot,
        ToolRoutingCatalogDiagnostics? routingCatalog = null) {
        ArgumentNullException.ThrowIfNull(runtimeIdentity);
        ArgumentNullException.ThrowIfNull(snapshot);

        runtimeIdentity.AppendLine();
        runtimeIdentity.AppendLine("[Capability snapshot]");
        runtimeIdentity.AppendLine(CapabilitySnapshotMarker);
        runtimeIdentity.AppendLine("registered_tools: " + snapshot.RegisteredTools);
        runtimeIdentity.AppendLine("enabled_pack_count: " + snapshot.EnabledPackCount);
        runtimeIdentity.AppendLine("plugin_count: " + snapshot.PluginCount);
        runtimeIdentity.AppendLine("enabled_plugin_count: " + snapshot.EnabledPluginCount);
        if (snapshot.ToolingSnapshot is not null) {
            runtimeIdentity.AppendLine(
                "tooling_snapshot: "
                + (string.IsNullOrWhiteSpace(snapshot.ToolingSnapshot.Source) ? "unknown" : snapshot.ToolingSnapshot.Source)
                + ", packs " + snapshot.ToolingSnapshot.Packs.Length
                + ", plugins " + snapshot.ToolingSnapshot.Plugins.Length);
            var toolingPackDetails = BuildCapabilitySnapshotToolingPackDetails(snapshot.ToolingSnapshot, MaxCapabilitySnapshotToolingPackDetails);
            if (toolingPackDetails.Length > 0) {
                runtimeIdentity.AppendLine("tooling_snapshot_packs: " + string.Join(" | ", toolingPackDetails));
            }

            var toolingPluginDetails = BuildCapabilitySnapshotToolingPluginDetails(snapshot.ToolingSnapshot, MaxCapabilitySnapshotToolingPluginDetails);
            if (toolingPluginDetails.Length > 0) {
                runtimeIdentity.AppendLine("tooling_snapshot_plugins: " + string.Join(" | ", toolingPluginDetails));
            }
        }

        if (snapshot.EnabledPackIds.Length > 0) {
            runtimeIdentity.AppendLine("enabled_packs: " + string.Join(", ", snapshot.EnabledPackIds));
        }

        if (snapshot.EnabledPluginIds.Length > 0) {
            runtimeIdentity.AppendLine("enabled_plugins: " + string.Join(", ", snapshot.EnabledPluginIds));
        }

        var activatablePackIds = NormalizeCapabilitySnapshotActivatablePackIds(snapshot.ToolingSnapshot);
        if (activatablePackIds.Length > 0) {
            runtimeIdentity.AppendLine("activatable_packs: " + string.Join(", ", activatablePackIds));
        }

        var activatablePluginIds = NormalizeCapabilitySnapshotActivatablePluginIds(snapshot.ToolingSnapshot);
        if (activatablePluginIds.Length > 0) {
            runtimeIdentity.AppendLine("activatable_plugins: " + string.Join(", ", activatablePluginIds));
        }

        runtimeIdentity.AppendLine("dangerous_tools_enabled: " + (snapshot.DangerousToolsEnabled ? "true" : "false"));
        if (snapshot.DangerousPackIds.Length > 0) {
            runtimeIdentity.AppendLine("dangerous_packs: " + string.Join(", ", snapshot.DangerousPackIds));
        }

        if (snapshot.EnabledPackEngineIds.Length > 0) {
            runtimeIdentity.AppendLine("enabled_pack_engines: " + string.Join(", ", snapshot.EnabledPackEngineIds));
        }

        if (snapshot.EnabledCapabilityTags.Length > 0) {
            runtimeIdentity.AppendLine("enabled_capability_tags: " + string.Join(", ", snapshot.EnabledCapabilityTags));
        }

        if (snapshot.RoutingFamilies.Length > 0) {
            runtimeIdentity.AppendLine("routing_families: " + string.Join(", ", snapshot.RoutingFamilies));
        }

        if (snapshot.RepresentativeExamples.Length > 0) {
            runtimeIdentity.AppendLine("representative_live_examples: " + string.Join(" | ", snapshot.RepresentativeExamples));
        }

        if (snapshot.DeferredWorkAffordances.Length > 0) {
            runtimeIdentity.AppendLine("deferred_work_affordances: " + string.Join(", ", snapshot.DeferredWorkAffordances.Select(static affordance => affordance.CapabilityId)));
            runtimeIdentity.AppendLine("deferred_work_affordance_details: " + string.Join(
                " | ",
                snapshot.DeferredWorkAffordances.Select(DeferredWorkAffordanceCatalog.FormatSummary)));
        }

        if (snapshot.CrossPackTargetPackDisplayNames.Length > 0) {
            runtimeIdentity.AppendLine("cross_pack_followup_targets: " + string.Join(", ", snapshot.CrossPackTargetPackDisplayNames));
        }

        if (snapshot.HealthyTools.Length > 0) {
            runtimeIdentity.AppendLine("healthy_tools: " + string.Join(", ", snapshot.HealthyTools));
        }
        var routingAutonomyReadiness = routingCatalog is null
            ? Array.Empty<string>()
            : ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(routingCatalog, maxItems: 4);
        if (routingAutonomyReadiness.Count > 0) {
            runtimeIdentity.AppendLine("routing_autonomy_readiness: " + string.Join(" | ", routingAutonomyReadiness));
        }
        if (snapshot.Autonomy is not null) {
            runtimeIdentity.AppendLine("autonomy_local_capable_tools: " + snapshot.Autonomy.LocalCapableToolCount);
            runtimeIdentity.AppendLine("autonomy_remote_capable_tools: " + snapshot.Autonomy.RemoteCapableToolCount);
            runtimeIdentity.AppendLine("autonomy_target_scoped_tools: " + snapshot.Autonomy.TargetScopedToolCount);
            runtimeIdentity.AppendLine("autonomy_remote_host_targeting_tools: " + snapshot.Autonomy.RemoteHostTargetingToolCount);
            runtimeIdentity.AppendLine("autonomy_setup_aware_tools: " + snapshot.Autonomy.SetupAwareToolCount);
            runtimeIdentity.AppendLine("autonomy_environment_discover_tools: " + snapshot.Autonomy.EnvironmentDiscoverToolCount);
            runtimeIdentity.AppendLine("autonomy_handoff_aware_tools: " + snapshot.Autonomy.HandoffAwareToolCount);
            runtimeIdentity.AppendLine("autonomy_recovery_aware_tools: " + snapshot.Autonomy.RecoveryAwareToolCount);
            runtimeIdentity.AppendLine("autonomy_write_capable_tools: " + snapshot.Autonomy.WriteCapableToolCount);
            runtimeIdentity.AppendLine("autonomy_governed_write_tools: " + snapshot.Autonomy.GovernedWriteToolCount);
            runtimeIdentity.AppendLine("autonomy_auth_required_tools: " + snapshot.Autonomy.AuthenticationRequiredToolCount);
            runtimeIdentity.AppendLine("autonomy_probe_capable_tools: " + snapshot.Autonomy.ProbeCapableToolCount);
            runtimeIdentity.AppendLine("autonomy_cross_pack_handoff_tools: " + snapshot.Autonomy.CrossPackHandoffToolCount);
            if (snapshot.Autonomy.LocalCapablePackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_local_capable_packs: " + string.Join(", ", snapshot.Autonomy.LocalCapablePackIds));
            }

            if (snapshot.Autonomy.RemoteCapablePackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_remote_capable_packs: " + string.Join(", ", snapshot.Autonomy.RemoteCapablePackIds));
            }

            if (snapshot.Autonomy.TargetScopedPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_target_scoped_packs: " + string.Join(", ", snapshot.Autonomy.TargetScopedPackIds));
            }

            if (snapshot.Autonomy.RemoteHostTargetingPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_remote_host_targeting_packs: " + string.Join(", ", snapshot.Autonomy.RemoteHostTargetingPackIds));
            }

            if (snapshot.Autonomy.EnvironmentDiscoverPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_environment_discover_packs: " + string.Join(", ", snapshot.Autonomy.EnvironmentDiscoverPackIds));
            }

            if (snapshot.Autonomy.WriteCapablePackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_write_capable_packs: " + string.Join(", ", snapshot.Autonomy.WriteCapablePackIds));
            }

            if (snapshot.Autonomy.GovernedWritePackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_governed_write_packs: " + string.Join(", ", snapshot.Autonomy.GovernedWritePackIds));
            }

            if (snapshot.Autonomy.AuthenticationRequiredPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_auth_required_packs: " + string.Join(", ", snapshot.Autonomy.AuthenticationRequiredPackIds));
            }

            if (snapshot.Autonomy.ProbeCapablePackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_probe_capable_packs: " + string.Join(", ", snapshot.Autonomy.ProbeCapablePackIds));
            }

            if (snapshot.Autonomy.CrossPackReadyPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_cross_pack_ready_packs: " + string.Join(", ", snapshot.Autonomy.CrossPackReadyPackIds));
            }

            if (snapshot.Autonomy.CrossPackTargetPackIds.Length > 0) {
                runtimeIdentity.AppendLine("autonomy_cross_pack_targets: " + string.Join(", ", snapshot.Autonomy.CrossPackTargetPackIds));
            }
        }
        if (snapshot.BackgroundScheduler is not null) {
            runtimeIdentity.AppendLine("background_scheduler_daemon_enabled: " + (snapshot.BackgroundScheduler.DaemonEnabled ? "true" : "false"));
            runtimeIdentity.AppendLine("background_scheduler_auto_pause_enabled: " + (snapshot.BackgroundScheduler.AutoPauseEnabled ? "true" : "false"));
            runtimeIdentity.AppendLine("background_scheduler_manual_pause_active: " + (snapshot.BackgroundScheduler.ManualPauseActive ? "true" : "false"));
            runtimeIdentity.AppendLine("background_scheduler_scheduled_pause_active: " + (snapshot.BackgroundScheduler.ScheduledPauseActive ? "true" : "false"));
            runtimeIdentity.AppendLine("background_scheduler_failure_threshold: " + snapshot.BackgroundScheduler.FailureThreshold);
            runtimeIdentity.AppendLine("background_scheduler_failure_pause_seconds: " + snapshot.BackgroundScheduler.FailurePauseSeconds);
            runtimeIdentity.AppendLine("background_scheduler_paused: " + (snapshot.BackgroundScheduler.Paused ? "true" : "false"));
            if (snapshot.BackgroundScheduler.MaintenanceWindowSpecs.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_maintenance_windows: " + string.Join(", ", snapshot.BackgroundScheduler.MaintenanceWindowSpecs));
            }
            if (snapshot.BackgroundScheduler.ActiveMaintenanceWindowSpecs.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_active_maintenance_windows: " + string.Join(", ", snapshot.BackgroundScheduler.ActiveMaintenanceWindowSpecs));
            }
            if (snapshot.BackgroundScheduler.AllowedPackIds.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_allowed_packs: " + string.Join(", ", snapshot.BackgroundScheduler.AllowedPackIds));
            }
            if (snapshot.BackgroundScheduler.BlockedPackIds.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_blocked_packs: " + string.Join(", ", snapshot.BackgroundScheduler.BlockedPackIds));
            }
            if (snapshot.BackgroundScheduler.AllowedThreadIds.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_allowed_threads: " + string.Join(", ", snapshot.BackgroundScheduler.AllowedThreadIds));
            }
            if (snapshot.BackgroundScheduler.BlockedThreadIds.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_blocked_threads: " + string.Join(", ", snapshot.BackgroundScheduler.BlockedThreadIds));
            }
            runtimeIdentity.AppendLine("background_scheduler_tracked_threads: " + snapshot.BackgroundScheduler.TrackedThreadCount);
            runtimeIdentity.AppendLine("background_scheduler_ready_threads: " + snapshot.BackgroundScheduler.ReadyThreadCount);
            runtimeIdentity.AppendLine("background_scheduler_running_threads: " + snapshot.BackgroundScheduler.RunningThreadCount);
            runtimeIdentity.AppendLine("background_scheduler_dependency_blocked_threads: " + snapshot.BackgroundScheduler.DependencyBlockedThreadCount);
            runtimeIdentity.AppendLine("background_scheduler_dependency_blocked_items: " + snapshot.BackgroundScheduler.DependencyBlockedItemCount);
            runtimeIdentity.AppendLine("background_scheduler_ready_items: " + snapshot.BackgroundScheduler.ReadyItemCount);
            runtimeIdentity.AppendLine("background_scheduler_running_items: " + snapshot.BackgroundScheduler.RunningItemCount);
            runtimeIdentity.AppendLine("background_scheduler_pending_readonly_items: " + snapshot.BackgroundScheduler.PendingReadOnlyItemCount);
            runtimeIdentity.AppendLine("background_scheduler_completed_executions: " + snapshot.BackgroundScheduler.CompletedExecutionCount);
            runtimeIdentity.AppendLine("background_scheduler_requeued_executions: " + snapshot.BackgroundScheduler.RequeuedExecutionCount);
            runtimeIdentity.AppendLine("background_scheduler_released_executions: " + snapshot.BackgroundScheduler.ReleasedExecutionCount);
            runtimeIdentity.AppendLine("background_scheduler_consecutive_failures: " + snapshot.BackgroundScheduler.ConsecutiveFailureCount);
            runtimeIdentity.AppendLine("background_scheduler_adaptive_idle_active: " + (snapshot.BackgroundScheduler.AdaptiveIdleActive ? "true" : "false"));
            if (!string.IsNullOrWhiteSpace(snapshot.BackgroundScheduler.LastOutcome)) {
                runtimeIdentity.AppendLine("background_scheduler_last_outcome: " + snapshot.BackgroundScheduler.LastOutcome);
            }
            if (snapshot.BackgroundScheduler.LastAdaptiveIdleUtcTicks > 0) {
                runtimeIdentity.AppendLine("background_scheduler_last_adaptive_idle_utc_ticks: " + snapshot.BackgroundScheduler.LastAdaptiveIdleUtcTicks);
            }
            if (snapshot.BackgroundScheduler.LastAdaptiveIdleDelaySeconds > 0) {
                runtimeIdentity.AppendLine("background_scheduler_last_adaptive_idle_delay_seconds: " + snapshot.BackgroundScheduler.LastAdaptiveIdleDelaySeconds);
            }
            if (!string.IsNullOrWhiteSpace(snapshot.BackgroundScheduler.LastAdaptiveIdleReason)) {
                runtimeIdentity.AppendLine("background_scheduler_last_adaptive_idle_reason: " + snapshot.BackgroundScheduler.LastAdaptiveIdleReason);
            }
            if (snapshot.BackgroundScheduler.PausedUntilUtcTicks > 0) {
                runtimeIdentity.AppendLine("background_scheduler_paused_until_utc_ticks: " + snapshot.BackgroundScheduler.PausedUntilUtcTicks);
            }
            if (!string.IsNullOrWhiteSpace(snapshot.BackgroundScheduler.PauseReason)) {
                runtimeIdentity.AppendLine("background_scheduler_pause_reason: " + snapshot.BackgroundScheduler.PauseReason);
            }
            if (snapshot.BackgroundScheduler.ReadyThreadIds.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_ready_thread_ids: " + string.Join(", ", snapshot.BackgroundScheduler.ReadyThreadIds));
            }
            if (snapshot.BackgroundScheduler.RecentActivity.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_recent_activity: " + string.Join(
                    " | ",
                    snapshot.BackgroundScheduler.RecentActivity
                        .Select(BuildBackgroundSchedulerActivitySummary)));
            }
            if (snapshot.BackgroundScheduler.ThreadSummaries.Length > 0) {
                runtimeIdentity.AppendLine("background_scheduler_thread_summaries: " + string.Join(
                    " | ",
                    snapshot.BackgroundScheduler.ThreadSummaries
                        .Select(BuildBackgroundSchedulerThreadSummaryText)));
            }
        }
        if (snapshot.ParityEntries.Length > 0) {
            runtimeIdentity.AppendLine("parity_engine_count: " + snapshot.ParityEntries.Length);
            runtimeIdentity.AppendLine("parity_attention_count: " + snapshot.ParityAttentionCount);
            runtimeIdentity.AppendLine("parity_missing_readonly_capabilities: " + snapshot.ParityMissingCapabilityCount);

            var attentionSummaries = ToolCapabilityParityInventoryBuilder.BuildAttentionSummaries(
                snapshot.ParityEntries,
                MaxCapabilitySnapshotParityAttention);
            if (attentionSummaries.Count > 0) {
                runtimeIdentity.AppendLine("parity_attention: " + string.Join(" | ", attentionSummaries));
            }

            var detailSummaries = ToolCapabilityParityInventoryBuilder.BuildDetailSummaries(
                snapshot.ParityEntries,
                MaxCapabilitySnapshotParityDetail);
            if (detailSummaries.Count > 0) {
                runtimeIdentity.AppendLine("parity_detail: " + string.Join(" | ", detailSummaries));
            }
        }

        runtimeIdentity.AppendLine("Use this snapshot only for routing and tool-availability decisions.");
        runtimeIdentity.AppendLine("Parity fields describe phase-1 read-only engine coverage; use them to avoid promising governed or missing surfaces as live tools.");
        runtimeIdentity.AppendLine("Background scheduler fields describe deferred read-only follow-up readiness across tracked threads.");
        runtimeIdentity.AppendLine("Deferred work affordances describe registered reporting/email/notification-style follow-up surfaces that the current runtime explicitly advertises.");
        runtimeIdentity.AppendLine("Do not narrate this snapshot to the user unless they explicitly ask about runtime, tooling, or bootstrap state.");

        runtimeIdentity.AppendLine();
        runtimeIdentity.AppendLine("[Skills snapshot]");
        runtimeIdentity.AppendLine(SkillsSnapshotMarker);
        runtimeIdentity.AppendLine("skill_count: " + snapshot.Skills.Length);
        if (snapshot.Skills.Length > 0) {
            runtimeIdentity.AppendLine("skills: " + string.Join(", ", snapshot.Skills));
        }

        runtimeIdentity.AppendLine("Use this skills snapshot only to decide reusable skill availability for this turn.");
        runtimeIdentity.AppendLine("Do not narrate this skills inventory unless the user explicitly asks about it.");
    }

    private static string[] BuildCapabilitySnapshotToolingPackDetails(
        SessionCapabilityToolingSnapshotDto toolingSnapshot,
        int maxItems) {
        if (toolingSnapshot.Packs.Length == 0 || maxItems == 0) {
            return Array.Empty<string>();
        }

        return toolingSnapshot.Packs
            .Where(static pack => pack is not null && !string.IsNullOrWhiteSpace(pack.Id))
            .OrderBy(static pack => pack.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .Select(static pack => FormatCapabilitySnapshotToolingPackDetail(pack))
            .Where(static detail => detail.Length > 0)
            .ToArray();
    }

    private static string[] BuildCapabilitySnapshotToolingPluginDetails(
        SessionCapabilityToolingSnapshotDto toolingSnapshot,
        int maxItems) {
        if (toolingSnapshot.Plugins.Length == 0 || maxItems == 0) {
            return Array.Empty<string>();
        }

        return toolingSnapshot.Plugins
            .Where(static plugin => plugin is not null && !string.IsNullOrWhiteSpace(plugin.Id))
            .OrderBy(static plugin => plugin.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static plugin => plugin.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .Select(static plugin => FormatCapabilitySnapshotToolingPluginDetail(plugin))
            .Where(static detail => detail.Length > 0)
            .ToArray();
    }

    private static string[] NormalizeCapabilitySnapshotActivatablePackIds(SessionCapabilityToolingSnapshotDto? toolingSnapshot) {
        if (toolingSnapshot?.Packs is not { Length: > 0 }) {
            return Array.Empty<string>();
        }

        return NormalizeDistinctStrings(
            toolingSnapshot.Packs
                .Where(static pack => pack is not null && pack.CanActivateOnDemand)
                .Select(static pack => NormalizePackId(pack.Id))
                .Where(static packId => packId.Length > 0),
            MaxCapabilitySnapshotPackIds);
    }

    private static string[] NormalizeCapabilitySnapshotActivatablePluginIds(SessionCapabilityToolingSnapshotDto? toolingSnapshot) {
        if (toolingSnapshot?.Plugins is not { Length: > 0 }) {
            return Array.Empty<string>();
        }

        return NormalizeDistinctStrings(
            toolingSnapshot.Plugins
                .Where(static plugin => plugin is not null && plugin.CanActivateOnDemand)
                .Select(static plugin => NormalizePackId(plugin.Id))
                .Where(static pluginId => pluginId.Length > 0),
            MaxCapabilitySnapshotPluginIds);
    }

    private void AppendDeferredDescriptorPreviewToolPromptBlock(StringBuilder runtimeIdentity) {
        ArgumentNullException.ThrowIfNull(runtimeIdentity);

        if (_registry.GetDefinitions().Count > 0) {
            return;
        }

        var definitions = GetDeferredToolDefinitionsForBootstrapDecision(options: null);
        if (definitions.Length == 0) {
            return;
        }

        runtimeIdentity.AppendLine("descriptor_preview_tool_count: " + definitions.Length);
        var toolDetails = definitions
            .Where(static definition => definition is not null && !string.IsNullOrWhiteSpace(definition.Name))
            .Take(MaxCapabilitySnapshotDescriptorPreviewTools)
            .Select(static definition => FormatDeferredDescriptorPreviewToolDetail(definition))
            .Where(static detail => detail.Length > 0)
            .ToArray();
        if (toolDetails.Length > 0) {
            runtimeIdentity.AppendLine("descriptor_preview_tools: " + string.Join(" | ", toolDetails));
        }

        var representativeExamples = NormalizeDistinctStrings(
            definitions
                .SelectMany(static definition => definition.RepresentativeExamples ?? Array.Empty<string>())
                .Select(static example => (example ?? string.Empty).Trim())
                .Where(static example => example.Length > 0),
            MaxCapabilitySnapshotDescriptorPreviewExamples);
        if (representativeExamples.Length > 0) {
            runtimeIdentity.AppendLine("descriptor_preview_examples: " + string.Join(" | ", representativeExamples));
        }

        runtimeIdentity.AppendLine("Descriptor preview tools are descriptor-only candidates. They are not live callable schemas yet, so use them for routing and likely activation targets only.");
    }

    private static string FormatDeferredDescriptorPreviewToolDetail(ToolDefinitionDto definition) {
        var name = (definition.Name ?? string.Empty).Trim();
        if (name.Length == 0) {
            return string.Empty;
        }

        var detail = new StringBuilder(name);
        detail.Append('[');
        var segments = new List<string>(capacity: 12);
        var packId = NormalizePackId(definition.PackId);
        if (packId.Length > 0) {
            segments.Add("pack=" + packId);
        }

        var category = (definition.Category ?? string.Empty).Trim();
        if (category.Length > 0) {
            segments.Add("category=" + category);
        }

        var executionScope = (definition.ExecutionScope ?? string.Empty).Trim();
        if (executionScope.Length > 0) {
            segments.Add("scope=" + executionScope);
        }

        var routingRole = (definition.RoutingRole ?? string.Empty).Trim();
        if (routingRole.Length > 0) {
            segments.Add("role=" + routingRole);
        }

        segments.Add(definition.IsWriteCapable ? "write" : "read");
        if (definition.SupportsRemoteHostTargeting) {
            segments.Add("remote_host");
        } else if (definition.SupportsRemoteExecution) {
            segments.Add("remote_capable");
        }

        if (definition.IsSetupAware) {
            segments.Add(string.IsNullOrWhiteSpace(definition.SetupToolName)
                ? "setup"
                : "setup=" + definition.SetupToolName.Trim());
        }

        if (definition.SupportsConnectivityProbe) {
            segments.Add(string.IsNullOrWhiteSpace(definition.ProbeToolName)
                ? "probe"
                : "probe=" + definition.ProbeToolName.Trim());
        }

        if (definition.IsRecoveryAware) {
            var recoveryToolName = definition.RecoveryToolNames.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            segments.Add(string.IsNullOrWhiteSpace(recoveryToolName)
                ? "recovery"
                : "recovery=" + recoveryToolName.Trim());
        }

        var handoffTargetToolName = definition.HandoffTargetToolNames.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(handoffTargetToolName)) {
            segments.Add("handoff=" + handoffTargetToolName.Trim());
        } else {
            var handoffTargetPackId = definition.HandoffTargetPackIds.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(handoffTargetPackId)) {
                segments.Add("handoff_pack=" + handoffTargetPackId.Trim());
            }
        }

        detail.Append(string.Join("|", segments));
        detail.Append(']');
        return detail.ToString();
    }

    private static string FormatCapabilitySnapshotToolingPackDetail(ToolPackInfoDto pack) {
        var label = string.IsNullOrWhiteSpace(pack.Name) ? NormalizePackId(pack.Id) : pack.Name.Trim();
        if (label.Length == 0) {
            return string.Empty;
        }

        var parts = new List<string> {
            pack.Enabled ? "enabled" : "disabled",
            FormatCapabilitySnapshotActivationState(pack.ActivationState, pack.Enabled),
            FormatCapabilitySnapshotSourceKind(pack.SourceKind)
        };
        if (!string.IsNullOrWhiteSpace(pack.EngineId)) {
            parts.Add(pack.EngineId.Trim());
        }

        if (pack.CapabilityTags.Length > 0) {
            parts.Add(string.Join("/", pack.CapabilityTags.Take(2)));
        }

        return label + "[" + string.Join("|", parts) + "]";
    }

    private static string FormatCapabilitySnapshotToolingPluginDetail(PluginInfoDto plugin) {
        var label = string.IsNullOrWhiteSpace(plugin.Name)
            ? NormalizePackId(plugin.Id)
            : plugin.Name.Trim();
        if (label.Length == 0) {
            return string.Empty;
        }

        var parts = new List<string> {
            plugin.Enabled ? "enabled" : "disabled",
            FormatCapabilitySnapshotActivationState(plugin.ActivationState, plugin.Enabled),
            FormatCapabilitySnapshotSourceKind(plugin.SourceKind)
        };
        if (!string.IsNullOrWhiteSpace(plugin.Origin)) {
            parts.Add(plugin.Origin.Trim());
        }

        if (plugin.PackIds.Length > 0) {
            parts.Add("packs=" + string.Join("/", plugin.PackIds.Take(2)));
        }

        return label + "[" + string.Join("|", parts) + "]";
    }

    private static string FormatCapabilitySnapshotSourceKind(ToolPackSourceKind sourceKind) {
        return sourceKind switch {
            ToolPackSourceKind.Builtin => "builtin",
            ToolPackSourceKind.ClosedSource => "closed_source",
            _ => "open_source"
        };
    }

    private static string FormatCapabilitySnapshotActivationState(string? activationState, bool enabled) {
        return ToolActivationStates.NormalizeOrDefault(activationState, enabled);
    }
}
