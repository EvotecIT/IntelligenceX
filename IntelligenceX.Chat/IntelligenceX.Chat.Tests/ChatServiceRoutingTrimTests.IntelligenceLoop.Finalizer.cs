using System;
using System.IO;
using System.Threading;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void FinalizePhaseHeartbeatFailure_DoesNothingWhenFailureIsNull() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();

        ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: null,
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesExpectedIOException() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();

        var ex = Record.Exception(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: new IOException("simulated-io"),
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsUnexpectedFailure() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        var failure = new InvalidOperationException("heartbeat-failed");

        var ex = Assert.Throws<InvalidOperationException>(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: failure,
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Same(failure, ex);
        Assert.Equal("heartbeat-failed", ex.Message);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsUnexpectedFailureWhenCancellationIsAlreadyRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();
        var failure = new InvalidOperationException("heartbeat-failed-with-cancellation");

        var ex = Assert.Throws<InvalidOperationException>(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: failure,
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Same(failure, ex);
        Assert.Equal("heartbeat-failed-with-cancellation", ex.Message);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsCanceledFailureFromUnrelatedToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        using var unrelatedCts = new CancellationTokenSource();
        unrelatedCts.Cancel();
        var failure = new OperationCanceledException("unrelated", innerException: null, unrelatedCts.Token);

        var ex = Assert.Throws<OperationCanceledException>(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: failure,
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Same(failure, ex);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesCanceledFailureForHeartbeatToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        heartbeatCts.Cancel();

        var ex = Record.Exception(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: new OperationCanceledException("heartbeat-canceled", innerException: null, heartbeatCts.Token),
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesCanceledFailureForRequestToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();

        var ex = Record.Exception(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: new OperationCanceledException("request-canceled", innerException: null, outerCts.Token),
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsGenericCanceledWhenNoCancellationIsRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        var failure = new OperationCanceledException("generic-canceled");

        var ex = Assert.Throws<OperationCanceledException>(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: failure,
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Same(failure, ex);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesGenericCanceledWhenHeartbeatCancellationIsRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        heartbeatCts.Cancel();

        var ex = Record.Exception(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: new OperationCanceledException("generic-canceled"),
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesGenericCanceledWhenRequestCancellationIsRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();

        var ex = Record.Exception(() => ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: new OperationCanceledException("generic-canceled"),
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token));

        Assert.Null(ex);
    }
}
