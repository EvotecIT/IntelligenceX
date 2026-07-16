using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Client;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Protects the shared request-scoped turn delivery and cancellation contracts.
/// </summary>
public sealed class ChatServiceTurnRunnerTests {
    /// <summary>
    /// Ensures every correlated update is delivered once, in order, without losing request options or result metadata.
    /// </summary>
    [Fact]
    public async Task RunAsync_ForwardsCompleteOrderedTurnAndPreservesRequestOptions() {
        var client = new FakeChatServiceClient();
        var runner = new ChatServiceTurnRunner(client);
        var request = new ChatRequest {
            RequestId = "turn-1",
            ThreadId = "thread-1",
            Text = "inspect the directory",
            Options = new ChatRequestOptions {
                Model = "gpt-test",
                MaxToolRounds = 7,
                ParallelTools = false,
                ToolTimeoutSeconds = 42
            }
        };
        var startedAt = DateTime.UtcNow;
        var metrics = new ChatMetricsMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = request.RequestId,
            ThreadId = request.ThreadId!,
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt.AddSeconds(1),
            DurationMs = 1000,
            ToolCallsCount = 2,
            ToolRounds = 1,
            Outcome = "ok"
        };
        var response = new ChatResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            ThreadId = request.ThreadId!,
            Text = "done",
            Tools = new ToolRunDto {
                Calls = new[] {
                    new ToolCallDto {
                        CallId = "call-1",
                        Name = "directory_list",
                        ArgumentsJson = "{}"
                    }
                }
            },
            TurnTimelineEvents = new[] {
                new TurnTimelineEventDto {
                    Status = ChatStatusCodes.ToolCompleted,
                    ToolName = "directory_list",
                    AtUtc = startedAt
                }
            }
        };
        client.RequestHandler = (sent, _) => {
            Assert.Same(request, sent);
            var sentChat = Assert.IsType<ChatRequest>(sent);
            Assert.Equal("gpt-test", sentChat.Options?.Model);
            Assert.Equal(7, sentChat.Options?.MaxToolRounds);
            Assert.False(sentChat.Options?.ParallelTools);
            Assert.Equal(42, sentChat.Options?.ToolTimeoutSeconds);

            client.Raise(new ChatDeltaMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = "another-turn",
                ThreadId = "thread-other",
                Text = "ignore"
            });
            client.Raise(new ChatStatusMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = request.RequestId,
                ThreadId = request.ThreadId!,
                Status = ChatStatusCodes.Thinking
            });
            client.Raise(new ChatAssistantProvisionalMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = request.RequestId,
                ThreadId = request.ThreadId!,
                Text = "draft"
            });
            client.Raise(new ChatInterimResultMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = request.RequestId,
                ThreadId = request.ThreadId!,
                Text = "reviewed draft",
                ToolCallsCount = 2,
                ToolOutputsCount = 2
            });
            client.Raise(metrics);
            client.Raise(response);
            return Task.FromResult<ChatServiceMessage>(response);
        };

        var updates = new List<ChatTurnUpdate>();
        var activeCallbacks = 0;
        var maximumConcurrentCallbacks = 0;
        var result = await runner.RunAsync(
            request,
            async (update, cancellationToken) => {
                _ = cancellationToken;
                var concurrent = Interlocked.Increment(ref activeCallbacks);
                maximumConcurrentCallbacks = Math.Max(maximumConcurrentCallbacks, concurrent);
                await Task.Yield();
                updates.Add(update);
                _ = Interlocked.Decrement(ref activeCallbacks);
            });

        Assert.Same(response, result.Response);
        Assert.Same(metrics, result.Metrics);
        Assert.Equal(1, maximumConcurrentCallbacks);
        Assert.Collection(
            updates,
            update => Assert.IsType<ChatTurnStatusUpdate>(update),
            update => Assert.IsType<ChatTurnProvisionalUpdate>(update),
            update => Assert.IsType<ChatTurnInterimUpdate>(update),
            update => Assert.IsType<ChatTurnMetricsUpdate>(update),
            update => Assert.IsType<ChatTurnCompletedUpdate>(update));
    }

    /// <summary>
    /// Ensures a correlated error update reaches the consumer before the request exception is observed.
    /// </summary>
    [Fact]
    public async Task RunAsync_ForwardsCorrelatedErrorBeforeRequestFailure() {
        var client = new FakeChatServiceClient();
        var runner = new ChatServiceTurnRunner(client);
        var request = new ChatRequest {
            RequestId = "turn-error",
            Text = "fail"
        };
        var error = new ErrorMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Error = "provider unavailable",
            Code = "chat_failed"
        };
        client.RequestHandler = (_, _) => {
            client.Raise(error);
            return Task.FromException<ChatServiceMessage>(
                new ChatServiceRequestException(error.Error, error.Code));
        };
        var updates = new List<ChatTurnUpdate>();

        var exception = await Assert.ThrowsAsync<ChatServiceRequestException>(() =>
            runner.RunAsync(
                request,
                (update, _) => {
                    updates.Add(update);
                    return ValueTask.CompletedTask;
                }));

        Assert.Equal("chat_failed", exception.Code);
        var forwarded = Assert.Single(updates);
        Assert.Same(error, Assert.IsType<ChatTurnErrorUpdate>(forwarded).Error);
    }

    /// <summary>
    /// Ensures all desktop consumers use the same cancel request shape.
    /// </summary>
    [Fact]
    public async Task CancelAsync_UsesSharedCancelContract() {
        var client = new FakeChatServiceClient();
        var runner = new ChatServiceTurnRunner(client);
        CancelChatRequest? captured = null;
        client.RequestHandler = (request, _) => {
            captured = Assert.IsType<CancelChatRequest>(request);
            return Task.FromResult<ChatServiceMessage>(new AckMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Ok = true
            });
        };

        await runner.CancelAsync("  turn-9  ");

        Assert.NotNull(captured);
        Assert.Equal("turn-9", captured.ChatRequestId);
        Assert.StartsWith("turn-cancel-", captured.RequestId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures requesting cancellation does not detach the active turn before its terminal frames arrive.
    /// </summary>
    [Fact]
    public async Task CancelAsync_KeepsRunAttachedThroughMetricsAndTerminalError() {
        var client = new FakeChatServiceClient();
        var runner = new ChatServiceTurnRunner(client);
        var chatResponse = new TaskCompletionSource<ChatServiceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ChatRequest {
            RequestId = "turn-cancel-target",
            Text = "long running task"
        };
        var now = DateTime.UtcNow;
        var metrics = new ChatMetricsMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = request.RequestId,
            ThreadId = "thread-1",
            StartedAtUtc = now,
            CompletedAtUtc = now.AddSeconds(1),
            Outcome = "canceled",
            ErrorCode = "chat_canceled"
        };
        var error = new ErrorMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Error = "Chat canceled by client.",
            Code = "chat_canceled"
        };
        client.RequestHandler = (sent, _) => {
            if (sent is ChatRequest) {
                chatStarted.TrySetResult(null);
                return chatResponse.Task;
            }

            var cancel = Assert.IsType<CancelChatRequest>(sent);
            Assert.Equal(request.RequestId, cancel.ChatRequestId);
            client.Raise(metrics);
            client.Raise(error);
            chatResponse.TrySetException(new ChatServiceRequestException(error.Error, error.Code));
            return Task.FromResult<ChatServiceMessage>(new AckMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = cancel.RequestId,
                Ok = true
            });
        };
        var updates = new List<ChatTurnUpdate>();
        var runTask = runner.RunAsync(
            request,
            (update, _) => {
                updates.Add(update);
                return ValueTask.CompletedTask;
            });
        await chatStarted.Task;

        await runner.CancelAsync(request.RequestId);
        var exception = await Assert.ThrowsAsync<ChatServiceRequestException>(() => runTask);

        Assert.Equal("chat_canceled", exception.Code);
        Assert.Collection(
            updates,
            update => Assert.Same(metrics, Assert.IsType<ChatTurnMetricsUpdate>(update).Metrics),
            update => Assert.Same(error, Assert.IsType<ChatTurnErrorUpdate>(update).Error));
    }

    private sealed class FakeChatServiceClient : IChatServiceClient {
        public event Action<ChatServiceMessage>? MessageReceived;

        public Func<ChatServiceRequest, CancellationToken, Task<ChatServiceMessage>> RequestHandler { get; set; } =
            static (_, _) => Task.FromException<ChatServiceMessage>(new InvalidOperationException("No request handler configured."));

        public async Task<TResponse> RequestAsync<TResponse>(ChatServiceRequest request, CancellationToken cancellationToken)
            where TResponse : ChatServiceMessage {
            var response = await RequestHandler(request, cancellationToken);
            return Assert.IsType<TResponse>(response);
        }

        public void Raise(ChatServiceMessage message) {
            MessageReceived?.Invoke(message);
        }
    }
}
