using System;
using System.IO;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Write-audit sink runtime mode.
/// </summary>
public enum ToolWriteAuditSinkMode {
    /// <summary>
    /// No audit sink is configured.
    /// </summary>
    None = 0,

    /// <summary>
    /// Append-only JSONL file sink.
    /// </summary>
    FileAppendOnly = 1,

    /// <summary>
    /// Append-only SQLite sink.
    /// </summary>
    SqliteAppendOnly = 2
}

/// <summary>
/// Authentication runtime preset.
/// </summary>
public enum ToolAuthenticationRuntimePreset {
    /// <summary>
    /// Balanced defaults.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Strict preflight/auth gating.
    /// </summary>
    Strict = 1,

    /// <summary>
    /// Local/lab profile with loose auth gating.
    /// </summary>
    Lab = 2
}

/// <summary>
/// Runtime policy options shared by Host and Service.
/// </summary>
public sealed record ToolRuntimePolicyOptions {
    /// <summary>
    /// Write-governance enforcement mode.
    /// </summary>
    public ToolWriteGovernanceMode WriteGovernanceMode { get; init; } = ToolWriteGovernanceMode.Enforced;

    /// <summary>
    /// When true, writes are blocked if no write-governance runtime is configured.
    /// </summary>
    public bool RequireWriteGovernanceRuntime { get; init; } = true;

    /// <summary>
    /// When true, writes are blocked if no write-audit sink is configured.
    /// </summary>
    public bool RequireWriteAuditSinkForWriteOperations { get; init; }

    /// <summary>
    /// Selected audit sink mode.
    /// </summary>
    public ToolWriteAuditSinkMode WriteAuditSinkMode { get; init; }

    /// <summary>
    /// Optional audit sink path (file/db).
    /// </summary>
    public string? WriteAuditSinkPath { get; init; }

    /// <summary>
    /// Authentication preset used to shape pack-level auth behavior.
    /// </summary>
    public ToolAuthenticationRuntimePreset AuthenticationPreset { get; init; } = ToolAuthenticationRuntimePreset.Default;

    /// <summary>
    /// When true, strict auth-runtime behavior is required.
    /// </summary>
    public bool RequireAuthenticationRuntime { get; init; }

    /// <summary>
    /// Optional run-as profile catalog path for tools that support run-as references.
    /// </summary>
    public string? RunAsProfilePath { get; init; }

    /// <summary>
    /// Optional authentication profile catalog path for tools that support explicit auth profile references.
    /// </summary>
    public string? AuthenticationProfilePath { get; init; }
}

/// <summary>
/// Resolved runtime policy options and derived preset effects.
/// </summary>
public sealed record ToolRuntimePolicyResolvedOptions {
    /// <summary>
    /// Effective options after mode/preset normalization.
    /// </summary>
    public required ToolRuntimePolicyOptions Options { get; init; }

    /// <summary>
    /// Effective strict SMTP probe requirement.
    /// </summary>
    public required bool RequireSuccessfulSmtpProbeForSend { get; init; }

    /// <summary>
    /// Effective SMTP probe max-age in seconds.
    /// </summary>
    public required int SmtpProbeMaxAgeSeconds { get; init; }
}

/// <summary>
/// Host/service settings contract used to build runtime policy options.
/// </summary>
public interface IToolRuntimePolicySettings {
    /// <summary>
    /// Write-governance enforcement mode.
    /// </summary>
    ToolWriteGovernanceMode WriteGovernanceMode { get; }

    /// <summary>
    /// When true, writes are blocked if no write-governance runtime is configured.
    /// </summary>
    bool RequireWriteGovernanceRuntime { get; }

    /// <summary>
    /// When true, writes are blocked if no write-audit sink is configured.
    /// </summary>
    bool RequireWriteAuditSinkForWriteOperations { get; }

    /// <summary>
    /// Selected audit sink mode.
    /// </summary>
    ToolWriteAuditSinkMode WriteAuditSinkMode { get; }

    /// <summary>
    /// Optional audit sink path (file/db).
    /// </summary>
    string? WriteAuditSinkPath { get; }

