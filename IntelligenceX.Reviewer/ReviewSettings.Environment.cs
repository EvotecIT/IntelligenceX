namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal static void ApplyEnvironment(ReviewSettings settings) {
        settings.RebaseToAgentProfileBaseline();
        ApplyEnvironmentCoreReviewSettings(settings);
        ApplyEnvironmentUsageAndSummarySettings(settings);
        ApplyEnvironmentScopeAndPromptSettings(settings);
        ApplyEnvironmentHistorySettings(settings);
        ApplyEnvironmentCiContextSettings(settings);
        ApplyEnvironmentSwarmSettings(settings);
        ApplyEnvironmentRetryAndDiagnosticsSettings(settings);
        ApplyEnvironmentCopilotAndAzureSettings(settings);
        ApplyEnvironmentCommentsAndCleanupSettings(settings);
        ApplyEnvironmentAgentProfileSettings(settings);
        if (!string.IsNullOrWhiteSpace(settings.AgentProfile) || settings.AgentProfileBaseline is not null) {
            settings.RefreshAgentProfileBaseline();
        }
        settings.ApplySelectedAgentProfile();
    }
}
