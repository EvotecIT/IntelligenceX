using IntelligenceX.Utils;

namespace IntelligenceX.PowerShell;

/// <summary>
/// Represents a combined health report for OpenAI and Copilot providers.
/// </summary>
public sealed class HealthReportRecord {
    /// <summary>
    /// Initializes a new health report record.
    /// </summary>
    /// <param name="openAi">OpenAI health status.</param>
    /// <param name="copilot">Copilot health status.</param>
    public HealthReportRecord(HealthCheckResult? openAi, HealthCheckResult? copilot) {
        OpenAI = openAi;
        Copilot = copilot;
    }

    /// <summary>
    /// Gets the OpenAI health status.
    /// </summary>
    public HealthCheckResult? OpenAI { get; }
    /// <summary>
    /// Gets the Copilot health status.
    /// </summary>
    public HealthCheckResult? Copilot { get; }
}
