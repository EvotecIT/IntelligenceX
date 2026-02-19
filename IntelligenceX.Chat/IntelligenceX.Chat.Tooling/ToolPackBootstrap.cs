using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.ReviewerSetup;

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
    /// Enables IX.FileSystem pack when available.
    /// </summary>
    public bool EnableFileSystemPack { get; init; } = true;

    /// <summary>
    /// Enables private IX.System pack when available.
    /// </summary>
    public bool EnableSystemPack { get; init; } = true;

    /// <summary>
    /// Enables private IX.ADPlayground pack when available.
    /// </summary>
    public bool EnableActiveDirectoryPack { get; init; } = true;

    /// <summary>
    /// Enables dangerous IX.PowerShell runtime pack when available.
    /// </summary>
    public bool EnablePowerShellPack { get; init; }

    /// <summary>
    /// Enables IX.TestimoX diagnostics pack when available.
    /// </summary>
    public bool EnableTestimoXPack { get; init; } = true;

    /// <summary>
    /// Enables IX.OfficeIMO read-only document ingestion pack when available.
    /// </summary>
    public bool EnableOfficeImoPack { get; init; } = true;

    /// <summary>
    /// Enables reviewer setup guidance pack.
    /// </summary>
    public bool EnableReviewerSetupPack { get; init; } = true;

    /// <summary>
    /// Enables Mailozaurr-backed Email pack when available.
    /// </summary>
    public bool EnableEmailPack { get; init; } = true;

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
public static class ToolPackBootstrap {
    private const string FileSystemPackTypeName = "IntelligenceX.Tools.FileSystem.FileSystemToolPack, IntelligenceX.Tools.FileSystem";
    private const string FileSystemOptionsTypeName = "IntelligenceX.Tools.FileSystem.FileSystemToolOptions, IntelligenceX.Tools.FileSystem";
    private const string SystemPackTypeName = "IntelligenceX.Tools.System.SystemToolPack, IntelligenceX.Tools.System";
    private const string SystemOptionsTypeName = "IntelligenceX.Tools.System.SystemToolOptions, IntelligenceX.Tools.System";
    private const string ActiveDirectoryPackTypeName = "IntelligenceX.Tools.ADPlayground.ActiveDirectoryToolPack, IntelligenceX.Tools.ADPlayground";
    private const string ActiveDirectoryOptionsTypeName = "IntelligenceX.Tools.ADPlayground.ActiveDirectoryToolOptions, IntelligenceX.Tools.ADPlayground";
    private const string PowerShellPackTypeName = "IntelligenceX.Tools.PowerShell.PowerShellToolPack, IntelligenceX.Tools.PowerShell";
    private const string PowerShellOptionsTypeName = "IntelligenceX.Tools.PowerShell.PowerShellToolOptions, IntelligenceX.Tools.PowerShell";
    private const string TestimoXPackTypeName = "IntelligenceX.Tools.TestimoX.TestimoXToolPack, IntelligenceX.Tools.TestimoX";
    private const string TestimoXOptionsTypeName = "IntelligenceX.Tools.TestimoX.TestimoXToolOptions, IntelligenceX.Tools.TestimoX";
    private const string OfficeImoPackTypeName = "IntelligenceX.Tools.OfficeIMO.OfficeImoToolPack, IntelligenceX.Tools.OfficeIMO";
    private const string OfficeImoOptionsTypeName = "IntelligenceX.Tools.OfficeIMO.OfficeImoToolOptions, IntelligenceX.Tools.OfficeIMO";
    private const string EmailPackTypeName = "IntelligenceX.Tools.Email.EmailToolPack, IntelligenceX.Tools.Email";
    private const string EmailOptionsTypeName = "IntelligenceX.Tools.Email.EmailToolOptions, IntelligenceX.Tools.Email";
    private const string DisabledByRuntimeConfigurationReason = "Disabled by runtime configuration.";
    private const string UnavailableReasonFallback = "Pack could not be loaded in this runtime.";

    internal const string PackSourceBuiltin = "builtin";
    internal const string PackSourceOpenSource = "open_source";
    internal const string PackSourceClosedSource = "closed_source";

    private static readonly KnownPackDefinition FileSystemPackDefinition = new(
        "fs",
        "File System",
        "Safe-by-default file system reads (restricted to AllowedRoots).",
        ToolCapabilityTier.ReadOnly,
        IsDangerous: false,
        PackSourceBuiltin);

