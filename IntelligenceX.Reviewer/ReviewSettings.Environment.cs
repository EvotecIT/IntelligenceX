namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal static void ApplyEnvironment(ReviewSettings settings) {
        ApplyEnvironmentCoreReviewSettings(settings);
        ApplyEnvironmentUsageAndSummarySettings(settings);
        ApplyEnvironmentScopeAndPromptSettings(settings);
        ApplyEnvironmentCiContextSettings(settings);
        ApplyEnvironmentSwarmSettings(settings);
        ApplyEnvironmentRetryAndDiagnosticsSettings(settings);
        ApplyEnvironmentCopilotAndAzureSettings(settings);
        ApplyEnvironmentCommentsAndCleanupSettings(settings);
    }
}
