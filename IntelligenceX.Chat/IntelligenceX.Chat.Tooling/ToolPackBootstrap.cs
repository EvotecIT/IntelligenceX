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
    /// Enables private IX.ActiveDirectory pack when available.
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
/// Shared tool pack bootstrap for both Host and Service.
/// </summary>
public static class ToolPackBootstrap {
    private const string FileSystemPackTypeName = "IntelligenceX.Tools.FileSystem.FileSystemToolPack, IntelligenceX.Tools.FileSystem";
    private const string FileSystemOptionsTypeName = "IntelligenceX.Tools.FileSystem.FileSystemToolOptions, IntelligenceX.Tools.FileSystem";
    private const string SystemPackTypeName = "IntelligenceX.Tools.System.SystemToolPack, IntelligenceX.Tools.System";
    private const string SystemOptionsTypeName = "IntelligenceX.Tools.System.SystemToolOptions, IntelligenceX.Tools.System";
    private const string ActiveDirectoryPackTypeName = "IntelligenceX.Tools.ActiveDirectory.ActiveDirectoryToolPack, IntelligenceX.Tools.ActiveDirectory";
    private const string ActiveDirectoryOptionsTypeName = "IntelligenceX.Tools.ActiveDirectory.ActiveDirectoryToolOptions, IntelligenceX.Tools.ActiveDirectory";
    private const string PowerShellPackTypeName = "IntelligenceX.Tools.PowerShell.PowerShellToolPack, IntelligenceX.Tools.PowerShell";
    private const string PowerShellOptionsTypeName = "IntelligenceX.Tools.PowerShell.PowerShellToolOptions, IntelligenceX.Tools.PowerShell";
    private const string TestimoXPackTypeName = "IntelligenceX.Tools.TestimoX.TestimoXToolPack, IntelligenceX.Tools.TestimoX";
    private const string TestimoXOptionsTypeName = "IntelligenceX.Tools.TestimoX.TestimoXToolOptions, IntelligenceX.Tools.TestimoX";
    private const string EmailPackTypeName = "IntelligenceX.Tools.Email.EmailToolPack, IntelligenceX.Tools.Email";
    private const string EmailOptionsTypeName = "IntelligenceX.Tools.Email.EmailToolOptions, IntelligenceX.Tools.Email";

    internal const string PackSourceBuiltin = "builtin";
    internal const string PackSourceOpenSource = "open_source";
    internal const string PackSourceClosedSource = "closed_source";