    /// <summary>
    /// Authentication preset used to shape pack-level auth behavior.
    /// </summary>
    ToolAuthenticationRuntimePreset AuthenticationRuntimePreset { get; }

    /// <summary>
    /// When true, strict auth-runtime behavior is required.
    /// </summary>
    bool RequireAuthenticationRuntime { get; }

    /// <summary>
    /// Optional run-as profile catalog path for tools that support run-as references.
    /// </summary>
    string? RunAsProfilePath { get; }

    /// <summary>
    /// Optional authentication profile catalog path for tools that support explicit auth profile references.
    /// </summary>
    string? AuthenticationProfilePath { get; }
}

/// <summary>
/// Resolved runtime policy context used during bootstrap.
/// </summary>
public sealed record ToolRuntimePolicyContext {
    /// <summary>
    /// Effective options.
    /// </summary>
    public required ToolRuntimePolicyOptions Options { get; init; }

    /// <summary>
    /// Shared auth probe store for probe-aware packs.
    /// </summary>
    public required IToolAuthenticationProbeStore AuthenticationProbeStore { get; init; }

    /// <summary>
    /// Optional write-audit sink.
    /// </summary>
    public IToolWriteAuditSink? WriteAuditSink { get; init; }

    /// <summary>
    /// Optional write-governance runtime.
    /// </summary>
    public IToolWriteGovernanceRuntime? WriteGovernanceRuntime { get; init; }

    /// <summary>
    /// Effective strict SMTP probe requirement.
    /// </summary>
    public required bool RequireSuccessfulSmtpProbeForSend { get; init; }

    /// <summary>
    /// Effective SMTP probe max-age in seconds.
    /// </summary>
    public required int SmtpProbeMaxAgeSeconds { get; init; }
}

/// <summary>
/// Runtime policy diagnostics snapshot exposed to host/service surfaces.
/// </summary>
public sealed record ToolRuntimePolicyDiagnostics {
    /// <summary>
    /// Effective write-governance mode.
    /// </summary>
    public required ToolWriteGovernanceMode WriteGovernanceMode { get; init; }
    /// <summary>
    /// When true, write-intent calls require a configured governance runtime.
    /// </summary>
    public required bool RequireWriteGovernanceRuntime { get; init; }
    /// <summary>
    /// Whether a governance runtime is currently configured.
    /// </summary>
    public required bool WriteGovernanceRuntimeConfigured { get; init; }
    /// <summary>
    /// When true, write-intent calls require a configured write-audit sink.
    /// </summary>
    public required bool RequireWriteAuditSinkForWriteOperations { get; init; }
    /// <summary>
    /// Effective write-audit sink mode.
    /// </summary>
    public required ToolWriteAuditSinkMode WriteAuditSinkMode { get; init; }
    /// <summary>
    /// Whether a write-audit sink is currently configured.
    /// </summary>
    public required bool WriteAuditSinkConfigured { get; init; }
    /// <summary>
    /// Optional configured write-audit sink path.
    /// </summary>
    public string? WriteAuditSinkPath { get; init; }
    /// <summary>
    /// Effective authentication runtime preset.
    /// </summary>
    public required ToolAuthenticationRuntimePreset AuthenticationPreset { get; init; }
    /// <summary>
    /// When true, strict authentication runtime behavior is required.
    /// </summary>
    public required bool RequireAuthenticationRuntime { get; init; }
    /// <summary>
    /// Whether authentication runtime dependencies are configured.
    /// </summary>
    public required bool AuthenticationRuntimeConfigured { get; init; }
    /// <summary>
    /// Whether successful SMTP probe validation is required before send actions.
    /// </summary>
    public required bool RequireSuccessfulSmtpProbeForSend { get; init; }
    /// <summary>
    /// Maximum accepted SMTP probe age in seconds.
    /// </summary>
    public required int SmtpProbeMaxAgeSeconds { get; init; }
    /// <summary>
    /// Optional run-as profile catalog path.
    /// </summary>
    public string? RunAsProfilePath { get; init; }

    /// <summary>
    /// Optional authentication profile catalog path.
    /// </summary>
    public string? AuthenticationProfilePath { get; init; }
}

