using System;
using System.IO;
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
    /// Ensures cold-start startup flow skips the guaranteed-failing short initial pipe probe
    /// and proceeds directly to service launch + retry connect.
    /// </summary>
    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    public void ShouldSkipStartupInitialPipeConnectProbe_ReturnsExpectedValue(
        bool fromUserAction,
        bool captureStartupPhaseTelemetry,
        bool hasTrackedRunningServiceProcess,
        bool expected) {
        var shouldSkip = MainWindow.ShouldSkipStartupInitialPipeConnectProbe(
            fromUserAction: fromUserAction,
            captureStartupPhaseTelemetry: captureStartupPhaseTelemetry,
            hasTrackedRunningServiceProcess: hasTrackedRunningServiceProcess);
        Assert.Equal(expected, shouldSkip);
    }

    /// <summary>
    /// Ensures startup-only sidecar recovery attempt runs for transient/disconnect failures
    /// and is disabled for user-action connects or when recovery budget is exhausted.
    /// </summary>
    [Theory]
    [InlineData(false, true, "timeout", false, 0, 1, true)]
    [InlineData(false, true, "cancel", false, 0, 1, true)]
    [InlineData(false, true, "disconnect", false, 0, 1, true)]
    [InlineData(false, true, "other", true, 0, 1, true)]
    [InlineData(false, true, "other", false, 0, 1, false)]
    [InlineData(true, true, "timeout", true, 0, 1, false)]
    [InlineData(false, false, "timeout", true, 0, 1, false)]
    [InlineData(false, true, "timeout", true, 1, 1, false)]
    [InlineData(false, true, "timeout", true, 0, 0, false)]
    public void ShouldAttemptStartupConnectRecoveryAfterRetryFailure_ReturnsExpectedValue(
        bool fromUserAction,
        bool captureStartupPhaseTelemetry,
        string exceptionKind,
        bool serviceProcessExited,
        int recoveryAttemptsConsumed,
        int recoveryAttemptLimit,
        bool expected) {
        Exception? exception = exceptionKind switch {
            "timeout" => new TimeoutException("timed out"),
            "cancel" => new OperationCanceledException("canceled"),
            "disconnect" => new IOException("Disconnected from service."),
            "other" => new InvalidOperationException("some other failure"),
            _ => null
        };

        var shouldAttempt = MainWindow.ShouldAttemptStartupConnectRecoveryAfterRetryFailure(
            fromUserAction: fromUserAction,
            captureStartupPhaseTelemetry: captureStartupPhaseTelemetry,
            connectException: exception,
            serviceProcessExited: serviceProcessExited,
            recoveryAttemptsConsumed: recoveryAttemptsConsumed,
            recoveryAttemptLimit: recoveryAttemptLimit);
        Assert.Equal(expected, shouldAttempt);
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
    /// Ensures connect attempt status text includes phase, attempt ordinal, and timeout labels.
    /// </summary>
    [Theory]
    [InlineData("connecting to service", 1, 3, 900, "Starting runtime... (connecting to service, phase startup_connect, attempt 1/3, timeout 900ms)")]
    [InlineData("retrying service connection", 2, 4, 2100, "Starting runtime... (retrying service connection, phase startup_connect, attempt 2/4, timeout 2.1s)")]
    public void BuildStartupConnectAttemptStatusText_FormatsExpectedText(
        string phaseLabel,
        int attemptNumber,
        int totalAttempts,
        int timeoutMs,
        string expected) {
        var text = MainWindow.BuildStartupConnectAttemptStatusText(
            phaseLabel,
            attemptNumber,
            totalAttempts,
            TimeSpan.FromMilliseconds(timeoutMs));

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Ensures retry-delay status text reports wait duration and next attempt ordinal.
    /// </summary>
    [Theory]
    [InlineData(2, 4, 300, "Starting runtime... (waiting 300ms, phase startup_connect before retry 2/4)")]
    [InlineData(5, 4, 1250, "Starting runtime... (waiting 1.3s, phase startup_connect before retry 5/5)")]
    public void BuildStartupConnectRetryDelayStatusText_FormatsExpectedText(
        int nextAttemptNumber,
        int totalAttempts,
        int delayMs,
        string expected) {
        var text = MainWindow.BuildStartupConnectRetryDelayStatusText(
            nextAttemptNumber,
            totalAttempts,
            TimeSpan.FromMilliseconds(delayMs));

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Ensures startup retry attempt ordinals include the initial cold-connect attempt.
    /// </summary>
    [Theory]
    [InlineData(-10, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(4, 6)]
    public void ResolveStartupConnectRetryDisplayAttemptNumber_ReturnsExpectedValue(
        int retryAttemptIndex,
        int expectedDisplayAttemptNumber) {
        var displayAttemptNumber = MainWindow.ResolveStartupConnectRetryDisplayAttemptNumber(retryAttemptIndex);
        Assert.Equal(expectedDisplayAttemptNumber, displayAttemptNumber);
    }

    /// <summary>
    /// Ensures startup retry total attempts include the initial cold-connect attempt.
    /// </summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(3, 4)]
    public void ResolveStartupConnectRetryDisplayTotalAttempts_ReturnsExpectedValue(
        int retryAttemptSlots,
        int expectedDisplayTotalAttempts) {
        var displayTotalAttempts = MainWindow.ResolveStartupConnectRetryDisplayTotalAttempts(retryAttemptSlots);
        Assert.Equal(expectedDisplayTotalAttempts, displayTotalAttempts);
    }

    /// <summary>
    /// Ensures startup retry progress and delay statuses stay aligned across loop iterations.
    /// </summary>
    [Fact]
    public void StartupRetryStatusText_UsesAttemptOrdinalsIncludingInitialConnect() {
        var totalAttempts = MainWindow.ResolveStartupConnectRetryDisplayTotalAttempts(3);
        var firstRetryAttempt = MainWindow.ResolveStartupConnectRetryDisplayAttemptNumber(0);
        var secondRetryAttempt = MainWindow.ResolveStartupConnectRetryDisplayAttemptNumber(1);

        var retryProgressText = MainWindow.BuildStartupConnectAttemptStatusText(
            "retrying service connection",
            firstRetryAttempt,
            totalAttempts,
            TimeSpan.FromMilliseconds(900));
        var retryDelayText = MainWindow.BuildStartupConnectRetryDelayStatusText(
            nextAttemptNumber: firstRetryAttempt + 1,
            totalAttempts,
            TimeSpan.FromMilliseconds(300));

        Assert.Equal(3, secondRetryAttempt);
        Assert.Equal("Starting runtime... (retrying service connection, phase startup_connect, attempt 2/4, timeout 900ms)", retryProgressText);
        Assert.Equal("Starting runtime... (waiting 300ms, phase startup_connect before retry 3/4)", retryDelayText);
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
    /// when no authentication state is known, interactive login is not in progress,
    /// and startup metadata sync is not already queued/active.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(true, false, false, false, false, true)]
    public void ShouldRunStartupDispatchAuthPrewarm_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress,
        bool expected) {
        var shouldRun = MainWindow.ShouldRunStartupDispatchAuthPrewarm(
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress,
            startupMetadataSyncQueued,
            startupMetadataSyncInProgress);
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
    /// native sessions where deferred mode is enabled and an interactive login flow is not already active.
    /// </summary>
    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, false, false, false)]
    public void ShouldDelayDeferredStartupMetadataSyncForInteractiveSignIn_ReturnsExpectedValue(
        bool deferStartupMetadataSync,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool expected) {
        var shouldSkip = MainWindow.ShouldDelayDeferredStartupMetadataSyncForInteractiveSignIn(
            deferStartupMetadataSync,
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress);
        Assert.Equal(expected, shouldSkip);
    }

    /// <summary>
    /// Ensures deferred startup metadata sync skips inline auth-refresh when the dedicated
    /// deferred startup auth phase is already queued, avoiding duplicated startup auth probes.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void ShouldRunDeferredStartupMetadataInlineAuthRefresh_ReturnsExpectedValue(
        bool startupAuthDeferredQueued,
        bool shutdownRequested,
        bool expected) {
        var shouldRun = MainWindow.ShouldRunDeferredStartupMetadataInlineAuthRefresh(
            startupAuthDeferredQueued: startupAuthDeferredQueued,
            shutdownRequested: shutdownRequested);
        Assert.Equal(expected, shouldRun);
    }

    /// <summary>
    /// Ensures deferred startup metadata plan always queues metadata sync when deferred,
    /// and only skips deferred metadata while interactive sign-in is pending.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, true, false, false, false, false, true, true, false, true, true)]
    [InlineData(false, true, false, false, false, true, true, true, true, true, false, true, true)]
    [InlineData(false, false, false, false, false, false, false, false, false, false, false, false, false)]
    public void ResolveDeferredStartupMetadataPlan_ReturnsExpectedValue(
        bool deferPostConnectMetadataSync,
        bool deferStartupHelloProbe,
        bool deferStartupToolCatalogSync,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool deferStartupAuthRefresh,
        bool deferStartupModelProfileSync,
        bool expectedDeferStartupMetadataSync,
        bool expectedQueueDeferredConnectMetadataSync,
        bool expectedSkipDeferredMetadataUntilAuthenticated,
        bool expectedDeferAuthRefresh,
        bool expectedDeferModelProfileSync) {
        var plan = MainWindow.ResolveDeferredStartupMetadataPlan(
            deferPostConnectMetadataSync: deferPostConnectMetadataSync,
            deferStartupHelloProbe: deferStartupHelloProbe,
            deferStartupToolCatalogSync: deferStartupToolCatalogSync,
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            isAuthenticated: isAuthenticated,
            loginInProgress: loginInProgress,
            deferStartupAuthRefresh: deferStartupAuthRefresh,
            deferStartupModelProfileSync: deferStartupModelProfileSync);

        Assert.Equal(expectedDeferStartupMetadataSync, plan.DeferStartupMetadataSync);
        Assert.Equal(expectedQueueDeferredConnectMetadataSync, plan.QueueDeferredConnectMetadataSync);
        Assert.Equal(expectedSkipDeferredMetadataUntilAuthenticated, plan.SkipDeferredMetadataUntilAuthenticated);
        Assert.Equal(expectedDeferAuthRefresh, plan.DeferAuthRefresh);
        Assert.Equal(expectedDeferModelProfileSync, plan.DeferModelProfileSync);
    }

    /// <summary>
    /// Ensures plugin launch path resolution handles root source directories safely
    /// and never emits relative fallback paths.
    /// </summary>
    [Fact]
    public void ResolveServiceLaunchPluginPaths_RootSourceDirectory_DoesNotEmitRelativePaths() {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        Assert.False(string.IsNullOrWhiteSpace(root));

        var paths = MainWindow.ResolveServiceLaunchPluginPaths(root!);
        Assert.All(paths, static path => Assert.True(Path.IsPathRooted(path)));
        Assert.DoesNotContain(paths, static path => string.Equals(path.Trim(), "plugins", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures built-in tool probe path resolution uses absolute directories only and never emits a relative tools fallback for root sources.
    /// </summary>
    [Fact]
    public void ResolveServiceLaunchBuiltInToolProbePaths_RootSourceDirectory_DoesNotEmitRelativeFallbacks() {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        Assert.False(string.IsNullOrWhiteSpace(root));

        var paths = MainWindow.ResolveServiceLaunchBuiltInToolProbePaths(root!);
        Assert.All(paths, static path => Assert.True(Path.IsPathRooted(path)));
        Assert.DoesNotContain(paths, static path => string.Equals(path.Trim(), "tools", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures workspace output probing stays opt-in once explicit launch probe paths exist.
    /// </summary>
    [Fact]
    public void ShouldEnableWorkspaceBuiltInToolOutputProbing_WithExplicitProbePaths_ReturnsFalse() {
        var shouldEnable = MainWindow.ShouldEnableWorkspaceBuiltInToolOutputProbing(new[] { @"C:\service", @"C:\service\tools" });
        Assert.False(shouldEnable);
    }

    /// <summary>
    /// Ensures workspace output probing remains available as a fallback when no explicit probe paths are provided.
    /// </summary>
    [Fact]
    public void ShouldEnableWorkspaceBuiltInToolOutputProbing_WithoutProbePaths_ReturnsTrue() {
        Assert.True(MainWindow.ShouldEnableWorkspaceBuiltInToolOutputProbing(Array.Empty<string>()));
        Assert.True(MainWindow.ShouldEnableWorkspaceBuiltInToolOutputProbing(null!));
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
    /// Ensures startup pending status text is explicit about sign-in gating vs generic metadata sync.
    /// </summary>
    [Theory]
    [InlineData(true, false, "Runtime connected. Sign in to finish loading tool packs... (phase startup_auth_wait, cause auth_wait)")]
    [InlineData(true, true, "Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)")]
    [InlineData(false, false, "Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)")]
    public void BuildStartupPendingStatusText_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        string expected) {
        var text = MainWindow.BuildStartupPendingStatusText(
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress: false);
        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Ensures startup pending status text uses explicit browser-completion copy while sign-in is in progress.
    /// </summary>
    [Fact]
    public void BuildStartupPendingStatusText_UsesBrowserContinuationCopy_WhenLoginInProgress() {
        var text = MainWindow.BuildStartupPendingStatusText(
            requiresInteractiveSignIn: true,
            isAuthenticated: false,
            loginInProgress: true);
        Assert.Equal(
            "Runtime connected. Finish sign-in in browser to continue loading tool packs... (phase startup_auth_wait, cause auth_wait)",
            text);
    }

    /// <summary>
    /// Ensures metadata-sync recovery copy keeps runtime readiness clear and avoids pinning startup phase context.
    /// </summary>
    [Theory]
    [InlineData(true, "Runtime is ready. Retrying tool metadata sync in background...")]
    [InlineData(false, "Runtime is ready. Tool metadata sync is degraded; some tools may be unavailable.")]
    public void BuildStartupMetadataSyncRecoveryStatusText_ReturnsExpectedValue(
        bool retryQueued,
        string expected) {
        var text = MainWindow.BuildStartupMetadataSyncRecoveryStatusText(retryQueued);

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Ensures deferred startup metadata sync waits for authenticated runtime state only when
    /// the active transport requires interactive sign-in and browser login is currently in progress.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    public void ShouldWaitForAuthenticationBeforeDeferredStartupMetadataSync_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool expected) {
        var shouldWait = MainWindow.ShouldWaitForAuthenticationBeforeDeferredStartupMetadataSync(
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress);
        Assert.Equal(expected, shouldWait);
    }

    /// <summary>
    /// Ensures startup auth-gate waiting state is retained only for explicit auth-wait exits
    /// and cleared for all non-auth-wait or settled-auth exit paths.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, false, true, true)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(false, false, true, false, true, false)]
    [InlineData(true, true, true, false, true, false)]
    public void ShouldKeepStartupAuthGateWaitingOnDeferredMetadataSyncExit_ReturnsExpectedValue(
        bool exitedForAuthWait,
        bool shutdownRequested,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool expected) {
        var shouldKeep = MainWindow.ShouldKeepStartupAuthGateWaitingOnDeferredMetadataSyncExit(
            exitedForAuthWait: exitedForAuthWait,
            shutdownRequested: shutdownRequested,
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            isAuthenticated: isAuthenticated,
            loginInProgress: loginInProgress);
        Assert.Equal(expected, shouldKeep);
    }

    /// <summary>
    /// Ensures post-login deferred metadata sync scheduling is queued once per login-success cycle.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ShouldQueueDeferredStartupMetadataSyncAfterLoginSuccess_ReturnsExpectedValue(
        bool shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync,
        bool loginSuccessMetadataSyncAlreadyQueued,
        bool expected) {
        var shouldQueue = MainWindow.ShouldQueueDeferredStartupMetadataSyncAfterLoginSuccess(
            shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync,
            loginSuccessMetadataSyncAlreadyQueued);
        Assert.Equal(expected, shouldQueue);
    }

    /// <summary>
    /// Ensures post-authentication startup metadata sync only queues when runtime is connected,
    /// sign-in is settled, and startup metadata is still missing.
    /// </summary>
    [Theory]
    [InlineData(false, true, true, false, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, false, false, false, true)]
    [InlineData(true, true, true, false, false, true)]
    public void ShouldQueueDeferredStartupMetadataSyncAfterAuthenticationReady_ReturnsExpectedValue(
        bool isConnected,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasSessionPolicy,
        bool expected) {
        var shouldQueue = MainWindow.ShouldQueueDeferredStartupMetadataSyncAfterAuthenticationReady(
            isConnected,
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress,
            hasSessionPolicy);
        Assert.Equal(expected, shouldQueue);
    }

    /// <summary>
    /// Ensures deferred startup model/profile sync waits while startup metadata sync is queued or active.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ShouldDelayStartupModelProfileSyncUntilMetadataReady_ReturnsExpectedValue(
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress,
        bool expected) {
        var shouldDelay = MainWindow.ShouldDelayStartupModelProfileSyncUntilMetadataReady(
            startupMetadataSyncQueued,
            startupMetadataSyncInProgress);
        Assert.Equal(expected, shouldDelay);
    }

    /// <summary>
    /// Ensures stale active startup metadata sync state is cleared only after watchdog threshold.
    /// </summary>
    [Theory]
    [InlineData(10, 35, false)]
    [InlineData(35, 35, true)]
    [InlineData(46, 35, true)]
    public void ShouldClearStaleActiveStartupMetadataSync_ReturnsExpectedValue(
        int elapsedSeconds,
        int staleThresholdSeconds,
        bool expected) {
        var shouldClear = MainWindow.ShouldClearStaleActiveStartupMetadataSync(
            TimeSpan.FromSeconds(elapsedSeconds),
            TimeSpan.FromSeconds(staleThresholdSeconds));
        Assert.Equal(expected, shouldClear);
    }

    /// <summary>
    /// Ensures stale queued startup metadata state only clears when still queued, not actively syncing, and over threshold.
    /// </summary>
    [Theory]
    [InlineData(false, false, 46, 35, false)]
    [InlineData(true, true, 46, 35, false)]
    [InlineData(true, false, 10, 35, false)]
    [InlineData(true, false, 35, 35, true)]
    public void ShouldClearStaleQueuedStartupMetadataSync_ReturnsExpectedValue(
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress,
        int queuedElapsedSeconds,
        int staleThresholdSeconds,
        bool expected) {
        var shouldClear = MainWindow.ShouldClearStaleQueuedStartupMetadataSync(
            startupMetadataSyncQueued,
            startupMetadataSyncInProgress,
            TimeSpan.FromSeconds(queuedElapsedSeconds),
            TimeSpan.FromSeconds(staleThresholdSeconds));
        Assert.Equal(expected, shouldClear);
    }

    /// <summary>
    /// Ensures startup profile apply skips redundant set-profile calls when runtime already uses the same profile.
    /// </summary>
    [Theory]
    [InlineData("default", "default", false, false)]
    [InlineData("default", "default", true, true)]
    [InlineData("default", "ops", false, true)]
    [InlineData("default", null, false, true)]
    [InlineData("ops", "ops", false, false)]
    [InlineData("ops", "default", false, true)]
    [InlineData("lab", "default", false, false)]
    public void ShouldApplyServiceProfile_ReturnsExpectedValue(
        string appProfileName,
        string? activeServiceProfileName,
        bool newThread,
        bool expected) {
        var availableProfiles = new[] { "default", "ops" };
        var shouldApply = MainWindow.ShouldApplyServiceProfile(
            availableProfiles,
            appProfileName,
            activeServiceProfileName,
            newThread);
        Assert.Equal(expected, shouldApply);
    }

    /// <summary>
    /// Ensures startup pending status prefers "verifying sign-in state" copy only while authentication
    /// is still unknown for connected native-runtime sessions.
    /// </summary>
    [Theory]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, true, true, false, false, false)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(true, true, false, false, true, false)]
    [InlineData(true, true, false, false, false, true)]
    public void ShouldShowStartupAuthVerificationPending_ReturnsExpectedValue(
        bool isConnectedStatus,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasExplicitUnauthenticatedProbeSnapshot,
        bool expected) {
        var shouldShow = MainWindow.ShouldShowStartupAuthVerificationPending(
            isConnectedStatus,
            requiresInteractiveSignIn,
            isAuthenticated,
            loginInProgress,
            hasExplicitUnauthenticatedProbeSnapshot);
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

    /// <summary>
    /// Ensures startup dispatch prewarm emits a system notice only when sign-in is still required or auth remains inconclusive.
    /// </summary>
    [Theory]
    [InlineData(false, null, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, null, true, true)]
    public void ShouldAppendStartupDispatchPrewarmSummary_ReturnsExpectedValue(
        bool authProbeAttempted,
        bool? authProbeAuthenticated,
        bool authProbeInconclusive,
        bool expected) {
        var shouldAppend = MainWindow.ShouldAppendStartupDispatchPrewarmSummary(
            authProbeAttempted,
            authProbeAuthenticated,
            authProbeInconclusive);
        Assert.Equal(expected, shouldAppend);
    }
}