    private static readonly KnownPackDefinition SystemPackDefinition = new(
        "system",
        "ComputerX",
        "ComputerX host inventory and diagnostics (read-only).",
        ToolCapabilityTier.ReadOnly,
        IsDangerous: false,
        PackSourceClosedSource);

    private static readonly KnownPackDefinition ActiveDirectoryPackDefinition = new(
        "ad",
        "ADPlayground",
        "ADPlayground-backed Active Directory analysis tools (read-oriented).",
        ToolCapabilityTier.SensitiveRead,
        IsDangerous: false,
        PackSourceClosedSource);

    private static readonly KnownPackDefinition PowerShellPackDefinition = new(
        "powershell",
        "PowerShell Runtime",
        "Opt-in shell runtime execution (windows_powershell / pwsh / cmd).",
        ToolCapabilityTier.DangerousWrite,
        IsDangerous: true,
        PackSourceBuiltin);

    private static readonly KnownPackDefinition TestimoXPackDefinition = new(
        "testimox",
        "TestimoX",
        "TestimoX rule discovery and targeted rule execution (read-oriented diagnostics).",
        ToolCapabilityTier.SensitiveRead,
        IsDangerous: false,
        PackSourceClosedSource);

    private static readonly KnownPackDefinition OfficeImoPackDefinition = new(
        "officeimo",
        "Office Documents (OfficeIMO)",
        "Read-only Office document ingestion (Word/Excel/PowerPoint/Markdown/PDF) backed by OfficeIMO.Reader.",
        ToolCapabilityTier.ReadOnly,
        IsDangerous: false,
        PackSourceOpenSource);

    private static readonly KnownPackDefinition ReviewerSetupPackDefinition = new(
        "reviewersetup",
        "Reviewer Setup",
        "Path contract and execution guidance for IntelligenceX reviewer onboarding.",
        ToolCapabilityTier.ReadOnly,
        IsDangerous: false,
        PackSourceBuiltin);

    private static readonly KnownPackDefinition EmailPackDefinition = new(
        "email",
        "Email (Mailozaurr)",
        "IMAP/SMTP workflows (search/get/send) via Mailozaurr.",
        ToolCapabilityTier.SensitiveRead,
        IsDangerous: false,
        PackSourceBuiltin);

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

        var allowedRoots = options.AllowedRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .ToArray();

        var packs = new List<IToolPack>();
        var availabilityById = new Dictionary<string, ToolPackAvailabilityInfo>(StringComparer.OrdinalIgnoreCase);

        var evxOptions = new EventLogToolOptions();
        foreach (var root in allowedRoots) {
            evxOptions.AllowedRoots.Add(root);
        }

