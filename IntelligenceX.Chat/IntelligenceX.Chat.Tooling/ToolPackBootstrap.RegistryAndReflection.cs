using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

public static partial class ToolPackBootstrap {
    private const string BuiltInToolAssemblyManifestResourceSuffix = ".BuiltInToolAssemblies.txt";
    private const string BuiltInToolProbePathsEnvironmentVariable = "INTELLIGENCEX_BUILTIN_TOOL_PROBE_PATHS";
    private static readonly object BuiltInToolDependencyResolverGate = new();
    private static readonly HashSet<string> BuiltInToolDependencyResolverProbeRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BuiltInToolDependencyResolverAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<AssemblyDependencyResolver> BuiltInToolDependencyResolvers = new();
    private static bool BuiltInToolDependencyResolverRegistered;
    private static readonly IReadOnlyList<KnownBuiltInPackBootstrapMetadata> KnownBuiltInPackBootstrapMetadataByIdentity = new[] {
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "active_directory",
            assemblyName: "IntelligenceX.Tools.ADPlayground",
            name: "ADPlayground",
            tier: ToolCapabilityTier.SensitiveRead,
            isDangerous: false,
            description: "ADPlayground-backed Active Directory analysis, discovery, verification, and governed lifecycle tools.",
            sourceKind: "closed_source",
            engineId: "adplayground",
            category: "active_directory",
            aliases: new[] { "ad", "adplayground", "ad_lifecycle", "adlifecycle", "joiner_leaver", "adplayground_lifecycle" },
            capabilityTags: new[] { "directory", "domain_scope", "identity_lifecycle", "remote_analysis", "governed_write" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "eventlog",
            assemblyName: "IntelligenceX.Tools.EventLog",
            name: "Event Log (EventViewerX)",
            tier: ToolCapabilityTier.SensitiveRead,
            isDangerous: false,
            description: "Windows Event Log and EVTX analysis plus governed channel policy, classic log administration, and collector subscription writes (restricted to AllowedRoots for EVTX file access).",
            sourceKind: "builtin",
            engineId: "eventviewerx",
            category: "eventlog",
            aliases: new[] { "eventviewerx", "event_log" },
            capabilityTags: new[] { "event_logs", "evtx", "local_analysis", "remote_analysis", "governed_write", "write_capable" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "system",
            assemblyName: "IntelligenceX.Tools.System",
            name: "ComputerX",
            tier: ToolCapabilityTier.ReadOnly,
            isDangerous: false,
            description: "ComputerX host inventory, diagnostics, and governed service plus scheduled-task lifecycle operations.",
            sourceKind: "closed_source",
            engineId: "computerx",
            category: "system",
            aliases: new[] { "computerx" },
            capabilityTags: new[] { "host_inventory", "local_analysis", "local_execution", "remote_analysis", "remote_execution", "governed_write" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "filesystem",
            assemblyName: "IntelligenceX.Tools.FileSystem",
            name: "File System",
            tier: ToolCapabilityTier.ReadOnly,
            isDangerous: false,
            description: "Safe-by-default file system reads (restricted to AllowedRoots).",
            sourceKind: "builtin",
            engineId: "filesystem",
            category: "filesystem",
            aliases: new[] { "fs" },
            capabilityTags: new[] { "disk", "filesystem", "local_analysis" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "email",
            assemblyName: "IntelligenceX.Tools.Email",
            name: "Email (Mailozaurr)",
            tier: ToolCapabilityTier.SensitiveRead,
            isDangerous: false,
            description: "IMAP/SMTP workflows (search/get/probe/send) via Mailozaurr.",
            sourceKind: "builtin",
            engineId: "mailozaurr",
            category: "email",
            aliases: new[] { "mailozaurr" },
            capabilityTags: new[] { "email", "imap", "smtp", "remote_analysis", ToolPackCapabilityTags.DeferredCapabilityEmail }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "powershell",
            assemblyName: "IntelligenceX.Tools.PowerShell",
            name: "PowerShell Runtime",
            tier: ToolCapabilityTier.DangerousWrite,
            isDangerous: true,
            description: "Opt-in shell runtime execution (windows_powershell / pwsh / cmd).",
            sourceKind: "builtin",
            engineId: "powershell_runtime",
            category: "powershell",
            aliases: new[] { "powershell_runtime" },
            capabilityTags: new[] { ToolPackCapabilityTags.LocalExecution, ToolPackCapabilityTags.RemoteExecution, "shell", ToolPackCapabilityTags.WriteCapable }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "testimox",
            assemblyName: "IntelligenceX.Tools.TestimoX",
            name: "TestimoX",
            tier: ToolCapabilityTier.SensitiveRead,
            isDangerous: false,
            description: "TestimoX rule, profile, baseline, and stored-run diagnostics.",
            sourceKind: "closed_source",
            engineId: "testimox",
            category: "testimox",
            aliases: new[] { "testimoxpack" },
            capabilityTags: new[] { "configuration", "evidence", "posture", "remote_analysis" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "testimox_analytics",
            assemblyName: "IntelligenceX.Tools.TestimoX.Analytics",
            name: "TestimoX Analytics",
            tier: ToolCapabilityTier.SensitiveRead,
            isDangerous: false,
            description: "Persisted TestimoX analytics, report, and history artifact inspection.",
            sourceKind: "closed_source",
            engineId: "testimox_analytics",
            category: "testimox",
            aliases: new[] { "testimoxanalytics" },
            capabilityTags: new[] { "analytics", "evidence", "local_analysis", "posture", "reporting", ToolPackCapabilityTags.DeferredCapabilityReporting }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "officeimo",
            assemblyName: "IntelligenceX.Tools.OfficeIMO",
            name: "Office Documents (OfficeIMO)",
            tier: ToolCapabilityTier.ReadOnly,
            isDangerous: false,
            description: "Read-only Office document ingestion (Word/Excel/PowerPoint/Markdown/PDF) backed by OfficeIMO.Reader.",
            sourceKind: "open_source",
            engineId: "officeimo",
            category: "officeimo",
            aliases: Array.Empty<string>(),
            capabilityTags: new[] { "document_analysis", ToolPackCapabilityTags.LocalAnalysis, "office" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "reviewer_setup",
            assemblyName: "IntelligenceX.Tools.ReviewerSetup",
            name: "Reviewer Setup",
            tier: ToolCapabilityTier.ReadOnly,
            isDangerous: false,
            description: "Path contract and execution guidance for IntelligenceX reviewer onboarding.",
            sourceKind: "builtin",
            engineId: "reviewer_setup",
            category: "reviewer_setup",
            aliases: new[] { "reviewersetup" },
            capabilityTags: new[] { ToolPackCapabilityTags.LocalAnalysis, "onboarding", "reviewer", "setup" }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "dnsclientx",
            assemblyName: "IntelligenceX.Tools.DnsClientX",
            name: "DnsClientX",
            tier: ToolCapabilityTier.ReadOnly,
            isDangerous: false,
            description: "Open-source DNS query and connectivity diagnostics.",
            sourceKind: "open_source",
            engineId: "dnsclientx",
            category: "dns",
            aliases: new[] { "dns_client_x" },
            capabilityTags: new[] { "dns", "network", ToolPackCapabilityTags.RemoteAnalysis }),
        CreateKnownBuiltInPackBootstrapMetadata(
            id: "domaindetective",
            assemblyName: "IntelligenceX.Tools.DomainDetective",
            name: "DomainDetective",
            tier: ToolCapabilityTier.ReadOnly,
            isDangerous: false,
            description: "Open-source domain, DNS, and network-path diagnostics.",
            sourceKind: "open_source",
            engineId: "domaindetective",
            category: "dns",
            aliases: new[] { "domain_detective" },
            capabilityTags: new[] { "dns", "domain_scope", "network_path", ToolPackCapabilityTags.RemoteAnalysis })
    };

    /// <summary>
    /// Resolves plugin search roots used by folder-based plugin loading.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Deterministic plugin search roots.</returns>
    public static IReadOnlyList<string> GetPluginSearchPaths(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return PluginFolderToolPackLoader.ResolvePluginSearchRoots(options)
            .Select(static root => root.Path)
            .ToArray();
    }

    /// <summary>
    /// Resolves trusted built-in assembly probe roots used when dependency-graph resolution is unavailable.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Deterministic built-in assembly probe roots.</returns>
    public static IReadOnlyList<string> GetBuiltInToolAssemblyProbePaths(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ResolveBuiltInToolAssemblyProbePaths(options, typeof(ToolPackBootstrap).Assembly);
    }

    /// <summary>
    /// Builds a stable fingerprint describing the currently discoverable pack/plugin surface.
    /// Persisted bootstrap previews should only be reused when this fingerprint matches.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Stable lowercase SHA-256 fingerprint.</returns>
    public static string BuildDiscoveryFingerprint(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var lines = new List<string> {
            "built_in_pack_loading=" + (options.EnableBuiltInPackLoading ? "1" : "0"),
            "plugin_folder_loading=" + (options.EnablePluginFolderLoading ? "1" : "0"),
            "workspace_builtin_output_probing=" + (options.EnableWorkspaceBuiltInToolOutputProbing ? "1" : "0")
        };

        var allowedAssemblyNames = ResolveAllowedBuiltInAssemblyNames(options);
        foreach (var allowedAssemblyName in allowedAssemblyNames.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)) {
            lines.Add("allowed_builtin_assembly=" + allowedAssemblyName);
        }

        if (options.EnableBuiltInPackLoading) {
            foreach (var assemblyName in EnumerateToolAssemblyNamesForDiscovery(options)) {
                AppendBuiltInAssemblyFingerprint(lines, assemblyName, options);
            }
        }

        foreach (var root in PluginFolderToolPackLoader.ResolvePluginSearchRoots(options)
                     .OrderBy(static root => root.Path, StringComparer.OrdinalIgnoreCase)) {
            AppendPluginSearchRootFingerprint(lines, root);
        }

        using var sha = SHA256.Create();
        var payload = string.Join("\n", lines);
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Builds a lightweight fingerprint for the descriptor-preview surface without probing live built-in assemblies.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Stable lowercase SHA-256 fingerprint for preview metadata.</returns>
    public static string BuildDeferredDescriptorPreviewFingerprint(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return BuildDeferredDescriptorPreviewFingerprint(CreateDeferredDescriptorPreviewCore(options));
    }

    /// <summary>
    /// Builds a stable fingerprint for descriptor-preview metadata that has already been materialized.
    /// </summary>
    /// <param name="preview">Descriptor-preview result.</param>
    /// <returns>Stable lowercase SHA-256 fingerprint for preview metadata.</returns>
    public static string BuildDeferredDescriptorPreviewFingerprint(ToolPackBootstrapResult preview) {
        if (preview is null) {
            throw new ArgumentNullException(nameof(preview));
        }

        var normalizedPayload = new {
            ToolDefinitions = preview.ToolDefinitions
                .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static tool => tool.PackId, StringComparer.OrdinalIgnoreCase)
                .Select(static tool => tool)
                .ToArray(),
            PackAvailability = preview.PackAvailability
                .OrderBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static pack => pack.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static pack => pack)
                .ToArray(),
            PluginAvailability = preview.PluginAvailability
                .OrderBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static plugin => plugin)
                .ToArray(),
            PluginCatalog = preview.PluginCatalog
                .OrderBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static plugin => plugin)
                .ToArray()
        };

        using var sha = SHA256.Create();
        var payload = JsonSerializer.Serialize(normalizedPayload);
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IReadOnlyList<BuiltInPackRegistrationCandidate> DiscoverBuiltInPacks(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var packTypes = DiscoverBuiltInPackTypes(options, options.OnBootstrapWarning);
        return BuildBuiltInPackRegistrationCandidates(packTypes, options);
    }

    private static ToolPackBootstrapResult CreateBuiltInDescriptorPreviewCore(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.EnableBuiltInPackLoading) {
            return new ToolPackBootstrapResult();
        }

        var disabledPackIds = BuildNormalizedPackIdSet(options.DisabledPackIds);
        var enabledPackIds = BuildNormalizedPackIdSet(options.EnabledPackIds);
        var availabilityById = new Dictionary<string, ToolPackAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        var pluginAvailabilityById = new Dictionary<string, ToolPluginAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        var pluginCatalogById = new Dictionary<string, ToolPluginCatalogInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var metadata in EnumerateKnownBuiltInPackBootstrapMetadata()) {
            var enabled = ResolveKnownPackEnabled(
                packId: metadata.PackId,
                enabledByDefault: metadata.DefaultEnabled,
                disabledPackIds: disabledPackIds,
                enabledPackIds: enabledPackIds);
            var disabledReason = enabled ? null : DisabledByRuntimeConfigurationReason;
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDescriptor(
                    descriptor: metadata.Descriptor,
                    enabled: enabled,
                    disabledReason: disabledReason,
                    descriptorOnly: true));
            UpsertPluginAvailability(
                pluginAvailabilityById,
                CreateBuiltInPluginAvailability(
                    metadata,
                    enabled,
                    disabledReason,
                    descriptorOnly: true));
            UpsertPluginCatalog(
                pluginCatalogById,
                CreateBuiltInPluginCatalog(metadata));
        }

