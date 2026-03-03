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
    /// Whether the pack is available and loaded in this runtime.
    /// </summary>
    public bool Enabled { get; init; }
    /// <summary>
    /// Human-readable reason when the pack is unavailable.
    /// </summary>
    public string? DisabledReason { get; init; }
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
}

/// <summary>
/// Shared tool pack bootstrap for both Host and Service.
/// </summary>
public static partial class ToolPackBootstrap {
    private const string DisabledByRuntimeConfigurationReason = "Disabled by runtime configuration.";
    private const string UnavailableReasonFallback = "Pack could not be loaded in this runtime.";

    internal const string PackSourceBuiltin = "builtin";
    internal const string PackSourceOpenSource = "open_source";
    internal const string PackSourceClosedSource = "closed_source";

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
        var pluginPaths = settings.PluginPaths?.ToArray() ?? Array.Empty<string>();
        var disabledPackIds = settings.DisabledPackIds?.ToArray() ?? Array.Empty<string>();
        var enabledPackIds = settings.EnabledPackIds?.ToArray() ?? Array.Empty<string>();

        return new ToolPackBootstrapOptions {
            AllowedRoots = allowedRoots,
            AdDomainController = settings.AdDomainController,
            AdDefaultSearchBaseDn = settings.AdDefaultSearchBaseDn,
            AdMaxResults = settings.AdMaxResults,
            PowerShellAllowWrite = settings.PowerShellAllowWrite,
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
        var builtInPacks = DiscoverBuiltInPacks(options);

        var packs = new List<IToolPack>();
        var availabilityById = new Dictionary<string, ToolPackAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);
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
                if (!ResolveKnownPackEnabled(
                        packId: builtInPack.PackId,
                        enabledByDefault: builtInPack.DefaultEnabled,
                        disabledPackIds: disabledPackIds,
                        enabledPackIds: enabledPackIds)) {
                    UpsertAvailability(
                        availabilityById,
                        CreateAvailabilityFromDescriptor(
                            descriptor: builtInPack.Pack.Descriptor,
                            enabled: false,
                            disabledReason: DisabledByRuntimeConfigurationReason));
                    return;
                }

                packs.Add(builtInPack.Pack);
                UpsertAvailability(
                    availabilityById,
                    CreateAvailabilityFromDescriptor(
                        descriptor: builtInPack.Pack.Descriptor,
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
                    onPackAvailability: availability => UpsertAvailability(availabilityById, availability));
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
            PackAvailability = availability
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
