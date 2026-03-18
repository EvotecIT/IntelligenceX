using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Options for building the default tool pack set.
/// </summary>
public sealed record ToolPackBootstrapOptions {
    /// <summary>
    /// Allowed filesystem roots (used by filesystem tools and EVTX file access).
    /// </summary>
    public IReadOnlyList<string> AllowedRoots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional Active Directory domain controller.
    /// </summary>
    public string? AdDomainController { get; init; }

    /// <summary>
    /// Optional default AD search base DN.
    /// </summary>
    public string? AdDefaultSearchBaseDn { get; init; }

    /// <summary>
    /// Max AD results returned by AD tools (0 or less = default).
    /// </summary>
    public int AdMaxResults { get; init; } = 1000;

    /// <summary>
    /// Includes maintenance path in reviewer setup guidance output.
    /// </summary>
    public bool ReviewerSetupIncludeMaintenancePath { get; init; } = true;

    /// <summary>
    /// Default timeout for IX.PowerShell command execution.
    /// </summary>
    public int PowerShellDefaultTimeoutMs { get; init; } = 60_000;

    /// <summary>
    /// Maximum timeout accepted by IX.PowerShell command execution.
    /// </summary>
    public int PowerShellMaxTimeoutMs { get; init; } = 300_000;

    /// <summary>
    /// Default output cap for IX.PowerShell command execution.
    /// </summary>
    public int PowerShellDefaultMaxOutputChars { get; init; } = 50_000;

    /// <summary>
    /// Maximum output cap accepted by IX.PowerShell command execution.
    /// </summary>
    public int PowerShellMaxOutputChars { get; init; } = 250_000;

    /// <summary>
    /// Enables read-write intent for IX.PowerShell runtime pack.
    /// </summary>
    public bool PowerShellAllowWrite { get; init; }

    /// <summary>
    /// Enables loading built-in packs discovered from trusted IntelligenceX.Tools assemblies.
    /// </summary>
    public bool EnableBuiltInPackLoading { get; init; } = true;

    /// <summary>
    /// Enables built-in assembly discovery from the default allowlist shipped with Chat tooling.
    /// </summary>
    public bool UseDefaultBuiltInToolAssemblyNames { get; init; } = true;

    /// <summary>
    /// Additional built-in tool assembly names to include in discovery (repeatable).
    /// </summary>
    public IReadOnlyList<string> BuiltInToolAssemblyNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Additional trusted probe roots searched when built-in tool assemblies are not resolvable from the dependency graph.
    /// </summary>
    public IReadOnlyList<string> BuiltInToolProbePaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Shared authentication probe store used by probe-aware packs.
    /// </summary>
    public IToolAuthenticationProbeStore? AuthenticationProbeStore { get; init; }

    /// <summary>
    /// Enforces successful SMTP probe validation before email send actions.
    /// </summary>
    public bool RequireSuccessfulSmtpProbeForSend { get; init; }

    /// <summary>
    /// Maximum accepted SMTP probe age in seconds when strict probe validation is enabled.
    /// </summary>
    public int SmtpProbeMaxAgeSeconds { get; init; } = 900;

    /// <summary>
    /// Optional run-as profile catalog path for packs supporting run-as profile references.
    /// </summary>
    public string? RunAsProfilePath { get; init; }

    /// <summary>
    /// Optional authentication profile catalog path for packs supporting explicit auth profile references.
    /// </summary>
    public string? AuthenticationProfilePath { get; init; }

    /// <summary>
    /// Optional warning sink used when an optional/private pack cannot be loaded.
    /// </summary>
    public Action<string>? OnBootstrapWarning { get; init; }

    /// <summary>
    /// Enables loading external folder-based plugins.
    /// </summary>
    public bool EnablePluginFolderLoading { get; init; } = true;

    /// <summary>
    /// Enables default plugin search roots:
    /// %LOCALAPPDATA%\IntelligenceX.Chat\plugins and AppBase\plugins.
    /// </summary>
    public bool EnableDefaultPluginPaths { get; init; } = true;

