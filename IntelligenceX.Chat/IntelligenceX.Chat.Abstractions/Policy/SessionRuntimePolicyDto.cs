namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Runtime governance/auth policy state for the active session.
/// </summary>
public sealed record SessionRuntimePolicyDto {
    /// <summary>
    /// Effective write-governance mode.
    /// </summary>
    public string WriteGovernanceMode { get; init; } = "enforced";

    /// <summary>
    /// When true, write-intent calls require a configured write-governance runtime.
    /// </summary>
    public bool RequireWriteGovernanceRuntime { get; init; } = true;

    /// <summary>
    /// Whether a write-governance runtime is currently configured.
    /// </summary>
    public bool WriteGovernanceRuntimeConfigured { get; init; }

    /// <summary>
    /// When true, write-intent calls require a configured audit sink.
    /// </summary>
    public bool RequireWriteAuditSinkForWriteOperations { get; init; }

    /// <summary>
    /// Effective write-audit sink mode.
    /// </summary>
    public string WriteAuditSinkMode { get; init; } = "none";

    /// <summary>
    /// Whether a write-audit sink is currently configured.
    /// </summary>
    public bool WriteAuditSinkConfigured { get; init; }

    /// <summary>
    /// Optional write-audit sink path.
    /// </summary>
    public string? WriteAuditSinkPath { get; init; }

    /// <summary>
    /// Effective authentication runtime preset.
    /// </summary>
    public string AuthenticationRuntimePreset { get; init; } = "default";

    /// <summary>
    /// When true, strict authentication runtime behavior is required.
    /// </summary>
    public bool RequireAuthenticationRuntime { get; init; }

    /// <summary>
    /// Whether authentication runtime dependencies are configured.
    /// </summary>
    public bool AuthenticationRuntimeConfigured { get; init; }

    /// <summary>
    /// When true, successful SMTP probe validation is required before send actions.
    /// </summary>
    public bool RequireSuccessfulSmtpProbeForSend { get; init; }

    /// <summary>
    /// Maximum accepted SMTP probe age in seconds.
    /// </summary>
    public int SmtpProbeMaxAgeSeconds { get; init; }

    /// <summary>
    /// Optional run-as profile catalog path.
    /// </summary>
    public string? RunAsProfilePath { get; init; }

    /// <summary>
    /// Optional authentication profile catalog path.
    /// </summary>
    public string? AuthenticationProfilePath { get; init; }
}
