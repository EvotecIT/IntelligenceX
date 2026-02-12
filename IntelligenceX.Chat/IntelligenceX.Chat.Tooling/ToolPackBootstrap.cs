using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ActiveDirectory;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.ReviewerSetup;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;

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
    /// Enables dangerous IX.PowerShell runtime pack.
    /// </summary>
    public bool EnablePowerShellPack { get; init; }

    /// <summary>
    /// Enables IX.TestimoX diagnostics pack.
    /// </summary>
    public bool EnableTestimoXPack { get; init; } = true;

    /// <summary>
    /// Enables reviewer setup guidance pack.
    /// </summary>
    public bool EnableReviewerSetupPack { get; init; } = true;

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
}

/// <summary>
/// Shared tool pack bootstrap for both Host and Service.
/// </summary>
public static class ToolPackBootstrap {
    /// <summary>
    /// Builds the default tool packs (read-only packs plus optional dangerous IX.PowerShell pack).
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>Tool pack list.</returns>
    public static IReadOnlyList<IToolPack> CreateDefaultReadOnlyPacks(ToolPackBootstrapOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var fsOptions = new FileSystemToolOptions();
        foreach (var root in options.AllowedRoots.Where(static r => !string.IsNullOrWhiteSpace(r))) {
            fsOptions.AllowedRoots.Add(root);
        }

        var evxOptions = new EventLogToolOptions();
        foreach (var root in options.AllowedRoots.Where(static r => !string.IsNullOrWhiteSpace(r))) {
            evxOptions.AllowedRoots.Add(root);
        }

        var adOptions = new ActiveDirectoryToolOptions {
            DomainController = options.AdDomainController,
            DefaultSearchBaseDn = options.AdDefaultSearchBaseDn,
            MaxResults = options.AdMaxResults > 0 ? options.AdMaxResults : 1000
        };

        var packs = new List<IToolPack> {
            new SystemToolPack(new SystemToolOptions()),
            new FileSystemToolPack(fsOptions),
            new EventLogToolPack(evxOptions),
            new ActiveDirectoryToolPack(adOptions)
        };

        if (options.EnablePowerShellPack) {
            packs.Add(new PowerShellToolPack(new PowerShellToolOptions {
                Enabled = true,
                DefaultTimeoutMs = options.PowerShellDefaultTimeoutMs,
                MaxTimeoutMs = options.PowerShellMaxTimeoutMs,
                DefaultMaxOutputChars = options.PowerShellDefaultMaxOutputChars,
                MaxOutputChars = options.PowerShellMaxOutputChars,
                AllowWrite = options.PowerShellAllowWrite
            }));
        }

        if (options.EnableTestimoXPack) {
            packs.Add(new TestimoXToolPack(new TestimoXToolOptions {
                Enabled = true
            }));
        }

        if (options.EnableReviewerSetupPack) {
            packs.Add(new ReviewerSetupToolPack(new ReviewerSetupToolOptions {
                IncludeMaintenancePath = options.ReviewerSetupIncludeMaintenancePath
            }));
        }

        // Pack constructors validate options, so creation is a good early fail-fast point.
        return packs;
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
}
