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
public static class ToolRuntimePolicyBootstrap {
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

    /// <summary>
    /// Parses write-governance mode from CLI/profile token.
    /// </summary>
    public static bool TryParseWriteGovernanceMode(string? value, out ToolWriteGovernanceMode mode) {
        mode = ToolWriteGovernanceMode.Enforced;
        var normalized = NormalizeToken(value);
        return normalized switch {
            "enforced" => true,
            "yolo" => AssignWriteGovernanceMode(ToolWriteGovernanceMode.Yolo, out mode),
            _ => false
        };
    }

    /// <summary>
    /// Parses write-audit sink mode from CLI/profile token.
    /// </summary>
    public static bool TryParseWriteAuditSinkMode(string? value, out ToolWriteAuditSinkMode mode) {
        mode = ToolWriteAuditSinkMode.None;
        var normalized = NormalizeToken(value);
        return normalized switch {
            "" => true,
            "none" => true,
            "file" => AssignWriteAuditSinkMode(ToolWriteAuditSinkMode.FileAppendOnly, out mode),
            "fileappendonly" => AssignWriteAuditSinkMode(ToolWriteAuditSinkMode.FileAppendOnly, out mode),
            "jsonl" => AssignWriteAuditSinkMode(ToolWriteAuditSinkMode.FileAppendOnly, out mode),
            "sql" => AssignWriteAuditSinkMode(ToolWriteAuditSinkMode.SqliteAppendOnly, out mode),
            "sqlite" => AssignWriteAuditSinkMode(ToolWriteAuditSinkMode.SqliteAppendOnly, out mode),
            "sqliteappendonly" => AssignWriteAuditSinkMode(ToolWriteAuditSinkMode.SqliteAppendOnly, out mode),
            _ => false
        };
    }

    /// <summary>
    /// Parses authentication preset from CLI/profile token.
    /// </summary>
    public static bool TryParseAuthenticationRuntimePreset(string? value, out ToolAuthenticationRuntimePreset preset) {
        preset = ToolAuthenticationRuntimePreset.Default;
        var normalized = NormalizeToken(value);
        return normalized switch {
            "" => true,
            "default" => true,
            "strict" => AssignAuthenticationPreset(ToolAuthenticationRuntimePreset.Strict, out preset),
            "lab" => AssignAuthenticationPreset(ToolAuthenticationRuntimePreset.Lab, out preset),
            _ => false
        };
    }

