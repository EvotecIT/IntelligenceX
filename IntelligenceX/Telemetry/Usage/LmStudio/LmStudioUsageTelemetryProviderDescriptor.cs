using System.Collections.Generic;

namespace IntelligenceX.Telemetry.Usage.LmStudio;

/// <summary>
/// Provider descriptor for LM Studio usage telemetry.
/// </summary>
public sealed class LmStudioUsageTelemetryProviderDescriptor : IUsageTelemetryProviderDescriptor {
    private static readonly IReadOnlyList<IUsageTelemetryAdapter> Adapters =
        new IUsageTelemetryAdapter[] { new LmStudioConversationUsageAdapter() };

    /// <inheritdoc />
    public string ProviderId => LmStudioConversationImport.StableProviderId;

    /// <inheritdoc />
    public IReadOnlyList<IUsageTelemetryAdapter> CreateAdapters() {
        return Adapters;
    }
}
