using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage.LmStudio;

/// <summary>
/// Discovers the default local LM Studio source root from the current machine.
/// </summary>
public sealed class LmStudioDefaultSourceRootDiscovery : IUsageTelemetryRootDiscovery {
    private readonly UsageTelemetryExternalProfileDiscovery _externalProfileDiscovery;

    /// <summary>
    /// Initializes a new discovery strategy for LM Studio local roots.
    /// </summary>
    public LmStudioDefaultSourceRootDiscovery()
        : this(UsageTelemetryExternalProfileDiscovery.Default) {
    }

    internal LmStudioDefaultSourceRootDiscovery(UsageTelemetryExternalProfileDiscovery externalProfileDiscovery) {
        _externalProfileDiscovery = externalProfileDiscovery ?? UsageTelemetryExternalProfileDiscovery.Default;
    }

    /// <inheritdoc />
    public string ProviderId => LmStudioConversationImport.StableProviderId;

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> DiscoverRoots() {
        var candidates = new List<UsageTelemetryDiscoveredRootCandidate>();
        var environmentRoots = new List<string>();
        var hasEnvironmentOverride = AddEnvironmentRoots(environmentRoots, "LMSTUDIO_HOME");
        hasEnvironmentOverride |= AddEnvironmentRoots(environmentRoots, "LM_STUDIO_HOME");

        if (!hasEnvironmentOverride) {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile)) {
                candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                    UsageSourceKind.LocalLogs,
                    Path.Combine(userProfile, ".lmstudio")));
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
                Path.Combine(profile.ProfilePath, ".lmstudio"),
                profile.PlatformHint,
                profile.MachineLabel));
        }

        return UsageTelemetryRootDiscoverySupport.BuildRoots(
            ProviderId,
            candidates.Where(static candidate => ContainsConversations(candidate.Path)));
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

    private static bool ContainsConversations(string path) {
        if (Directory.Exists(Path.Combine(path, "conversations"))) {
            return true;
        }

        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(leaf, "conversations", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path);
    }
}
