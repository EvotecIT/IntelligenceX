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
    /// Enables IX.DnsClientX open-source DNS query tools when available.
    /// </summary>
    public bool EnableDnsClientXPack { get; init; } = true;

    /// <summary>
    /// Enables IX.DomainDetective open-source domain diagnostics tools when available.
    /// </summary>
    public bool EnableDomainDetectivePack { get; init; } = true;

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
    /// Enables dangerous IX.PowerShell runtime pack when available.
    /// </summary>
    bool EnablePowerShellPack { get; }

    /// <summary>
    /// Enables read-write intent for IX.PowerShell runtime pack.
    /// </summary>
    bool PowerShellAllowWrite { get; }

    /// <summary>
    /// Enables IX.TestimoX diagnostics pack when available.
    /// </summary>
    bool EnableTestimoXPack { get; }

    /// <summary>
    /// Enables IX.OfficeIMO read-only document ingestion pack when available.
    /// </summary>
    bool EnableOfficeImoPack { get; }

    /// <summary>
    /// Enables default plugin search roots.
    /// </summary>
    bool EnableDefaultPluginPaths { get; }

    /// <summary>
    /// Additional plugin search paths (repeatable).
    /// </summary>
    IReadOnlyList<string> PluginPaths { get; }
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
    private const string DnsClientXPackTypeName = "IntelligenceX.Tools.DnsClientX.DnsClientXToolPack, IntelligenceX.Tools.DnsClientX";
    private const string DnsClientXOptionsTypeName = "IntelligenceX.Tools.DnsClientX.DnsClientXToolOptions, IntelligenceX.Tools.DnsClientX";
    private const string DomainDetectivePackTypeName = "IntelligenceX.Tools.DomainDetective.DomainDetectiveToolPack, IntelligenceX.Tools.DomainDetective";
    private const string DomainDetectiveOptionsTypeName = "IntelligenceX.Tools.DomainDetective.DomainDetectiveToolOptions, IntelligenceX.Tools.DomainDetective";
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

    private static readonly KnownPackDefinition DnsClientXPackDefinition = new(
        "dnsclientx",
        "DnsClientX",
        "Open-source DNS query and record inspection tools.",
        ToolCapabilityTier.ReadOnly,
        IsDangerous: false,
        PackSourceOpenSource);

    private static readonly KnownPackDefinition DomainDetectivePackDefinition = new(
        "domaindetective",
        "DomainDetective",
        "Open-source domain/server diagnostics and posture analysis tools.",
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

        return new ToolPackBootstrapOptions {
            AllowedRoots = allowedRoots,
            AdDomainController = settings.AdDomainController,
            AdDefaultSearchBaseDn = settings.AdDefaultSearchBaseDn,
            AdMaxResults = settings.AdMaxResults,
            EnablePowerShellPack = settings.EnablePowerShellPack,
            PowerShellAllowWrite = settings.PowerShellAllowWrite,
            EnableTestimoXPack = settings.EnableTestimoXPack,
            EnableOfficeImoPack = settings.EnableOfficeImoPack,
            EnableDefaultPluginPaths = settings.EnableDefaultPluginPaths,
            PluginPaths = pluginPaths,
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

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableDnsClientXPack,
            definition: DnsClientXPackDefinition,
            packLabel: "IX.DnsClientX",
            packTypeName: DnsClientXPackTypeName,
            optionsTypeName: DnsClientXOptionsTypeName,
            configureOptions: null,
            onWarning: options.OnBootstrapWarning);

        AddOptionalReflectionPack(
            packs: packs,
            availabilityById: availabilityById,
            enabledByConfiguration: options.EnableDomainDetectivePack,
            definition: DomainDetectivePackDefinition,
            packLabel: "IX.DomainDetective",
            packTypeName: DomainDetectivePackTypeName,
            optionsTypeName: DomainDetectiveOptionsTypeName,
            configureOptions: null,
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
                SetPropertyIfPresent(emailOptions, "AuthenticationProfilePath", options.AuthenticationProfilePath);
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


}
