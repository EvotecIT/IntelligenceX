using System;
using System.IO;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

public static partial class ToolRuntimePolicyBootstrap {
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
    /// <param name="setRequireExplicitRoutingMetadata">Setter for explicit-routing metadata requirement.</param>
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
        Action<bool> setRequireExplicitRoutingMetadata,
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
        if (setRequireExplicitRoutingMetadata is null) {
            throw new ArgumentNullException(nameof(setRequireExplicitRoutingMetadata));
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
            case "--require-explicit-routing-metadata":
                setRequireExplicitRoutingMetadata(true);
                return true;
            case "--allow-inferred-routing-metadata":
                setRequireExplicitRoutingMetadata(false);
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
    /// <param name="requireExplicitRoutingMetadata">Persisted explicit-routing metadata requirement.</param>
    /// <param name="requireAuthenticationRuntime">Persisted auth runtime requirement.</param>
    /// <param name="runAsProfilePath">Persisted run-as profile path.</param>
    /// <param name="authenticationProfilePath">Persisted auth profile path.</param>
    /// <param name="setWriteGovernanceMode">Setter for parsed write-governance mode.</param>
    /// <param name="setRequireWriteGovernanceRuntime">Setter for write-runtime requirement.</param>
    /// <param name="setRequireWriteAuditSinkForWriteOperations">Setter for write-audit-sink requirement.</param>
    /// <param name="setWriteAuditSinkMode">Setter for parsed write-audit-sink mode.</param>
    /// <param name="setWriteAuditSinkPath">Setter for write-audit-sink path.</param>
    /// <param name="setAuthenticationRuntimePreset">Setter for parsed auth runtime preset.</param>
    /// <param name="setRequireExplicitRoutingMetadata">Setter for explicit-routing metadata requirement.</param>
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
        bool requireExplicitRoutingMetadata,
        bool requireAuthenticationRuntime,
        string? runAsProfilePath,
        string? authenticationProfilePath,
        Action<ToolWriteGovernanceMode> setWriteGovernanceMode,
        Action<bool> setRequireWriteGovernanceRuntime,
        Action<bool> setRequireWriteAuditSinkForWriteOperations,
        Action<ToolWriteAuditSinkMode> setWriteAuditSinkMode,
        Action<string?> setWriteAuditSinkPath,
        Action<ToolAuthenticationRuntimePreset> setAuthenticationRuntimePreset,
        Action<bool> setRequireExplicitRoutingMetadata,
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
        if (setRequireExplicitRoutingMetadata is null) {
            throw new ArgumentNullException(nameof(setRequireExplicitRoutingMetadata));
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

        setRequireExplicitRoutingMetadata(requireExplicitRoutingMetadata);
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
        writeLine("  --require-explicit-routing-metadata  Require explicit routing metadata during tool registration (default: on).");
        writeLine("  --allow-inferred-routing-metadata  Allow inferred routing metadata during tool registration.");
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
