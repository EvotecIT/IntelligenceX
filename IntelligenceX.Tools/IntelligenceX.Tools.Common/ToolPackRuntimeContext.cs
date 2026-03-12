using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Host/runtime settings made available to tool packs during bootstrap.
/// </summary>
public sealed record ToolPackRuntimeContext {
    /// <summary>
    /// Allowed filesystem roots for file-based tooling.
    /// </summary>
    public IReadOnlyList<string> AllowedRoots { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional Active Directory domain controller/FQDN hint.
    /// </summary>
    public string? AdDomainController { get; init; }

    /// <summary>
    /// Optional default Active Directory search base DN.
    /// </summary>
    public string? AdDefaultSearchBaseDn { get; init; }

    /// <summary>
    /// Default maximum AD result limit when applicable.
    /// </summary>
    public int AdMaxResults { get; init; }

    /// <summary>
    /// Includes maintenance guidance for reviewer setup workflows.
    /// </summary>
    public bool ReviewerSetupIncludeMaintenancePath { get; init; } = true;

    /// <summary>
    /// Default timeout for IX.PowerShell execution.
    /// </summary>
    public int PowerShellDefaultTimeoutMs { get; init; } = 60_000;

    /// <summary>
    /// Maximum timeout accepted by IX.PowerShell execution.
    /// </summary>
    public int PowerShellMaxTimeoutMs { get; init; } = 300_000;

    /// <summary>
    /// Default output cap for IX.PowerShell execution.
    /// </summary>
    public int PowerShellDefaultMaxOutputChars { get; init; } = 50_000;

    /// <summary>
    /// Maximum output cap accepted by IX.PowerShell execution.
    /// </summary>
    public int PowerShellMaxOutputChars { get; init; } = 250_000;

    /// <summary>
    /// Enables read-write IX.PowerShell operations when policy allows.
    /// </summary>
    public bool PowerShellAllowWrite { get; init; }

    /// <summary>
    /// Shared authentication probe store used by probe-aware packs.
    /// </summary>
    public IToolAuthenticationProbeStore? AuthenticationProbeStore { get; init; }

    /// <summary>
    /// Enforces recent successful SMTP probe validation before send actions.
    /// </summary>
    public bool RequireSuccessfulSmtpProbeForSend { get; init; }

    /// <summary>
    /// Maximum age in seconds for successful SMTP probe reuse.
    /// </summary>
    public int SmtpProbeMaxAgeSeconds { get; init; } = 900;

    /// <summary>
    /// Optional run-as profile catalog path.
    /// </summary>
    public string? RunAsProfilePath { get; init; }

    /// <summary>
    /// Optional authentication profile catalog path.
    /// </summary>
    public string? AuthenticationProfilePath { get; init; }

    /// <summary>
    /// Effective normalized runtime option bag after host defaults and overrides are merged.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> EffectivePackRuntimeOptionBag { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Implemented by tool-pack option classes that can self-apply runtime settings during bootstrap.
/// </summary>
public interface IToolPackRuntimeConfigurable {
    /// <summary>
    /// Applies shared host/runtime settings to the current options instance.
    /// </summary>
    /// <param name="context">Resolved runtime context.</param>
    void ApplyRuntimeContext(ToolPackRuntimeContext context);
}
