using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private const int MaxHostCapabilitySnapshotIds = 8;
    private const int MaxHostCapabilitySnapshotSkills = 8;

    private static ToolPackBootstrapResult BuildPackBootstrapResult(
        ReplOptions options,
        ToolRuntimePolicyContext runtimePolicy,
        Action<string>? onBootstrapWarning = null) {
        var bootstrapOptions = BuildBootstrapOptions(options, runtimePolicy, onBootstrapWarning);
        var bootstrapResult = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(bootstrapOptions);
        if (ToolPackBootstrap.IsPluginOnlyModeNoPacks(bootstrapOptions, bootstrapResult.Packs.Count)) {
            var pluginRoots = ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions);
            onBootstrapWarning?.Invoke(ToolPackBootstrap.BuildPluginOnlyNoPacksWarning(pluginRoots.Count));
        }

        return bootstrapResult;
    }

    private static IReadOnlyList<IToolPack> BuildPacks(
        ReplOptions options,
        ToolRuntimePolicyContext runtimePolicy,
        Action<string>? onBootstrapWarning = null) {
        return BuildPackBootstrapResult(options, runtimePolicy, onBootstrapWarning).Packs;
    }

    private static IReadOnlyList<string> GetPluginSearchPaths(ReplOptions options, ToolRuntimePolicyContext runtimePolicy) {
        return ToolPackBootstrap.GetPluginSearchPaths(BuildBootstrapOptions(options, runtimePolicy));
    }

    private static ToolPackBootstrapOptions BuildBootstrapOptions(
        ReplOptions options,
        ToolRuntimePolicyContext runtimePolicy,
        Action<string>? onBootstrapWarning = null) {
        return ToolPackBootstrap.CreateRuntimeBootstrapOptions(options, runtimePolicy, onBootstrapWarning);
    }

    private static ToolRuntimePolicyOptions BuildRuntimePolicyOptions(ReplOptions options) {
        return ToolRuntimePolicyBootstrap.CreateOptions(options);
    }

    private static void WritePolicyBanner(
        ReplOptions options,
        IReadOnlyList<IToolPack> packs,
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        IReadOnlyList<ToolPluginAvailabilityInfo> pluginAvailability,
        ToolRuntimePolicyContext runtimePolicyContext,
        ToolRuntimePolicyDiagnostics runtimePolicyDiagnostics,
        ToolRoutingCatalogDiagnostics routingCatalogDiagnostics,
        ToolOrchestrationCatalog orchestrationCatalog,
        IReadOnlyList<string>? bootstrapWarnings = null) {
        var descriptors = ToolPackBootstrap.GetDescriptors(packs);
        var dangerousEnabled = descriptors.Any(static p => p.IsDangerous || p.Tier == ToolCapabilityTier.DangerousWrite);
        var pluginPaths = GetPluginSearchPaths(options, runtimePolicyContext);
        var familySummaries = ToolRoutingCatalogDiagnosticsBuilder.FormatFamilySummaries(routingCatalogDiagnostics, maxItems: 8);
        var routingWarnings = ToolRoutingCatalogDiagnosticsBuilder.BuildWarnings(routingCatalogDiagnostics, maxWarnings: 12);
        var routingReadiness = ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(routingCatalogDiagnostics, maxItems: 6);
        var parityEntries = BuildCapabilityParityEntries(packs, runtimePolicyDiagnostics.RequireExplicitRoutingMetadata);
        var parityDetails = ToolCapabilityParityInventoryBuilder.BuildDetailSummaries(parityEntries, maxItems: 8);
        var parityAttention = ToolCapabilityParityInventoryBuilder.BuildAttentionSummaries(parityEntries, maxItems: 6);
        var unavailablePackWarnings = BuildUnavailablePackAvailabilityWarnings(packAvailability);
        var formattedBootstrapWarnings = BuildFormattedPackWarnings(bootstrapWarnings, packAvailability);
        var capabilitySnapshot = BuildHostCapabilitySnapshot(
            options.AllowedRoots.Count,
            BuildToolDefinitions(packs, runtimePolicyDiagnostics.RequireExplicitRoutingMetadata),
            packAvailability,
            pluginAvailability,
            routingCatalogDiagnostics,
            orchestrationCatalog);
        var capabilityHighlights = BuildCapabilitySnapshotHighlights(capabilitySnapshot);

        Console.WriteLine("Policy:");
        Console.WriteLine($"  Mode: {(dangerousEnabled ? "mixed (dangerous pack enabled)" : "read-only (no writes implied)")}");
        Console.WriteLine(
            $"  Write governance: mode={ToolRuntimePolicyBootstrap.FormatWriteGovernanceMode(runtimePolicyDiagnostics.WriteGovernanceMode)}, " +
            $"runtime_required={(runtimePolicyDiagnostics.RequireWriteGovernanceRuntime ? "on" : "off")}, " +
            $"runtime_configured={(runtimePolicyDiagnostics.WriteGovernanceRuntimeConfigured ? "yes" : "no")}");
        Console.WriteLine(
            $"  Write audit sink: mode={ToolRuntimePolicyBootstrap.FormatWriteAuditSinkMode(runtimePolicyDiagnostics.WriteAuditSinkMode)}, " +
            $"required={(runtimePolicyDiagnostics.RequireWriteAuditSinkForWriteOperations ? "on" : "off")}, " +
            $"configured={(runtimePolicyDiagnostics.WriteAuditSinkConfigured ? "yes" : "no")}, " +
            $"path={(string.IsNullOrWhiteSpace(runtimePolicyDiagnostics.WriteAuditSinkPath) ? "(none)" : runtimePolicyDiagnostics.WriteAuditSinkPath)}");
        Console.WriteLine(
            $"  Auth runtime: preset={ToolRuntimePolicyBootstrap.FormatAuthenticationRuntimePreset(runtimePolicyDiagnostics.AuthenticationPreset)}, " +
            $"explicit_routing_required={(runtimePolicyDiagnostics.RequireExplicitRoutingMetadata ? "on" : "off")}, " +
            $"required={(runtimePolicyDiagnostics.RequireAuthenticationRuntime ? "on" : "off")}, " +
            $"configured={(runtimePolicyDiagnostics.AuthenticationRuntimeConfigured ? "yes" : "no")}, " +
            $"smtp_probe_required={(runtimePolicyDiagnostics.RequireSuccessfulSmtpProbeForSend ? "on" : "off")}, " +
            $"smtp_probe_max_age_seconds={runtimePolicyDiagnostics.SmtpProbeMaxAgeSeconds}, " +
            $"run_as_profile_path={(string.IsNullOrWhiteSpace(runtimePolicyDiagnostics.RunAsProfilePath) ? "(none)" : runtimePolicyDiagnostics.RunAsProfilePath)}, " +
            $"auth_profile_path={(string.IsNullOrWhiteSpace(runtimePolicyDiagnostics.AuthenticationProfilePath) ? "(none)" : runtimePolicyDiagnostics.AuthenticationProfilePath)}");
        var maxTable = options.MaxTableRows <= 0 ? "(none)" : options.MaxTableRows.ToString();
        var maxSample = options.MaxSample <= 0 ? "(none)" : options.MaxSample.ToString();
        Console.WriteLine($"  Response shaping: max_table_rows={maxTable}, max_sample={maxSample}, redact={(options.Redact ? "on" : "off")}");
        Console.WriteLine($"  Allowed roots: {(options.AllowedRoots.Count == 0 ? "(none)" : string.Join("; ", options.AllowedRoots))}");
        Console.WriteLine($"  Plugin search paths: {(pluginPaths.Count == 0 ? "(none)" : string.Join("; ", pluginPaths))}");
        Console.WriteLine($"  Routing catalog: {ToolRoutingCatalogDiagnosticsBuilder.FormatSummary(routingCatalogDiagnostics)}");
        Console.WriteLine($"  Capability snapshot: {FormatCapabilitySnapshotSummary(capabilitySnapshot)}");
        if (parityEntries.Length > 0) {
            Console.WriteLine($"  Capability parity: {ToolCapabilityParityInventoryBuilder.FormatSummary(parityEntries)}");
        }
        if (capabilityHighlights.Count > 0) {
            Console.WriteLine("  Capability readiness:");
            foreach (var highlight in capabilityHighlights) {
                Console.WriteLine($"    - {highlight}");
            }
        }
        if (parityDetails.Count > 0) {
            Console.WriteLine("  Capability parity detail:");
            foreach (var line in parityDetails) {
                Console.WriteLine($"    - {line}");
            }
        }
        if (familySummaries.Count > 0) {
            Console.WriteLine("  Routing families:");
            foreach (var familySummary in familySummaries) {
                Console.WriteLine($"    - {familySummary}");
            }
        }
        if (routingReadiness.Count > 0) {
            Console.WriteLine("  Routing autonomy readiness:");
            foreach (var highlight in routingReadiness) {
                Console.WriteLine($"    - {highlight}");
            }
        }
        if (routingWarnings.Count > 0) {
            Console.WriteLine("  Routing warnings:");
            foreach (var warning in routingWarnings) {
                Console.WriteLine($"    - {warning}");
            }
        }
        if (parityAttention.Count > 0) {
            Console.WriteLine("  Capability parity attention:");
            foreach (var warning in parityAttention) {
                Console.WriteLine($"    - {warning}");
            }
        }
        Console.WriteLine("  Packs:");

        foreach (var p in descriptors) {
            Console.WriteLine($"    - {p.Id} ({p.Tier})");
        }

        if (unavailablePackWarnings.Count > 0) {
            foreach (var line in StartupWarningPreviewFormatter.BuildLines(
                         unavailablePackWarnings,
                         static warning => warning,
                         "Unavailable packs:",
                         "Found {0} unavailable pack(s):",
                         "Use /tools or /toolsjson to inspect full pack availability details.")) {
                Console.WriteLine(line.Length == 0 ? string.Empty : $"  {line}");
            }
        }

        if (formattedBootstrapWarnings.Count > 0) {
            foreach (var line in StartupWarningPreviewFormatter.BuildLines(
                         formattedBootstrapWarnings,
                         static warning => warning,
                         "Pack warnings:",
                         "Found {0} startup pack warning(s):",
                         "Use /tools or /toolsjson to inspect full bootstrap warning details.")) {
                Console.WriteLine(line.Length == 0 ? string.Empty : $"  {line}");
            }
        }

        Console.WriteLine($"  Dangerous tools: {(dangerousEnabled ? "enabled (explicit opt-in)" : "disabled")}");
    }

    private static ToolRoutingCatalogDiagnostics BuildRoutingCatalogDiagnostics(
        IReadOnlyList<IToolPack> packs,
        bool requireExplicitRoutingMetadata) {
        return BuildHostToolRegistry(packs, requireExplicitRoutingMetadata).RoutingCatalogDiagnostics;
    }

    private static HostToolRegistryContext BuildHostToolRegistry(
        IReadOnlyList<IToolPack> packs,
        bool requireExplicitRoutingMetadata) {
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = requireExplicitRoutingMetadata
        };
        ToolPackBootstrap.RegisterAll(registry, packs);
        return new HostToolRegistryContext(
            registry,
            ToolRoutingCatalogDiagnosticsBuilder.Build(registry),
            ToolOrchestrationCatalog.Build(registry.GetDefinitions()));
    }

    private static SessionCapabilityParityEntryDto[] BuildCapabilityParityEntries(
        IReadOnlyList<IToolPack> packs,
        bool requireExplicitRoutingMetadata) {
        var registry = BuildHostToolRegistry(packs, requireExplicitRoutingMetadata).Registry;
        var descriptors = ToolPackBootstrap.GetDescriptors(packs);
        var availability = descriptors
            .Select(static descriptor => new ToolPackAvailabilityInfo {
                Id = descriptor.Id ?? string.Empty,
                Name = descriptor.Name ?? string.Empty,
                Description = descriptor.Description,
                Tier = descriptor.Tier,
                IsDangerous = descriptor.IsDangerous,
                SourceKind = descriptor.SourceKind ?? string.Empty,
                Enabled = true
            })
            .ToArray();
        return ToolCapabilityParityInventoryBuilder.Build(registry.GetDefinitions(), availability);
    }

    private static IReadOnlyList<ToolDefinition> BuildToolDefinitions(
        IReadOnlyList<IToolPack> packs,
        bool requireExplicitRoutingMetadata) {
        return BuildHostToolRegistry(packs, requireExplicitRoutingMetadata).Registry.GetDefinitions();
    }

    internal static SessionCapabilitySnapshotDto BuildHostCapabilitySnapshot(
        int allowedRootCount,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IEnumerable<ToolPackAvailabilityInfo> packAvailability,
        IEnumerable<ToolPluginAvailabilityInfo>? pluginAvailability,
        ToolRoutingCatalogDiagnostics routingCatalogDiagnostics,
        ToolOrchestrationCatalog orchestrationCatalog) {
        var normalizedPackAvailability = (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>()).ToArray();
        var normalizedPluginAvailability = (pluginAvailability ?? Array.Empty<ToolPluginAvailabilityInfo>()).ToArray();
        var familyActions = routingCatalogDiagnostics.FamilyActions.Count == 0
            ? Array.Empty<SessionRoutingFamilyActionSummaryDto>()
            : routingCatalogDiagnostics.FamilyActions
                .Where(static summary =>
                    !string.IsNullOrWhiteSpace(summary.Family)
                    && !string.IsNullOrWhiteSpace(summary.ActionId))
                .Select(static summary => new SessionRoutingFamilyActionSummaryDto {
                    Family = summary.Family.Trim(),
                    ActionId = summary.ActionId.Trim(),
                    ToolCount = Math.Max(0, summary.ToolCount)
                })
                .ToArray();
        var routingFamilies = NormalizeDistinctStrings(
            familyActions
                .Select(static item => item.Family)
                .Where(family => ToolSelectionMetadata.TryNormalizeDomainIntentFamily(family, out _)),
            MaxHostCapabilitySnapshotIds);
        var enabledPackIds = NormalizeDistinctStrings(
            normalizedPackAvailability
                .Where(static pack => pack.Enabled)
                .Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Id)),
            MaxHostCapabilitySnapshotIds);
        var allPluginIds = NormalizeDistinctStrings(
            normalizedPluginAvailability.Select(static plugin => ToolPackBootstrap.NormalizePackId(plugin.Id)),
            maxItems: 0);
        if (allPluginIds.Length == 0) {
            allPluginIds = NormalizeDistinctStrings(
                normalizedPackAvailability.Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Id)),
                maxItems: 0);
        }

        var enabledPluginIds = NormalizeDistinctStrings(
            normalizedPluginAvailability
                .Where(static plugin => plugin.Enabled)
                .Select(static plugin => ToolPackBootstrap.NormalizePackId(plugin.Id)),
            MaxHostCapabilitySnapshotIds);
        if (enabledPluginIds.Length == 0) {
            enabledPluginIds = NormalizeDistinctStrings(
                normalizedPackAvailability
                    .Where(static pack => pack.Enabled)
                    .Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Id)),
                MaxHostCapabilitySnapshotIds);
        }
        var enabledPackEngineIds = NormalizeDistinctStrings(
            normalizedPackAvailability
                .Where(static pack => pack.Enabled)
                .Select(static pack => ToolPackMetadataNormalizer.NormalizeDescriptorToken(pack.EngineId))
                .Where(static engineId => engineId.Length > 0),
            MaxHostCapabilitySnapshotIds);
        var enabledCapabilityTags = NormalizeDistinctStrings(
            normalizedPackAvailability
                .Where(static pack => pack.Enabled)
                .SelectMany(static pack => pack.CapabilityTags ?? Array.Empty<string>())
                .Select(static tag => ToolPackMetadataNormalizer.NormalizeDescriptorToken(tag))
                .Where(static tag => tag.Length > 0),
            maxItems: 12);

        var skills = NormalizeDistinctStrings(
            normalizedPluginAvailability.SelectMany(static plugin => plugin.SkillIds ?? Array.Empty<string>()),
            MaxHostCapabilitySnapshotSkills);
        var autonomy = ToolAutonomySummaryBuilder.BuildCapabilityAutonomySummary(
            normalizedPackAvailability,
            orchestrationCatalog,
            MaxHostCapabilitySnapshotIds);
        var parityEntries = ToolCapabilityParityInventoryBuilder.Build(toolDefinitions, normalizedPackAvailability);
        var parityAttentionCount = parityEntries.Count(static entry =>
            !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.HealthyStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Status, ToolCapabilityParityInventoryBuilder.PackUnavailableStatus, StringComparison.OrdinalIgnoreCase));
        var parityMissingCapabilityCount = parityEntries.Sum(static entry => Math.Max(0, entry.MissingCapabilityCount));

        return new SessionCapabilitySnapshotDto {
            RegisteredTools = Math.Max(0, routingCatalogDiagnostics.TotalTools),
            EnabledPackCount = enabledPackIds.Length,
            PluginCount = allPluginIds.Length,
            EnabledPluginCount = enabledPluginIds.Length,
            ToolingAvailable = enabledPackIds.Length > 0 || routingCatalogDiagnostics.TotalTools > 0,
            AllowedRootCount = Math.Max(0, allowedRootCount),
            EnabledPackIds = enabledPackIds,
            EnabledPluginIds = enabledPluginIds,
            EnabledPackEngineIds = enabledPackEngineIds,
            EnabledCapabilityTags = enabledCapabilityTags,
            RoutingFamilies = routingFamilies,
            FamilyActions = familyActions,
            Skills = skills,
            HealthyTools = Array.Empty<string>(),
            RemoteReachabilityMode = ResolveHostRemoteReachabilityMode(routingCatalogDiagnostics),
            Autonomy = autonomy,
            ParityEntries = parityEntries,
            ParityAttentionCount = Math.Max(0, parityAttentionCount),
            ParityMissingCapabilityCount = Math.Max(0, parityMissingCapabilityCount)
        };
    }

    internal static string FormatCapabilitySnapshotSummary(SessionCapabilitySnapshotDto? snapshot) {
        if (snapshot is null) {
            return "not available";
        }

        var autonomy = snapshot.Autonomy;
        var autonomySummary = autonomy is null
            ? "autonomy n/a"
            : $"autonomy remote-capable {autonomy.RemoteCapableToolCount}, cross-pack {autonomy.CrossPackHandoffToolCount}";
        return $"tools={snapshot.RegisteredTools}, enabled_packs={snapshot.EnabledPackCount}, plugins={snapshot.EnabledPluginCount}/{snapshot.PluginCount}, remote_reachability={snapshot.RemoteReachabilityMode ?? "unknown"}, {autonomySummary}";
    }

    internal static IReadOnlyList<string> BuildToolsInspectionLines(
        int allowedRootCount,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        IReadOnlyList<ToolPluginAvailabilityInfo> pluginAvailability,
        ToolRoutingCatalogDiagnostics routingCatalogDiagnostics,
        ToolOrchestrationCatalog orchestrationCatalog,
        bool showToolIds) {
        var lines = new List<string>();
        var snapshot = BuildHostCapabilitySnapshot(
            allowedRootCount,
            toolDefinitions,
            packAvailability,
            pluginAvailability,
            routingCatalogDiagnostics,
            orchestrationCatalog);
        lines.Add($"Capability snapshot: {FormatCapabilitySnapshotSummary(snapshot)}");

        var capabilityHighlights = BuildCapabilitySnapshotHighlights(snapshot);
        for (var i = 0; i < capabilityHighlights.Count; i++) {
            lines.Add("[capability] " + capabilityHighlights[i]);
        }

        lines.Add($"Routing catalog: {ToolRoutingCatalogDiagnosticsBuilder.FormatSummary(routingCatalogDiagnostics)}");
        var routingReadiness = ToolRoutingCatalogDiagnosticsBuilder.BuildAutonomyReadinessHighlights(routingCatalogDiagnostics, maxItems: 6);
        for (var i = 0; i < routingReadiness.Count; i++) {
            lines.Add("[routing] " + routingReadiness[i]);
        }

        var packLines = BuildPackInspectionLines(packAvailability, orchestrationCatalog);
        if (packLines.Count > 0) {
            lines.Add("Pack readiness:");
            for (var i = 0; i < packLines.Count; i++) {
                lines.Add("- " + packLines[i]);
            }
        }

        lines.Add($"Tools ({toolDefinitions.Count}):");
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var definition = toolDefinitions[i];
            if (definition is null) {
                continue;
            }

            var toolLine = BuildToolInspectionLine(definition, orchestrationCatalog, showToolIds);
            if (toolLine.Length > 0) {
                lines.Add("- " + toolLine);
            }
        }

        return lines;
    }

    internal static ToolListMessage BuildHostToolsExportMessage(
        int allowedRootCount,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        IReadOnlyList<ToolPluginAvailabilityInfo> pluginAvailability,
        ToolRoutingCatalogDiagnostics routingCatalogDiagnostics,
        ToolOrchestrationCatalog orchestrationCatalog) {
        return new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "host.toolsjson",
            Tools = ToolCatalogExportBuilder.BuildToolDefinitionDtos(toolDefinitions, orchestrationCatalog, packAvailability),
            Packs = ToolCatalogExportBuilder.BuildPackInfoDtos(packAvailability, orchestrationCatalog),
            RoutingCatalog = ToolCatalogExportBuilder.BuildRoutingCatalogDiagnosticsDto(routingCatalogDiagnostics),
            CapabilitySnapshot = BuildHostCapabilitySnapshot(
                allowedRootCount,
                toolDefinitions,
                packAvailability,
                pluginAvailability,
                routingCatalogDiagnostics,
                orchestrationCatalog)
        };
    }

    internal static IReadOnlyList<string> BuildCapabilitySnapshotHighlights(SessionCapabilitySnapshotDto? snapshot) {
        if (snapshot is null) {
            return Array.Empty<string>();
        }

        var highlights = new List<string>();
        if (snapshot.EnabledPackIds.Length > 0) {
            highlights.Add("enabled packs: " + string.Join(", ", snapshot.EnabledPackIds));
        }

        if (snapshot.EnabledPluginIds.Length > 0) {
            highlights.Add("enabled plugins: " + string.Join(", ", snapshot.EnabledPluginIds));
        }

        if (snapshot.EnabledPackEngineIds.Length > 0) {
            highlights.Add("enabled engines: " + string.Join(", ", snapshot.EnabledPackEngineIds));
        }

        if (snapshot.EnabledCapabilityTags.Length > 0) {
            highlights.Add("enabled capability tags: " + string.Join(", ", snapshot.EnabledCapabilityTags));
        }

        if (snapshot.RoutingFamilies.Length > 0) {
            highlights.Add("routing families: " + string.Join(", ", snapshot.RoutingFamilies));
        }

        if (snapshot.Skills.Length > 0) {
            highlights.Add("skills: " + string.Join(", ", snapshot.Skills));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RemoteReachabilityMode)) {
            highlights.Add("remote reachability: " + snapshot.RemoteReachabilityMode);
        }

        var autonomy = snapshot.Autonomy;
        if (autonomy is not null) {
            highlights.Add(
                "autonomy surface: remote-capable " + autonomy.RemoteCapableToolCount
                + ", setup-aware " + autonomy.SetupAwareToolCount
                + ", handoff-aware " + autonomy.HandoffAwareToolCount
                + ", recovery-aware " + autonomy.RecoveryAwareToolCount
                + ", cross-pack " + autonomy.CrossPackHandoffToolCount);
            if (autonomy.RemoteCapablePackIds.Length > 0) {
                highlights.Add("remote-capable packs: " + string.Join(", ", autonomy.RemoteCapablePackIds));
            }

            if (autonomy.CrossPackReadyPackIds.Length > 0) {
                highlights.Add("cross-pack ready packs: " + string.Join(", ", autonomy.CrossPackReadyPackIds));
            }

            if (autonomy.CrossPackTargetPackIds.Length > 0) {
                highlights.Add("cross-pack targets: " + string.Join(", ", autonomy.CrossPackTargetPackIds));
            }
        }

        if (snapshot.ParityAttentionCount > 0 || snapshot.ParityMissingCapabilityCount > 0) {
            highlights.Add(
                "parity: attention " + snapshot.ParityAttentionCount
                + ", missing capabilities " + snapshot.ParityMissingCapabilityCount);
        }

        return highlights;
    }

    private static IReadOnlyList<string> BuildPackInspectionLines(
        IReadOnlyList<ToolPackAvailabilityInfo> packAvailability,
        ToolOrchestrationCatalog orchestrationCatalog) {
        if (packAvailability.Count == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var orderedPacks = packAvailability
            .Where(static pack => pack.Enabled)
            .OrderBy(static pack => ToolPackBootstrap.NormalizePackId(pack.Id), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < orderedPacks.Length; i++) {
            var pack = orderedPacks[i];
            var packId = ToolPackBootstrap.NormalizePackId(pack.Id);
            if (packId.Length == 0) {
                continue;
            }

            var summary = ToolAutonomySummaryBuilder.BuildPackAutonomySummary(packId, orchestrationCatalog, maxItems: 6);
            if (summary is null) {
                continue;
            }

            var parts = new List<string> {
                $"tools={summary.TotalTools}",
                $"remote-capable={summary.RemoteCapableTools}",
                $"setup-aware={summary.SetupAwareTools}",
                $"handoff-aware={summary.HandoffAwareTools}",
                $"recovery-aware={summary.RecoveryAwareTools}",
                $"cross-pack={summary.CrossPackHandoffTools}"
            };
            if (summary.CrossPackTargetPacks.Length > 0) {
                parts.Add("targets=" + string.Join(", ", summary.CrossPackTargetPacks));
            }

            lines.Add($"{FormatPackInspectionLabel(pack)}: {string.Join(", ", parts)}");
        }

        return lines;
    }

    private static string BuildToolInspectionLine(
        ToolDefinition definition,
        ToolOrchestrationCatalog orchestrationCatalog,
        bool showToolIds) {
        var id = showToolIds ? $" ({definition.Name})" : string.Empty;
        var description = string.IsNullOrWhiteSpace(definition.Description) ? null : definition.Description!.Trim();
        var details = new List<string>();
        if (orchestrationCatalog.TryGetEntry(definition.Name, out var entry)) {
            if (entry.PackId.Length > 0) {
                details.Add("pack=" + entry.PackId);
            }

            if (entry.Role.Length > 0) {
                details.Add("role=" + entry.Role);
            }

            if (entry.ExecutionScope.Length > 0) {
                details.Add("scope=" + entry.ExecutionScope);
            }

            if (entry.RemoteHostArguments.Count > 0) {
                details.Add("remote_args=" + string.Join("/", entry.RemoteHostArguments));
            }

            if (entry.SetupToolName.Length > 0) {
                details.Add("setup=" + entry.SetupToolName);
            }

            if (entry.HandoffEdges.Count > 0) {
                var handoffTargets = entry.HandoffEdges
                    .Select(static edge => FormatHandoffTarget(edge.TargetPackId, edge.TargetToolName))
                    .Where(static target => target.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (handoffTargets.Length > 0) {
                    details.Add("handoff=" + string.Join(", ", handoffTargets));
                }
            }

            if (entry.RecoveryToolNames.Count > 0) {
                details.Add("recovery=" + string.Join(", ", entry.RecoveryToolNames));
            }
        }

        var suffix = details.Count == 0 ? string.Empty : $" [{string.Join(", ", details)}]";
        return string.IsNullOrWhiteSpace(description)
            ? $"{GetToolDisplayName(definition.Name)}{id}{suffix}"
            : $"{GetToolDisplayName(definition.Name)}{id}: {description}{suffix}";
    }

    private static string FormatPackInspectionLabel(ToolPackAvailabilityInfo pack) {
        var packId = ToolPackBootstrap.NormalizePackId(pack.Id);
        var packName = (pack.Name ?? string.Empty).Trim();
        if (packName.Length == 0) {
            return packId;
        }

        return string.Equals(packName, packId, StringComparison.OrdinalIgnoreCase)
            ? packName
            : $"{packName} [{packId}]";
    }

    private static string FormatHandoffTarget(string targetPackId, string targetToolName) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(targetPackId);
        var normalizedToolName = (targetToolName ?? string.Empty).Trim();
        if (normalizedPackId.Length == 0) {
            return normalizedToolName;
        }

        if (normalizedToolName.Length == 0) {
            return normalizedPackId;
        }

        return normalizedPackId + "/" + normalizedToolName;
    }

    private static string ResolveHostRemoteReachabilityMode(ToolRoutingCatalogDiagnostics routingCatalogDiagnostics) {
        if (routingCatalogDiagnostics.RemoteCapableTools > 0) {
            return "remote_capable";
        }

        return routingCatalogDiagnostics.TotalTools > 0 ? "local_only" : "unknown";
    }

    private sealed record HostToolRegistryContext(
        ToolRegistry Registry,
        ToolRoutingCatalogDiagnostics RoutingCatalogDiagnostics,
        ToolOrchestrationCatalog OrchestrationCatalog);

    private static string[] NormalizeDistinctStrings(IEnumerable<string>? values, int maxItems) {
        if (values is null) {
            return Array.Empty<string>();
        }

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var value in values) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !dedupe.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
            if (maxItems > 0 && list.Count >= maxItems) {
                break;
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }

    private static string? ApplyRuntimeShaping(string? instructions, ReplOptions options) {
        return ToolResponseShaping.AppendSessionResponseShapingInstructions(
            instructions,
            options.MaxTableRows,
            options.MaxSample,
            options.Redact);
    }

    private static string RedactText(string text) {
        return ToolResponseShaping.RedactEmailLikeTokens(text);
    }

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken ct, int seconds) {
        if (seconds <= 0) {
            return null;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    // Tool errors are returned as JSON strings to the model. Use the shared contract helper so
    // tool packs and hosts converge on the same envelope over time.
}