    /// <summary>
    /// Applies one runtime-policy CLI argument using shared parsing and validation semantics.
    /// </summary>
    /// <param name="argument">Current CLI argument token.</param>
    /// <param name="consumeValue">Callback that consumes the next CLI value token when required.</param>
    /// <param name="setWriteGovernanceMode">Setter for write-governance mode.</param>
    /// <param name="setRequireWriteGovernanceRuntime">Setter for write-runtime requirement.</param>
    /// <param name="setRequireWriteAuditSinkForWriteOperations">Setter for write-audit-sink requirement.</param>
    /// <param name="setWriteAuditSinkMode">Setter for write-audit-sink mode.</param>
    /// <param name="setWriteAuditSinkPath">Setter for write-audit-sink path.</param>
    /// <param name="setAuthenticationRuntimePreset">Setter for auth runtime preset.</param>
    /// <param name="setRequireAuthenticationRuntime">Setter for auth runtime requirement.</param>
    /// <param name="setRunAsProfilePath">Setter for run-as profile path.</param>
    /// <param name="setAuthenticationProfilePath">Setter for auth profile path.</param>
    /// <param name="handled">True when <paramref name="argument" /> is a runtime-policy argument.</param>
    /// <param name="error">Validation error when parsing fails; otherwise null.</param>
    /// <returns>True when parsing succeeded (or argument was not handled), false when handled parse failed.</returns>
    public static bool TryApplyRuntimePolicyCliArgument(
        string? argument,
        Func<(bool Success, string? Value, string? Error)> consumeValue,
        Action<ToolWriteGovernanceMode> setWriteGovernanceMode,
        Action<bool> setRequireWriteGovernanceRuntime,
        Action<bool> setRequireWriteAuditSinkForWriteOperations,
        Action<ToolWriteAuditSinkMode> setWriteAuditSinkMode,
        Action<string?> setWriteAuditSinkPath,
        Action<ToolAuthenticationRuntimePreset> setAuthenticationRuntimePreset,
        Action<bool> setRequireAuthenticationRuntime,
        Action<string?> setRunAsProfilePath,
        Action<string?> setAuthenticationProfilePath,
        out bool handled,
        out string? error) {
        if (consumeValue is null) {
            throw new ArgumentNullException(nameof(consumeValue));
        }
        if (setWriteGovernanceMode is null) {
            throw new ArgumentNullException(nameof(setWriteGovernanceMode));
        }
        if (setRequireWriteGovernanceRuntime is null) {
            throw new ArgumentNullException(nameof(setRequireWriteGovernanceRuntime));
        }
        if (setRequireWriteAuditSinkForWriteOperations is null) {
            throw new ArgumentNullException(nameof(setRequireWriteAuditSinkForWriteOperations));
        }
        if (setWriteAuditSinkMode is null) {
            throw new ArgumentNullException(nameof(setWriteAuditSinkMode));
        }
        if (setWriteAuditSinkPath is null) {
            throw new ArgumentNullException(nameof(setWriteAuditSinkPath));
        }
        if (setAuthenticationRuntimePreset is null) {
            throw new ArgumentNullException(nameof(setAuthenticationRuntimePreset));
        }
        if (setRequireAuthenticationRuntime is null) {
            throw new ArgumentNullException(nameof(setRequireAuthenticationRuntime));
        }
        if (setRunAsProfilePath is null) {
            throw new ArgumentNullException(nameof(setRunAsProfilePath));
        }
        if (setAuthenticationProfilePath is null) {
            throw new ArgumentNullException(nameof(setAuthenticationProfilePath));
        }

        handled = true;
        error = null;
        switch (argument) {
            case "--write-governance-mode":
                var writeModeValue = consumeValue();
                if (!writeModeValue.Success) {
                    error = writeModeValue.Error;
                    return false;
                }
                if (!TryParseWriteGovernanceMode(writeModeValue.Value, out var writeMode)) {
                    error = WriteGovernanceModeParseError;
                    return false;
                }
                setWriteGovernanceMode(writeMode);
                return true;
            case "--require-write-governance-runtime":
                setRequireWriteGovernanceRuntime(true);
                return true;
            case "--no-require-write-governance-runtime":
                setRequireWriteGovernanceRuntime(false);
                return true;
            case "--require-write-audit-sink":
                setRequireWriteAuditSinkForWriteOperations(true);
                return true;
            case "--no-require-write-audit-sink":
                setRequireWriteAuditSinkForWriteOperations(false);
                return true;
            case "--write-audit-sink-mode":
                var writeAuditSinkModeValue = consumeValue();
                if (!writeAuditSinkModeValue.Success) {
                    error = writeAuditSinkModeValue.Error;
                    return false;
                }
                if (!TryParseWriteAuditSinkMode(writeAuditSinkModeValue.Value, out var writeAuditSinkMode)) {
                    error = WriteAuditSinkModeParseError;
                    return false;
                }
                setWriteAuditSinkMode(writeAuditSinkMode);
                return true;
            case "--write-audit-sink-path":
                var writeAuditSinkPathValue = consumeValue();
                if (!writeAuditSinkPathValue.Success) {
                    error = writeAuditSinkPathValue.Error;
                    return false;
                }
                setWriteAuditSinkPath(writeAuditSinkPathValue.Value);
                return true;
            case "--auth-runtime-preset":
                var authPresetValue = consumeValue();
                if (!authPresetValue.Success) {
                    error = authPresetValue.Error;
                    return false;
                }
                if (!TryParseAuthenticationRuntimePreset(authPresetValue.Value, out var authPreset)) {
                    error = AuthenticationRuntimePresetParseError;
                    return false;
                }
                setAuthenticationRuntimePreset(authPreset);
                return true;
            case "--require-auth-runtime":
                setRequireAuthenticationRuntime(true);
                return true;
            case "--no-require-auth-runtime":
                setRequireAuthenticationRuntime(false);
                return true;
            case "--run-as-profile-path":
                var runAsProfilePath = consumeValue();
                if (!runAsProfilePath.Success) {
                    error = runAsProfilePath.Error;
                    return false;
                }
                setRunAsProfilePath(runAsProfilePath.Value);
                return true;
            case "--auth-profile-path":
                var authProfilePath = consumeValue();
                if (!authProfilePath.Success) {
                    error = authProfilePath.Error;
                    return false;
                }
                setAuthenticationProfilePath(authProfilePath.Value);
                return true;
            default:
                handled = false;
                return true;
        }
    }

