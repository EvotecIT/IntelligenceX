using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage.Claude;

/// <summary>
/// Discovers the default local Claude source root from the current machine.
/// </summary>
public sealed class ClaudeDefaultSourceRootDiscovery : IUsageTelemetryRootDiscovery {
    /// <inheritdoc />
    public string ProviderId => "claude";

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> DiscoverRoots() {
        var paths = new List<string>();
        var configRoots = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configRoots)) {
            foreach (var segment in configRoots.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
                var candidate = segment.Trim();
                if (string.IsNullOrWhiteSpace(candidate)) {
                    continue;
                }

                paths.Add(NormalizeProjectsPath(candidate));
            }
        } else {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile)) {
                paths.Add(Path.Combine(userProfile, ".config", "claude", "projects"));
                paths.Add(Path.Combine(userProfile, ".claude", "projects"));
            }
        }

        return paths
            .Where(path => Directory.Exists(path))
            .Select(path => UsageTelemetryIdentity.NormalizePath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new SourceRootRecord(
                SourceRootRecord.CreateStableId(ProviderId, UsageSourceKind.LocalLogs, path),
                ProviderId,
                UsageSourceKind.LocalLogs,
                path))
            .ToArray();
    }

    private static string NormalizeProjectsPath(string rootPath) {
        var normalized = UsageTelemetryIdentity.NormalizePath(rootPath);
        var leaf = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(leaf, "projects", StringComparison.OrdinalIgnoreCase)) {
            return normalized;
        }

        return Path.Combine(normalized, "projects");
    }
}
