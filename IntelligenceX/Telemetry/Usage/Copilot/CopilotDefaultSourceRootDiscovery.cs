using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Telemetry.Usage.Copilot;

/// <summary>
/// Discovers default local GitHub Copilot CLI telemetry roots.
/// </summary>
public sealed class CopilotDefaultSourceRootDiscovery : IUsageTelemetryRootDiscovery {
    private readonly UsageTelemetryExternalProfileDiscovery _externalProfileDiscovery;

    /// <summary>
    /// Initializes a new discovery strategy for Copilot local roots.
    /// </summary>
    public CopilotDefaultSourceRootDiscovery()
        : this(UsageTelemetryExternalProfileDiscovery.Default) {
    }

    internal CopilotDefaultSourceRootDiscovery(UsageTelemetryExternalProfileDiscovery externalProfileDiscovery) {
        _externalProfileDiscovery = externalProfileDiscovery ?? UsageTelemetryExternalProfileDiscovery.Default;
    }

    /// <inheritdoc />
    public string ProviderId => CopilotSessionImport.StableProviderId;

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> DiscoverRoots() {
        var candidates = new List<UsageTelemetryDiscoveredRootCandidate>();
        var environmentRoots = new List<string>();
        var hasEnvironmentOverride = AddEnvironmentRoots(environmentRoots, "COPILOT_HOME");
        hasEnvironmentOverride |= AddEnvironmentRoots(environmentRoots, "GITHUB_COPILOT_HOME");

        if (!hasEnvironmentOverride) {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile)) {
                candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                    UsageSourceKind.LocalLogs,
                    Path.Combine(userProfile, ".copilot")));
            }
        }

        foreach (var path in environmentRoots) {
            candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                UsageSourceKind.LocalLogs,
                path));
        }

        foreach (var profile in _externalProfileDiscovery.DiscoverProfiles()) {
            candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                profile.SourceKind,
                Path.Combine(profile.ProfilePath, ".copilot"),
                profile.PlatformHint,
                profile.MachineLabel));
        }

        return UsageTelemetryRootDiscoverySupport.BuildRoots(
            ProviderId,
            candidates.FindAll(static candidate => ContainsSessionState(candidate.Path)));
    }

    private static bool ContainsSessionState(string path) {
        if (Directory.Exists(Path.Combine(path, "session-state"))) {
            return true;
        }

        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(leaf, "session-state", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path);
    }

    private static bool AddEnvironmentRoots(ICollection<string> paths, string variableName) {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var added = false;
        foreach (var segment in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)) {
            var candidate = segment.Trim();
            if (!string.IsNullOrWhiteSpace(candidate)) {
                paths.Add(candidate);
                added = true;
            }
        }

        return added;
    }
}
