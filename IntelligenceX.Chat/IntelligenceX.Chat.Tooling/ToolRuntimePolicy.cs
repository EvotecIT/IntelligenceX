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
}

/// <summary>
/// Shared runtime-policy bootstrap helpers for Host and Service.
/// </summary>
public static class ToolRuntimePolicyBootstrap {
    private const string StrictImmutableAuditProviderId = "ix.audit.append_only";
    private const string StrictRollbackProviderId = "ix.rollback.catalog";

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

        var probeStore = new InMemoryToolAuthenticationProbeStore();
        var requireStrictProbe = options.RequireAuthenticationRuntime || options.AuthenticationPreset == ToolAuthenticationRuntimePreset.Strict;
        var probeMaxAgeSeconds = ResolveSmtpProbeMaxAgeSeconds(options.AuthenticationPreset);
        var normalizedRunAsPath = NormalizeOptionalPath(options.RunAsProfilePath);

        IToolWriteGovernanceRuntime? writeRuntime = null;
        if (options.WriteGovernanceMode == ToolWriteGovernanceMode.Enforced && options.RequireWriteGovernanceRuntime) {
            writeRuntime = new ToolWriteGovernanceStrictRuntime {
                ImmutableAuditProviderId = StrictImmutableAuditProviderId,
                RollbackProviderId = StrictRollbackProviderId
            };
        }

        var sinkPath = NormalizeOptionalPath(options.WriteAuditSinkPath);
        var writeAuditSink = TryCreateWriteAuditSink(options.WriteAuditSinkMode, sinkPath, onWarning);
        if (options.RequireWriteAuditSinkForWriteOperations && writeAuditSink is null) {
            Warn(onWarning, "write audit sink is required but was not configured.");
        }

        return new ToolRuntimePolicyContext {
            Options = options with {
                WriteAuditSinkPath = sinkPath,
                RunAsProfilePath = normalizedRunAsPath
            },
            AuthenticationProbeStore = probeStore,
            WriteAuditSink = writeAuditSink,
            WriteGovernanceRuntime = writeRuntime,
            RequireSuccessfulSmtpProbeForSend = requireStrictProbe,
            SmtpProbeMaxAgeSeconds = probeMaxAgeSeconds
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
            RunAsProfilePath = context.Options.RunAsProfilePath
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