    /// <summary>
    /// Additional plugin search paths (repeatable).
    /// </summary>
    public IReadOnlyList<string> PluginPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explicitly disabled pack ids (normalized at consumption time).
    /// </summary>
    public IReadOnlyList<string> DisabledPackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explicitly enabled pack ids (normalized at consumption time).
    /// Used to opt-in packs whose manifest default is disabled.
    /// </summary>
    public IReadOnlyList<string> EnabledPackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional plugin archive extraction cache root.
    /// Defaults to %LOCALAPPDATA%\IntelligenceX.Chat\plugin-cache when not provided.
    /// </summary>
    public string? PluginArchiveCacheRoot { get; init; }

    /// <summary>
    /// Pack-keyed runtime option bag applied to pack option constructors.
    /// Keys use normalized pack ids (<c>active_directory</c>, <c>powershell</c>, ...)
    /// and may include the global key <c>*</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> PackRuntimeOptionBag { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Host/service settings contract used to build runtime pack bootstrap options.
/// </summary>
public interface IToolPackRuntimeSettings {
    /// <summary>
    /// Allowed filesystem roots (used by filesystem tools and EVTX file access).
    /// </summary>
    IReadOnlyList<string> AllowedRoots { get; }

    /// <summary>
    /// Optional Active Directory domain controller.
    /// </summary>
    string? AdDomainController { get; }

    /// <summary>
    /// Optional default AD search base DN.
    /// </summary>
    string? AdDefaultSearchBaseDn { get; }

    /// <summary>
    /// Max AD results returned by AD tools (0 or less = default).
    /// </summary>
    int AdMaxResults { get; }

    /// <summary>
    /// Enables read-write intent for IX.PowerShell runtime pack.
    /// </summary>
    bool PowerShellAllowWrite { get; }

    /// <summary>
    /// Enables loading built-in packs discovered from trusted IntelligenceX.Tools assemblies.
    /// </summary>
    bool EnableBuiltInPackLoading { get; }

    /// <summary>
    /// Enables built-in assembly discovery from the default allowlist shipped with Chat tooling.
    /// </summary>
    bool UseDefaultBuiltInToolAssemblyNames { get; }

    /// <summary>
    /// Additional built-in tool assembly names to include in discovery (repeatable).
    /// </summary>
    IReadOnlyList<string> BuiltInToolAssemblyNames { get; }

    /// <summary>
    /// Additional trusted probe roots searched when built-in tool assemblies are not resolvable from the dependency graph.
    /// </summary>
    IReadOnlyList<string> BuiltInToolProbePaths { get; }

    /// <summary>
    /// Enables default plugin search roots.
    /// </summary>
    bool EnableDefaultPluginPaths { get; }

    /// <summary>
    /// Additional plugin search paths (repeatable).
    /// </summary>
    IReadOnlyList<string> PluginPaths { get; }

    /// <summary>
    /// Explicitly disabled pack ids (normalized at consumption time).
    /// </summary>
    IReadOnlyList<string> DisabledPackIds { get; }