        return new ToolPackBootstrapResult {
            Packs = Array.Empty<IToolPack>(),
            PackAvailability = availabilityById.Values
                .OrderBy(static pack => pack.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PluginAvailability = pluginAvailabilityById.Values
                .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PluginCatalog = pluginCatalogById.Values
                .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static ToolPackBootstrapResult CreateDeferredDescriptorPreviewCore(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var builtInPreview = CreateBuiltInDescriptorPreviewCore(options);
        if (!options.EnablePluginFolderLoading) {
            return builtInPreview;
        }

        var disabledPackIds = BuildNormalizedPackIdSet(options.DisabledPackIds);
        var enabledPackIds = BuildNormalizedPackIdSet(options.EnabledPackIds);
        var pluginAvailabilityById = new Dictionary<string, ToolPluginAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        var pluginCatalogById = new Dictionary<string, ToolPluginCatalogInfo>(StringComparer.OrdinalIgnoreCase);
        var deferredToolDefinitions = new Dictionary<string, IntelligenceX.Chat.Abstractions.Protocol.ToolDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        var enabledDeferredPreviewPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < builtInPreview.PluginAvailability.Count; i++) {
            UpsertPluginAvailability(pluginAvailabilityById, builtInPreview.PluginAvailability[i]);
        }
        for (var i = 0; i < builtInPreview.PluginCatalog.Count; i++) {
            UpsertPluginCatalog(pluginCatalogById, builtInPreview.PluginCatalog[i]);
        }

        var pluginPreviewCatalog = PluginFolderToolPackLoader.CreatePluginCatalogPreview(options, options.OnBootstrapWarning);
        var pluginPreviewTools = PluginFolderToolPackLoader.CreatePluginToolDefinitionPreview(options, options.OnBootstrapWarning);
        for (var i = 0; i < pluginPreviewCatalog.Count; i++) {
            var catalog = pluginPreviewCatalog[i];
            var normalizedPluginId = NormalizePackId(catalog.Id);
            var enabled = ResolveDeferredPreviewPluginEnabled(
                catalog,
                normalizedPluginId,
                disabledPackIds,
                enabledPackIds);
            var disabledReason = ResolveDeferredPreviewPluginDisabledReason(
                catalog,
                normalizedPluginId,
                enabled,
                disabledPackIds,
                enabledPackIds);
            UpsertPluginCatalog(pluginCatalogById, catalog);
            UpsertPluginAvailability(
                pluginAvailabilityById,
                CreatePluginPreviewAvailability(catalog, enabled, disabledReason, descriptorOnly: true));
            if (enabled && catalog.PackIds is { Length: > 0 }) {
                for (var packIndex = 0; packIndex < catalog.PackIds.Length; packIndex++) {
                    var normalizedPackId = NormalizePackId(catalog.PackIds[packIndex]);
                    if (normalizedPackId.Length > 0) {
                        enabledDeferredPreviewPackIds.Add(normalizedPackId);
                    }
                }
            }
        }
        for (var i = 0; i < pluginPreviewTools.Count; i++) {
            var packId = NormalizePackId(pluginPreviewTools[i].PackId);
            if (packId.Length == 0 || !enabledDeferredPreviewPackIds.Contains(packId)) {
                continue;
            }

            deferredToolDefinitions[pluginPreviewTools[i].Name] = pluginPreviewTools[i];
        }

        return builtInPreview with {
            ToolDefinitions = ToolCatalogExportBuilder.OrderToolDefinitionDtosForCatalog(
                deferredToolDefinitions.Values.ToArray()),
            PluginAvailability = pluginAvailabilityById.Values
                .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PluginCatalog = pluginCatalogById.Values
                .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static ToolPackBootstrapResult ActivatePackOnDemandCore(
        ToolPackBootstrapOptions options,
        string packId,
        IEnumerable<IToolPack>? existingPacks) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var normalizedTargetPackId = NormalizePackId(packId);
        if (normalizedTargetPackId.Length == 0) {
            return new ToolPackBootstrapResult();
        }

        var existingPackList = existingPacks?.Where(static pack => pack is not null).ToArray() ?? Array.Empty<IToolPack>();
        var existingPackIds = new HashSet<string>(
            existingPackList
                .Select(static pack => NormalizePackId(pack.Descriptor.Id))
                .Where(static id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (existingPackIds.Contains(normalizedTargetPackId)) {
            return new ToolPackBootstrapResult();
        }

        var builtInResult = options.EnableBuiltInPackLoading
            ? ActivateBuiltInPackOnDemand(options, normalizedTargetPackId, existingPackIds)
            : new ToolPackBootstrapResult();
        if (builtInResult.Packs.Count > 0) {
            return builtInResult;
        }

        var pluginResult = options.EnablePluginFolderLoading
            ? PluginFolderToolPackLoader.LoadPluginPacksForPackId(options, normalizedTargetPackId, existingPackList, options.OnBootstrapWarning)
            : new ToolPackBootstrapResult();
        if (pluginResult.Packs.Count > 0) {
            return pluginResult;
        }

        if (builtInResult.PackAvailability.Count > 0 || builtInResult.PluginAvailability.Count > 0 || builtInResult.PluginCatalog.Count > 0) {
            return builtInResult;
        }

        return pluginResult;
    }

    private static ToolPackBootstrapResult ActivateBuiltInPackOnDemand(
        ToolPackBootstrapOptions options,
        string normalizedTargetPackId,
        HashSet<string> existingPackIds) {
        var disabledPackIds = BuildNormalizedPackIdSet(options.DisabledPackIds);
        var enabledPackIds = BuildNormalizedPackIdSet(options.EnabledPackIds);
        KnownBuiltInPackBootstrapMetadata? targetMetadata = null;
        if (TryResolveKnownBuiltInPackBootstrapMetadata(normalizedTargetPackId, out var resolvedTargetMetadata)) {
            targetMetadata = resolvedTargetMetadata;
            if (existingPackIds.Contains(normalizedTargetPackId)) {
                return new ToolPackBootstrapResult();
            }

            var targetEnabled = ResolveKnownPackEnabled(
                packId: targetMetadata.PackId,
                enabledByDefault: targetMetadata.DefaultEnabled,
                disabledPackIds: disabledPackIds,
                enabledPackIds: enabledPackIds);
            if (!targetEnabled) {
                return BuildBuiltInActivationResult(
                    targetMetadata.Descriptor,
                    pack: null,
                    enabled: false,
                    defaultEnabled: targetMetadata.DefaultEnabled,
                    disabledReason: DisabledByRuntimeConfigurationReason,
                    candidateType: typeof(ToolPackBootstrap),
                    metadataOverride: targetMetadata);
            }
        }

        var packTypes = DiscoverBuiltInPackTypes(options, options.OnBootstrapWarning, normalizedTargetPackId);

        for (var i = 0; i < packTypes.Count; i++) {
            var packType = packTypes[i];
            if (TryResolveKnownBuiltInPackBootstrapMetadata(packType, out var metadata)
                && !string.Equals(metadata.PackId, normalizedTargetPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (TryCreateDisabledKnownBuiltInPackCandidate(packType, disabledPackIds, enabledPackIds, out var disabledCandidate)) {
                if (!string.Equals(disabledCandidate.PackId, normalizedTargetPackId, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                return BuildBuiltInActivationResult(
                    disabledCandidate.Descriptor,
                    pack: null,
                    enabled: false,
                    defaultEnabled: disabledCandidate.DefaultEnabled,
                    disabledReason: DisabledByRuntimeConfigurationReason,
                    candidateType: packType,
                    metadataOverride: metadata);
            }

            if (!TryCreateBuiltInPack(packType, options, out var createdPack, out var error)) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='{NormalizeDisabledReason(error)}'",
                    shouldWarn: true);
                continue;
            }

            IToolPack normalizedPack;
            try {
                normalizedPack = RequireDeclaredSourceKind(createdPack, packType.FullName ?? packType.Name);
            } catch (Exception ex) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='{NormalizeDisabledReason(ex.Message)}'",
                    shouldWarn: true);
                continue;
            }

            var normalizedPackId = NormalizePackId(normalizedPack.Descriptor.Id);
            if (!string.Equals(normalizedPackId, normalizedTargetPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (existingPackIds.Contains(normalizedPackId)) {
                return new ToolPackBootstrapResult();
            }

            var defaultEnabled = metadata is not null
                ? metadata.DefaultEnabled
                : (!normalizedPack.Descriptor.IsDangerous
                   && normalizedPack.Descriptor.Tier != ToolCapabilityTier.DangerousWrite);
            var enabled = ResolveKnownPackEnabled(
                packId: normalizedPackId,
                enabledByDefault: defaultEnabled,
                disabledPackIds: disabledPackIds,
                enabledPackIds: enabledPackIds);
            var disabledReason = enabled ? null : DisabledByRuntimeConfigurationReason;
            return BuildBuiltInActivationResult(
                normalizedPack.Descriptor,
                enabled ? normalizedPack : null,
                enabled,
                defaultEnabled,
                disabledReason,
                packType,
                metadata);
        }

        if (targetMetadata is not null) {
            return BuildBuiltInActivationResult(
                targetMetadata.Descriptor,
                pack: null,
                enabled: false,
                defaultEnabled: targetMetadata.DefaultEnabled,
                disabledReason: UnavailableReasonFallback,
                candidateType: typeof(ToolPackBootstrap),
                metadataOverride: targetMetadata);
        }

        return new ToolPackBootstrapResult();
    }

    private static ToolPackBootstrapResult BuildBuiltInActivationResult(
        ToolPackDescriptor descriptor,
        IToolPack? pack,
        bool enabled,
        bool defaultEnabled,
        string? disabledReason,
        Type candidateType,
        KnownBuiltInPackBootstrapMetadata? metadataOverride) {
        var normalizedPackId = NormalizePackId(descriptor.Id);
        var packList = pack is null ? Array.Empty<IToolPack>() : new[] { pack };
        var packAvailability = new[] {
            CreateAvailabilityFromDescriptor(
                descriptor,
                enabled,
                disabledReason,
                descriptorOnly: false)
        };
        var pluginAvailability = new[] {
            metadataOverride is not null
                ? CreateBuiltInPluginAvailability(metadataOverride, enabled, disabledReason, descriptorOnly: false)
                : CreateBuiltInPluginAvailability(
                    new BuiltInPackRegistrationCandidate(
                        PackId: normalizedPackId,
                        Descriptor: descriptor,
                        PackType: candidateType,
                        Pack: pack,
                        DefaultEnabled: defaultEnabled),
                    enabled,
                    disabledReason,
                    descriptorOnly: false)
        };
        var pluginCatalog = new[] {
            metadataOverride is not null
                ? CreateBuiltInPluginCatalog(metadataOverride)
                : CreateBuiltInPluginCatalog(
                    new BuiltInPackRegistrationCandidate(
                        PackId: normalizedPackId,
                        Descriptor: descriptor,
                        PackType: candidateType,
                        Pack: pack,
                        DefaultEnabled: defaultEnabled))
        };

        return new ToolPackBootstrapResult {
            Packs = packList,
            PackAvailability = packAvailability,
            PluginAvailability = pluginAvailability,
            PluginCatalog = pluginCatalog
        };
    }

    private static bool ResolveDeferredPreviewPluginEnabled(
        ToolPluginCatalogInfo catalog,
        string normalizedPluginId,
        HashSet<string> disabledPackIds,
        HashSet<string> enabledPackIds) {
        var declaredPackIds = (catalog.PackIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (declaredPackIds.Length > 0) {
            for (var i = 0; i < declaredPackIds.Length; i++) {
                if (ResolveKnownPackEnabled(
                        packId: declaredPackIds[i],
                        enabledByDefault: catalog.DefaultEnabled,
                        disabledPackIds: disabledPackIds,
                        enabledPackIds: enabledPackIds)) {
                    return true;
                }
            }

            return false;
        }

        return ResolveKnownPackEnabled(
            packId: normalizedPluginId.Length == 0 ? catalog.Id : normalizedPluginId,
            enabledByDefault: catalog.DefaultEnabled,
            disabledPackIds: disabledPackIds,
            enabledPackIds: enabledPackIds);
    }

    private static string? ResolveDeferredPreviewPluginDisabledReason(
        ToolPluginCatalogInfo catalog,
        string normalizedPluginId,
        bool enabled,
        HashSet<string> disabledPackIds,
        HashSet<string> enabledPackIds) {
        if (enabled) {
            return null;
        }

        var declaredPackIds = (catalog.PackIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (declaredPackIds.Length > 0) {
            var anyExplicitlyDisabled = false;
            for (var i = 0; i < declaredPackIds.Length; i++) {
                if (disabledPackIds.Contains(declaredPackIds[i])) {
                    anyExplicitlyDisabled = true;
                    continue;
                }

                if (catalog.DefaultEnabled || enabledPackIds.Contains(declaredPackIds[i])) {
                    return null;
                }
            }

            if (anyExplicitlyDisabled) {
                return DisabledByRuntimeConfigurationReason;
            }

            if (!catalog.DefaultEnabled) {
                return DisabledByPluginManifestDefaultReason;
            }
        }

        if (normalizedPluginId.Length > 0
            && disabledPackIds.Contains(normalizedPluginId)) {
            return DisabledByRuntimeConfigurationReason;
        }

        if (!catalog.DefaultEnabled
            && (normalizedPluginId.Length == 0 || !enabledPackIds.Contains(normalizedPluginId))) {
            return DisabledByPluginManifestDefaultReason;
        }

        return DisabledByRuntimeConfigurationReason;
    }

    private static IReadOnlyList<KnownBuiltInPackBootstrapMetadata> EnumerateKnownBuiltInPackBootstrapMetadata() {
        var seenPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadata = new List<KnownBuiltInPackBootstrapMetadata>(KnownBuiltInPackBootstrapMetadataByIdentity.Count);
        for (var i = 0; i < KnownBuiltInPackBootstrapMetadataByIdentity.Count; i++) {
            var candidate = KnownBuiltInPackBootstrapMetadataByIdentity[i];
            if (!seenPackIds.Add(candidate.PackId)) {
                continue;
            }

            metadata.Add(candidate);
        }

        return metadata;
    }

    private static IReadOnlyList<BuiltInPackRegistrationCandidate> BuildBuiltInPackRegistrationCandidates(
        IReadOnlyList<Type> packTypes,
        ToolPackBootstrapOptions options) {
        if (packTypes is null) {
            throw new ArgumentNullException(nameof(packTypes));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var candidates = new List<BuiltInPackRegistrationCandidate>();
        var candidatePackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var disabledPackIds = BuildNormalizedPackIdSet(options.DisabledPackIds);
        var enabledPackIds = BuildNormalizedPackIdSet(options.EnabledPackIds);

        for (var i = 0; i < packTypes.Count; i++) {
            var packType = packTypes[i];
            if (TryCreateDisabledKnownBuiltInPackCandidate(packType, disabledPackIds, enabledPackIds, out var disabledCandidate)) {
                EnsureNoPackIdentityNormalizationCollisions(
                    descriptorIdsByNormalizedPackId,
                    disabledCandidate.Descriptor);
                candidates.Add(disabledCandidate);
                candidatePackIds.Add(disabledCandidate.PackId);
                continue;
            }

            if (!TryCreateBuiltInPack(packType, options, out var pack, out var error)) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='{NormalizeDisabledReason(error)}'",
                    shouldWarn: true);
                continue;
            }

            IToolPack normalizedPack;
            try {
                normalizedPack = RequireDeclaredSourceKind(pack, packType.FullName ?? packType.Name);
            } catch (Exception ex) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='{NormalizeDisabledReason(ex.Message)}'",
                    shouldWarn: true);
                continue;
            }

            var normalizedPackId = NormalizePackId(normalizedPack.Descriptor.Id);
            if (normalizedPackId.Length == 0) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_skipped type='{packType.FullName ?? packType.Name}' reason='descriptor id is missing.'",
                    shouldWarn: true);
                continue;
            }

            EnsureNoPackIdentityNormalizationCollisions(
                descriptorIdsByNormalizedPackId,
                normalizedPack.Descriptor);

            var defaultEnabled = !normalizedPack.Descriptor.IsDangerous
                                 && normalizedPack.Descriptor.Tier != ToolCapabilityTier.DangerousWrite;
            candidates.Add(new BuiltInPackRegistrationCandidate(
                PackId: normalizedPackId,
                Descriptor: normalizedPack.Descriptor,
                PackType: packType,
                Pack: normalizedPack,
                DefaultEnabled: defaultEnabled));
            candidatePackIds.Add(normalizedPackId);
        }

        for (var i = 0; i < KnownBuiltInPackBootstrapMetadataByIdentity.Count; i++) {
            var metadata = KnownBuiltInPackBootstrapMetadataByIdentity[i];
            if (candidatePackIds.Contains(metadata.PackId)) {
                continue;
            }

            var enabled = ResolveKnownPackEnabled(
                packId: metadata.PackId,
                enabledByDefault: metadata.DefaultEnabled,
                disabledPackIds: disabledPackIds,
                enabledPackIds: enabledPackIds);
            if (enabled) {
                continue;
            }

            EnsureNoPackIdentityNormalizationCollisions(
                descriptorIdsByNormalizedPackId,
                metadata.Descriptor);
            candidates.Add(new BuiltInPackRegistrationCandidate(
                PackId: metadata.PackId,
                Descriptor: metadata.Descriptor,
                PackType: typeof(ToolPackBootstrap),
                Pack: null,
                DefaultEnabled: metadata.DefaultEnabled));
            candidatePackIds.Add(metadata.PackId);
        }

        return candidates
            .OrderBy(static candidate => candidate.PackId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryCreateDisabledKnownBuiltInPackCandidate(
        Type packType,
        HashSet<string> disabledPackIds,
        HashSet<string> enabledPackIds,
        out BuiltInPackRegistrationCandidate candidate) {
        candidate = null!;
        if (!TryResolveKnownBuiltInPackBootstrapMetadata(packType, out var metadata)) {
            return false;
        }

        var enabled = ResolveKnownPackEnabled(
            packId: metadata.Descriptor.Id,
            enabledByDefault: metadata.DefaultEnabled,
            disabledPackIds: disabledPackIds,
            enabledPackIds: enabledPackIds);
        if (enabled) {
            return false;
        }

        candidate = new BuiltInPackRegistrationCandidate(
            PackId: metadata.PackId,
            Descriptor: metadata.Descriptor,
            PackType: packType,
            Pack: null,
            DefaultEnabled: metadata.DefaultEnabled);
        return true;
    }

    private static bool TryResolveKnownBuiltInPackBootstrapMetadata(
        string packId,
        out KnownBuiltInPackBootstrapMetadata metadata) {
        metadata = null!;
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        for (var i = 0; i < KnownBuiltInPackBootstrapMetadataByIdentity.Count; i++) {
            var candidate = KnownBuiltInPackBootstrapMetadataByIdentity[i];
            if (string.Equals(candidate.PackId, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                metadata = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveKnownBuiltInPackBootstrapMetadata(
        Type packType,
        out KnownBuiltInPackBootstrapMetadata metadata) {
        metadata = null!;
        if (packType is null) {
            return false;
        }

        var compactTypeName = ToolPackIdentityCatalog.NormalizePackCompactId(packType.Name);
        var compactFullName = ToolPackIdentityCatalog.NormalizePackCompactId(packType.FullName);
        if (compactTypeName.Length == 0 && compactFullName.Length == 0) {
            return false;
        }

        var bestScore = 0;
        KnownBuiltInPackBootstrapMetadata? bestMatch = null;
        for (var i = 0; i < KnownBuiltInPackBootstrapMetadataByIdentity.Count; i++) {
            var candidate = KnownBuiltInPackBootstrapMetadataByIdentity[i];
            var score = 0;
            for (var tokenIndex = 0; tokenIndex < candidate.IdentityTokens.Length; tokenIndex++) {
                var compactToken = ToolPackIdentityCatalog.NormalizePackCompactId(candidate.IdentityTokens[tokenIndex]);
                if (compactToken.Length == 0) {
                    continue;
                }

                if ((compactTypeName.Length > 0 && compactTypeName.Contains(compactToken, StringComparison.OrdinalIgnoreCase))
                    || (compactFullName.Length > 0 && compactFullName.Contains(compactToken, StringComparison.OrdinalIgnoreCase))) {
                    score = Math.Max(score, compactToken.Length);
                }
            }

            if (score <= bestScore) {
                continue;
            }

            bestScore = score;
            bestMatch = candidate;
        }

        if (bestMatch is null) {
            return false;
        }

        metadata = bestMatch;
        return true;
    }

    private static KnownBuiltInPackBootstrapMetadata CreateKnownBuiltInPackBootstrapMetadata(
        string id,
        string assemblyName,
        string name,
        ToolCapabilityTier tier,
        bool isDangerous,
        string description,
        string sourceKind,
        string engineId,
        string category,
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> capabilityTags) {
        var searchTokens = ToolPackIdentityCatalog.GetPackSearchTokens(id).ToArray();
        var descriptor = new ToolPackDescriptor {
            Id = id,
            Name = name,
            Aliases = aliases,
            Tier = tier,
            IsDangerous = isDangerous,
            Description = description,
            SourceKind = sourceKind,
            EngineId = engineId,
            Category = category,
            CapabilityTags = capabilityTags,
            SearchTokens = searchTokens
        };

        var identityTokens = new List<string>(capacity: 6 + aliases.Count + searchTokens.Length) {
            id,
            name,
            engineId,
            category
        };
        for (var i = 0; i < aliases.Count; i++) {
            identityTokens.Add(aliases[i]);
        }
        for (var i = 0; i < searchTokens.Length; i++) {
            identityTokens.Add(searchTokens[i]);
        }

        return new KnownBuiltInPackBootstrapMetadata(
            PackId: NormalizePackId(id),
            AssemblyName: assemblyName.Trim(),
            Descriptor: descriptor,
            DefaultEnabled: !isDangerous && tier != ToolCapabilityTier.DangerousWrite,
            IdentityTokens: identityTokens
                .Where(static token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AppendBuiltInAssemblyFingerprint(
        ICollection<string> lines,
        AssemblyName assemblyName,
        ToolPackBootstrapOptions options) {
        var simpleName = (assemblyName.Name ?? string.Empty).Trim();
        if (simpleName.Length == 0) {
            return;
        }

        if (TryResolveLoadedAssemblyLocation(assemblyName, out var loadedPath)
            && TryGetDiscoveryFileStamp(loadedPath, out var loadedStamp)) {
            lines.Add($"builtin_assembly={simpleName}|loaded|path={loadedPath}|{loadedStamp}");
            return;
        }

        if (TryResolveTrustedToolAssemblyPath(
                assemblyName,
                options,
                includeWorkspaceProjectOutputs: options.EnableWorkspaceBuiltInToolOutputProbing,
                out var trustedPath)
            && TryGetDiscoveryFileStamp(trustedPath, out var trustedStamp)) {
            lines.Add($"builtin_assembly={simpleName}|trusted|path={trustedPath}|{trustedStamp}");
            return;
        }

        lines.Add($"builtin_assembly={simpleName}|unresolved");
    }

    private static bool TryResolveLoadedAssemblyLocation(AssemblyName assemblyName, out string location) {
        location = string.Empty;
        var simpleName = (assemblyName.Name ?? string.Empty).Trim();
        if (simpleName.Length == 0) {
            return false;
        }

        foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!string.Equals(loadedAssembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var candidate = NormalizeDiscoveryPath(TryGetAssemblyFilePath(loadedAssembly));
            if (candidate.Length == 0) {
                continue;
            }

            location = candidate;
            return true;
        }

        return false;
    }

    private static void AppendPluginSearchRootFingerprint(ICollection<string> lines, PluginFolderToolPackLoader.PluginSearchRoot root) {
        var normalizedRootPath = NormalizeDiscoveryPath(root.Path);
        if (normalizedRootPath.Length == 0) {
            return;
        }

        var exists = Directory.Exists(normalizedRootPath);
        lines.Add($"plugin_root={normalizedRootPath}|explicit={(root.IsExplicit ? "1" : "0")}|exists={(exists ? "1" : "0")}");
        if (!exists) {
            return;
        }

        if (IsDiscoveryPluginFolder(normalizedRootPath) || LooksLikeDiscoveryManifestlessPluginFolder(normalizedRootPath)) {
            AppendPluginDirectoryFingerprint(lines, normalizedRootPath);
        }

        string[] archives;
        try {
            archives = Directory
                .EnumerateFiles(normalizedRootPath, "*" + PluginFolderToolPackLoader.PluginArchiveSuffix, SearchOption.TopDirectoryOnly)
                .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch {
            archives = Array.Empty<string>();
        }

        foreach (var archive in archives) {
            var normalizedArchivePath = NormalizeDiscoveryPath(archive);
            if (normalizedArchivePath.Length == 0) {
                continue;
            }

            lines.Add(TryGetDiscoveryFileStamp(normalizedArchivePath, out var archiveStamp)
                ? $"plugin_archive={normalizedArchivePath}|{archiveStamp}"
                : $"plugin_archive={normalizedArchivePath}|missing");
        }

        string[] directories;
        try {
            directories = Directory
                .EnumerateDirectories(normalizedRootPath, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch {
            directories = Array.Empty<string>();
        }

        foreach (var directory in directories) {
            if (!IsDiscoveryPluginFolder(directory) && !LooksLikeDiscoveryManifestlessPluginFolder(directory)) {
                continue;
            }

            AppendPluginDirectoryFingerprint(lines, directory);
        }
    }

    private static void AppendPluginDirectoryFingerprint(ICollection<string> lines, string pluginDirectory) {
        var normalizedPluginDirectory = NormalizeDiscoveryPath(pluginDirectory);
        if (normalizedPluginDirectory.Length == 0) {
            return;
        }

        var manifestPath = Path.Combine(normalizedPluginDirectory, PluginFolderToolPackLoader.ManifestFileName);
        var pluginIdentity = ResolveDiscoveryPluginIdentity(normalizedPluginDirectory);
        lines.Add($"plugin_dir={normalizedPluginDirectory}|id={pluginIdentity}");
        lines.Add(TryGetDiscoveryFileStamp(manifestPath, out var manifestStamp)
            ? $"plugin_manifest={manifestPath}|{manifestStamp}"
            : $"plugin_manifest={manifestPath}|missing");

        string[] assemblies;
        try {
            assemblies = Directory
                .EnumerateFiles(normalizedPluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch {
            assemblies = Array.Empty<string>();
        }

        foreach (var assemblyPath in assemblies) {
            var normalizedAssemblyPath = NormalizeDiscoveryPath(assemblyPath);
            if (normalizedAssemblyPath.Length == 0) {
                continue;
            }

            lines.Add(TryGetDiscoveryFileStamp(normalizedAssemblyPath, out var assemblyStamp)
                ? $"plugin_assembly={normalizedAssemblyPath}|{assemblyStamp}"
                : $"plugin_assembly={normalizedAssemblyPath}|missing");
        }
    }

    private static bool TryGetDiscoveryFileStamp(string path, out string stamp) {
        stamp = string.Empty;
        try {
            var normalizedPath = NormalizeDiscoveryPath(path);
            if (normalizedPath.Length == 0 || !File.Exists(normalizedPath)) {
                return false;
            }

            var file = new FileInfo(normalizedPath);
            stamp = "len=" + file.Length + "|ticks=" + file.LastWriteTimeUtc.Ticks;
            return true;
        } catch {
            return false;
        }
    }

    private static string NormalizeDiscoveryPath(string? path) {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        try {
            return Path.GetFullPath(normalized);
        } catch {
            return normalized;
        }
    }

    private static bool IsDiscoveryPluginFolder(string path) {
        var normalizedPath = NormalizeDiscoveryPath(path);
        if (normalizedPath.Length == 0 || !Directory.Exists(normalizedPath)) {
            return false;
        }

        return File.Exists(Path.Combine(normalizedPath, PluginFolderToolPackLoader.ManifestFileName));
    }

    private static bool LooksLikeDiscoveryManifestlessPluginFolder(string path) {
        var normalizedPath = NormalizeDiscoveryPath(path);
        if (normalizedPath.Length == 0 || !Directory.Exists(normalizedPath) || IsDiscoveryPluginFolder(normalizedPath)) {
            return false;
        }

        try {
            return Directory.EnumerateFiles(normalizedPath, "*.dll", SearchOption.TopDirectoryOnly).Any();
        } catch {
            return false;
        }
    }

    private static string ResolveDiscoveryPluginIdentity(string pluginDirectory) {
        var normalizedPluginDirectory = NormalizeDiscoveryPath(pluginDirectory);
        var fallback = (Path.GetFileName(normalizedPluginDirectory) ?? string.Empty).Trim();
        if (fallback.Length == 0) {
            return string.Empty;
        }

        var manifestPath = Path.Combine(normalizedPluginDirectory, PluginFolderToolPackLoader.ManifestFileName);
        if (!File.Exists(manifestPath)) {
            return fallback;
        }

        try {
            using var stream = File.OpenRead(manifestPath);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Object) {
                return fallback;
            }

            if (!json.RootElement.TryGetProperty("pluginId", out var pluginIdProperty)
                || pluginIdProperty.ValueKind != JsonValueKind.String) {
                return fallback;
            }

            var pluginId = (pluginIdProperty.GetString() ?? string.Empty).Trim();
            return pluginId.Length == 0 ? fallback : pluginId;
        } catch {
            return fallback;
        }
    }

    private static IReadOnlyList<Type> DiscoverBuiltInPackTypes(ToolPackBootstrapOptions options, Action<string>? onWarning) {
        return DiscoverBuiltInPackTypes(options, onWarning, targetPackId: null);
    }

    private static IReadOnlyList<Type> DiscoverBuiltInPackTypes(
        ToolPackBootstrapOptions options,
        Action<string>? onWarning,
        string? targetPackId) {
        var toolPackTypes = new List<Type>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assemblyName in EnumerateToolAssemblyNamesForDiscovery(options, targetPackId)) {
            var assembly = TryLoadToolAssembly(assemblyName, options, onWarning);
            if (assembly is null) {
                continue;
            }

            foreach (var type in EnumerateLoadableTypes(assembly, onWarning)) {
                var fullName = type.FullName;
                if (string.IsNullOrWhiteSpace(fullName) || !seen.Add(fullName)) {
                    continue;
                }

                if (!type.IsClass
                    || type.IsAbstract
                    || type.ContainsGenericParameters
                    || !typeof(IToolPack).IsAssignableFrom(type)) {
                    continue;
                }

                toolPackTypes.Add(type);
            }
        }

        return toolPackTypes
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<AssemblyName> EnumerateToolAssemblyNamesForDiscovery(ToolPackBootstrapOptions options) {
        return EnumerateToolAssemblyNamesForDiscovery(options, targetPackId: null);
    }

    private static IEnumerable<AssemblyName> EnumerateToolAssemblyNamesForDiscovery(
        ToolPackBootstrapOptions options,
        string? targetPackId) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var discovered = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);
        var allowedAssemblyNames = ResolveAllowedBuiltInAssemblyNames(options, targetPackId);
        if (allowedAssemblyNames.Count == 0) {
            return Array.Empty<AssemblyName>();
        }

        void AddAssemblyName(AssemblyName? candidate) {
            if (candidate is null
                || string.IsNullOrWhiteSpace(candidate.Name)
                || !IsBuiltInToolAssemblyName(candidate.Name)
                || !allowedAssemblyNames.Contains(candidate.Name)) {
                return;
            }

            if (!discovered.ContainsKey(candidate.Name)) {
                discovered[candidate.Name] = candidate;
            }
        }

        foreach (var allowedAssemblyName in allowedAssemblyNames) {
            try {
                AddAssemblyName(new AssemblyName(allowedAssemblyName));
            } catch (Exception ex) when (ex is ArgumentException or FileLoadException) {
                Warn(
                    options.OnBootstrapWarning,
                    $"[startup] built_in_pack_assembly_skipped assembly='{allowedAssemblyName}' reason='invalid assembly name: {NormalizeDisabledReason(ex.Message)}'",
                    shouldWarn: true);
            }
        }

        foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
            AddAssemblyName(loadedAssembly.GetName());
        }

        return discovered.Values
            .OrderBy(static name => name.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> ResolveAllowedBuiltInAssemblyNames(ToolPackBootstrapOptions options) {
        return ResolveAllowedBuiltInAssemblyNames(options, targetPackId: null);
    }

    private static HashSet<string> ResolveAllowedBuiltInAssemblyNames(
        ToolPackBootstrapOptions options,
        string? targetPackId) {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.UseDefaultBuiltInToolAssemblyNames) {
            var discoveredDefaultAssemblyNames = DiscoverDefaultBuiltInAssemblyNames(options.OnBootstrapWarning);
            for (var i = 0; i < discoveredDefaultAssemblyNames.Count; i++) {
                var defaultAssemblyName = (discoveredDefaultAssemblyNames[i] ?? string.Empty).Trim();
                if (defaultAssemblyName.Length > 0 && IsBuiltInToolAssemblyName(defaultAssemblyName)) {
                    allowed.Add(defaultAssemblyName);
                }
            }
        }

        if (options.BuiltInToolAssemblyNames is { Count: > 0 } configuredAssemblyNames) {
            for (var i = 0; i < configuredAssemblyNames.Count; i++) {
                var configuredAssemblyName = (configuredAssemblyNames[i] ?? string.Empty).Trim();
                if (configuredAssemblyName.Length == 0) {
                    continue;
                }

                try {
                    var parsedAssemblyName = new AssemblyName(configuredAssemblyName);
                    var simpleName = (parsedAssemblyName.Name ?? string.Empty).Trim();
                    if (simpleName.Length == 0 || !IsBuiltInToolAssemblyName(simpleName)) {
                        continue;
                    }

                    allowed.Add(simpleName);
                } catch (Exception ex) when (ex is ArgumentException or FileLoadException) {
                    Warn(
                        options.OnBootstrapWarning,
                        $"[startup] built_in_pack_assembly_skipped assembly='{configuredAssemblyName}' reason='invalid assembly name: {NormalizeDisabledReason(ex.Message)}'",
                        shouldWarn: true);
                }
            }
        }

        var skippedKnownAssemblyNames = ResolveDisabledKnownBuiltInAssemblyNamesForDiscovery(options, targetPackId);
        if (skippedKnownAssemblyNames.Count > 0) {
            allowed.RemoveWhere(skippedKnownAssemblyNames.Contains);
        }

        return allowed;
    }

    private static HashSet<string> ResolveDisabledKnownBuiltInAssemblyNamesForDiscovery(
        ToolPackBootstrapOptions options,
        string? targetPackId) {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabledPackIds = BuildNormalizedPackIdSet(options.DisabledPackIds);
        var enabledPackIds = BuildNormalizedPackIdSet(options.EnabledPackIds);
        var normalizedTargetPackId = NormalizePackId(targetPackId);

        for (var i = 0; i < KnownBuiltInPackBootstrapMetadataByIdentity.Count; i++) {
            var metadata = KnownBuiltInPackBootstrapMetadataByIdentity[i];
            if (normalizedTargetPackId.Length > 0
                && string.Equals(metadata.PackId, normalizedTargetPackId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var enabled = ResolveKnownPackEnabled(
                packId: metadata.PackId,
                enabledByDefault: metadata.DefaultEnabled,
                disabledPackIds: disabledPackIds,
                enabledPackIds: enabledPackIds);
            if (enabled) {
                continue;
            }

            var assemblyName = (metadata.AssemblyName ?? string.Empty).Trim();
            if (assemblyName.Length > 0 && IsBuiltInToolAssemblyName(assemblyName)) {
                skipped.Add(assemblyName);
            }
        }

        return skipped;
    }

    private static IReadOnlyList<string> DiscoverDefaultBuiltInAssemblyNames(Action<string>? onWarning) {
        try {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddBuiltInToolAssemblyNamesFromKnownMetadata(discovered);

            var bootstrapAssembly = typeof(ToolPackBootstrap).Assembly;
            foreach (var depsFilePath in EnumerateDependencyContextFiles(bootstrapAssembly)) {
                using var stream = File.OpenRead(depsFilePath);
                using var document = JsonDocument.Parse(stream);
                AddBuiltInToolAssemblyNamesFromDependencyContext(document.RootElement, discovered);
            }

            AddBuiltInToolAssemblyNamesFromEmbeddedManifest(bootstrapAssembly, discovered);

            if (discovered.Count == 0) {
                Warn(
                    onWarning,
                    "[startup] built_in_pack_default_discovery_failed reason='known metadata, dependency graph, and embedded manifest did not expose built-in tool assemblies.'",
                    shouldWarn: true);
                return Array.Empty<string>();
            }

            return discovered
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (Exception ex) {
            Warn(
                onWarning,
                $"[startup] built_in_pack_default_discovery_failed reason='{NormalizeDisabledReason(ex.Message)}'",
                shouldWarn: true);
            return Array.Empty<string>();
        }
    }

    private static void AddBuiltInToolAssemblyNamesFromKnownMetadata(HashSet<string> discovered) {
        if (discovered is null) {
            return;
        }

        for (var i = 0; i < KnownBuiltInPackBootstrapMetadataByIdentity.Count; i++) {
            var assemblyName = (KnownBuiltInPackBootstrapMetadataByIdentity[i].AssemblyName ?? string.Empty).Trim();
            if (assemblyName.Length > 0 && IsBuiltInToolAssemblyName(assemblyName)) {
                discovered.Add(assemblyName);
            }
        }
    }

    private static void AddBuiltInToolAssemblyNamesFromEmbeddedManifest(Assembly bootstrapAssembly, HashSet<string> discovered) {
        if (bootstrapAssembly is null || discovered is null) {
            return;
        }

        using var stream = TryOpenBuiltInToolAssemblyManifestStream(bootstrapAssembly);
        if (stream is null) {
            return;
        }

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream) {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            var candidate = line.Trim();
            if (candidate.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }

            if (IsBuiltInToolAssemblyName(candidate)) {
                discovered.Add(candidate);
            }
        }
    }

    private static Stream? TryOpenBuiltInToolAssemblyManifestStream(Assembly bootstrapAssembly) {
        var manifestResourceNames = bootstrapAssembly.GetManifestResourceNames();
        for (var i = 0; i < manifestResourceNames.Length; i++) {
            var resourceName = manifestResourceNames[i];
            if (!resourceName.EndsWith(BuiltInToolAssemblyManifestResourceSuffix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return bootstrapAssembly.GetManifestResourceStream(resourceName);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDependencyContextFiles(Assembly bootstrapAssembly) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var appContextDepsFiles = AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string;
        if (!string.IsNullOrWhiteSpace(appContextDepsFiles)) {
            var candidates = appContextDepsFiles.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < candidates.Length; i++) {
                var candidate = candidates[i];
                if (File.Exists(candidate) && seen.Add(candidate)) {
                    yield return candidate;
                }
            }
        }

        var bootstrapAssemblyPath = TryGetAssemblyFilePath(bootstrapAssembly);
        if (string.IsNullOrWhiteSpace(bootstrapAssemblyPath)) {
            var depsFileName = $"{bootstrapAssembly.GetName().Name}.deps.json";
            var appContextDepsFile = Path.Combine(AppContext.BaseDirectory, depsFileName);
            if (File.Exists(appContextDepsFile) && seen.Add(appContextDepsFile)) {
                yield return appContextDepsFile;
            }

            yield break;
        }

        var siblingDepsFile = Path.ChangeExtension(bootstrapAssemblyPath, ".deps.json");
        if (!string.IsNullOrWhiteSpace(siblingDepsFile) && File.Exists(siblingDepsFile) && seen.Add(siblingDepsFile)) {
            yield return siblingDepsFile;
        }
    }

    private static void AddBuiltInToolAssemblyNamesFromDependencyContext(JsonElement root, HashSet<string> discovered) {
        if (!root.TryGetProperty("targets", out var targetsElement)
            || targetsElement.ValueKind != JsonValueKind.Object) {
            return;
        }

        foreach (var targetProperty in targetsElement.EnumerateObject()) {
            if (targetProperty.Value.ValueKind != JsonValueKind.Object) {
                continue;
            }

            foreach (var libraryProperty in targetProperty.Value.EnumerateObject()) {
                var libraryName = libraryProperty.Name;
                var slashIndex = libraryName.IndexOf('/');
                if (slashIndex <= 0) {
                    continue;
                }

                var simpleName = libraryName.Substring(0, slashIndex).Trim();
                if (!IsBuiltInToolAssemblyName(simpleName)) {
                    continue;
                }

                discovered.Add(simpleName);
            }
        }
    }

    private static bool IsBuiltInToolAssemblyName(string? assemblyName) {
        if (string.IsNullOrWhiteSpace(assemblyName)) {
            return false;
        }

        var normalized = assemblyName.Trim();
        if (!normalized.StartsWith("IntelligenceX.Tools.", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (string.Equals(normalized, "IntelligenceX.Tools.Common", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return normalized.IndexOf(".Tests", StringComparison.OrdinalIgnoreCase) < 0
               && normalized.IndexOf(".Benchmarks", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static Assembly? TryLoadToolAssembly(AssemblyName assemblyName, ToolPackBootstrapOptions options, Action<string>? onWarning) {
        try {
            var requestedName = (assemblyName.Name ?? string.Empty).Trim();
            if (requestedName.Length == 0) {
                return null;
            }

            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(loadedAssembly =>
                    string.Equals(loadedAssembly.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase));
            if (alreadyLoaded is not null) {
                return alreadyLoaded;
            }

            if (!TryResolveTrustedToolAssemblyPath(assemblyName, options, out var trustedAssemblyPath)) {
                Warn(
                    onWarning,
                    $"[startup] built_in_pack_assembly_skipped assembly='{requestedName}' reason='trusted assembly path not found.'",
                    shouldWarn: true);
                return null;
            }

            EnsureBuiltInToolDependencyResolverConfigured(trustedAssemblyPath, options);
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(trustedAssemblyPath);
        } catch (Exception ex) {
            Warn(
                onWarning,
                $"[startup] built_in_pack_assembly_skipped assembly='{assemblyName.Name ?? "<unknown>"}' reason='{NormalizeDisabledReason(ex.Message)}'",
                shouldWarn: true);
            return null;
        }
    }

    private static bool TryResolveTrustedToolAssemblyPath(AssemblyName assemblyName, ToolPackBootstrapOptions options, out string trustedAssemblyPath) {
        return TryResolveTrustedToolAssemblyPath(
            assemblyName,
            options,
            includeWorkspaceProjectOutputs: options.EnableWorkspaceBuiltInToolOutputProbing,
            out trustedAssemblyPath);
    }

    private static bool TryResolveTrustedToolAssemblyPath(
        AssemblyName assemblyName,
        ToolPackBootstrapOptions options,
        bool includeWorkspaceProjectOutputs,
        out string trustedAssemblyPath) {
        return TryResolveTrustedToolAssemblyPathCore(
            assemblyName,
            options,
            includeWorkspaceProjectOutputs,
            TryGetAssemblyFilePath(typeof(ToolPackBootstrap).Assembly),
            out trustedAssemblyPath);
    }

    private static bool TryResolveTrustedToolAssemblyPathCore(
        AssemblyName assemblyName,
        ToolPackBootstrapOptions options,
        bool includeWorkspaceProjectOutputs,
        string? bootstrapAssemblyPath,
        out string trustedAssemblyPath) {
        trustedAssemblyPath = string.Empty;
        var assemblyNameValue = (assemblyName.Name ?? string.Empty).Trim();
        if (assemblyNameValue.Length == 0) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(bootstrapAssemblyPath)) {
            return false;
        }

        var bootstrapDirectory = Path.GetDirectoryName(bootstrapAssemblyPath);
        if (string.IsNullOrWhiteSpace(bootstrapDirectory)) {
            return false;
        }

        AssemblyDependencyResolver? dependencyResolver = null;
        try {
            dependencyResolver = new AssemblyDependencyResolver(bootstrapAssemblyPath);
        } catch (Exception) {
            dependencyResolver = null;
        }

        if (dependencyResolver is not null) {
            var resolvedAssemblyPath = dependencyResolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedAssemblyPath)) {
                var normalizedResolvedPath = Path.GetFullPath(resolvedAssemblyPath);
                if (File.Exists(normalizedResolvedPath)) {
                    trustedAssemblyPath = normalizedResolvedPath;
                    return true;
                }
            }
        }

        if (includeWorkspaceProjectOutputs) {
            if (TryResolveTrustedToolAssemblyPathFromWorkspaceProjectOutputs(
                    assemblyName,
                    bootstrapAssemblyPath,
                    out var workspaceProjectOutputPath)) {
                trustedAssemblyPath = workspaceProjectOutputPath;
                return true;
            }
        }

        var probeRoots = ResolveBuiltInToolAssemblyProbePaths(options, typeof(ToolPackBootstrap).Assembly);
        if (TryResolveTrustedToolAssemblyPathFromProbeRoots(assemblyName, probeRoots, out var probedAssemblyPath)) {
            trustedAssemblyPath = probedAssemblyPath;
            return true;
        }

        return false;
    }

    private static void EnsureBuiltInToolDependencyResolverConfigured(string trustedAssemblyPath, ToolPackBootstrapOptions options) {
        if (string.IsNullOrWhiteSpace(trustedAssemblyPath)) {
            return;
        }

        var probeRoots = BuildTrustedBuiltInDependencyProbeRoots(trustedAssemblyPath, options);

        lock (BuiltInToolDependencyResolverGate) {
            RegisterTrustedBuiltInDependencyResolver_NoLock(trustedAssemblyPath);
            PreloadTrustedBuiltInCompanionAssemblies_NoLock(trustedAssemblyPath, probeRoots);

            for (var i = 0; i < probeRoots.Count; i++) {
                var candidate = probeRoots[i];
                if (string.IsNullOrWhiteSpace(candidate)) {
                    continue;
                }

                try {
                    var normalizedPath = Path.GetFullPath(candidate);
                    if (Directory.Exists(normalizedPath)) {
                        BuiltInToolDependencyResolverProbeRoots.Add(normalizedPath);
                    }
                } catch (Exception) {
                    // Ignore malformed runtime probe roots and keep the resolver usable.
                }
            }

            if (BuiltInToolDependencyResolverRegistered) {
                return;
            }

            AssemblyLoadContext.Default.Resolving += ResolveTrustedBuiltInToolDependencyAssembly;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveTrustedBuiltInToolDependencyAssemblyFromAppDomain;
            BuiltInToolDependencyResolverRegistered = true;
        }
    }

    private static void PreloadTrustedBuiltInCompanionAssemblies_NoLock(string trustedAssemblyPath, IReadOnlyList<string> probeRoots) {
        var candidateAssemblyPaths = GetTrustedBuiltInCompanionAssemblyPathsCore(trustedAssemblyPath, probeRoots);
        for (var i = 0; i < candidateAssemblyPaths.Count; i++) {
            var candidateAssemblyPath = candidateAssemblyPaths[i];
            try {
                var candidateAssemblyName = AssemblyName.GetAssemblyName(candidateAssemblyPath);
                var requestedName = (candidateAssemblyName.Name ?? string.Empty).Trim();
                if (requestedName.Length == 0) {
                    continue;
                }

                var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(loadedAssembly =>
                        string.Equals(loadedAssembly.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase));
                if (alreadyLoaded) {
                    continue;
                }

                RegisterTrustedBuiltInDependencyResolver_NoLock(candidateAssemblyPath);
                AssemblyLoadContext.Default.LoadFromAssemblyPath(candidateAssemblyPath);
            } catch (Exception) {
                // Ignore companion preload failures. The runtime resolver still has a chance to load them lazily.
            }
        }
    }

    internal static IReadOnlyList<string> GetTrustedBuiltInCompanionAssemblyPathsForTesting(string trustedAssemblyPath) {
        lock (BuiltInToolDependencyResolverGate) {
            return GetTrustedBuiltInCompanionAssemblyPathsCore(trustedAssemblyPath, Array.Empty<string>());
        }
    }

    internal static IReadOnlyList<string> GetTrustedBuiltInCompanionAssemblyPathsForTesting(string trustedAssemblyPath, ToolPackBootstrapOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        lock (BuiltInToolDependencyResolverGate) {
            return GetTrustedBuiltInCompanionAssemblyPathsCore(
                trustedAssemblyPath,
                BuildTrustedBuiltInDependencyProbeRoots(trustedAssemblyPath, options));
        }
    }

    private static IReadOnlyList<string> GetTrustedBuiltInCompanionAssemblyPathsCore(string trustedAssemblyPath, IReadOnlyList<string> probeRoots) {
        if (string.IsNullOrWhiteSpace(trustedAssemblyPath)) {
            return Array.Empty<string>();
        }

        string normalizedTrustedAssemblyPath;
        string? trustedAssemblyDirectory;
        AssemblyDependencyResolver dependencyResolver;
        try {
            normalizedTrustedAssemblyPath = Path.GetFullPath(trustedAssemblyPath);
            trustedAssemblyDirectory = Path.GetDirectoryName(normalizedTrustedAssemblyPath);
            dependencyResolver = new AssemblyDependencyResolver(normalizedTrustedAssemblyPath);
        } catch (Exception) {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(trustedAssemblyDirectory) || !Directory.Exists(trustedAssemblyDirectory)) {
            return Array.Empty<string>();
        }

        var candidateAssemblyPaths = new List<string>();
        var seenCandidateAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            AddCandidateAssemblies(trustedAssemblyDirectory);
            for (var i = 0; i < probeRoots.Count; i++) {
                AddCandidateAssemblies(probeRoots[i]);
            }
        } catch (Exception) {
            return Array.Empty<string>();
        }

        var runtimeAssetFileNames = ReadTrustedBuiltInRuntimeAssetFileNames(trustedAssemblyPath);
        var filteredCandidateAssemblyPaths = new List<string>();
        foreach (var candidateAssemblyPath in candidateAssemblyPaths) {
            try {
                var candidateAssemblyName = AssemblyName.GetAssemblyName(candidateAssemblyPath);
                var requestedName = (candidateAssemblyName.Name ?? string.Empty).Trim();
                if (requestedName.Length == 0) {
                    continue;
                }

                var candidateFileName = Path.GetFileName(candidateAssemblyPath);
                if (runtimeAssetFileNames.Contains(candidateFileName)) {
                    filteredCandidateAssemblyPaths.Add(candidateAssemblyPath);
                    continue;
                }

                var resolvedAssemblyPath = dependencyResolver.ResolveAssemblyToPath(candidateAssemblyName);
                if (string.IsNullOrWhiteSpace(resolvedAssemblyPath)) {
                    continue;
                }

                var normalizedResolvedAssemblyPath = Path.GetFullPath(resolvedAssemblyPath);
                if (!string.Equals(normalizedResolvedAssemblyPath, candidateAssemblyPath, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(normalizedResolvedAssemblyPath), candidateFileName, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                filteredCandidateAssemblyPaths.Add(candidateAssemblyPath);
            } catch (Exception) {
                // Ignore malformed or unrelated sibling assemblies and keep companion filtering usable.
            }
        }

        return filteredCandidateAssemblyPaths;

        void AddCandidateAssemblies(string? candidateDirectory) {
            if (string.IsNullOrWhiteSpace(candidateDirectory) || !Directory.Exists(candidateDirectory)) {
                return;
            }

            foreach (var candidateAssemblyPath in Directory.EnumerateFiles(candidateDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                         .Select(Path.GetFullPath)
                         .Where(path => !string.Equals(path, normalizedTrustedAssemblyPath, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(static path => Path.GetFileName(path).StartsWith("System.", StringComparison.OrdinalIgnoreCase))
                         .ThenByDescending(static path => Path.GetFileName(path).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                         .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)) {
                if (seenCandidateAssemblyPaths.Add(candidateAssemblyPath)) {
                    candidateAssemblyPaths.Add(candidateAssemblyPath);
                }
            }
        }
    }

    private static HashSet<string> ReadTrustedBuiltInRuntimeAssetFileNames(string trustedAssemblyPath) {
        var runtimeAssetFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(trustedAssemblyPath)) {
            return runtimeAssetFileNames;
        }

        string depsJsonPath;
        try {
            depsJsonPath = Path.ChangeExtension(Path.GetFullPath(trustedAssemblyPath), ".deps.json");
        } catch (Exception) {
            return runtimeAssetFileNames;
        }

        if (!File.Exists(depsJsonPath)) {
            return runtimeAssetFileNames;
        }

        try {
            using var stream = File.OpenRead(depsJsonPath);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("targets", out var targetsElement)
                || targetsElement.ValueKind != JsonValueKind.Object) {
                return runtimeAssetFileNames;
            }

            foreach (var targetProperty in targetsElement.EnumerateObject()) {
                if (targetProperty.Value.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                foreach (var libraryProperty in targetProperty.Value.EnumerateObject()) {
                    if (!libraryProperty.Value.TryGetProperty("runtime", out var runtimeElement)
                        || runtimeElement.ValueKind != JsonValueKind.Object) {
                        continue;
                    }

                    foreach (var runtimeAssetProperty in runtimeElement.EnumerateObject()) {
                        var fileName = Path.GetFileName(runtimeAssetProperty.Name);
                        if (string.IsNullOrWhiteSpace(fileName)
                            || !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                            || fileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)) {
                            continue;
                        }

                        runtimeAssetFileNames.Add(fileName);
                    }
                }
            }
        } catch (Exception) {
            return runtimeAssetFileNames;
        }

        return runtimeAssetFileNames;
    }

    private static IReadOnlyList<string> BuildTrustedBuiltInDependencyProbeRoots(string trustedAssemblyPath, ToolPackBootstrapOptions options) {
        var probeRoots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddProbeRoot(string? candidate) {
            if (string.IsNullOrWhiteSpace(candidate)) {
                return;
            }

            try {
                var normalizedPath = Path.GetFullPath(candidate);
                if (Directory.Exists(normalizedPath) && seen.Add(normalizedPath)) {
                    probeRoots.Add(normalizedPath);
                }
            } catch (Exception) {
                // Ignore malformed runtime probe roots and keep the resolver usable.
            }
        }

        AddProbeRoot(Path.GetDirectoryName(trustedAssemblyPath));
        var configuredProbeRoots = ResolveBuiltInToolAssemblyProbePaths(options, typeof(ToolPackBootstrap).Assembly);
        for (var i = 0; i < configuredProbeRoots.Count; i++) {
            AddProbeRoot(configuredProbeRoots[i]);
        }

        return probeRoots;
    }

    private static void RegisterTrustedBuiltInDependencyResolver_NoLock(string trustedAssemblyPath) {
        if (string.IsNullOrWhiteSpace(trustedAssemblyPath)) {
            return;
        }

        try {
            var normalizedTrustedAssemblyPath = Path.GetFullPath(trustedAssemblyPath);
            if (!File.Exists(normalizedTrustedAssemblyPath)) {
                return;
            }

            if (BuiltInToolDependencyResolverAssemblyPaths.Add(normalizedTrustedAssemblyPath)) {
                BuiltInToolDependencyResolvers.Add(new AssemblyDependencyResolver(normalizedTrustedAssemblyPath));
            }
        } catch (Exception) {
            // Ignore malformed assembly paths and keep the resolver usable.
        }
    }

    private static Assembly? ResolveTrustedBuiltInToolDependencyAssembly(AssemblyLoadContext context, AssemblyName assemblyName) {
        return ResolveTrustedBuiltInToolDependencyAssemblyCore(
            assemblyName,
            loadAssembly: trustedAssemblyPath => context.LoadFromAssemblyPath(trustedAssemblyPath));
    }

    private static Assembly? ResolveTrustedBuiltInToolDependencyAssemblyFromAppDomain(object? sender, ResolveEventArgs args) {
        _ = sender;
        if (string.IsNullOrWhiteSpace(args.Name)) {
            return null;
        }

        return ResolveTrustedBuiltInToolDependencyAssemblyCore(
            new AssemblyName(args.Name),
            loadAssembly: trustedAssemblyPath => AssemblyLoadContext.Default.LoadFromAssemblyPath(trustedAssemblyPath));
    }

    private static Assembly? ResolveTrustedBuiltInToolDependencyAssemblyCore(
        AssemblyName assemblyName,
        Func<string, Assembly> loadAssembly) {
        var requestedName = (assemblyName.Name ?? string.Empty).Trim();
        if (requestedName.Length == 0) {
            return null;
        }

        var alreadyLoaded = FindAlreadyLoadedAssembly_NoLock(requestedName);
        if (alreadyLoaded is not null) {
            return alreadyLoaded;
        }

        AssemblyDependencyResolver[] dependencyResolvers;
        string[] probeRoots;
        lock (BuiltInToolDependencyResolverGate) {
            if (BuiltInToolDependencyResolvers.Count == 0 && BuiltInToolDependencyResolverProbeRoots.Count == 0) {
                return null;
            }

            dependencyResolvers = BuiltInToolDependencyResolvers.ToArray();
            probeRoots = BuiltInToolDependencyResolverProbeRoots.ToArray();
        }

        for (var i = 0; i < dependencyResolvers.Length; i++) {
            try {
                var resolvedAssemblyPath = dependencyResolvers[i].ResolveAssemblyToPath(assemblyName);
                if (string.IsNullOrWhiteSpace(resolvedAssemblyPath)) {
                    continue;
                }

                var normalizedResolvedPath = Path.GetFullPath(resolvedAssemblyPath);
                if (!File.Exists(normalizedResolvedPath)) {
                    continue;
                }

                lock (BuiltInToolDependencyResolverGate) {
                    RegisterTrustedBuiltInDependencyResolver_NoLock(normalizedResolvedPath);
                    var loadedResolvedAssembly = FindAlreadyLoadedAssembly_NoLock(requestedName, normalizedResolvedPath);
                    if (loadedResolvedAssembly is not null) {
                        return loadedResolvedAssembly;
                    }
                }
                return loadAssembly(normalizedResolvedPath);
            } catch (Exception) {
                // Ignore resolver failures and continue with trusted probe roots.
            }
        }

        if (!TryResolveTrustedToolAssemblyPathFromProbeRoots(assemblyName, probeRoots, out var trustedAssemblyPath)) {
            return null;
        }

        try {
            lock (BuiltInToolDependencyResolverGate) {
                RegisterTrustedBuiltInDependencyResolver_NoLock(trustedAssemblyPath);
                var loadedResolvedAssembly = FindAlreadyLoadedAssembly_NoLock(requestedName, trustedAssemblyPath);
                if (loadedResolvedAssembly is not null) {
                    return loadedResolvedAssembly;
                }
            }
            return loadAssembly(trustedAssemblyPath);
        } catch (Exception) {
            return null;
        }
    }

    internal static void EnsureBuiltInToolDependencyResolverConfiguredForTesting(string trustedAssemblyPath, ToolPackBootstrapOptions? options = null) {
        EnsureBuiltInToolDependencyResolverConfigured(trustedAssemblyPath, options ?? new ToolPackBootstrapOptions());
    }

    internal static Assembly? ResolveTrustedBuiltInToolDependencyAssemblyForTesting(AssemblyName assemblyName, Func<string, Assembly> loadAssembly) {
        return ResolveTrustedBuiltInToolDependencyAssemblyCore(assemblyName, loadAssembly);
    }

    internal static bool TryResolveTrustedToolAssemblyPathForTesting(
        AssemblyName assemblyName,
        ToolPackBootstrapOptions options,
        bool includeWorkspaceProjectOutputs,
        string bootstrapAssemblyPath,
        out string trustedAssemblyPath) {
        return TryResolveTrustedToolAssemblyPathCore(
            assemblyName,
            options,
            includeWorkspaceProjectOutputs,
            bootstrapAssemblyPath,
            out trustedAssemblyPath);
    }

    internal static bool TryResolveTrustedToolAssemblyPathFromWorkspaceProjectOutputsForTesting(
        AssemblyName assemblyName,
        string bootstrapAssemblyPath,
        out string trustedAssemblyPath) {
        return TryResolveTrustedToolAssemblyPathFromWorkspaceProjectOutputs(
            assemblyName,
            bootstrapAssemblyPath,
            out trustedAssemblyPath);
    }

    private static Assembly? FindAlreadyLoadedAssembly_NoLock(string requestedName, string? assemblyPath = null) {
        var normalizedAssemblyPath = NormalizeLoadedAssemblyPath(assemblyPath);
        foreach (var loadedAssembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (!string.IsNullOrWhiteSpace(normalizedAssemblyPath)) {
                var loadedAssemblyPath = NormalizeLoadedAssemblyPath(TryGetAssemblyFilePath(loadedAssembly));
                if (!string.IsNullOrWhiteSpace(loadedAssemblyPath)
                    && string.Equals(loadedAssemblyPath, normalizedAssemblyPath, StringComparison.OrdinalIgnoreCase)) {
                    return loadedAssembly;
                }
            }

            if (string.Equals(loadedAssembly.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase)) {
                return loadedAssembly;
            }
        }

        return null;
    }

    private static string? NormalizeLoadedAssemblyPath(string? assemblyPath) {
        if (string.IsNullOrWhiteSpace(assemblyPath)) {
            return null;
        }

        try {
            return Path.GetFullPath(assemblyPath);
        } catch (Exception) {
            return null;
        }
    }

    private static string? TryGetAssemblyFilePath(Assembly assembly) {
        ArgumentNullException.ThrowIfNull(assembly);

        try {
            var location = assembly.Location;
            if (!string.IsNullOrWhiteSpace(location)) {
                return location;
            }
        } catch (NotSupportedException) {
            // Single-file apps can throw when probing Assembly.Location.
        }

        var assemblyName = assembly.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName)) {
            var candidateDll = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
            if (File.Exists(candidateDll)) {
                return candidateDll;
            }

            var candidateExe = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.exe");
            if (File.Exists(candidateExe)) {
                return candidateExe;
            }
        }

        return null;
    }

    internal static string? TryGetAssemblyFilePathForTesting(Assembly assembly) {
        return TryGetAssemblyFilePath(assembly);
    }

    internal static bool TryResolveLoadedAssemblyLocationForTesting(AssemblyName assemblyName, out string location) {
        return TryResolveLoadedAssemblyLocation(assemblyName, out location);
    }

    private static IReadOnlyList<string> ResolveBuiltInToolAssemblyProbePaths(ToolPackBootstrapOptions options, Assembly bootstrapAssembly) {
        var discovered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddProbeRoot(string? candidate) {
            if (string.IsNullOrWhiteSpace(candidate)) {
                return;
            }

            string normalizedPath;
            try {
                normalizedPath = Path.GetFullPath(candidate.Trim());
            } catch (Exception) {
                return;
            }

            if (!Directory.Exists(normalizedPath) || !seen.Add(normalizedPath)) {
                return;
            }

            discovered.Add(normalizedPath);
        }

        if (options.BuiltInToolProbePaths is { Count: > 0 }) {
            for (var i = 0; i < options.BuiltInToolProbePaths.Count; i++) {
                AddProbeRoot(options.BuiltInToolProbePaths[i]);
            }
        }

        var environmentProbePaths = Environment.GetEnvironmentVariable(BuiltInToolProbePathsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentProbePaths)) {
            var candidates = environmentProbePaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < candidates.Length; i++) {
                AddProbeRoot(candidates[i]);
            }
        }

        var bootstrapAssemblyPath = TryGetAssemblyFilePath(bootstrapAssembly);
        if (!string.IsNullOrWhiteSpace(bootstrapAssemblyPath)) {
            AddProbeRoot(Path.GetDirectoryName(bootstrapAssemblyPath));
        }

        AddProbeRoot(AppContext.BaseDirectory);
        AddProbeRoot(Path.Combine(AppContext.BaseDirectory, "tools"));
        AddProbeRoot(Path.Combine(AppContext.BaseDirectory, "service"));
        AddProbeRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service")));

        return discovered;
    }

    private static bool TryResolveTrustedToolAssemblyPathFromProbeRoots(
        AssemblyName assemblyName,
        IReadOnlyList<string> probeRoots,
        out string trustedAssemblyPath) {
        trustedAssemblyPath = string.Empty;
        var assemblyNameValue = (assemblyName.Name ?? string.Empty).Trim();
        if (assemblyNameValue.Length == 0 || probeRoots.Count == 0) {
            return false;
        }

        var fileName = assemblyNameValue + ".dll";
        for (var i = 0; i < probeRoots.Count; i++) {
            var probeRoot = probeRoots[i];
            if (string.IsNullOrWhiteSpace(probeRoot)) {
                continue;
            }

            var candidatePath = Path.Combine(probeRoot, fileName);
            if (!File.Exists(candidatePath)) {
                continue;
            }

            try {
                var candidateAssemblyName = AssemblyName.GetAssemblyName(candidatePath);
                if (!string.Equals(candidateAssemblyName.Name, assemblyNameValue, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                trustedAssemblyPath = Path.GetFullPath(candidatePath);
                return true;
            } catch (Exception) {
                // Ignore malformed or mismatched assemblies in trusted probe roots.
            }
        }

        return false;
    }

    private static bool TryResolveTrustedToolAssemblyPathFromWorkspaceProjectOutputs(
        AssemblyName assemblyName,
        string bootstrapAssemblyPath,
        out string trustedAssemblyPath) {
        trustedAssemblyPath = string.Empty;
        var assemblyNameValue = (assemblyName.Name ?? string.Empty).Trim();
        if (assemblyNameValue.Length == 0) {
            return false;
        }

        var repoRoot = TryFindWorkspaceRepoRoot(bootstrapAssemblyPath);
        if (string.IsNullOrWhiteSpace(repoRoot)) {
            return false;
        }

        var projectDirectory = Path.Combine(repoRoot, "IntelligenceX.Tools", assemblyNameValue);
        if (!Directory.Exists(projectDirectory)) {
            return false;
        }

        var fileName = assemblyNameValue + ".dll";
        var preferredBuildConfiguration = TryResolveBuildConfigurationSegment(bootstrapAssemblyPath);
        IEnumerable<string> candidatePaths;
        try {
            candidatePaths = Directory.EnumerateFiles(projectDirectory, fileName, SearchOption.AllDirectories)
                .Where(static path => path.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
                                      || path.IndexOf($"{Path.AltDirectorySeparatorChar}bin{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(static path => path.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0
                                      && path.IndexOf($"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => MatchesBuildConfigurationSegment(path, preferredBuildConfiguration))
                .ThenByDescending(static path => path.IndexOf($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
                                                || path.IndexOf($"{Path.AltDirectorySeparatorChar}Release{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(static path => path.IndexOf($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
                                                || path.IndexOf($"{Path.AltDirectorySeparatorChar}Debug{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(static path => path.IndexOf("net10.0", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(static path => path.IndexOf("net9.0", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(static path => path.IndexOf("net8.0", StringComparison.OrdinalIgnoreCase) >= 0);
        } catch (Exception) {
            return false;
        }

        foreach (var candidatePath in candidatePaths) {
            try {
                var candidateAssemblyName = AssemblyName.GetAssemblyName(candidatePath);
                if (!string.Equals(candidateAssemblyName.Name, assemblyNameValue, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                trustedAssemblyPath = candidatePath;
                return true;
            } catch (Exception) {
                // Ignore malformed candidate outputs and continue scanning known project outputs.
            }
        }

        return false;
    }

    private static string TryResolveBuildConfigurationSegment(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (ContainsPathSegment(path, "Debug")) {
            return "Debug";
        }

        if (ContainsPathSegment(path, "Release")) {
            return "Release";
        }

        return string.Empty;
    }

    private static bool MatchesBuildConfigurationSegment(string path, string buildConfiguration) {
        if (string.IsNullOrWhiteSpace(buildConfiguration) || string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        return ContainsPathSegment(path, buildConfiguration);
    }

    private static bool ContainsPathSegment(string path, string segment) {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(segment)) {
            return false;
        }

        var normalizedSegment = Path.DirectorySeparatorChar + segment + Path.DirectorySeparatorChar;
        if (path.IndexOf(normalizedSegment, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        var alternateNormalizedSegment = Path.AltDirectorySeparatorChar + segment + Path.AltDirectorySeparatorChar;
        return path.IndexOf(alternateNormalizedSegment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string TryFindWorkspaceRepoRoot(string? bootstrapAssemblyPath) {
        if (string.IsNullOrWhiteSpace(bootstrapAssemblyPath)) {
            return string.Empty;
        }

        DirectoryInfo? directory;
        try {
            var fullAssemblyPath = Path.GetFullPath(bootstrapAssemblyPath);
            directory = new FileInfo(fullAssemblyPath).Directory;
        } catch (Exception) {
            return string.Empty;
        }

        while (directory is not null) {
            var solutionPath = Path.Combine(directory.FullName, "IntelligenceX.sln");
            var toolsDirectory = Path.Combine(directory.FullName, "IntelligenceX.Tools");
            if (File.Exists(solutionPath) && Directory.Exists(toolsDirectory)) {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    private static IReadOnlyList<Type> EnumerateLoadableTypes(Assembly assembly, Action<string>? onWarning) {
        try {
            return assembly.GetTypes();
        } catch (ReflectionTypeLoadException ex) {
            var types = ex.Types
                .Where(static type => type is not null)
                .Cast<Type>()
                .ToArray();
            if (types.Length == 0) {
                var firstLoaderError = ex.LoaderExceptions?.FirstOrDefault();
                Warn(
                    onWarning,
                    $"[startup] built_in_pack_assembly_skipped assembly='{assembly.GetName().Name ?? "<unknown>"}' reason='{NormalizeDisabledReason(firstLoaderError?.Message)}'",
                    shouldWarn: true);
            }
            return types;
        }
    }

    private static bool TryCreateBuiltInPack(
        Type packType,
        ToolPackBootstrapOptions bootstrapOptions,
        out IToolPack pack,
        out string error) {
        pack = null!;
        error = string.Empty;

        try {
            var parameterlessCtor = packType.GetConstructor(Type.EmptyTypes);
            if (parameterlessCtor is not null) {
                var created = parameterlessCtor.Invoke(parameters: null);
                if (created is IToolPack parameterlessPack) {
                    pack = parameterlessPack;
                    return true;
                }
            }

            var constructors = packType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            for (var i = 0; i < constructors.Length; i++) {
                var constructor = constructors[i];
                var parameters = constructor.GetParameters();
                if (parameters.Length != 1) {
                    continue;
                }

                object? options;
                try {
                    options = Activator.CreateInstance(parameters[0].ParameterType);
                } catch {
                    continue;
                }

                if (options is null) {
                    continue;
                }

                ConfigurePackOptions(
                    options,
                    bootstrapOptions,
                    packType,
                    explicitPackKey: TryResolveDeclaredPackId(constructor, options));
                var created = constructor.Invoke(new[] { options });
                if (created is IToolPack optionsPack) {
                    pack = optionsPack;
                    return true;
                }
            }

            error = "No supported constructor found (expected parameterless or single-options constructor).";
            return false;
        } catch (Exception ex) {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static void ConfigurePackOptions(
        object options,
        ToolPackBootstrapOptions bootstrapOptions,
        Type packType,
        string? explicitPackKey = null) {
        if (options is IToolPackRuntimeConfigurable configurableOptions) {
            configurableOptions.ApplyRuntimeContext(BuildRuntimeContext(bootstrapOptions));
        }

        ConfigurePackOptionsFromRuntimeBag(options, bootstrapOptions, packType, explicitPackKey);
    }

    private static string? TryResolveDeclaredPackId(ConstructorInfo constructor, object options) {
        try {
            var created = constructor.Invoke(new[] { options });
            if (created is not IToolPack pack) {
                return null;
            }

            var descriptorId = pack.Descriptor.Id;
            return string.IsNullOrWhiteSpace(descriptorId) ? null : descriptorId.Trim();
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Registers all provided packs into the registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    public static void RegisterAll(ToolRegistry registry, IEnumerable<IToolPack> packs) {
        RegisterAll(registry, packs, toolPackIdsByToolName: null, onRegistrationProgressWarning: null);
    }

    /// <summary>
    /// Registers all provided packs into the registry and optionally records tool-to-pack ownership.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    /// <param name="toolPackIdsByToolName">
    /// Optional sink populated with registered tool definition name to normalized pack id mappings.
    /// </param>
    public static void RegisterAll(ToolRegistry registry, IEnumerable<IToolPack> packs, IDictionary<string, string>? toolPackIdsByToolName) {
        RegisterAll(registry, packs, toolPackIdsByToolName, onRegistrationProgressWarning: null);
    }

    /// <summary>
    /// Registers all provided packs into the registry and optionally records tool-to-pack ownership/progress diagnostics.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    /// <param name="toolPackIdsByToolName">
    /// Optional sink populated with registered tool definition name to normalized pack id mappings.
    /// </param>
    /// <param name="onRegistrationProgressWarning">Optional startup warning sink for per-pack registration progress.</param>
    public static void RegisterAll(
        ToolRegistry registry,
        IEnumerable<IToolPack> packs,
        IDictionary<string, string>? toolPackIdsByToolName,
        Action<string>? onRegistrationProgressWarning) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        var packList = packs as IReadOnlyList<IToolPack> ?? packs.ToArray();
        var knownDefinitions = new HashSet<string>(
            registry.GetDefinitions().Select(static definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalPacks = Math.Max(1, packList.Count);

        for (var packIndex = 0; packIndex < packList.Count; packIndex++) {
            var pack = packList[packIndex];
            var descriptorId = (pack.Descriptor.Id ?? string.Empty).Trim();
            var normalizedPackId = NormalizePackId(descriptorId);
            EnsureNoPackIdentityNormalizationCollisions(descriptorIdsByNormalizedPackId, pack.Descriptor);
            if (normalizedPackId.Length == 0) {
                normalizedPackId = "pack_" + (packIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            EmitPackRegistrationProgress(
                onRegistrationProgressWarning,
                normalizedPackId,
                phase: "begin",
                index: packIndex + 1,
                total: totalPacks,
                elapsedMs: null,
                toolsRegistered: null,
                totalTools: null,
                failed: null);

            var registerStopwatch = Stopwatch.StartNew();
            var failed = false;
            var toolsRegistered = 0;
            try {
                pack.Register(registry);

                foreach (var definition in registry.GetDefinitions()) {
                    if (!knownDefinitions.Add(definition.Name)) {
                        continue;
                    }

                    toolsRegistered++;
                    if (toolPackIdsByToolName is not null) {
                        toolPackIdsByToolName[definition.Name] = normalizedPackId;
                    }
                }
            } catch {
                failed = true;
                throw;
            } finally {
                registerStopwatch.Stop();

                EmitPackRegistrationProgress(
                    onRegistrationProgressWarning,
                    normalizedPackId,
                    phase: "end",
                    index: packIndex + 1,
                    total: totalPacks,
                    elapsedMs: Math.Max(1, (long)registerStopwatch.Elapsed.TotalMilliseconds),
                    toolsRegistered: toolsRegistered,
                    totalTools: knownDefinitions.Count,
                    failed: failed);
            }
        }
    }

    /// <summary>
    /// Extracts pack descriptors.
    /// </summary>
    /// <param name="packs">Tool packs.</param>
    /// <returns>Descriptor list.</returns>
    public static IReadOnlyList<ToolPackDescriptor> GetDescriptors(IEnumerable<IToolPack> packs) {
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        var list = new List<ToolPackDescriptor>();
        foreach (var p in packs) {
            list.Add(p.Descriptor);
        }
        return list;
    }

    /// <summary>
    /// Normalizes a source-kind label to one of:
    /// <c>builtin</c>, <c>open_source</c>, or <c>closed_source</c>.
    /// Missing or unknown values are invalid.
    /// </summary>
    /// <param name="sourceKind">Raw source-kind value.</param>
    /// <param name="descriptorId">
    /// Optional descriptor id (accepted for compatibility; not used for inference).
    /// </param>
    /// <returns>Normalized source-kind label.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceKind"/> is missing or invalid.</exception>
    public static string NormalizeSourceKind(string? sourceKind, string? descriptorId = null) {
        _ = descriptorId;
        if (TryNormalizeSourceKind(sourceKind, out var normalized)) {
            return normalized;
        }

        throw new ArgumentException(
            $"SourceKind must be one of '{PackSourceBuiltin}', '{PackSourceOpenSource}', or '{PackSourceClosedSource}' (aliases: open/public, closed/private/internal).",
            nameof(sourceKind));
    }

    /// <summary>
    /// Attempts to normalize a source-kind label to one of:
    /// <c>builtin</c>, <c>open_source</c>, or <c>closed_source</c>.
    /// </summary>
    /// <param name="sourceKind">Raw source-kind value.</param>
    /// <param name="normalized">Normalized source-kind when parsing succeeds.</param>
    /// <returns><see langword="true"/> when normalization succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryNormalizeSourceKind(string? sourceKind, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceKind)) {
            return false;
        }

        var raw = sourceKind.Trim().ToLowerInvariant();
        if (raw is PackSourceBuiltin or PackSourceOpenSource or PackSourceClosedSource) {
            normalized = raw;
            return true;
        }

        if (raw is "open" or "opensource" or "public") {
            normalized = PackSourceOpenSource;
            return true;
        }

        if (raw is "closed" or "private" or "internal") {
            normalized = PackSourceClosedSource;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes descriptor ids into canonical pack ids used across policy and filtering.
    /// </summary>
    /// <param name="descriptorId">Descriptor id.</param>
    /// <returns>Canonical pack id, or empty string when input is empty.</returns>
    public static string NormalizePackId(string? descriptorId) {
        return ToolSelectionMetadata.NormalizePackId(descriptorId);
    }

    private static void EnsureNoPackIdNormalizationCollision(
        IDictionary<string, string> descriptorIdsByNormalizedPackId,
        string descriptorId,
        string normalizedPackId) {
        if (normalizedPackId.Length == 0) {
            return;
        }

        var normalizedDescriptorId = NormalizeCollisionDescriptorId(descriptorId);
        if (descriptorIdsByNormalizedPackId.TryGetValue(normalizedPackId, out var existingDescriptorId)
            && !string.Equals(existingDescriptorId, normalizedDescriptorId, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Tool pack ids '{existingDescriptorId}' and '{normalizedDescriptorId}' both normalize to '{normalizedPackId}'.");
        }

        descriptorIdsByNormalizedPackId[normalizedPackId] = normalizedDescriptorId;
    }

    private static void EnsureNoPackIdentityNormalizationCollisions(
        IDictionary<string, string> descriptorIdsByNormalizedPackId,
        ToolPackDescriptor descriptor) {
        var descriptorId = (descriptor.Id ?? string.Empty).Trim();
        var normalizedPrimaryPackId = NormalizePackId(descriptorId);
        EnsureNoPackIdNormalizationCollision(descriptorIdsByNormalizedPackId, descriptorId, normalizedPrimaryPackId);

        if (descriptor.Aliases is not { Count: > 0 }) {
            return;
        }

        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < descriptor.Aliases.Count; i++) {
            var alias = (descriptor.Aliases[i] ?? string.Empty).Trim();
            if (alias.Length == 0 || !seenAliases.Add(alias)) {
                continue;
            }

            var normalizedAliasToken = NormalizePackAliasToken(alias);
            if (normalizedAliasToken.Length == 0) {
                continue;
            }

            var knownAliasPackId = NormalizePackId(alias);
            var aliasMapsKnownIdentity = ToolPackIdentityCatalog.IsKnownPackIdentityToken(alias);
            if (normalizedPrimaryPackId.Length > 0
                && aliasMapsKnownIdentity
                && !string.Equals(knownAliasPackId, normalizedPrimaryPackId, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"Tool pack alias '{alias}' for '{NormalizeCollisionDescriptorId(descriptorId)}' resolves to known pack '{knownAliasPackId}' instead of '{normalizedPrimaryPackId}'.");
            }

            EnsureNoPackIdNormalizationCollision(descriptorIdsByNormalizedPackId, descriptorId, normalizedAliasToken);
        }
    }

    private static string NormalizeCollisionDescriptorId(string descriptorId) {
        var normalized = (descriptorId ?? string.Empty).Trim();
        return normalized.Length == 0 ? "<empty>" : normalized;
    }

    private static string NormalizePackAliasToken(string? value) {
        return ToolPackMetadataNormalizer.NormalizeDescriptorToken(value);
    }

    private static void EmitPackRegistrationProgress(
        Action<string>? onRegistrationProgressWarning,
        string normalizedPackId,
        string phase,
        int index,
        int total,
        long? elapsedMs,
        int? toolsRegistered,
        int? totalTools,
        bool? failed) {
        if (onRegistrationProgressWarning is null) {
            return;
        }

        var packId = string.IsNullOrWhiteSpace(normalizedPackId) ? "pack" : normalizedPackId.Trim();
        var boundedIndex = Math.Max(1, index);
        var boundedTotal = Math.Max(boundedIndex, total);
        if (string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase)) {
            var boundedElapsedMs = Math.Max(1, elapsedMs ?? 1);
            var boundedToolsRegistered = Math.Max(0, toolsRegistered ?? 0);
            var boundedTotalTools = Math.Max(boundedToolsRegistered, totalTools ?? 0);
            onRegistrationProgressWarning(
                $"[startup] pack_register_progress pack='{packId}' phase='end' index='{boundedIndex}' total='{boundedTotal}' " +
                $"elapsed_ms='{boundedElapsedMs}' tools_registered='{boundedToolsRegistered}' total_tools='{boundedTotalTools}' failed='{(failed.GetValueOrDefault() ? 1 : 0)}'");
            return;
        }

        onRegistrationProgressWarning(
            $"[startup] pack_register_progress pack='{packId}' phase='begin' index='{boundedIndex}' total='{boundedTotal}'");
    }

    internal static IToolPack WithSourceKind(IToolPack pack, string sourceKind) {
        if (pack is null) {
            throw new ArgumentNullException(nameof(pack));
        }

        var descriptor = pack.Descriptor;
        var normalized = NormalizeSourceKind(sourceKind, descriptor.Id);
        if (string.Equals(descriptor.SourceKind, normalized, StringComparison.OrdinalIgnoreCase)) {
            return pack;
        }

        return new DescriptorOverrideToolPack(pack, descriptor with { SourceKind = normalized });
    }

    private static ToolPackAvailabilityInfo CreateAvailabilityFromDescriptor(
        ToolPackDescriptor descriptor,
        bool enabled,
        string? disabledReason,
        bool descriptorOnly = false) {
        if (descriptor is null) {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var normalizedId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedId : descriptor.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);
        var normalizedEngineId = ToolPackMetadataNormalizer.NormalizeDescriptorToken(descriptor.EngineId);
        var normalizedAliases = NormalizePackAliases(
            packId: normalizedId.Length == 0 ? descriptor.Id : normalizedId,
            aliases: descriptor.Aliases);
        var normalizedCategory = NormalizePackCategory(descriptor.Category, descriptor.Id);
        var normalizedCapabilityTags = NormalizeDistinctDescriptorTokens(descriptor.CapabilityTags);
        var normalizedSearchTokens = NormalizePackSearchTokens(
            packId: normalizedId.Length == 0 ? descriptor.Id : normalizedId,
            aliases: normalizedAliases,
            category: normalizedCategory,
            engineId: normalizedEngineId,
            explicitSearchTokens: descriptor.SearchTokens);
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);

        return new ToolPackAvailabilityInfo {
            Id = normalizedId.Length == 0 ? descriptor.Id : normalizedId,
            Name = normalizedName,
            Description = normalizedDescription,
            Tier = descriptor.Tier,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            SourceKind = normalizedSourceKind,
            EngineId = normalizedEngineId.Length == 0 ? null : normalizedEngineId,
            Aliases = normalizedAliases,
            Category = normalizedCategory,
            CapabilityTags = normalizedCapabilityTags,
            SearchTokens = normalizedSearchTokens,
            CapabilityParity = descriptor.CapabilityParity ?? Array.Empty<ToolCapabilityParitySliceDescriptor>(),
            Enabled = enabled,
            DescriptorOnly = descriptorOnly,
            DisabledReason = enabled ? null : normalizedReason
        };
    }

    private static ToolPluginAvailabilityInfo CreateBuiltInPluginAvailability(
        BuiltInPackRegistrationCandidate candidate,
        bool enabled,
        string? disabledReason,
        bool descriptorOnly = false) {
        var descriptor = candidate.Descriptor;
        var normalizedPackId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedPackId : descriptor.Name.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);
        var version = candidate.PackType.Assembly.GetName().Version?.ToString();

        return new ToolPluginAvailabilityInfo {
            Id = normalizedPackId.Length == 0 ? descriptor.Id : normalizedPackId,
            Name = normalizedName,
            Version = string.IsNullOrWhiteSpace(version) ? null : version,
            Origin = PackSourceBuiltin,
            SourceKind = normalizedSourceKind,
            DefaultEnabled = candidate.DefaultEnabled,
            Enabled = enabled,
            DescriptorOnly = descriptorOnly,
            DisabledReason = normalizedReason,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            PackIds = normalizedPackId.Length == 0 ? Array.Empty<string>() : new[] { normalizedPackId },
            SkillDirectories = Array.Empty<string>(),
            SkillIds = Array.Empty<string>()
        };
    }

    private static ToolPluginAvailabilityInfo CreateBuiltInPluginAvailability(
        KnownBuiltInPackBootstrapMetadata metadata,
        bool enabled,
        string? disabledReason,
        bool descriptorOnly = false) {
        var descriptor = metadata.Descriptor;
        var normalizedPackId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedPackId : descriptor.Name.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);

        return new ToolPluginAvailabilityInfo {
            Id = normalizedPackId.Length == 0 ? descriptor.Id : normalizedPackId,
            Name = normalizedName,
            Version = null,
            Origin = PackSourceBuiltin,
            SourceKind = normalizedSourceKind,
            DefaultEnabled = metadata.DefaultEnabled,
            Enabled = enabled,
            DescriptorOnly = descriptorOnly,
            DisabledReason = normalizedReason,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            PackIds = normalizedPackId.Length == 0 ? Array.Empty<string>() : new[] { normalizedPackId },
            SkillDirectories = Array.Empty<string>(),
            SkillIds = Array.Empty<string>()
        };
    }

    private static ToolPluginCatalogInfo CreateBuiltInPluginCatalog(BuiltInPackRegistrationCandidate candidate) {
        var descriptor = candidate.Descriptor;
        var normalizedPackId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedPackId : descriptor.Name.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);
        var version = candidate.PackType.Assembly.GetName().Version?.ToString();

        return new ToolPluginCatalogInfo {
            Id = normalizedPackId.Length == 0 ? descriptor.Id : normalizedPackId,
            Name = normalizedName,
            Version = string.IsNullOrWhiteSpace(version) ? null : version,
            Origin = PackSourceBuiltin,
            SourceKind = normalizedSourceKind,
            DefaultEnabled = candidate.DefaultEnabled,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            PackIds = normalizedPackId.Length == 0 ? Array.Empty<string>() : new[] { normalizedPackId },
            SkillDirectories = Array.Empty<string>(),
            SkillIds = Array.Empty<string>()
        };
    }

    private static ToolPluginCatalogInfo CreateBuiltInPluginCatalog(KnownBuiltInPackBootstrapMetadata metadata) {
        var descriptor = metadata.Descriptor;
        var normalizedPackId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedPackId : descriptor.Name.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);

        return new ToolPluginCatalogInfo {
            Id = normalizedPackId.Length == 0 ? descriptor.Id : normalizedPackId,
            Name = normalizedName,
            Version = null,
            Origin = PackSourceBuiltin,
            SourceKind = normalizedSourceKind,
            DefaultEnabled = metadata.DefaultEnabled,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            PackIds = normalizedPackId.Length == 0 ? Array.Empty<string>() : new[] { normalizedPackId },
            SkillDirectories = Array.Empty<string>(),
            SkillIds = Array.Empty<string>()
        };
    }

    private static ToolPluginAvailabilityInfo CreatePluginPreviewAvailability(
        ToolPluginCatalogInfo catalog,
        bool enabled,
        string? disabledReason,
        bool descriptorOnly = true) {
        var normalizedPluginId = NormalizePackId(catalog.Id);
        var normalizedName = string.IsNullOrWhiteSpace(catalog.Name)
            ? (normalizedPluginId.Length == 0 ? catalog.Id : normalizedPluginId)
            : catalog.Name.Trim();
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);

        return new ToolPluginAvailabilityInfo {
            Id = normalizedPluginId.Length == 0 ? catalog.Id : normalizedPluginId,
            Name = normalizedName,
            Version = string.IsNullOrWhiteSpace(catalog.Version) ? null : catalog.Version.Trim(),
            Origin = string.IsNullOrWhiteSpace(catalog.Origin) ? "plugin_folder" : catalog.Origin.Trim(),
            SourceKind = string.IsNullOrWhiteSpace(catalog.SourceKind) ? PackSourceOpenSource : catalog.SourceKind.Trim(),
            DefaultEnabled = catalog.DefaultEnabled,
            Enabled = enabled,
            DescriptorOnly = descriptorOnly,
            DisabledReason = normalizedReason,
            IsDangerous = catalog.IsDangerous,
            PackIds = catalog.PackIds ?? Array.Empty<string>(),
            RootPath = string.IsNullOrWhiteSpace(catalog.RootPath) ? null : catalog.RootPath.Trim(),
            SkillDirectories = catalog.SkillDirectories ?? Array.Empty<string>(),
            SkillIds = catalog.SkillIds ?? Array.Empty<string>()
        };
    }

    private static void UpsertAvailability(Dictionary<string, ToolPackAvailabilityInfo> availabilityById, ToolPackAvailabilityInfo availability) {
        var normalizedPackId = NormalizePackId(availability.Id);
        if (normalizedPackId.Length == 0) {
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(availability.Name) ? normalizedPackId : availability.Name.Trim();
        var normalizedEngineId = ToolPackMetadataNormalizer.NormalizeDescriptorToken(availability.EngineId);
        var normalizedAliases = NormalizePackAliases(
            packId: normalizedPackId,
            aliases: availability.Aliases);
        var normalizedCategory = NormalizePackCategory(availability.Category, normalizedPackId);
        var normalizedCapabilityTags = NormalizeDistinctDescriptorTokens(availability.CapabilityTags);
        var normalizedSearchTokens = NormalizePackSearchTokens(
            packId: normalizedPackId,
            aliases: normalizedAliases,
            category: normalizedCategory,
            engineId: normalizedEngineId,
            explicitSearchTokens: availability.SearchTokens);
        availabilityById[normalizedPackId] = availability with {
            Id = normalizedPackId,
            Name = normalizedName,
            EngineId = normalizedEngineId.Length == 0 ? null : normalizedEngineId,
            Aliases = normalizedAliases,
            Category = normalizedCategory,
            CapabilityTags = normalizedCapabilityTags,
            SearchTokens = normalizedSearchTokens,
            CapabilityParity = availability.CapabilityParity ?? Array.Empty<ToolCapabilityParitySliceDescriptor>()
        };
    }

    private static void UpsertPluginAvailability(
        Dictionary<string, ToolPluginAvailabilityInfo> availabilityById,
        ToolPluginAvailabilityInfo availability) {
        var normalizedPluginId = NormalizePackId(availability.Id);
        if (normalizedPluginId.Length == 0) {
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(availability.Name) ? normalizedPluginId : availability.Name.Trim();
        var normalizedPackIds = (availability.PackIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedSkillDirectories = (availability.SkillDirectories ?? Array.Empty<string>())
            .Select(static path => (path ?? string.Empty).Trim())
            .Where(static path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedSkillIds = (availability.SkillIds ?? Array.Empty<string>())
            .Select(static skillId => (skillId ?? string.Empty).Trim())
            .Where(static skillId => skillId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static skillId => skillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedReason = availability.Enabled ? null : NormalizeDisabledReason(availability.DisabledReason);

        availabilityById[normalizedPluginId] = availability with {
            Id = normalizedPluginId,
            Name = normalizedName,
            PackIds = normalizedPackIds,
            SkillDirectories = normalizedSkillDirectories,
            SkillIds = normalizedSkillIds,
            DisabledReason = normalizedReason
        };
    }

    private static void UpsertPluginCatalog(
        Dictionary<string, ToolPluginCatalogInfo> catalogById,
        ToolPluginCatalogInfo catalog) {
        var normalizedPluginId = NormalizePackId(catalog.Id);
        if (normalizedPluginId.Length == 0) {
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(catalog.Name) ? normalizedPluginId : catalog.Name.Trim();
        var normalizedPackIds = (catalog.PackIds ?? Array.Empty<string>())
            .Select(static packId => NormalizePackId(packId))
            .Where(static packId => packId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedSkillDirectories = (catalog.SkillDirectories ?? Array.Empty<string>())
            .Select(static path => (path ?? string.Empty).Trim())
            .Where(static path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedSkillIds = (catalog.SkillIds ?? Array.Empty<string>())
            .Select(static skillId => (skillId ?? string.Empty).Trim())
            .Where(static skillId => skillId.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static skillId => skillId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        catalogById[normalizedPluginId] = catalog with {
            Id = normalizedPluginId,
            Name = normalizedName,
            PackIds = normalizedPackIds,
            SkillDirectories = normalizedSkillDirectories,
            SkillIds = normalizedSkillIds
        };
    }

    private static IToolPack RequireDeclaredSourceKind(IToolPack pack, string packLabel) {
        var descriptorSourceKind = (pack.Descriptor.SourceKind ?? string.Empty).Trim();
        if (descriptorSourceKind.Length == 0) {
            throw new InvalidOperationException($"{packLabel} pack is missing descriptor SourceKind.");
        }

        return WithSourceKind(pack, descriptorSourceKind);
    }

    private static string NormalizeDisabledReason(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return UnavailableReasonFallback;
        }

        normalized = normalized.Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return normalized.Length == 0 ? UnavailableReasonFallback : normalized;
    }

    private static void Warn(Action<string>? onWarning, string message, bool shouldWarn) {
        if (!shouldWarn) {
            return;
        }
        onWarning?.Invoke(message);
    }

    private static IReadOnlyList<string> NormalizeDistinctDescriptorTokens(IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var token = ToolPackMetadataNormalizer.NormalizeDescriptorToken(values[i]);
            if (token.Length == 0 || !seen.Add(token)) {
                continue;
            }

            normalized.Add(token);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    /// <summary>
    /// Normalizes a pack category token, falling back to the known pack identity catalog when needed.
    /// </summary>
    /// <param name="category">Declared pack category.</param>
    /// <param name="packId">Pack identifier used for fallback category resolution.</param>
    /// <returns>Normalized category token, or <see langword="null"/> when no category can be resolved.</returns>
    public static string? NormalizePackCategory(string? category, string? packId) {
        var normalizedCategory = ToolPackMetadataNormalizer.NormalizeDescriptorToken(category);
        if (normalizedCategory.Length > 0) {
            return normalizedCategory;
        }

        return ToolPackIdentityCatalog.TryGetCategory(packId, out var fallbackCategory)
            ? ToolPackMetadataNormalizer.NormalizeDescriptorToken(fallbackCategory)
            : null;
    }

    /// <summary>
    /// Normalizes runtime aliases advertised by a pack while preserving the canonical pack id as the source of truth.
    /// </summary>
    /// <param name="packId">Canonical pack identifier.</param>
    /// <param name="aliases">Optional pack aliases.</param>
    /// <returns>Normalized, deduplicated aliases that do not repeat the canonical pack id.</returns>
    public static string[] NormalizePackAliases(string? packId, IReadOnlyList<string>? aliases) {
        if (aliases is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var canonicalPackId = NormalizePackId(packId);
        var normalizedAliases = new List<string>(aliases.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < aliases.Count; i++) {
            var alias = NormalizePackAliasToken(aliases[i]);
            if (alias.Length == 0
                || string.Equals(alias, canonicalPackId, StringComparison.OrdinalIgnoreCase)
                || !seen.Add(alias)) {
                continue;
            }

            normalizedAliases.Add(alias);
        }

        normalizedAliases.Sort(StringComparer.OrdinalIgnoreCase);
        return normalizedAliases.Count == 0 ? Array.Empty<string>() : normalizedAliases.ToArray();
    }

    /// <summary>
    /// Normalizes the search-token surface advertised by a pack for planner/routing prompts.
    /// </summary>
    /// <param name="packId">Canonical pack identifier.</param>
    /// <param name="aliases">Optional pack aliases.</param>
    /// <param name="category">Optional normalized pack category.</param>
    /// <param name="engineId">Optional normalized engine identifier.</param>
    /// <param name="explicitSearchTokens">Optional pack-declared search tokens.</param>
    /// <returns>Normalized, deduplicated search tokens.</returns>
    public static string[] NormalizePackSearchTokens(
        string? packId,
        IReadOnlyList<string>? aliases,
        string? category,
        string? engineId,
        IReadOnlyList<string>? explicitSearchTokens) {
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddToken(string? value) {
            var token = ToolPackMetadataNormalizer.NormalizeDescriptorToken(value);
            if (token.Length == 0 || !seen.Add(token)) {
                return;
            }

            tokens.Add(token);
        }

        AddToken(packId);
        if (aliases is { Count: > 0 }) {
            for (var i = 0; i < aliases.Count; i++) {
                AddToken(aliases[i]);
            }
        }

        AddToken(category);
        AddToken(engineId);
        if (explicitSearchTokens is { Count: > 0 }) {
            for (var i = 0; i < explicitSearchTokens.Count; i++) {
                AddToken(explicitSearchTokens[i]);
            }
        }

        if ((explicitSearchTokens is not { Count: > 0 })
            && (aliases is not { Count: > 0 })
            && !string.IsNullOrWhiteSpace(packId)) {
            foreach (var token in ToolPackIdentityCatalog.GetPackSearchTokens(packId)) {
                AddToken(token);
            }
        }

        tokens.Sort(StringComparer.OrdinalIgnoreCase);
        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private sealed class DescriptorOverrideToolPack : IToolPack {
        private readonly IToolPack _inner;

        public DescriptorOverrideToolPack(IToolPack inner, ToolPackDescriptor descriptor) {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public ToolPackDescriptor Descriptor { get; }

        public void Register(ToolRegistry registry) {
            _inner.Register(registry);
        }
    }

    private sealed record KnownBuiltInPackBootstrapMetadata(
        string PackId,
        string AssemblyName,
        ToolPackDescriptor Descriptor,
        bool DefaultEnabled,
        string[] IdentityTokens);

    private sealed record BuiltInPackRegistrationCandidate(
        string PackId,
        ToolPackDescriptor Descriptor,
        Type PackType,
        IToolPack? Pack,
        bool DefaultEnabled);

}
