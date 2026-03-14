using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage.Claude;

/// <summary>
/// Discovers the default local Claude source root from the current machine.
/// </summary>
public sealed class ClaudeDefaultSourceRootDiscovery : IUsageTelemetryRootDiscovery {
    private readonly UsageTelemetryExternalProfileDiscovery _externalProfileDiscovery;

    /// <summary>
    /// Initializes a new discovery strategy for Claude local roots.
    /// </summary>
    public ClaudeDefaultSourceRootDiscovery()
        : this(UsageTelemetryExternalProfileDiscovery.Default) {
    }

    internal ClaudeDefaultSourceRootDiscovery(UsageTelemetryExternalProfileDiscovery externalProfileDiscovery) {
        _externalProfileDiscovery = externalProfileDiscovery ?? UsageTelemetryExternalProfileDiscovery.Default;
    }

    /// <inheritdoc />
    public string ProviderId => "claude";

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> DiscoverRoots() {
        var candidates = new List<UsageTelemetryDiscoveredRootCandidate>();
        var configRoots = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configRoots)) {
            foreach (var segment in configRoots.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
                var candidate = segment.Trim();
                if (string.IsNullOrWhiteSpace(candidate)) {
                    continue;
                }

                candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                    UsageSourceKind.LocalLogs,
                    NormalizeProjectsPath(candidate)));
            }
        } else {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile)) {
                AddProfileCandidates(candidates, userProfile, UsageSourceKind.LocalLogs);
            }
        }

        foreach (var profile in _externalProfileDiscovery.DiscoverProfiles()) {
            AddProfileCandidates(candidates, profile.ProfilePath, profile.SourceKind, profile.PlatformHint, profile.MachineLabel);
        }

        return UsageTelemetryRootDiscoverySupport.BuildRoots(ProviderId, candidates);
    }

    private static string NormalizeProjectsPath(string rootPath) {
        var normalized = UsageTelemetryIdentity.NormalizePath(rootPath);
        var leaf = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(leaf, "projects", StringComparison.OrdinalIgnoreCase)) {
            return normalized;
        }

        return Path.Combine(normalized, "projects");
    }

    private static void AddProfileCandidates(
        ICollection<UsageTelemetryDiscoveredRootCandidate> candidates,
        string profilePath,
        UsageSourceKind sourceKind,
        string? platformHint = null,
        string? machineLabel = null) {
        candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
            sourceKind,
            Path.Combine(profilePath, ".config", "claude", "projects"),
            platformHint,
            machineLabel));
        candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
            sourceKind,
            Path.Combine(profilePath, ".claude", "projects"),
            platformHint,
            machineLabel));
    }
}
