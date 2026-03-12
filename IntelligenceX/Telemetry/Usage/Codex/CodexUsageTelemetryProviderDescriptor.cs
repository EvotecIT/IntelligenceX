using System;
using System.Collections.Generic;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Usage.Codex;

/// <summary>
/// Provider descriptor for Codex usage telemetry.
/// </summary>
public sealed class CodexUsageTelemetryProviderDescriptor : IUsageTelemetryProviderDescriptor {
    private static readonly IReadOnlyList<IUsageTelemetryAdapter> Adapters =
        new IUsageTelemetryAdapter[] { new CodexSessionUsageAdapter() };

    /// <inheritdoc />
    public string ProviderId => "codex";

    /// <inheritdoc />
    public IReadOnlyList<IUsageTelemetryAdapter> CreateAdapters() {
        return Adapters;
    }
}