    /// <summary>
    /// Explicitly enabled pack ids (normalized at consumption time).
    /// </summary>
    IReadOnlyList<string> EnabledPackIds { get; }
}

/// <summary>
/// Availability metadata for a known tool pack in the current runtime.
/// </summary>
public sealed record ToolPackAvailabilityInfo {
    /// <summary>
    /// Stable pack id.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Human-friendly pack name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Optional pack description.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Capability tier.
    /// </summary>
    public ToolCapabilityTier Tier { get; init; }
    /// <summary>
    /// Whether this pack includes dangerous/write operations.
    /// </summary>
    public bool IsDangerous { get; init; }
    /// <summary>
    /// Normalized source kind.
    /// </summary>
    public required string SourceKind { get; init; }
    /// <summary>
    /// Stable engine identifier when the pack maps to a known upstream engine.
    /// </summary>
    public string? EngineId { get; init; }
    /// <summary>
    /// Normalized runtime aliases advertised by the pack.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Normalized pack category advertised by the pack.
    /// </summary>
    public string? Category { get; init; }
    /// <summary>
    /// Normalized capability tags advertised by the pack.
    /// </summary>
    public IReadOnlyList<string> CapabilityTags { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Normalized pack-oriented search tokens used by routing/planner prompts.
    /// </summary>
    public IReadOnlyList<string> SearchTokens { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Pack-owned capability parity slices published for runtime capability inventories.
    /// </summary>
    public IReadOnlyList<ToolCapabilityParitySliceDescriptor> CapabilityParity { get; init; } = Array.Empty<ToolCapabilityParitySliceDescriptor>();
    /// <summary>
    /// Whether the pack is available and loaded in this runtime.
    /// </summary>
    public bool Enabled { get; init; }
    /// <summary>
    /// Human-readable reason when the pack is unavailable.
    /// </summary>
    public string? DisabledReason { get; init; }
}

/// <summary>
/// Availability metadata for a plugin-style tool source in the current runtime.
/// </summary>
public sealed record ToolPluginAvailabilityInfo {
    /// <summary>
    /// Stable plugin identifier.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Human-friendly plugin name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Optional plugin version from manifest or assembly metadata.
    /// </summary>
    public string? Version { get; init; }
    /// <summary>
    /// Plugin origin classification (for example builtin or folder).
    /// </summary>
    public required string Origin { get; init; }
    /// <summary>
    /// Normalized source kind for the plugin.
    /// </summary>
    public required string SourceKind { get; init; }
    /// <summary>
    /// Whether the plugin is enabled by default before runtime overrides.
    /// </summary>
    public bool DefaultEnabled { get; init; }
    /// <summary>
    /// Whether any pack from this plugin is enabled in the current runtime.
    /// </summary>
    public bool Enabled { get; init; }
    /// <summary>
    /// Human-readable reason when the plugin is unavailable.
    /// </summary>
    public string? DisabledReason { get; init; }
    /// <summary>
    /// Whether the plugin exposes dangerous/write capability.
    /// </summary>
    public bool IsDangerous { get; init; }
    /// <summary>
    /// Normalized pack ids contributed by this plugin.
    /// </summary>
    public string[] PackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Optional root path for folder-based plugins.
    /// </summary>
    public string? RootPath { get; init; }
    /// <summary>
    /// Optional skill directories exposed by the plugin.
    /// </summary>
    public string[] SkillDirectories { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Optional resolved skill identifiers exposed by the plugin.
    /// </summary>
    public string[] SkillIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of bootstrapping default tool packs with pack availability metadata.
/// </summary>
public sealed record ToolPackBootstrapResult {
    /// <summary>
    /// Loaded tool packs.
    /// </summary>
    public IReadOnlyList<IToolPack> Packs { get; init; } = Array.Empty<IToolPack>();
    /// <summary>
    /// Runtime availability for known packs.
    /// </summary>
    public IReadOnlyList<ToolPackAvailabilityInfo> PackAvailability { get; init; } = Array.Empty<ToolPackAvailabilityInfo>();
    /// <summary>
    /// Runtime availability for known plugins.
    /// </summary>
    public IReadOnlyList<ToolPluginAvailabilityInfo> PluginAvailability { get; init; } = Array.Empty<ToolPluginAvailabilityInfo>();
}

/// <summary>
/// Shared tool pack bootstrap for both Host and Service.
/// </summary>
public static partial class ToolPackBootstrap {
    private const string DisabledByRuntimeConfigurationReason = "Disabled by runtime configuration.";
    private const string UnavailableReasonFallback = "Pack could not be loaded in this runtime.";
    /// <summary>
    /// Canonical hint message used when plugin-only mode resolves to an empty pack set.
    /// </summary>
    public const string PluginOnlyNoPacksMessage =
        "Built-in packs are disabled and no plugin packs were loaded. Chat will run without tools until packs are enabled.";

    internal const string PackSourceBuiltin = "builtin";
    internal const string PackSourceOpenSource = "open_source";
    internal const string PackSourceClosedSource = "closed_source";
    internal const string PackOptionKeyGlobal = "*";
    internal const string PackOptionKeyActiveDirectory = "active_directory";
    internal const string PackOptionKeyPowerShell = "powershell";
    internal const string PackOptionKeyEmail = "email";
    internal const string PackOptionKeyReviewerSetup = "reviewer_setup";

    /// <summary>
    /// Returns <c>true</c> when runtime configuration selects plugin-only mode and no packs were loaded.
    /// </summary>
    /// <param name="options">Resolved pack bootstrap options.</param>
    /// <param name="loadedPackCount">Number of loaded packs.</param>
    /// <returns><c>true</c> when plugin-only runtime has no loaded packs; otherwise <c>false</c>.</returns>
    public static bool IsPluginOnlyModeNoPacks(ToolPackBootstrapOptions options, int loadedPackCount) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return !options.EnableBuiltInPackLoading && loadedPackCount <= 0;
    }

    /// <summary>
    /// Builds a normalized startup warning payload for plugin-only no-pack startup states.
    /// </summary>
    /// <param name="pluginRootCount">Number of resolved plugin roots considered during bootstrap.</param>
    /// <returns>Structured warning string suitable for startup warning sinks.</returns>
    public static string BuildPluginOnlyNoPacksWarning(int pluginRootCount) {
        return $"[startup] no_tool_packs_loaded mode='plugin_only' built_in_packs='0' plugin_roots='{Math.Max(0, pluginRootCount)}' " +
               $"hint='{PluginOnlyNoPacksMessage}'";
    }

    /// <summary>
    /// Creates runtime bootstrap options from shared host/service settings and policy context.
    /// </summary>
    /// <param name="settings">Host/service tool-pack settings.</param>
    /// <param name="runtimePolicyContext">Resolved runtime policy context.</param>
    /// <param name="onBootstrapWarning">Optional warning sink used during pack bootstrap.</param>
    /// <returns>Mapped bootstrap options.</returns>
    public static ToolPackBootstrapOptions CreateRuntimeBootstrapOptions(
        IToolPackRuntimeSettings settings,
        ToolRuntimePolicyContext runtimePolicyContext,
        Action<string>? onBootstrapWarning = null) {
        if (settings is null) {
            throw new ArgumentNullException(nameof(settings));
        }
        if (runtimePolicyContext is null) {
            throw new ArgumentNullException(nameof(runtimePolicyContext));
        }

        var allowedRoots = settings.AllowedRoots?.ToArray() ?? Array.Empty<string>();
        var builtInToolAssemblyNames = settings.BuiltInToolAssemblyNames?.ToArray() ?? Array.Empty<string>();
        var builtInToolProbePaths = settings.BuiltInToolProbePaths?.ToArray() ?? Array.Empty<string>();
        var pluginPaths = settings.PluginPaths?.ToArray() ?? Array.Empty<string>();
        var disabledPackIds = settings.DisabledPackIds?.ToArray() ?? Array.Empty<string>();
        var enabledPackIds = settings.EnabledPackIds?.ToArray() ?? Array.Empty<string>();
        var baseOptions = new ToolPackBootstrapOptions {
            AllowedRoots = allowedRoots,
            AdDomainController = settings.AdDomainController,
            AdDefaultSearchBaseDn = settings.AdDefaultSearchBaseDn,
            AdMaxResults = settings.AdMaxResults,
            PowerShellAllowWrite = settings.PowerShellAllowWrite,
            EnableBuiltInPackLoading = settings.EnableBuiltInPackLoading,
            UseDefaultBuiltInToolAssemblyNames = settings.UseDefaultBuiltInToolAssemblyNames,
            BuiltInToolAssemblyNames = builtInToolAssemblyNames,
            BuiltInToolProbePaths = builtInToolProbePaths,
            EnableDefaultPluginPaths = settings.EnableDefaultPluginPaths,
            PluginPaths = pluginPaths,
            DisabledPackIds = disabledPackIds,
            EnabledPackIds = enabledPackIds,
            AuthenticationProbeStore = runtimePolicyContext.AuthenticationProbeStore,
            RequireSuccessfulSmtpProbeForSend = runtimePolicyContext.RequireSuccessfulSmtpProbeForSend,
            SmtpProbeMaxAgeSeconds = runtimePolicyContext.SmtpProbeMaxAgeSeconds,
            RunAsProfilePath = runtimePolicyContext.Options.RunAsProfilePath,
            AuthenticationProfilePath = runtimePolicyContext.Options.AuthenticationProfilePath,
            OnBootstrapWarning = onBootstrapWarning
        };

        return baseOptions with {
            PackRuntimeOptionBag = NormalizePackRuntimeOptionBag(baseOptions.PackRuntimeOptionBag)
        };
    }

    internal static ToolPackRuntimeContext BuildRuntimeContext(ToolPackBootstrapOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        var effectiveOptionBag = BuildEffectivePackRuntimeOptionBag(options)
            .ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyDictionary<string, object?>)pair.Value,
                StringComparer.OrdinalIgnoreCase);

        return new ToolPackRuntimeContext {
            AllowedRoots = options.AllowedRoots?.ToArray() ?? Array.Empty<string>(),
            AdDomainController = options.AdDomainController,
            AdDefaultSearchBaseDn = options.AdDefaultSearchBaseDn,
            AdMaxResults = options.AdMaxResults,
            ReviewerSetupIncludeMaintenancePath = options.ReviewerSetupIncludeMaintenancePath,
            PowerShellDefaultTimeoutMs = options.PowerShellDefaultTimeoutMs,
            PowerShellMaxTimeoutMs = options.PowerShellMaxTimeoutMs,
            PowerShellDefaultMaxOutputChars = options.PowerShellDefaultMaxOutputChars,
            PowerShellMaxOutputChars = options.PowerShellMaxOutputChars,
            PowerShellAllowWrite = options.PowerShellAllowWrite,
            AuthenticationProbeStore = options.AuthenticationProbeStore,
            RequireSuccessfulSmtpProbeForSend = options.RequireSuccessfulSmtpProbeForSend,
            SmtpProbeMaxAgeSeconds = options.SmtpProbeMaxAgeSeconds,
            RunAsProfilePath = options.RunAsProfilePath,
            AuthenticationProfilePath = options.AuthenticationProfilePath,
            EffectivePackRuntimeOptionBag = effectiveOptionBag
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> NormalizePackRuntimeOptionBag(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? optionBag) {
        if (optionBag is null || optionBag.Count == 0) {
            return new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in optionBag) {
            var normalizedPackKey = NormalizePackId(entry.Key);
            if (string.Equals(entry.Key, PackOptionKeyGlobal, StringComparison.Ordinal)) {
                normalizedPackKey = PackOptionKeyGlobal;
            }

            if (normalizedPackKey.Length == 0) {
                continue;
            }

            if (entry.Value is null || entry.Value.Count == 0) {
                normalized[normalizedPackKey] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var propertyBag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entry.Value) {
                var propertyName = (property.Key ?? string.Empty).Trim();
                if (propertyName.Length == 0) {
                    continue;
                }

                propertyBag[propertyName] = property.Value;
            }

            normalized[normalizedPackKey] = propertyBag;
        }

        return normalized;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> BuildGlobalRuntimeOptionBag(
        ToolPackBootstrapOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase) {
            [PackOptionKeyGlobal] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                ["AllowedRoots"] = options.AllowedRoots?.ToArray() ?? Array.Empty<string>(),
                ["RunAsProfilePath"] = options.RunAsProfilePath,
                ["AuthenticationProfilePath"] = options.AuthenticationProfilePath,
                ["AuthenticationProbeStore"] = options.AuthenticationProbeStore,
                ["RequireSuccessfulSmtpProbeForSend"] = options.RequireSuccessfulSmtpProbeForSend,
                ["SmtpProbeMaxAgeSeconds"] = options.SmtpProbeMaxAgeSeconds
            }
        };
    }

    /// <summary>
    /// Builds the default tool packs (public read-only packs plus optional private packs when available).
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Tool pack list.</returns>
    public static IReadOnlyList<IToolPack> CreateDefaultReadOnlyPacks(ToolPackBootstrapOptions options) {
        return CreateDefaultReadOnlyPacksWithAvailability(options).Packs;
    }

    /// <summary>
    /// Builds the default tool packs together with structured per-pack availability diagnostics.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Loaded packs and pack availability metadata.</returns>
    public static ToolPackBootstrapResult CreateDefaultReadOnlyPacksWithAvailability(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var disabledPackIds = BuildNormalizedPackIdSet(options.DisabledPackIds);
        var enabledPackIds = BuildNormalizedPackIdSet(options.EnabledPackIds);
        var builtInPacks = options.EnableBuiltInPackLoading
            ? DiscoverBuiltInPacks(options)
            : Array.Empty<BuiltInPackRegistrationCandidate>();

        var packs = new List<IToolPack>();
        var availabilityById = new Dictionary<string, ToolPackAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        var pluginAvailabilityById = new Dictionary<string, ToolPluginAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
        var knownBootstrapStepTotal = builtInPacks.Count + (options.EnablePluginFolderLoading ? 1 : 0);
        var knownBootstrapStepIndex = 0;

        void RunBootstrapStep(string stepId, Action action) {
            knownBootstrapStepIndex++;
            var normalizedStepId = NormalizePackId(stepId);
            if (normalizedStepId.Length == 0) {
                normalizedStepId = $"step_{knownBootstrapStepIndex}";
            }

            var stepIndex = Math.Clamp(knownBootstrapStepIndex, 1, Math.Max(1, knownBootstrapStepTotal));
            var stepTotal = Math.Max(stepIndex, knownBootstrapStepTotal);
            options.OnBootstrapWarning?.Invoke(
                $"[startup] pack_load_progress pack='{normalizedStepId}' phase='begin' index='{stepIndex}' total='{stepTotal}'");
            var stepStopwatch = Stopwatch.StartNew();
            var failed = false;
            try {
                action();
            } catch {
                failed = true;
                throw;
            } finally {
                stepStopwatch.Stop();
                options.OnBootstrapWarning?.Invoke(
                    $"[startup] pack_load_progress pack='{normalizedStepId}' phase='end' index='{stepIndex}' total='{stepTotal}' " +
                    $"elapsed_ms='{Math.Max(1, (long)stepStopwatch.Elapsed.TotalMilliseconds)}' failed='{(failed ? 1 : 0)}'");
            }
        }

        for (var i = 0; i < builtInPacks.Count; i++) {
            var builtInPack = builtInPacks[i];
            RunBootstrapStep(builtInPack.PackId, () => {
                var enabled = ResolveKnownPackEnabled(
                    packId: builtInPack.PackId,
                    enabledByDefault: builtInPack.DefaultEnabled,
                    disabledPackIds: disabledPackIds,
                    enabledPackIds: enabledPackIds);
                var disabledReason = enabled ? null : DisabledByRuntimeConfigurationReason;
                if (!enabled) {
                    UpsertAvailability(
                        availabilityById,
                        CreateAvailabilityFromDescriptor(
                            descriptor: builtInPack.Pack.Descriptor,
                            enabled: false,
                            disabledReason: disabledReason));
                    UpsertPluginAvailability(
                        pluginAvailabilityById,
                        CreateBuiltInPluginAvailability(
                            builtInPack,
                            enabled: false,
                            disabledReason: disabledReason));
                    return;
                }

                packs.Add(builtInPack.Pack);
                UpsertAvailability(
                    availabilityById,
                        CreateAvailabilityFromDescriptor(
                            descriptor: builtInPack.Pack.Descriptor,
                            enabled: true,
                            disabledReason: null));
                UpsertPluginAvailability(
                    pluginAvailabilityById,
                    CreateBuiltInPluginAvailability(
                        builtInPack,
                        enabled: true,
                        disabledReason: null));
            });
        }

        if (options.EnablePluginFolderLoading) {
            RunBootstrapStep("plugins", () => {
                var existingPackIds = new HashSet<string>(
                    packs
                        .Select(static pack => NormalizePackId(pack.Descriptor.Id))
                        .Where(static id => !string.IsNullOrWhiteSpace(id)),
                    StringComparer.OrdinalIgnoreCase);

                PluginFolderToolPackLoader.AddPluginPacks(
                    packs: packs,
                    options: options,
                    existingPackIds: existingPackIds,
                    onWarning: options.OnBootstrapWarning,
                    onPackAvailability: availability => UpsertAvailability(availabilityById, availability),
                    onPluginAvailability: plugin => UpsertPluginAvailability(pluginAvailabilityById, plugin));
            });
        }

        for (var i = 0; i < packs.Count; i++) {
            var descriptor = packs[i].Descriptor;
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDescriptor(
                    descriptor: descriptor,
                    enabled: true,
                    disabledReason: null));
        }

        var availability = availabilityById.Values
            .OrderBy(static pack => pack.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ToolPackBootstrapResult {
            Packs = packs,
            PackAvailability = availability,
            PluginAvailability = pluginAvailabilityById.Values
                .OrderBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static HashSet<string> BuildNormalizedPackIdSet(IReadOnlyList<string>? packIds) {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packIds is null || packIds.Count == 0) {
            return normalized;
        }

        for (var i = 0; i < packIds.Count; i++) {
            var packId = NormalizePackId(packIds[i]);
            if (packId.Length == 0) {
                continue;
            }
            normalized.Add(packId);
        }

        return normalized;
    }

    private static bool ResolveKnownPackEnabled(
        string packId,
        bool enabledByDefault,
        HashSet<string> disabledPackIds,
        HashSet<string> enabledPackIds) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return enabledByDefault;
        }

        if (disabledPackIds.Contains(normalizedPackId)) {
            return false;
        }

        if (enabledPackIds.Contains(normalizedPackId)) {
            return true;
        }

        return enabledByDefault;
    }


}