    /// <summary>
    /// Builds the default tool packs (public read-only packs plus optional private packs when available).
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Tool pack list.</returns>
    public static IReadOnlyList<IToolPack> CreateDefaultReadOnlyPacks(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var allowedRoots = options.AllowedRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .ToArray();

        var evxOptions = new EventLogToolOptions();
        foreach (var root in allowedRoots) {
            evxOptions.AllowedRoots.Add(root);
        }

        var packs = new List<IToolPack> {
            WithSourceKind(new EventLogToolPack(evxOptions), PackSourceBuiltin)
        };

        if (options.EnableFileSystemPack) {
            TryAddPack(
                packs,
                packLabel: "IX.FileSystem",
                packTypeName: FileSystemPackTypeName,
                optionsTypeName: FileSystemOptionsTypeName,
                configureOptions: fsOptions => {
                    AddStringListValuesIfPresent(fsOptions, "AllowedRoots", allowedRoots);
                },
                sourceKind: PackSourceBuiltin,
                warnWhenUnavailable: true,
                onWarning: options.OnBootstrapWarning);
        }

        if (options.EnableSystemPack) {
            TryAddPack(
                packs,
                packLabel: "IX.System",
                packTypeName: SystemPackTypeName,
                optionsTypeName: SystemOptionsTypeName,
                configureOptions: null,
                sourceKind: PackSourceClosedSource,
                warnWhenUnavailable: false,
                onWarning: options.OnBootstrapWarning);
        }

        if (options.EnableActiveDirectoryPack) {
            TryAddPack(
                packs,
                packLabel: "IX.ActiveDirectory",
                packTypeName: ActiveDirectoryPackTypeName,
                optionsTypeName: ActiveDirectoryOptionsTypeName,
                configureOptions: adOptions => {
                    SetPropertyIfPresent(adOptions, "DomainController", options.AdDomainController);
                    SetPropertyIfPresent(adOptions, "DefaultSearchBaseDn", options.AdDefaultSearchBaseDn);
                    SetPropertyIfPresent(adOptions, "MaxResults", options.AdMaxResults > 0 ? options.AdMaxResults : 1000);
                },
                sourceKind: PackSourceClosedSource,
                warnWhenUnavailable: false,
                onWarning: options.OnBootstrapWarning);
        }

        if (options.EnablePowerShellPack) {
            TryAddPack(
                packs,
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
                sourceKind: PackSourceBuiltin,
                warnWhenUnavailable: true,
                onWarning: options.OnBootstrapWarning);
        }

        if (options.EnableTestimoXPack) {
            TryAddPack(
                packs,
                packLabel: "IX.TestimoX",
                packTypeName: TestimoXPackTypeName,
                optionsTypeName: TestimoXOptionsTypeName,
                configureOptions: testimoOptions => {
                    SetPropertyIfPresent(testimoOptions, "Enabled", true);
                },
                sourceKind: PackSourceClosedSource,
                warnWhenUnavailable: false,
                onWarning: options.OnBootstrapWarning);
        }

        if (options.EnableReviewerSetupPack) {
            packs.Add(WithSourceKind(
                new ReviewerSetupToolPack(new ReviewerSetupToolOptions {
                    IncludeMaintenancePath = options.ReviewerSetupIncludeMaintenancePath
                }),
                PackSourceBuiltin));
        }

        if (options.EnableEmailPack) {
            TryAddPack(
                packs,
                packLabel: "IX.Email",
                packTypeName: EmailPackTypeName,
                optionsTypeName: EmailOptionsTypeName,
                configureOptions: null,
                sourceKind: PackSourceBuiltin,
                warnWhenUnavailable: true,
                onWarning: options.OnBootstrapWarning);
        }

        if (options.EnablePluginFolderLoading) {
            var existingPackIds = new HashSet<string>(
                packs
                    .Select(static pack => pack.Descriptor.Id)
                    .Where(static id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            PluginFolderToolPackLoader.AddPluginPacks(
                packs: packs,
                options: options,
                existingPackIds: existingPackIds,
                onWarning: options.OnBootstrapWarning);
        }

        return packs;
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
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (packs is null) {
            throw new ArgumentNullException(nameof(packs));
        }

        foreach (var pack in packs) {
            pack.Register(registry);
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

    internal static string NormalizeSourceKind(string? sourceKind, string descriptorId) {
        if (!string.IsNullOrWhiteSpace(sourceKind)) {
            var normalized = sourceKind.Trim().ToLowerInvariant();
            if (normalized is PackSourceBuiltin or PackSourceOpenSource or PackSourceClosedSource) {
                return normalized;
            }

            if (normalized is "open" or "opensource" or "public") {
                return PackSourceOpenSource;
            }

            if (normalized is "closed" or "private" or "internal") {
                return PackSourceClosedSource;
            }
        }

        if (string.IsNullOrWhiteSpace(descriptorId)) {
            return PackSourceOpenSource;
        }

        return NormalizeDescriptorId(descriptorId) switch {
            "eventlog" => PackSourceBuiltin,
            "fs" => PackSourceBuiltin,
            "powershell" => PackSourceBuiltin,
            "reviewersetup" => PackSourceBuiltin,
            "email" => PackSourceBuiltin,
            "system" => PackSourceClosedSource,
            "ad" => PackSourceClosedSource,
            "testimox" => PackSourceClosedSource,
            _ => PackSourceOpenSource
        };
    }

    private static string NormalizeDescriptorId(string descriptorId) {
        var normalized = (descriptorId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        if (normalized.StartsWith("ix", StringComparison.Ordinal)) {
            normalized = normalized[2..];
        } else if (normalized.StartsWith("intelligencex", StringComparison.Ordinal)) {
            normalized = normalized["intelligencex".Length..];
        }

        return normalized switch {
            "computerx" => "system",
            "adplayground" => "ad",
            "activedirectory" => "ad",
            "filesystem" => "fs",
            _ => normalized
        };
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

    private static bool TryAddPack(
        List<IToolPack> packs,
        string packLabel,
        string packTypeName,
        string? optionsTypeName,
        Action<object>? configureOptions,
        string sourceKind,
        bool warnWhenUnavailable,
        Action<string>? onWarning) {
        try {
            var packType = ResolveType(packTypeName);
            if (packType is null) {
                Warn(onWarning, $"{packLabel} pack unavailable (assembly not found).", warnWhenUnavailable);
                return false;
            }

            object? options = null;
            if (!string.IsNullOrWhiteSpace(optionsTypeName)) {
                var optionsType = ResolveType(optionsTypeName);
                if (optionsType is null) {
                    Warn(onWarning, $"{packLabel} pack unavailable (options type not found).", warnWhenUnavailable);
                    return false;
                }

                options = Activator.CreateInstance(optionsType);
                if (options is null) {
                    Warn(onWarning, $"{packLabel} pack unavailable (cannot create options instance).", warnWhenUnavailable);
                    return false;
                }
                configureOptions?.Invoke(options);
            }

            object? instance = options is null
                ? Activator.CreateInstance(packType)
                : Activator.CreateInstance(packType, options);

            if (instance is not IToolPack pack) {
                Warn(onWarning, $"{packLabel} pack unavailable (does not implement IToolPack).", warnWhenUnavailable);
                return false;
            }

            packs.Add(WithSourceKind(pack, sourceKind));
            return true;
        } catch (Exception ex) {
            Warn(onWarning, $"{packLabel} pack skipped: {ex.Message}", warnWhenUnavailable);
            return false;
        }
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
}
