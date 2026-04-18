using IntelligenceX.OpenAI.Native;
using IntelligenceX.Copilot;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    // Test-only forwarders: keep connectivity behavior tests compile-time bound without private reflection.

    /// <summary>Test-only forwarder for native connectivity preflight.</summary>
    internal Task PreflightNativeConnectivityForTestsAsync(OpenAINativeOptions options, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        PreflightNativeConnectivityAsync(options, timeout, cancellationToken);

    /// <summary>Test-only forwarder for preflight exception mapping.</summary>
    internal static Exception? MapPreflightConnectivityExceptionForTests(HttpRequestException ex, string host,
        TimeSpan timeout, bool cancellationRequested) =>
        MapPreflightConnectivityException(ex, host, timeout, cancellationRequested);

    /// <summary>Test-only forwarder for swarm shadow plan resolution.</summary>
    internal static ReviewSwarmShadowPlan BuildSwarmShadowPlanForTests(ReviewSettings settings) =>
        ReviewSwarmShadowPlanner.Build(settings);

    /// <summary>Test-only forwarder for Copilot CLI option resolution.</summary>
    internal CopilotClientOptions BuildCopilotClientOptionsForTests() =>
        BuildCopilotClientOptions();

    /// <summary>Test-only forwarder for swarm shadow plan rendering.</summary>
    internal static string RenderSwarmShadowPlanForTests(ReviewSwarmShadowPlan plan) =>
        ReviewSwarmShadowPlanner.Render(plan);

    /// <summary>Test-only forwarder for swarm reviewer prompt shaping.</summary>
    internal static string BuildSwarmShadowReviewerPromptForTests(string basePrompt,
        ReviewSwarmShadowReviewerPlan reviewer) =>
        ReviewSwarmShadowRunner.BuildReviewerPrompt(basePrompt, reviewer);

    /// <summary>Test-only forwarder for swarm shadow execution with a fake model executor.</summary>
    internal static Task<ReviewSwarmShadowRunResult> RunSwarmShadowForTestsAsync(ReviewSettings settings,
        ReviewSwarmShadowPlan plan, string basePrompt,
        Func<ReviewSwarmShadowReviewerPlan, string, CancellationToken, Task<string>> executeReviewerAsync,
        CancellationToken cancellationToken = default) =>
        ReviewSwarmShadowRunner.RunAsync(settings, plan, basePrompt, executeReviewerAsync,
            (_, _, _) => Task.FromResult("## Summary 📝\nAggregated shadow result."), cancellationToken);

    /// <summary>Test-only forwarder for swarm shadow execution with fake reviewer and aggregator executors.</summary>
    internal static Task<ReviewSwarmShadowRunResult> RunSwarmShadowForTestsAsync(ReviewSettings settings,
        ReviewSwarmShadowPlan plan, string basePrompt,
        Func<ReviewSwarmShadowReviewerPlan, string, CancellationToken, Task<string>> executeReviewerAsync,
        Func<ReviewSwarmShadowAggregatorPlan, string, CancellationToken, Task<string>> executeAggregatorAsync,
        CancellationToken cancellationToken = default) =>
        ReviewSwarmShadowRunner.RunAsync(settings, plan, basePrompt, executeReviewerAsync, executeAggregatorAsync,
            cancellationToken);

    /// <summary>Test-only forwarder for swarm aggregator prompt shaping.</summary>
    internal static string BuildSwarmShadowAggregatorPromptForTests(string basePrompt, ReviewSwarmShadowPlan plan,
        IReadOnlyList<ReviewSwarmShadowReviewerResult> results) =>
        ReviewSwarmShadowRunner.BuildAggregatorPrompt(basePrompt, plan, results);

    /// <summary>Test-only forwarder for swarm shadow JSON artifact rendering.</summary>
    internal static string BuildSwarmShadowArtifactJsonForTests(PullRequestContext context, ReviewSwarmShadowPlan plan,
        ReviewSwarmShadowRunResult result) =>
        ReviewSwarmShadowArtifacts.BuildJson(context, plan, result);

    /// <summary>Test-only forwarder for swarm shadow Markdown artifact rendering.</summary>
    internal static string BuildSwarmShadowArtifactMarkdownForTests(PullRequestContext context, ReviewSwarmShadowPlan plan,
        ReviewSwarmShadowRunResult result) =>
        ReviewSwarmShadowArtifacts.BuildMarkdown(context, plan, result);

    /// <summary>Test-only forwarder for swarm shadow metrics rendering.</summary>
    internal static string BuildSwarmShadowMetricsJsonLineForTests(PullRequestContext context,
        ReviewSwarmShadowRunResult result) =>
        ReviewSwarmShadowArtifacts.BuildMetricsJsonLine(context, result);

    /// <summary>Test-only forwarder for review history JSON artifact rendering.</summary>
    internal static string BuildReviewHistoryArtifactJsonForTests(PullRequestContext context,
        ReviewHistorySnapshot snapshot, string renderedPromptSection) =>
        ReviewHistoryArtifacts.BuildJson(context, snapshot, renderedPromptSection);

    /// <summary>Test-only forwarder for review history Markdown artifact rendering.</summary>
    internal static string BuildReviewHistoryArtifactMarkdownForTests(PullRequestContext context,
        ReviewHistorySnapshot snapshot, string renderedPromptSection) =>
        ReviewHistoryArtifacts.BuildMarkdown(context, snapshot, renderedPromptSection);

    /// <summary>Test-only forwarder for swarm shadow result diagnostics.</summary>
    internal static string RenderSwarmShadowResultsForTests(ReviewSwarmShadowRunResult result) =>
        ReviewSwarmShadowRunner.RenderResultSummary(result);
}
