using System.Collections.Generic;

namespace IntelligenceX.Telemetry.Usage.Claude;

/// <summary>
/// Provider descriptor for Claude usage telemetry.
/// </summary>
public sealed class ClaudeUsageTelemetryProviderDescriptor : IUsageTelemetryProviderDescriptor {
    private static readonly IReadOnlyList<IUsageTelemetryAdapter> Adapters =
        new IUsageTelemetryAdapter[] { new ClaudeSessionUsageAdapter() };

    /// <inheritdoc />
    public string ProviderId => "claude";

    /// <inheritdoc />
    public IReadOnlyList<IUsageTelemetryAdapter> CreateAdapters() {
        return Adapters;
    }
}
