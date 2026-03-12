using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Telemetry.Usage.Codex;

/// <summary>
/// Discovers the default local Codex source root from the current machine.
/// </summary>
public sealed class CodexDefaultSourceRootDiscovery : IUsageTelemetryRootDiscovery {
    /// <inheritdoc />
    public string ProviderId => "codex";

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> DiscoverRoots() {
        var codexHome = CodexAuthStore.ResolveCodexHome();
        if (string.IsNullOrWhiteSpace(codexHome) || !Directory.Exists(codexHome)) {
            return Array.Empty<SourceRootRecord>();
        }

        var root = new SourceRootRecord(
            SourceRootRecord.CreateStableId(ProviderId, UsageSourceKind.LocalLogs, codexHome),
            ProviderId,
            UsageSourceKind.LocalLogs,
            codexHome);
        return new[] { root };
    }
}
