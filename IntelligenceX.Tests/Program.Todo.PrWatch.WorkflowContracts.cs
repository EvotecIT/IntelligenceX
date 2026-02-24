namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestPrWatchMonitorWorkflowReviewTriggersIncludeSubmittedAndEdited() {
        var workflowPath = Path.Combine(".github", "workflows", "ix-pr-babysit-monitor.yml");
        var content = File.ReadAllText(workflowPath);

        AssertContainsText(content, "pull_request_review:", "monitor workflow defines pull_request_review trigger");
        AssertContainsText(content, "- submitted", "monitor workflow includes submitted review trigger");
        AssertContainsText(content, "- edited", "monitor workflow includes edited review trigger");
    }

    private static void TestPrWatchMonitorWorkflowExcludesReviewCommentTrigger() {
        var workflowPath = Path.Combine(".github", "workflows", "ix-pr-babysit-monitor.yml");
        var content = File.ReadAllText(workflowPath);

        AssertEqual(false, content.Contains("pull_request_review_comment:", StringComparison.Ordinal),
            "monitor workflow should not define pull_request_review_comment trigger");
    }
#endif
}