/// <summary>
/// Shared runtime-policy bootstrap helpers for Host and Service.
/// </summary>
public static partial class ToolRuntimePolicyBootstrap {
    /// <summary>
    /// Canonical parse error for invalid <c>--write-governance-mode</c> values.
    /// </summary>
    public const string WriteGovernanceModeParseError = "--write-governance-mode must be one of: enforced, yolo.";

    /// <summary>
    /// Canonical parse error for invalid <c>--write-audit-sink-mode</c> values.
    /// </summary>
    public const string WriteAuditSinkModeParseError = "--write-audit-sink-mode must be one of: none, file, sqlite.";

    /// <summary>
    /// Canonical parse error for invalid <c>--auth-runtime-preset</c> values.
    /// </summary>
    public const string AuthenticationRuntimePresetParseError = "--auth-runtime-preset must be one of: default, strict, lab.";

    private const string StrictImmutableAuditProviderId = "ix.audit.append_only";
    private const string StrictRollbackProviderId = "ix.rollback.catalog";

    /// <summary>
    /// Creates runtime policy options from shared host/service settings.
    /// </summary>
    /// <param name="settings">Runtime policy settings.</param>
    /// <returns>Mapped runtime policy options.</returns>
    public static ToolRuntimePolicyOptions CreateOptions(IToolRuntimePolicySettings settings) {
        if (settings is null) {
            throw new ArgumentNullException(nameof(settings));
        }

        return new ToolRuntimePolicyOptions {
            WriteGovernanceMode = settings.WriteGovernanceMode,
            RequireWriteGovernanceRuntime = settings.RequireWriteGovernanceRuntime,
            RequireWriteAuditSinkForWriteOperations = settings.RequireWriteAuditSinkForWriteOperations,
            WriteAuditSinkMode = settings.WriteAuditSinkMode,
            WriteAuditSinkPath = settings.WriteAuditSinkPath,
            AuthenticationPreset = settings.AuthenticationRuntimePreset,
            RequireAuthenticationRuntime = settings.RequireAuthenticationRuntime,
            RunAsProfilePath = settings.RunAsProfilePath,
            AuthenticationProfilePath = settings.AuthenticationProfilePath
        };
    }

    /// <summary>
    /// Creates a runtime policy context from options.
    /// </summary>
    /// <param name="options">Runtime policy options.</param>
    /// <param name="onWarning">Optional warning sink.</param>
    /// <returns>Resolved policy context.</returns>
    public static ToolRuntimePolicyContext CreateContext(ToolRuntimePolicyOptions options, Action<string>? onWarning = null) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var resolved = ResolveOptions(options);
        var effectiveOptions = resolved.Options;
        var probeStore = new InMemoryToolAuthenticationProbeStore();

        IToolWriteGovernanceRuntime? writeRuntime = null;
        if (effectiveOptions.RequireWriteGovernanceRuntime) {
            writeRuntime = new ToolWriteGovernanceStrictRuntime {
                ImmutableAuditProviderId = StrictImmutableAuditProviderId,
                RollbackProviderId = StrictRollbackProviderId
            };
        }

        var writeAuditSink = TryCreateWriteAuditSink(effectiveOptions.WriteAuditSinkMode, effectiveOptions.WriteAuditSinkPath, onWarning);
        if (effectiveOptions.RequireWriteAuditSinkForWriteOperations && writeAuditSink is null) {
            Warn(onWarning, "write audit sink is required but was not configured.");
        }

