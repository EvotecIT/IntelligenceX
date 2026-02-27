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

        InvokeFinalize(
            heartbeatFailure: null,
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesExpectedIOException() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();

        AssertSuppressed(
            failure: new IOException("simulated-io"),
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsUnexpectedFailure() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        var failure = new InvalidOperationException("heartbeat-failed");

        var ex = AssertRethrownSame(
            failure: failure,
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
        Assert.Equal("heartbeat-failed", ex.Message);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsUnexpectedFailureWhenCancellationIsAlreadyRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();
        var failure = new InvalidOperationException("heartbeat-failed-with-cancellation");

        var ex = AssertRethrownSame(
            failure: failure,
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
        Assert.Equal("heartbeat-failed-with-cancellation", ex.Message);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsCanceledFailureFromUnrelatedToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        using var unrelatedCts = new CancellationTokenSource();
        unrelatedCts.Cancel();
        var failure = new OperationCanceledException("unrelated", innerException: null, unrelatedCts.Token);

        AssertRethrownSame(
            failure: failure,
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesCanceledFailureForHeartbeatToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        heartbeatCts.Cancel();

        AssertSuppressed(
            failure: new OperationCanceledException("heartbeat-canceled", innerException: null, heartbeatCts.Token),
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesCanceledFailureForRequestToken() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();

        AssertSuppressed(
            failure: new OperationCanceledException("request-canceled", innerException: null, outerCts.Token),
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_RethrowsGenericCanceledWhenNoCancellationIsRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        var failure = new OperationCanceledException("generic-canceled");

        AssertRethrownSame(
            failure: failure,
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesGenericCanceledWhenHeartbeatCancellationIsRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        heartbeatCts.Cancel();

        AssertSuppressed(
            failure: new OperationCanceledException("generic-canceled"),
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    [Fact]
    public void FinalizePhaseHeartbeatFailure_SuppressesGenericCanceledWhenRequestCancellationIsRequested() {
        using var heartbeatCts = new CancellationTokenSource();
        using var outerCts = new CancellationTokenSource();
        outerCts.Cancel();

        AssertSuppressed(
            failure: new OperationCanceledException("generic-canceled"),
            heartbeatCancellationToken: heartbeatCts.Token,
            cancellationToken: outerCts.Token);
    }

    private static void AssertSuppressed(Exception failure, CancellationToken heartbeatCancellationToken, CancellationToken cancellationToken) {
        var ex = Record.Exception(() => InvokeFinalize(failure, heartbeatCancellationToken, cancellationToken));
        Assert.Null(ex);
    }

    private static TException AssertRethrownSame<TException>(TException failure, CancellationToken heartbeatCancellationToken,
        CancellationToken cancellationToken)
        where TException : Exception {
        var ex = Assert.Throws<TException>(() => InvokeFinalize(failure, heartbeatCancellationToken, cancellationToken));
        Assert.Same(failure, ex);
        return ex;
    }

    private static void InvokeFinalize(Exception? heartbeatFailure, CancellationToken heartbeatCancellationToken, CancellationToken cancellationToken) {
        ChatServiceSession.FinalizePhaseHeartbeatFailure(
            heartbeatFailure: heartbeatFailure,
            phaseStatus: "phase_review",
            requestId: "req-intelligence-loop",
            threadId: "thread-intelligence-loop",
            heartbeatCancellationToken: heartbeatCancellationToken,
            cancellationToken: cancellationToken);
    }
}
