using IntelligenceX.Utils;

namespace IntelligenceX.PowerShell;

public sealed class HealthReportRecord {
    public HealthReportRecord(HealthCheckResult? openAi, HealthCheckResult? copilot) {
        OpenAI = openAi;
        Copilot = copilot;
    }

    public HealthCheckResult? OpenAI { get; }
    public HealthCheckResult? Copilot { get; }
}
