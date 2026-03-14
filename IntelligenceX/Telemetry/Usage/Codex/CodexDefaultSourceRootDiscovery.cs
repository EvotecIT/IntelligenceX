using System;
using System.Collections.Generic;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Telemetry.Usage.Codex;

/// <summary>
/// Discovers the default local Codex source root from the current machine.
/// </summary>
public sealed class CodexDefaultSourceRootDiscovery : IUsageTelemetryRootDiscovery {
    private readonly UsageTelemetryExternalProfileDiscovery _externalProfileDiscovery;

    /// <summary>
    /// Initializes a new discovery strategy for Codex local roots.
    /// </summary>
    public CodexDefaultSourceRootDiscovery()
        : this(UsageTelemetryExternalProfileDiscovery.Default) {
    }

    internal CodexDefaultSourceRootDiscovery(UsageTelemetryExternalProfileDiscovery externalProfileDiscovery) {
        _externalProfileDiscovery = externalProfileDiscovery ?? UsageTelemetryExternalProfileDiscovery.Default;
    }

    /// <inheritdoc />
    public string ProviderId => "codex";

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> DiscoverRoots() {
        var candidates = new List<UsageTelemetryDiscoveredRootCandidate>();

        var codexHome = CodexAuthStore.ResolveCodexHome();
        if (!string.IsNullOrWhiteSpace(codexHome)) {
            candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                UsageSourceKind.LocalLogs,
                codexHome));
        }

        foreach (var profile in _externalProfileDiscovery.DiscoverProfiles()) {
            candidates.Add(new UsageTelemetryDiscoveredRootCandidate(
                profile.SourceKind,
                System.IO.Path.Combine(profile.ProfilePath, ".codex"),
                profile.PlatformHint,
                profile.MachineLabel));
        }

        return UsageTelemetryRootDiscoverySupport.BuildRoots(ProviderId, candidates);
    }
}