    /// <summary>
    /// Applies runtime-policy values loaded from a persisted profile.
    /// </summary>
    /// <param name="writeGovernanceMode">Persisted write-governance mode token.</param>
    /// <param name="requireWriteGovernanceRuntime">Persisted write-runtime requirement.</param>
    /// <param name="requireWriteAuditSinkForWriteOperations">Persisted write-audit-sink requirement.</param>
    /// <param name="writeAuditSinkMode">Persisted write-audit-sink mode token.</param>
    /// <param name="writeAuditSinkPath">Persisted write-audit-sink path.</param>
    /// <param name="authenticationRuntimePreset">Persisted auth runtime preset token.</param>
    /// <param name="requireAuthenticationRuntime">Persisted auth runtime requirement.</param>
    /// <param name="runAsProfilePath">Persisted run-as profile path.</param>
    /// <param name="authenticationProfilePath">Persisted auth profile path.</param>
    /// <param name="setWriteGovernanceMode">Setter for parsed write-governance mode.</param>
    /// <param name="setRequireWriteGovernanceRuntime">Setter for write-runtime requirement.</param>
    /// <param name="setRequireWriteAuditSinkForWriteOperations">Setter for write-audit-sink requirement.</param>
    /// <param name="setWriteAuditSinkMode">Setter for parsed write-audit-sink mode.</param>
    /// <param name="setWriteAuditSinkPath">Setter for write-audit-sink path.</param>
    /// <param name="setAuthenticationRuntimePreset">Setter for parsed auth runtime preset.</param>
    /// <param name="setRequireAuthenticationRuntime">Setter for auth runtime requirement.</param>
    /// <param name="setRunAsProfilePath">Setter for run-as profile path.</param>
    /// <param name="setAuthenticationProfilePath">Setter for auth profile path.</param>
    public static void ApplyProfileRuntimePolicy(
        string? writeGovernanceMode,
        bool requireWriteGovernanceRuntime,
        bool requireWriteAuditSinkForWriteOperations,
        string? writeAuditSinkMode,
        string? writeAuditSinkPath,
        string? authenticationRuntimePreset,
        bool requireAuthenticationRuntime,
        string? runAsProfilePath,
        string? authenticationProfilePath,
        Action<ToolWriteGovernanceMode> setWriteGovernanceMode,
        Action<bool> setRequireWriteGovernanceRuntime,
        Action<bool> setRequireWriteAuditSinkForWriteOperations,
        Action<ToolWriteAuditSinkMode> setWriteAuditSinkMode,
        Action<string?> setWriteAuditSinkPath,
        Action<ToolAuthenticationRuntimePreset> setAuthenticationRuntimePreset,
        Action<bool> setRequireAuthenticationRuntime,
        Action<string?> setRunAsProfilePath,
        Action<string?> setAuthenticationProfilePath) {
        if (setWriteGovernanceMode is null) {
            throw new ArgumentNullException(nameof(setWriteGovernanceMode));
        }
        if (setRequireWriteGovernanceRuntime is null) {
            throw new ArgumentNullException(nameof(setRequireWriteGovernanceRuntime));
        }
        if (setRequireWriteAuditSinkForWriteOperations is null) {
            throw new ArgumentNullException(nameof(setRequireWriteAuditSinkForWriteOperations));
        }
        if (setWriteAuditSinkMode is null) {
            throw new ArgumentNullException(nameof(setWriteAuditSinkMode));
        }
        if (setWriteAuditSinkPath is null) {
            throw new ArgumentNullException(nameof(setWriteAuditSinkPath));
        }
        if (setAuthenticationRuntimePreset is null) {
            throw new ArgumentNullException(nameof(setAuthenticationRuntimePreset));
        }
        if (setRequireAuthenticationRuntime is null) {
            throw new ArgumentNullException(nameof(setRequireAuthenticationRuntime));
        }
        if (setRunAsProfilePath is null) {
            throw new ArgumentNullException(nameof(setRunAsProfilePath));
        }
        if (setAuthenticationProfilePath is null) {
            throw new ArgumentNullException(nameof(setAuthenticationProfilePath));
        }

        if (TryParseWriteGovernanceMode(writeGovernanceMode, out var parsedWriteMode)) {
            setWriteGovernanceMode(parsedWriteMode);
        }

        setRequireWriteGovernanceRuntime(requireWriteGovernanceRuntime);
        setRequireWriteAuditSinkForWriteOperations(requireWriteAuditSinkForWriteOperations);

        if (TryParseWriteAuditSinkMode(writeAuditSinkMode, out var parsedWriteAuditMode)) {
            setWriteAuditSinkMode(parsedWriteAuditMode);
        }

        setWriteAuditSinkPath(writeAuditSinkPath);

        if (TryParseAuthenticationRuntimePreset(authenticationRuntimePreset, out var parsedAuthPreset)) {
            setAuthenticationRuntimePreset(parsedAuthPreset);
        }

        setRequireAuthenticationRuntime(requireAuthenticationRuntime);
        setRunAsProfilePath(runAsProfilePath);
        setAuthenticationProfilePath(authenticationProfilePath);
    }

