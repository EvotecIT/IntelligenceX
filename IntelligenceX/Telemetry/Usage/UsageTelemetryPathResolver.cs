using System;
using System.IO;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Resolves persistent storage paths for usage telemetry.
/// </summary>
public static class UsageTelemetryPathResolver {
    /// <summary>
    /// Environment variable that globally enables or disables runtime usage telemetry.
    /// </summary>
    public const string EnableEnvironmentVariable = "INTELLIGENCEX_USAGE_TELEMETRY";

    /// <summary>
    /// Environment variable that overrides the telemetry SQLite database path.
    /// </summary>
    public const string DatabasePathEnvironmentVariable = "INTELLIGENCEX_USAGE_DB";

    /// <summary>
    /// Resolves the SQLite database path for runtime usage telemetry.
    /// </summary>
    public static string? ResolveDatabasePath(string? explicitPath = null, bool enabledByDefault = false) {
        var resolvedExplicitPath = NormalizeExplicitPath(explicitPath);
        if (!string.IsNullOrWhiteSpace(resolvedExplicitPath)) {
            return resolvedExplicitPath;
        }

        var envEnabled = ReadOptionalBoolean(Environment.GetEnvironmentVariable(EnableEnvironmentVariable));
        if (envEnabled.HasValue && !envEnabled.Value) {
            return null;
        }

        var envPath = NormalizeExplicitPath(Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(envPath)) {
            return envPath;
        }

        if (envEnabled.GetValueOrDefault(enabledByDefault)) {
            return BuildDefaultDatabasePath();
        }

        return null;
    }

    internal static string BuildDefaultDatabasePath() {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory)) {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        if (string.IsNullOrWhiteSpace(baseDirectory)) {
            baseDirectory = Path.Combine(Path.GetTempPath(), "IntelligenceX");
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, "IntelligenceX", "telemetry", "usage.db"));
    }

    private static string? NormalizeExplicitPath(string? value) {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return null;
        }
        var resolved = trimmed!;

        if (resolved.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            resolved.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            Directory.Exists(resolved)) {
            resolved = Path.Combine(resolved, "usage.db");
        }

        return Path.GetFullPath(resolved);
    }

    private static bool? ReadOptionalBoolean(string? value) {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            return null;
        }
        var resolved = trimmed!;

        switch (resolved.ToLowerInvariant()) {
            case "1":
            case "true":
            case "yes":
            case "on":
            case "enabled":
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
            case "disabled":
                return false;
            default:
                return null;
        }
    }
}