        return new ToolRuntimePolicyContext {
            Options = effectiveOptions,
            AuthenticationProbeStore = probeStore,
            WriteAuditSink = writeAuditSink,
            WriteGovernanceRuntime = writeRuntime,
            RequireSuccessfulSmtpProbeForSend = resolved.RequireSuccessfulSmtpProbeForSend,
            SmtpProbeMaxAgeSeconds = resolved.SmtpProbeMaxAgeSeconds
        };
    }

    /// <summary>
    /// Resolves effective runtime options from explicit flags and selected modes/presets.
    /// </summary>
    /// <param name="options">Runtime policy options.</param>
    /// <returns>Resolved options and derived preset effects.</returns>
    public static ToolRuntimePolicyResolvedOptions ResolveOptions(ToolRuntimePolicyOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        var effectiveRequireWriteGovernanceRuntime = options.WriteGovernanceMode == ToolWriteGovernanceMode.Enforced &&
                                                     options.RequireWriteGovernanceRuntime;
        var effectiveRequireAuthenticationRuntime = options.RequireAuthenticationRuntime ||
                                                    options.AuthenticationPreset == ToolAuthenticationRuntimePreset.Strict;
        var normalizedAuditSinkPath = NormalizeOptionalPath(options.WriteAuditSinkPath);
        var normalizedRunAsPath = NormalizeOptionalPath(options.RunAsProfilePath);
        var normalizedAuthProfilePath = NormalizeOptionalPath(options.AuthenticationProfilePath);

        var effectiveOptions = options with {
            RequireWriteGovernanceRuntime = effectiveRequireWriteGovernanceRuntime,
            RequireAuthenticationRuntime = effectiveRequireAuthenticationRuntime,
            WriteAuditSinkPath = normalizedAuditSinkPath,
            RunAsProfilePath = normalizedRunAsPath,
            AuthenticationProfilePath = normalizedAuthProfilePath
        };

        return new ToolRuntimePolicyResolvedOptions {
            Options = effectiveOptions,
            RequireSuccessfulSmtpProbeForSend = effectiveRequireAuthenticationRuntime,
            SmtpProbeMaxAgeSeconds = ResolveSmtpProbeMaxAgeSeconds(effectiveOptions.AuthenticationPreset)
        };
    }

    /// <summary>
    /// Applies runtime policy settings to a tool registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="context">Policy context.</param>
    /// <returns>Runtime diagnostics snapshot.</returns>
    public static ToolRuntimePolicyDiagnostics ApplyToRegistry(ToolRegistry registry, ToolRuntimePolicyContext context) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        registry.WriteGovernanceMode = context.Options.WriteGovernanceMode;
        registry.RequireWriteGovernanceRuntime = context.Options.RequireWriteGovernanceRuntime;
        registry.RequireWriteAuditSinkForWriteOperations = context.Options.RequireWriteAuditSinkForWriteOperations;
        registry.WriteGovernanceRuntime = context.WriteGovernanceRuntime;
        registry.WriteAuditSink = context.WriteAuditSink;

        return BuildDiagnostics(context);
    }

    /// <summary>
    /// Builds a runtime diagnostics snapshot from policy context.
    /// </summary>
    /// <param name="context">Policy context.</param>
    /// <returns>Diagnostics snapshot.</returns>
    public static ToolRuntimePolicyDiagnostics BuildDiagnostics(ToolRuntimePolicyContext context) {
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }

        return new ToolRuntimePolicyDiagnostics {
            WriteGovernanceMode = context.Options.WriteGovernanceMode,
            RequireWriteGovernanceRuntime = context.Options.RequireWriteGovernanceRuntime,
            WriteGovernanceRuntimeConfigured = context.WriteGovernanceRuntime is not null,
            RequireWriteAuditSinkForWriteOperations = context.Options.RequireWriteAuditSinkForWriteOperations,
            WriteAuditSinkMode = context.Options.WriteAuditSinkMode,
            WriteAuditSinkConfigured = context.WriteAuditSink is not null,
            WriteAuditSinkPath = context.Options.WriteAuditSinkPath,
            AuthenticationPreset = context.Options.AuthenticationPreset,
            RequireAuthenticationRuntime = context.Options.RequireAuthenticationRuntime,
            AuthenticationRuntimeConfigured = context.AuthenticationProbeStore is not null,
            RequireSuccessfulSmtpProbeForSend = context.RequireSuccessfulSmtpProbeForSend,
            SmtpProbeMaxAgeSeconds = context.SmtpProbeMaxAgeSeconds,
            RunAsProfilePath = context.Options.RunAsProfilePath,
            AuthenticationProfilePath = context.Options.AuthenticationProfilePath
        };
    }


}
