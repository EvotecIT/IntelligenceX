using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatTurnCompletionFrameOrderingTests {
    [Fact]
    public void OrderTurnCompletionFrames_PutsMetricsBeforeTerminalResponse() {
        var now = DateTime.UtcNow;
        var metrics = new ChatMetricsMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = "turn-1",
            ThreadId = "thread-1",
            StartedAtUtc = now,
            CompletedAtUtc = now,
            Outcome = "ok"
        };
        var response = new ChatResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "turn-1",
            ThreadId = "thread-1",
            Text = "done"
        };

        var frames = ChatServiceSession.OrderTurnCompletionFrames(metrics, response);

        Assert.Collection(
            frames,
            frame => Assert.Same(metrics, frame),
            frame => Assert.Same(response, frame));
    }

    [Fact]
    public void OrderTurnCompletionFrames_KeepsResponseWhenMetricsUnavailable() {
        var response = new ErrorMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "turn-1",
            Error = "failed",
            Code = "chat_failed"
        };

        var frames = ChatServiceSession.OrderTurnCompletionFrames(metrics: null, response);

        Assert.Same(response, Assert.Single(frames));
    }
}
