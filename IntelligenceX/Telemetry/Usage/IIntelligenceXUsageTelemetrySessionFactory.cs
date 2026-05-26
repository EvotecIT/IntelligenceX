using System;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Creates optional persistent usage telemetry sessions for <see cref="IntelligenceXClient"/>.
/// </summary>
public interface IIntelligenceXUsageTelemetrySessionFactory {
    /// <summary>
    /// Creates and attaches a usage telemetry session when persistence is enabled.
    /// </summary>
    /// <param name="client">The connected IntelligenceX client.</param>
    /// <param name="options">The client options that requested telemetry.</param>
    /// <returns>A disposable telemetry session, or <see langword="null"/> when persistence is disabled.</returns>
    IDisposable? TryCreate(IntelligenceXClient client, IntelligenceXClientOptions options);
}