        var eventLogPack = RequireDeclaredSourceKind(new EventLogToolPack(evxOptions), "Event Log");
        packs.Add(eventLogPack);
        UpsertAvailability(
            availabilityById,
            CreateAvailabilityFromDescriptor(
                descriptor: eventLogPack.Descriptor,
                enabled: true,
                disabledReason: null));

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableFileSystemPack,
            definition: FileSystemPackDefinition,
            packLabel: "IX.FileSystem",
            packTypeName: FileSystemPackTypeName,
            optionsTypeName: FileSystemOptionsTypeName,
            configureOptions: fsOptions => {
                AddStringListValuesIfPresent(fsOptions, "AllowedRoots", allowedRoots);
            },
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableSystemPack,
            definition: SystemPackDefinition,
            packLabel: "IX.System",
            packTypeName: SystemPackTypeName,
            optionsTypeName: SystemOptionsTypeName,
            configureOptions: null,
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableActiveDirectoryPack,
            definition: ActiveDirectoryPackDefinition,
            packLabel: "IX.ADPlayground",
            packTypeName: ActiveDirectoryPackTypeName,
            optionsTypeName: ActiveDirectoryOptionsTypeName,
            configureOptions: adOptions => {
                SetPropertyIfPresent(adOptions, "DomainController", options.AdDomainController);
                SetPropertyIfPresent(adOptions, "DefaultSearchBaseDn", options.AdDefaultSearchBaseDn);
                SetPropertyIfPresent(adOptions, "MaxResults", options.AdMaxResults > 0 ? options.AdMaxResults : 1000);
            },
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnablePowerShellPack,
            definition: PowerShellPackDefinition,
            packLabel: "IX.PowerShell",
            packTypeName: PowerShellPackTypeName,
            optionsTypeName: PowerShellOptionsTypeName,
            configureOptions: psOptions => {
                SetPropertyIfPresent(psOptions, "Enabled", true);
                SetPropertyIfPresent(psOptions, "DefaultTimeoutMs", options.PowerShellDefaultTimeoutMs);
                SetPropertyIfPresent(psOptions, "MaxTimeoutMs", options.PowerShellMaxTimeoutMs);
                SetPropertyIfPresent(psOptions, "DefaultMaxOutputChars", options.PowerShellDefaultMaxOutputChars);
                SetPropertyIfPresent(psOptions, "MaxOutputChars", options.PowerShellMaxOutputChars);
                SetPropertyIfPresent(psOptions, "AllowWrite", options.PowerShellAllowWrite);
            },
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableTestimoXPack,
            definition: TestimoXPackDefinition,
            packLabel: "IX.TestimoX",
            packTypeName: TestimoXPackTypeName,
            optionsTypeName: TestimoXOptionsTypeName,
            configureOptions: testimoOptions => {
                SetPropertyIfPresent(testimoOptions, "Enabled", true);
            },
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableOfficeImoPack,
            definition: OfficeImoPackDefinition,
            packLabel: "IX.OfficeIMO",
            packTypeName: OfficeImoPackTypeName,
            optionsTypeName: OfficeImoOptionsTypeName,
            configureOptions: officeImoOptions => {
                AddStringListValuesIfPresent(officeImoOptions, "AllowedRoots", allowedRoots);
            },
            onWarning: options.OnBootstrapWarning);

        AddOptionalBuiltInPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableReviewerSetupPack,
            definition: ReviewerSetupPackDefinition,
            createPack: () => RequireDeclaredSourceKind(
                new ReviewerSetupToolPack(new ReviewerSetupToolOptions {
                    IncludeMaintenancePath = options.ReviewerSetupIncludeMaintenancePath
                }),
                packLabel: "Reviewer Setup"),
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableEmailPack,
            definition: EmailPackDefinition,
            packLabel: "IX.Email",
            packTypeName: EmailPackTypeName,
            optionsTypeName: EmailOptionsTypeName,
            configureOptions: emailOptions => {
                SetPropertyIfPresent(emailOptions, "AuthenticationProbeStore", options.AuthenticationProbeStore);
                SetPropertyIfPresent(emailOptions, "RequireSuccessfulSmtpProbeForSend", options.RequireSuccessfulSmtpProbeForSend);
                SetPropertyIfPresent(emailOptions, "SmtpProbeMaxAgeSeconds", options.SmtpProbeMaxAgeSeconds);
                SetPropertyIfPresent(emailOptions, "RunAsProfilePath", options.RunAsProfilePath);
                SetPropertyIfPresent(emailOptions, "AuthenticationProfilePath", options.RunAsProfilePath);
            },
            onWarning: options.OnBootstrapWarning);

        if (options.EnablePluginFolderLoading) {
            var existingPackIds = new HashSet<string>(
                packs
                    .Select(static pack => NormalizePackId(pack.Descriptor.Id))
                    .Where(static id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            PluginFolderToolPackLoader.AddPluginPacks(
                packs: packs,
                options: options,
                existingPackIds: existingPackIds,
                onWarning: options.OnBootstrapWarning);
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
    /// Registers all provided packs into the registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="packs">Packs to register.</param>
    public static void RegisterAll(ToolRegistry registry, IEnumerable<IToolPack> packs) {
        RegisterAll(registry, packs, toolPackIdsByToolName: null);
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
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        var knownDefinitions = new HashSet<string>(
            registry.GetDefinitions().Select(static definition => definition.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var pack in packs) {
            pack.Register(registry);

            var normalizedPackId = NormalizePackId(pack.Descriptor.Id);
            if (normalizedPackId.Length == 0) {
                foreach (var definition in registry.GetDefinitions()) {
                    knownDefinitions.Add(definition.Name);
                }
                continue;
            }

            foreach (var definition in registry.GetDefinitions()) {
                if (!knownDefinitions.Add(definition.Name)) {
                    continue;
                }

                if (toolPackIdsByToolName is not null && normalizedPackId.Length > 0) {
                    toolPackIdsByToolName[definition.Name] = normalizedPackId;
                }
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
        var normalized = (descriptorId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        return normalized;
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

    private static void AddOptionalReflectionPack(
        List<IToolPack> packs,
        Dictionary<string, ToolPackAvailabilityInfo> availabilityById,
        bool enabledByConfiguration,
        KnownPackDefinition definition,
        string packLabel,
        string packTypeName,
        string? optionsTypeName,
        Action<object>? configureOptions,
        Action<string>? onWarning) {
        if (!enabledByConfiguration) {
            UpsertAvailability(availabilityById, CreateAvailabilityFromDefinition(definition, enabled: false, DisabledByRuntimeConfigurationReason));
            return;
        }

        var loaded = TryAddPack(
            packs: packs,
            packLabel: packLabel,
            packTypeName: packTypeName,
            optionsTypeName: optionsTypeName,
            configureOptions: configureOptions,
            warnWhenUnavailable: true,
            onWarning: onWarning,
            loadedPack: out var loadedPack,
            unavailableReason: out var unavailableReason);

        if (loaded && loadedPack is not null) {
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDescriptor(
                    descriptor: loadedPack.Descriptor,
                    enabled: true,
                    disabledReason: null));
            return;
        }

        UpsertAvailability(
            availabilityById,
            CreateAvailabilityFromDefinition(
                definition: definition,
                enabled: false,
                disabledReason: unavailableReason));
    }

    private static void AddOptionalBuiltInPack(
        List<IToolPack> packs,
        Dictionary<string, ToolPackAvailabilityInfo> availabilityById,
        bool enabledByConfiguration,
        KnownPackDefinition definition,
        Func<IToolPack> createPack,
        Action<string>? onWarning) {
        if (!enabledByConfiguration) {
            UpsertAvailability(availabilityById, CreateAvailabilityFromDefinition(definition, enabled: false, DisabledByRuntimeConfigurationReason));
            return;
        }

        try {
            var pack = createPack();
            packs.Add(pack);
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDescriptor(
                    descriptor: pack.Descriptor,
                    enabled: true,
                    disabledReason: null));
        } catch (Exception ex) {
            var reason = NormalizeDisabledReason(ex.Message);
            Warn(onWarning, $"{definition.Name} pack skipped: {reason}", shouldWarn: true);
            UpsertAvailability(
                availabilityById,
                CreateAvailabilityFromDefinition(
                    definition: definition,
                    enabled: false,
                    disabledReason: reason));
        }
    }

    private static ToolPackAvailabilityInfo CreateAvailabilityFromDefinition(KnownPackDefinition definition, bool enabled, string? disabledReason) {
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);
        return new ToolPackAvailabilityInfo {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Tier = definition.Tier,
            IsDangerous = definition.IsDangerous,
            SourceKind = NormalizeSourceKind(definition.SourceKind, definition.Id),
            Enabled = enabled,
            DisabledReason = enabled ? null : normalizedReason
        };
    }

    private static ToolPackAvailabilityInfo CreateAvailabilityFromDescriptor(ToolPackDescriptor descriptor, bool enabled, string? disabledReason) {
        if (descriptor is null) {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var normalizedId = NormalizePackId(descriptor.Id);
        var normalizedName = string.IsNullOrWhiteSpace(descriptor.Name) ? normalizedId : descriptor.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description.Trim();
        var normalizedSourceKind = NormalizeSourceKind(descriptor.SourceKind, descriptor.Id);
        var normalizedReason = enabled ? null : NormalizeDisabledReason(disabledReason);

        return new ToolPackAvailabilityInfo {
            Id = normalizedId.Length == 0 ? descriptor.Id : normalizedId,
            Name = normalizedName,
            Description = normalizedDescription,
            Tier = descriptor.Tier,
            IsDangerous = descriptor.IsDangerous || descriptor.Tier == ToolCapabilityTier.DangerousWrite,
            SourceKind = normalizedSourceKind,
            Enabled = enabled,
            DisabledReason = enabled ? null : normalizedReason
        };
    }

    private static void UpsertAvailability(Dictionary<string, ToolPackAvailabilityInfo> availabilityById, ToolPackAvailabilityInfo availability) {
        var normalizedPackId = NormalizePackId(availability.Id);
        if (normalizedPackId.Length == 0) {
            return;
        }

        var normalizedName = string.IsNullOrWhiteSpace(availability.Name) ? normalizedPackId : availability.Name.Trim();
        availabilityById[normalizedPackId] = availability with { Id = normalizedPackId, Name = normalizedName };
    }

    private static bool TryAddPack(
        List<IToolPack> packs,
        string packLabel,
        string packTypeName,
        string? optionsTypeName,
        Action<object>? configureOptions,
        bool warnWhenUnavailable,
        Action<string>? onWarning,
        out IToolPack? loadedPack,
        out string unavailableReason) {
        loadedPack = null;
        unavailableReason = UnavailableReasonFallback;

        try {
            var packType = ResolveType(packTypeName);
            if (packType is null) {
                unavailableReason = "Required assembly was not found.";
                Warn(onWarning, $"{packLabel} pack unavailable (assembly not found).", warnWhenUnavailable);
                return false;
            }

            object? options = null;
            if (!string.IsNullOrWhiteSpace(optionsTypeName)) {
                var optionsType = ResolveType(optionsTypeName);
                if (optionsType is null) {
                    unavailableReason = "Pack options type was not found.";
                    Warn(onWarning, $"{packLabel} pack unavailable (options type not found).", warnWhenUnavailable);
                    return false;
                }

                options = Activator.CreateInstance(optionsType);
                if (options is null) {
                    unavailableReason = "Could not create pack options.";
                    Warn(onWarning, $"{packLabel} pack unavailable (cannot create options instance).", warnWhenUnavailable);
                    return false;
                }
                configureOptions?.Invoke(options);
            }

            object? instance = options is null
                ? Activator.CreateInstance(packType)
                : Activator.CreateInstance(packType, options);

            if (instance is not IToolPack pack) {
                unavailableReason = "Pack type does not implement IToolPack.";
                Warn(onWarning, $"{packLabel} pack unavailable (does not implement IToolPack).", warnWhenUnavailable);
                return false;
            }

            loadedPack = RequireDeclaredSourceKind(pack, packLabel);
            packs.Add(loadedPack);
            unavailableReason = string.Empty;
            return true;
        } catch (Exception ex) {
            unavailableReason = NormalizeDisabledReason(ex.Message);
            Warn(onWarning, $"{packLabel} pack skipped: {unavailableReason}", warnWhenUnavailable);
            return false;
        }
    }

    private static IToolPack RequireDeclaredSourceKind(IToolPack pack, string packLabel) {
        var descriptorSourceKind = (pack.Descriptor.SourceKind ?? string.Empty).Trim();
        if (descriptorSourceKind.Length == 0) {
            throw new InvalidOperationException($"{packLabel} pack is missing descriptor SourceKind.");
        }

        return WithSourceKind(pack, descriptorSourceKind);
    }

    private static Type? ResolveType(string assemblyQualifiedTypeName) {
        var resolved = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
        if (resolved is not null) {
            return resolved;
        }

        var parts = assemblyQualifiedTypeName.Split(',', count: 2, options: StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            return null;
        }

        try {
            var assembly = Assembly.Load(new AssemblyName(parts[1]));
            return assembly.GetType(parts[0], throwOnError: false, ignoreCase: false);
        } catch {
            return null;
        }
    }

    private static void SetPropertyIfPresent(object instance, string propertyName, object? value) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite) {
            return;
        }

        if (value is null) {
            property.SetValue(instance, null);
            return;
        }

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var valueType = value.GetType();

        if (targetType.IsAssignableFrom(valueType)) {
            property.SetValue(instance, value);
            return;
        }

        try {
            var converted = Convert.ChangeType(value, targetType);
            property.SetValue(instance, converted);
        } catch {
            // Ignore conversion failures; keep pack defaults.
        }
    }

    private static void AddStringListValuesIfPresent(object instance, string propertyName, IEnumerable<string> values) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead) {
            return;
        }

        if (property.GetValue(instance) is not System.Collections.IList list) {
            return;
        }

        foreach (var value in values) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            list.Add(value);
        }
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

    private sealed record KnownPackDefinition(
        string Id,
        string Name,
        string Description,
        ToolCapabilityTier Tier,
        bool IsDangerous,
        string SourceKind);
}
