using System.Collections.Generic;

namespace IntelligenceX.Telemetry.Usage.Copilot;

/// <summary>
/// Provider descriptor for GitHub Copilot local session telemetry.
/// </summary>
public sealed class CopilotUsageTelemetryProviderDescriptor : IUsageTelemetryProviderDescriptor {
    private static readonly IReadOnlyList<IUsageTelemetryAdapter> Adapters =
        new IUsageTelemetryAdapter[] { new CopilotSessionUsageAdapter() };

    /// <inheritdoc />
    public string ProviderId => CopilotSessionImport.StableProviderId;

    /// <inheritdoc />
    public IReadOnlyList<IUsageTelemetryAdapter> CreateAdapters() {
        return Adapters;
    }
}