    /// <summary>
    /// Formats write-governance mode for profile persistence.
    /// </summary>
    public static string FormatWriteGovernanceMode(ToolWriteGovernanceMode mode) {
        return mode == ToolWriteGovernanceMode.Yolo ? "yolo" : "enforced";
    }

    /// <summary>
    /// Formats write-audit sink mode for profile persistence.
    /// </summary>
    public static string FormatWriteAuditSinkMode(ToolWriteAuditSinkMode mode) {
        return mode switch {
            ToolWriteAuditSinkMode.FileAppendOnly => "file",
            ToolWriteAuditSinkMode.SqliteAppendOnly => "sqlite",
            _ => "none"
        };
    }

    /// <summary>
    /// Formats authentication preset for profile persistence.
    /// </summary>
    public static string FormatAuthenticationRuntimePreset(ToolAuthenticationRuntimePreset preset) {
        return preset switch {
            ToolAuthenticationRuntimePreset.Strict => "strict",
            ToolAuthenticationRuntimePreset.Lab => "lab",
            _ => "default"
        };
    }

    /// <summary>
    /// Writes shared CLI help lines for runtime policy options.
    /// </summary>
    /// <param name="writeLine">Line writer callback.</param>
    public static void WriteRuntimePolicyCliHelp(Action<string> writeLine) {
        if (writeLine is null) {
            throw new ArgumentNullException(nameof(writeLine));
        }

        writeLine("  --write-governance-mode <MODE>  Write governance mode: enforced|yolo (default: enforced).");
        writeLine("  --require-write-governance-runtime  Require configured write runtime for write-intent calls (default: on).");
        writeLine("  --no-require-write-governance-runtime Disable runtime requirement for write-intent calls.");
        writeLine("  --require-write-audit-sink  Require write audit sink for write-intent calls.");
        writeLine("  --no-require-write-audit-sink Disable write audit sink requirement.");
        writeLine("  --write-audit-sink-mode <MODE>  Write audit sink mode: none|file|sqlite (default: none).");
        writeLine("  --write-audit-sink-path <PATH>  Write audit sink path (JSONL file or SQLite db).");
        writeLine("  --auth-runtime-preset <MODE>  Auth runtime preset: default|strict|lab (default: default).");
        writeLine("  --require-auth-runtime   Require strict auth runtime gating for write-capable auth flows.");
        writeLine("  --no-require-auth-runtime Disable strict auth runtime requirement.");
        writeLine("  --run-as-profile-path <PATH>  Run-as profile catalog path for auth-aware packs.");
        writeLine("  --auth-profile-path <PATH>  Authentication profile catalog path for auth-aware packs.");
    }

    private static IToolWriteAuditSink? TryCreateWriteAuditSink(
        ToolWriteAuditSinkMode mode,
        string? sinkPath,
        Action<string>? onWarning) {
        if (mode == ToolWriteAuditSinkMode.None) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sinkPath)) {
            Warn(onWarning, $"write audit sink mode '{FormatWriteAuditSinkMode(mode)}' requires --write-audit-sink-path.");
            return null;
        }

        try {
            return mode switch {
                ToolWriteAuditSinkMode.FileAppendOnly => new AppendOnlyJsonlToolWriteAuditSink(sinkPath),
                ToolWriteAuditSinkMode.SqliteAppendOnly => new SqliteAppendOnlyToolWriteAuditSink(sinkPath),
                _ => null
            };
        } catch (Exception ex) {
            Warn(onWarning, $"failed to initialize write audit sink: {ex.Message}");
            return null;
        }
    }

    private static int ResolveSmtpProbeMaxAgeSeconds(ToolAuthenticationRuntimePreset preset) {
        return preset switch {
            ToolAuthenticationRuntimePreset.Strict => 600,
            ToolAuthenticationRuntimePreset.Lab => 3600,
            _ => 900
        };
    }

    private static string? NormalizeOptionalPath(string? path) {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        try {
            return Path.GetFullPath(normalized);
        } catch {
            return normalized;
        }
    }

    private static string NormalizeToken(string? value) {
        return (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static bool AssignWriteGovernanceMode(ToolWriteGovernanceMode value, out ToolWriteGovernanceMode mode) {
        mode = value;
        return true;
    }

    private static bool AssignWriteAuditSinkMode(ToolWriteAuditSinkMode value, out ToolWriteAuditSinkMode mode) {
        mode = value;
        return true;
    }

    private static bool AssignAuthenticationPreset(ToolAuthenticationRuntimePreset value, out ToolAuthenticationRuntimePreset preset) {
        preset = value;
        return true;
    }

    private static void Warn(Action<string>? onWarning, string message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return;
        }

        onWarning?.Invoke(message.Trim());
    }
}
