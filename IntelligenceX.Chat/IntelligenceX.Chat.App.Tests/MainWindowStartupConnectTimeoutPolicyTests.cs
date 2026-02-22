using System;
using System.Threading.Tasks;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests startup connect timeout policy selection for initial pipe probes.
/// </summary>
public sealed class MainWindowStartupConnectTimeoutPolicyTests {
    /// <summary>
    /// Ensures startup connect initial probe timeout is short on cold start,
    /// and remains conservative for user action or known-running sidecar reconnects.
    /// </summary>
    [Theory]
    [InlineData(true, true, 2000)]
    [InlineData(true, false, 2000)]
    [InlineData(false, true, 2000)]
    [InlineData(false, false, 100)]
    public void ResolveStartupInitialPipeConnectTimeout_ReturnsExpectedTimeout(
        bool fromUserAction,
        bool hasTrackedRunningServiceProcess,
        int expectedTimeoutMs) {
        var timeout = MainWindow.ResolveStartupInitialPipeConnectTimeout(fromUserAction, hasTrackedRunningServiceProcess);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedTimeoutMs), timeout);
    }

    /// <summary>
    /// Ensures startup initial connect settlement grace is only enabled for user action
    /// or known-running sidecar reconnect paths, and skipped for cold-start probes.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ShouldUseStartupInitialConnectSettlementGrace_ReturnsExpectedValue(
        bool fromUserAction,
        bool hasTrackedRunningServiceProcess,
        bool expected) {
        var shouldUseGrace = MainWindow.ShouldUseStartupInitialConnectSettlementGrace(fromUserAction, hasTrackedRunningServiceProcess);
        Assert.Equal(expected, shouldUseGrace);
    }

    /// <summary>
    /// Ensures startup-only connect budget is enabled exclusively for non-user startup flow connects.
    /// </summary>
    [Theory]
    [InlineData(true, true, null)]
    [InlineData(true, false, null)]
    [InlineData(false, false, null)]
    [InlineData(false, true, 6000)]
    public void ResolveStartupConnectBudget_ReturnsExpectedBudget(
        bool fromUserAction,
        bool captureStartupPhaseTelemetry,
        int? expectedTimeoutMs) {
        var timeout = MainWindow.ResolveStartupConnectBudget(fromUserAction, captureStartupPhaseTelemetry);
        var expected = expectedTimeoutMs.HasValue
            ? TimeSpan.FromMilliseconds(expectedTimeoutMs.Value)
            : (TimeSpan?)null;
        Assert.Equal(expected, timeout);
    }

    /// <summary>
    /// Ensures explicit connect-budget overrides are honored for dispatch connects,
    /// and non-positive overrides disable the budget.
    /// </summary>
    [Theory]
    [InlineData(8000, 8000)]
    [InlineData(0, null)]
    [InlineData(-10, null)]
    public void ResolveStartupConnectBudget_HonorsOverrideWhenProvided(
        int overrideBudgetMs,
        int? expectedTimeoutMs) {
        var timeout = MainWindow.ResolveStartupConnectBudget(
            fromUserAction: false,
            captureStartupPhaseTelemetry: false,
            overrideBudget: TimeSpan.FromMilliseconds(overrideBudgetMs));
        var expected = expectedTimeoutMs.HasValue
            ? TimeSpan.FromMilliseconds(expectedTimeoutMs.Value)
            : (TimeSpan?)null;
        Assert.Equal(expected, timeout);
    }

    /// <summary>
    /// Ensures startup webview wait budget is enabled only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, 4000)]
    public void ResolveStartupWebViewBudget_ReturnsExpectedBudget(
        bool captureStartupPhaseTelemetry,
        int? expectedTimeoutMs) {
        var timeout = MainWindow.ResolveStartupWebViewBudget(captureStartupPhaseTelemetry);
        var expected = expectedTimeoutMs.HasValue
            ? TimeSpan.FromMilliseconds(expectedTimeoutMs.Value)
            : (TimeSpan?)null;
        Assert.Equal(expected, timeout);
    }

    /// <summary>
    /// Ensures adaptive startup WebView budget remains conservative by default,
    /// tightens only after stable completions, and falls back with cooldown after exhaustion.
    /// </summary>
    [Theory]
    [InlineData(false, null, 0, 0, 0, null, null)]
    [InlineData(true, null, 0, 0, 0, null, 4000)]
    [InlineData(true, 900, 0, 1, 0, null, 4000)]
    [InlineData(true, 900, 0, 2, 0, null, 2200)]
    [InlineData(true, 900, 0, 2, 0, 2500, 2200)]
    [InlineData(true, 900, 0, 2, 0, 4000, 3700)]
    [InlineData(true, 900, 0, 3, 0, 3700, 3400)]
    [InlineData(true, 1600, 0, 2, 0, 4000, 3700)]
    [InlineData(true, 2600, 0, 2, 0, null, 3700)]
    [InlineData(true, 3050, 0, 2, 0, null, 4000)]
    [InlineData(true, 900, 1, 0, 2, 4000, 4000)]
    [InlineData(true, 900, 0, 3, 1, 4000, 4000)]
    public void ResolveStartupWebViewBudget_AdaptivePolicyIsHardwareSafe(
        bool captureStartupPhaseTelemetry,
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs,
        int? expectedTimeoutMs) {
        var timeout = MainWindow.ResolveStartupWebViewBudget(
            captureStartupPhaseTelemetry,
            lastEnsureWebViewMs,
            consecutiveBudgetExhaustions,
            consecutiveStableCompletions,
            adaptiveCooldownRunsRemaining,
            lastAppliedBudgetMs);
        var expected = expectedTimeoutMs.HasValue
            ? TimeSpan.FromMilliseconds(expectedTimeoutMs.Value)
            : (TimeSpan?)null;
        Assert.Equal(expected, timeout);
    }

    /// <summary>
    /// Ensures startup webview budget policy reasons stay stable for key adaptive/conservative branches.
    /// </summary>
    [Theory]
    [InlineData(false, null, 0, 0, 0, null, null)]
    [InlineData(true, 900, 0, 2, 2, 4000, "cooldown_conservative")]
    [InlineData(true, 900, 1, 0, 0, 4000, "exhaustion_conservative")]
    [InlineData(true, 900, 0, 0, 0, null, "insufficient_stability")]
    [InlineData(true, null, 0, 2, 0, null, "missing_last_ensure")]
    [InlineData(true, 3050, 0, 2, 0, 4000, "conservative_tier")]
    [InlineData(true, 900, 0, 2, 0, null, "fast_tier_new")]
    [InlineData(true, 900, 0, 2, 0, 2000, "fast_tier_nondecreasing")]
    [InlineData(true, 900, 0, 2, 0, 4000, "fast_tier_downshift_capped")]
    [InlineData(true, 900, 0, 2, 0, 2300, "fast_tier_downshift_full")]
    public void ResolveStartupWebViewBudgetReason_ReturnsExpectedReason(
        bool captureStartupPhaseTelemetry,
        int? lastEnsureWebViewMs,
        int consecutiveBudgetExhaustions,
        int consecutiveStableCompletions,
        int adaptiveCooldownRunsRemaining,
        int? lastAppliedBudgetMs,
        string? expectedReason) {
        var reason = MainWindow.ResolveStartupWebViewBudgetReason(
            captureStartupPhaseTelemetry,
            lastEnsureWebViewMs,
            consecutiveBudgetExhaustions,
            consecutiveStableCompletions,
            adaptiveCooldownRunsRemaining,
            lastAppliedBudgetMs);
        Assert.Equal(expectedReason, reason);
    }

    /// <summary>
    /// Ensures connect attempt timeout is capped by remaining startup budget, including exhaustion behavior.
    /// </summary>
    [Theory]
    [InlineData(2000, null, 0, true, 2000)]
    [InlineData(2000, 4000, 1000, true, 2000)]
    [InlineData(6000, 4000, 1000, true, 3000)]
    [InlineData(2000, 4000, 3900, true, 100)]
    [InlineData(2000, 4000, 3950, false, 0)]
    [InlineData(2000, 4000, 4000, false, 0)]
    public void TryResolveStartupConnectAttemptTimeout_UsesBudgetCap(
        int requestedTimeoutMs,
        int? budgetMs,
        int elapsedMs,
        bool expectedResolved,
        int expectedTimeoutMs) {
        var resolved = MainWindow.TryResolveStartupConnectAttemptTimeout(
            requestedTimeout: TimeSpan.FromMilliseconds(requestedTimeoutMs),
            startupConnectBudget: budgetMs.HasValue ? TimeSpan.FromMilliseconds(budgetMs.Value) : null,
            startupConnectElapsed: TimeSpan.FromMilliseconds(elapsedMs),
            timeout: out var timeout);

        Assert.Equal(expectedResolved, resolved);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedTimeoutMs), timeout);
    }

    /// <summary>
    /// Ensures connect attempt hard timeout keeps a small guardrail grace above requested timeout.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(150, 500)]
    [InlineData(2000, 2350)]
    public void ResolveConnectAttemptHardTimeout_ReturnsExpectedTimeout(
        int timeoutMs,
        int expectedHardTimeoutMs) {
        var hardTimeout = MainWindow.ResolveConnectAttemptHardTimeout(TimeSpan.FromMilliseconds(timeoutMs));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedHardTimeoutMs), hardTimeout);
    }

    /// <summary>
    /// Ensures dispatch cooldown applies only for non-priority paths without a tracked running sidecar.
    /// Priority login/queued-turn recovery should bypass cooldown and attempt reconnect immediately.
    /// </summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void ShouldApplyDispatchConnectFailureCooldown_ReturnsExpectedValue(
        bool hasTrackedRunningServiceProcess,
        bool prioritizeLatency,
        bool expected) {
        var shouldApply = MainWindow.ShouldApplyDispatchConnectFailureCooldown(
            hasTrackedRunningServiceProcess,
            prioritizeLatency);
        Assert.Equal(expected, shouldApply);
    }

    /// <summary>
    /// Ensures joined in-flight connect timeout probes are only attempted when
    /// callers actually joined an in-flight task and supplied a positive connect budget.
    /// </summary>
    [Theory]
    [InlineData(true, 8000, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, -1, false)]
    [InlineData(false, 8000, false)]
    public void ShouldProbeExistingClientAfterJoinedConnectTimeout_ReturnsExpectedValue(
        bool joinedExistingInFlight,
        int connectBudgetMs,
        bool expected) {
        var shouldProbe = MainWindow.ShouldProbeExistingClientAfterJoinedConnectTimeout(
            joinedExistingInFlight,
            TimeSpan.FromMilliseconds(connectBudgetMs));
        Assert.Equal(expected, shouldProbe);
    }

    /// <summary>
    /// Ensures post-cancel connect settlement treats near-boundary successful completion as success.
    /// </summary>
    [Fact]
    public async Task TryAwaitConnectTaskSettlementAsync_ReturnsTrue_WhenTaskCompletesWithinGrace() {
        var connectTask = Task.Delay(30);
        var settled = await MainWindow.TryAwaitConnectTaskSettlementAsync(connectTask, TimeSpan.FromMilliseconds(200));
        Assert.True(settled);
    }

    /// <summary>
    /// Ensures post-cancel connect settlement preserves original connect task failures.
    /// </summary>
    [Fact]
    public async Task TryAwaitConnectTaskSettlementAsync_PropagatesFaults() {
        var connectTask = Task.FromException(new InvalidOperationException("connect-fault"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await MainWindow.TryAwaitConnectTaskSettlementAsync(connectTask, TimeSpan.FromMilliseconds(200)));
        Assert.Equal("connect-fault", ex.Message);
    }

    /// <summary>
    /// Ensures post-cancel connect settlement reports timeout when task does not settle within grace.
    /// </summary>
    [Fact]
    public async Task TryAwaitConnectTaskSettlementAsync_ReturnsFalse_WhenTaskDoesNotCompleteWithinGrace() {
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = await MainWindow.TryAwaitConnectTaskSettlementAsync(pending.Task, TimeSpan.FromMilliseconds(50));
        Assert.False(settled);
    }

    /// <summary>
    /// Ensures startup cold-connect path skips settlement grace without awaiting or observing the connect task.
    /// </summary>
    [Fact]
    public async Task TryPreserveConnectCompletionAfterCancellationAsync_ReturnsFalseWithoutAwaiting_WhenSettlementDisabled() {
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingSettled = await MainWindow.TryPreserveConnectCompletionAfterCancellationAsync(
            pending.Task,
            allowSettlementGrace: false);

        Assert.False(pendingSettled);
        Assert.False(pending.Task.IsCompleted);

        var faultedSettled = await MainWindow.TryPreserveConnectCompletionAfterCancellationAsync(
            Task.FromException(new InvalidOperationException("connect-fault")),
            allowSettlementGrace: false);
        Assert.False(faultedSettled);
    }

    /// <summary>
    /// Ensures startup reconnect paths still preserve near-boundary connect completion when settlement grace is enabled.
    /// </summary>
    [Fact]
    public async Task TryPreserveConnectCompletionAfterCancellationAsync_ReturnsTrue_WhenSettlementEnabledAndTaskSettles() {
        var connectTask = Task.Delay(30);

        var settled = await MainWindow.TryPreserveConnectCompletionAfterCancellationAsync(
            connectTask,
            allowSettlementGrace: true);

        Assert.True(settled);
    }

    /// <summary>
    /// Ensures model/profile sync is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupModelProfileSync_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupModelProfileSync(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup hello probe is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupHelloProbe_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupHelloProbe(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup tool-catalog sync is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupToolCatalogSync_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupToolCatalogSync(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup auth refresh is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupAuthRefresh_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupAuthRefresh(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup webview post-init state sync is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupWebViewPostInitialization_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupWebViewPostInitialization(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup dispatch prewarm auth probe only runs for native transport
    /// when no authentication state is known and interactive login is not in progress.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void ShouldRunStartupDispatchAuthPrewarm_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool expected) {
        var shouldRun = MainWindow.ShouldRunStartupDispatchAuthPrewarm(
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress);
        Assert.Equal(expected, shouldRun);
    }

    /// <summary>
    /// Ensures dispatch auth probe can bypass network round-trips when an explicit
    /// unauthenticated snapshot is already cached and current state is unauthenticated.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void ShouldBypassDispatchAuthProbeForKnownUnauthenticatedState_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool hasExplicitUnauthenticatedProbeSnapshot,
        bool expected) {
        var shouldBypass = MainWindow.ShouldBypassDispatchAuthProbeForKnownUnauthenticatedState(
            requiresInteractiveSignIn,
            isAuthenticated,
            hasExplicitUnauthenticatedProbeSnapshot);
        Assert.Equal(expected, shouldBypass);
    }

    /// <summary>
    /// Ensures deferred startup metadata sync is skipped only for unauthenticated
    /// native sessions where deferred mode is enabled and interactive login is idle.
    /// </summary>
    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, false, false, true)]
    public void ShouldSkipDeferredStartupMetadataSyncForUnauthenticatedNative_ReturnsExpectedValue(
        bool deferStartupMetadataSync,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool expected) {
        var shouldSkip = MainWindow.ShouldSkipDeferredStartupMetadataSyncForUnauthenticatedNative(
            deferStartupMetadataSync,
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress);
        Assert.Equal(expected, shouldSkip);
    }

    /// <summary>
    /// Ensures tools-loading indicator is shown only while startup metadata is actively pending.
    /// </summary>
    [Theory]
    [InlineData(false, false, 1, true, false)]
    [InlineData(true, true, 1, true, false)]
    [InlineData(true, false, 1, false, true)]
    [InlineData(true, false, 2, true, true)]
    [InlineData(true, false, 2, false, false)]
    public void ShouldShowToolsLoading_ReturnsExpectedValue(
        bool isConnected,
        bool hasSessionPolicy,
        int startupFlowState,
        bool startupMetadataSyncQueued,
        bool expected) {
        var shouldShow = MainWindow.ShouldShowToolsLoading(
            isConnected,
            hasSessionPolicy,
            startupFlowState,
            startupMetadataSyncQueued);
        Assert.Equal(expected, shouldShow);
    }

    /// <summary>
    /// Ensures startup dispatch prewarm system summary includes stable timing details.
    /// </summary>
    [Theory]
    [InlineData(420, false, null, false, null, "Startup prewarm ready: runtime connected in 420ms (auth check deferred).")]
    [InlineData(380, true, true, false, 95, "Startup prewarm ready: runtime connected in 380ms; account verified in 95ms.")]
    [InlineData(510, true, false, false, 1200, "Startup prewarm ready: runtime connected in 510ms; sign-in still required (checked in 1200ms).")]
    [InlineData(0, true, false, false, 1207, "Startup prewarm ready: runtime already connected; sign-in still required (checked in 1207ms).")]
    [InlineData(0, true, null, true, 700, "Startup prewarm ready: runtime already connected; auth check inconclusive after 700ms (will verify on first message).")]
    public void BuildStartupDispatchPrewarmSummary_ReturnsExpectedText(
        long connectMs,
        bool authProbeAttempted,
        bool? authProbeAuthenticated,
        bool authProbeInconclusive,
        int? authProbeMs,
        string expected) {
        var summary = MainWindow.BuildStartupDispatchPrewarmSummary(
            connectMs,
            authProbeAttempted,
            authProbeAuthenticated,
            authProbeInconclusive,
            authProbeMs.HasValue ? (long?)authProbeMs.Value : null);
        Assert.Equal(expected, summary);
    }
}
