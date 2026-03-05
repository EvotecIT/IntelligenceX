using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo HandleChatRequestAsyncMethod =
        typeof(ChatServiceSession).GetMethod("HandleChatRequestAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("HandleChatRequestAsync not found.");
    private static readonly MethodInfo HandleCancelChatAsyncMethod =
        typeof(ChatServiceSession).GetMethod("HandleCancelChatAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("HandleCancelChatAsync not found.");

    [Fact]
    public async Task HandleChatRequestAsync_EmitsDeterministicStatusOrder_ForDefaultFlow() {
        using var server = new DeterministicCompatibleHttpServer(_ => BuildTextOnlyCompletion("Lifecycle default flow completed."));

        var session = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl), Stream.Null);
        using var client = await ConnectCompatibleClientAsync(server.BaseUrl);
        var thread = await client.StartNewThreadAsync("mock-local-model");
        var request = new ChatRequest {
            RequestId = "req-lifecycle-default",
            ThreadId = thread.Id,
            Text = "Check status flow for default request.",
            Options = new ChatRequestOptions {
                MaxToolRounds = 1,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokeHandleChatRequestAsync(session, client, writer, request, CancellationToken.None);
        await WaitForRequestStatusAsync(capture, request.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(10));

        var requestFrames = ParseCapturedFrames(capture.Snapshot())
            .Where(frame => string.Equals(frame.RequestId, request.RequestId, StringComparison.Ordinal))
            .ToList();
        var statuses = requestFrames
            .Where(frame => string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase))
            .Select(frame => frame.Status)
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Cast<string>()
            .ToList();

        AssertStatusSubsequence(statuses, ChatStatusCodes.Accepted, ChatStatusCodes.ContextReady, ChatStatusCodes.Done);

        var doneStatusIndex = requestFrames.FindIndex(frame =>
            string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase)
            && string.Equals(frame.Status, ChatStatusCodes.Done, StringComparison.OrdinalIgnoreCase));
        var terminalResponseIndex = requestFrames.FindIndex(frame => IsTerminalResponseFrame(frame));
        Assert.True(doneStatusIndex >= 0);
        Assert.True(terminalResponseIndex > doneStatusIndex);
    }

    [Fact]
    public async Task HandleChatRequestAsync_EmitsDeterministicStatusOrder_ForTimeoutFlow() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => {
            if (responseIndex == 1) {
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }

            return BuildTextOnlyCompletion("This response should not complete before timeout.");
        });

        var session = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl), Stream.Null);
        using var client = await ConnectCompatibleClientAsync(server.BaseUrl);
        var thread = await client.StartNewThreadAsync("mock-local-model");
        var request = new ChatRequest {
            RequestId = "req-lifecycle-timeout",
            ThreadId = thread.Id,
            Text = "Run a timeout status-order regression.",
            Options = new ChatRequestOptions {
                MaxToolRounds = 1,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0,
                TurnTimeoutSeconds = 1
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokeHandleChatRequestAsync(session, client, writer, request, CancellationToken.None);
        await WaitForRequestStatusAsync(capture, request.RequestId!, ChatStatusCodes.Timeout, TimeSpan.FromSeconds(10));
        await WaitForRequestErrorCodeAsync(capture, request.RequestId!, "chat_timeout", TimeSpan.FromSeconds(10));

        var requestFrames = ParseCapturedFrames(capture.Snapshot())
            .Where(frame => string.Equals(frame.RequestId, request.RequestId, StringComparison.Ordinal))
            .ToList();
        var statuses = requestFrames
            .Where(frame => string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase))
            .Select(frame => frame.Status)
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Cast<string>()
            .ToList();

        AssertStatusSubsequence(statuses, ChatStatusCodes.Accepted, ChatStatusCodes.ContextReady, ChatStatusCodes.Timeout);
        Assert.DoesNotContain(statuses, status => string.Equals(status, ChatStatusCodes.Done, StringComparison.OrdinalIgnoreCase));

        var timeoutStatusIndex = requestFrames.FindIndex(frame =>
            string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase)
            && string.Equals(frame.Status, ChatStatusCodes.Timeout, StringComparison.OrdinalIgnoreCase));
        var timeoutErrorIndex = requestFrames.FindIndex(frame =>
            IsTerminalResponseFrame(frame)
            && string.Equals(frame.Code, "chat_timeout", StringComparison.OrdinalIgnoreCase));
        Assert.True(timeoutStatusIndex >= 0);
        Assert.True(timeoutErrorIndex > timeoutStatusIndex);
    }

    [Fact]
    public async Task HandleChatRequestAsync_EmitsSessionQueueHeartbeat_WithPositionAndElapsed() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => {
            if (responseIndex == 1) {
                Thread.Sleep(TimeSpan.FromSeconds(7));
                return BuildTextOnlyCompletion("Primary queued turn finished.");
            }

            return BuildTextOnlyCompletion("Queued turn completed after wait.");
        });

        var session = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl), Stream.Null);
        using var client = await ConnectCompatibleClientAsync(server.BaseUrl);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var activeRequest = new ChatRequest {
            RequestId = "req-session-queue-active",
            ThreadId = thread.Id,
            Text = "Run long diagnostic one."
        };
        var queuedRequest = new ChatRequest {
            RequestId = "req-session-queue-waiting",
            ThreadId = thread.Id,
            Text = "Run queued diagnostic two."
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokeHandleChatRequestAsync(session, client, writer, activeRequest, CancellationToken.None);
        await InvokeHandleChatRequestAsync(session, client, writer, queuedRequest, CancellationToken.None);

        await WaitForRequestStatusCountAsync(
                capture,
                queuedRequest.RequestId!,
                ChatStatusCodes.TurnQueued,
                minimumCount: 2,
                timeout: TimeSpan.FromSeconds(20))
            ;

        var queuedStatusMessages = ParseCapturedFrames(capture.Snapshot())
            .Where(frame => string.Equals(frame.RequestId, queuedRequest.RequestId, StringComparison.Ordinal))
            .Where(frame => string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase))
            .Where(frame => string.Equals(frame.Status, ChatStatusCodes.TurnQueued, StringComparison.OrdinalIgnoreCase))
            .Select(frame => frame.Message ?? string.Empty)
            .ToList();

        Assert.Contains("Queued turn request (2 in session lane).", queuedStatusMessages, StringComparer.Ordinal);
        Assert.Contains(
            queuedStatusMessages,
            message => message.Contains("Still queued in session lane (2 in session lane, waiting ", StringComparison.Ordinal)
                       && message.EndsWith("s).", StringComparison.Ordinal));

        await WaitForRequestStatusAsync(capture, activeRequest.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(20));
        await WaitForRequestStatusAsync(capture, queuedRequest.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task HandleChatRequestAsync_EmitsGlobalLaneHeartbeat_WithElapsedProgress() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => {
            if (responseIndex == 1) {
                Thread.Sleep(TimeSpan.FromSeconds(7));
                return BuildTextOnlyCompletion("Global lane holder completed.");
            }

            return BuildTextOnlyCompletion("Global lane waiter completed.");
        });

        const int laneConcurrency = 1;
        var sessionA = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl, laneConcurrency), Stream.Null);
        var sessionB = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl, laneConcurrency), Stream.Null);

        using var clientA = await ConnectCompatibleClientAsync(server.BaseUrl);
        using var clientB = await ConnectCompatibleClientAsync(server.BaseUrl);
        var threadA = await clientA.StartNewThreadAsync("mock-local-model");
        var threadB = await clientB.StartNewThreadAsync("mock-local-model");

        var laneHolderRequest = new ChatRequest {
            RequestId = "req-global-lane-holder",
            ThreadId = threadA.Id,
            Text = "Hold the lane with a long run."
        };
        var laneWaiterRequest = new ChatRequest {
            RequestId = "req-global-lane-waiter",
            ThreadId = threadB.Id,
            Text = "Wait behind lane holder."
        };

        using var captureA = new SynchronizedCaptureStream();
        using var writerA = new StreamWriter(captureA, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        using var captureB = new SynchronizedCaptureStream();
        using var writerB = new StreamWriter(captureB, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokeHandleChatRequestAsync(sessionA, clientA, writerA, laneHolderRequest, CancellationToken.None);
        await WaitForRequestStatusAsync(captureA, laneHolderRequest.RequestId!, ChatStatusCodes.ContextReady, TimeSpan.FromSeconds(5));
        await InvokeHandleChatRequestAsync(sessionB, clientB, writerB, laneWaiterRequest, CancellationToken.None);

        await WaitForRequestStatusCountAsync(
                captureB,
                laneWaiterRequest.RequestId!,
                ChatStatusCodes.ExecutionLaneWaiting,
                minimumCount: 2,
                timeout: TimeSpan.FromSeconds(20))
            ;

        var waitingMessages = ParseCapturedFrames(captureB.Snapshot())
            .Where(frame => string.Equals(frame.RequestId, laneWaiterRequest.RequestId, StringComparison.Ordinal))
            .Where(frame => string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase))
            .Where(frame => string.Equals(frame.Status, ChatStatusCodes.ExecutionLaneWaiting, StringComparison.OrdinalIgnoreCase))
            .Select(frame => frame.Message ?? string.Empty)
            .ToList();

        Assert.Contains("Waiting for global execution lane...", waitingMessages, StringComparer.Ordinal);
        Assert.Contains(
            waitingMessages,
            message => message.StartsWith("Still waiting for global execution lane (", StringComparison.Ordinal)
                       && message.EndsWith("s elapsed).", StringComparison.Ordinal));

        await WaitForRequestStatusAsync(captureA, laneHolderRequest.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(20));
        await WaitForRequestStatusAsync(captureB, laneWaiterRequest.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task HandleCancelChatAsync_CancelActiveRun_AllowsQueuedRunToProgressUnderContention() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => {
            if (responseIndex == 1) {
                Thread.Sleep(TimeSpan.FromSeconds(8));
                return BuildTextOnlyCompletion("Canceled run should not deliver final response.");
            }

            return BuildTextOnlyCompletion("Queued run completed after active cancellation.");
        });

        var session = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl), Stream.Null);
        using var client = await ConnectCompatibleClientAsync(server.BaseUrl);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var activeRequest = new ChatRequest {
            RequestId = "req-cancel-active",
            ThreadId = thread.Id,
            Text = "Run long active task."
        };
        var queuedRequest = new ChatRequest {
            RequestId = "req-after-cancel-active",
            ThreadId = thread.Id,
            Text = "Run immediately after active cancellation."
        };
        var cancelRequest = new CancelChatRequest {
            RequestId = "req-cancel-active-control",
            ChatRequestId = activeRequest.RequestId
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokeHandleChatRequestAsync(session, client, writer, activeRequest, CancellationToken.None);
        await InvokeHandleChatRequestAsync(session, client, writer, queuedRequest, CancellationToken.None);
        await WaitForRequestStatusAsync(capture, activeRequest.RequestId!, ChatStatusCodes.ContextReady, TimeSpan.FromSeconds(5));
        await WaitForRequestStatusAsync(capture, queuedRequest.RequestId!, ChatStatusCodes.TurnQueued, TimeSpan.FromSeconds(5));

        await InvokeHandleCancelChatAsync(session, writer, cancelRequest, CancellationToken.None);

        await WaitForAckAsync(capture, cancelRequest.RequestId, TimeSpan.FromSeconds(5));
        await WaitForRequestErrorCodeAsync(capture, activeRequest.RequestId!, "chat_canceled", TimeSpan.FromSeconds(10));
        await WaitForRequestStatusAsync(capture, queuedRequest.RequestId!, ChatStatusCodes.ContextReady, TimeSpan.FromSeconds(20));
        await WaitForRequestTerminalFrameAsync(capture, queuedRequest.RequestId!, TimeSpan.FromSeconds(30));
        Assert.False(
            HasRequestErrorCode(capture.Snapshot(), queuedRequest.RequestId!, "chat_canceled"),
            "Queued request should not be canceled when canceling the active request.");
    }

    [Fact]
    public async Task HandleCancelChatAsync_CancelQueuedRun_KeepsRemainingQueueProgressing() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => {
            if (responseIndex == 1) {
                Thread.Sleep(TimeSpan.FromSeconds(7));
                return BuildTextOnlyCompletion("Head run completed.");
            }

            return responseIndex switch {
                2 => BuildTextOnlyCompletion("This canceled queued run should not complete."),
                3 => BuildTextOnlyCompletion("Trailing queued run completed."),
                _ => BuildTextOnlyCompletion("Unexpected request.")
            };
        });

        var session = new ChatServiceSession(CreateCompatibleHttpServiceOptions(server.BaseUrl), Stream.Null);
        using var client = await ConnectCompatibleClientAsync(server.BaseUrl);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var activeRequest = new ChatRequest {
            RequestId = "req-queue-head",
            ThreadId = thread.Id,
            Text = "Run long queue head."
        };
        var canceledQueuedRequest = new ChatRequest {
            RequestId = "req-queue-canceled",
            ThreadId = thread.Id,
            Text = "Run this queued task then cancel it."
        };
        var trailingQueuedRequest = new ChatRequest {
            RequestId = "req-queue-trailing",
            ThreadId = thread.Id,
            Text = "Run after canceled queued task."
        };
        var cancelRequest = new CancelChatRequest {
            RequestId = "req-cancel-queued-control",
            ChatRequestId = canceledQueuedRequest.RequestId
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        await InvokeHandleChatRequestAsync(session, client, writer, activeRequest, CancellationToken.None);
        await InvokeHandleChatRequestAsync(session, client, writer, canceledQueuedRequest, CancellationToken.None);
        await InvokeHandleChatRequestAsync(session, client, writer, trailingQueuedRequest, CancellationToken.None);

        await WaitForRequestStatusAsync(capture, canceledQueuedRequest.RequestId!, ChatStatusCodes.TurnQueued, TimeSpan.FromSeconds(5));
        await WaitForRequestStatusAsync(capture, trailingQueuedRequest.RequestId!, ChatStatusCodes.TurnQueued, TimeSpan.FromSeconds(5));

        await InvokeHandleCancelChatAsync(session, writer, cancelRequest, CancellationToken.None);

        await WaitForAckAsync(capture, cancelRequest.RequestId, TimeSpan.FromSeconds(5));
        await WaitForRequestStatusAsync(capture, activeRequest.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(20));
        await WaitForRequestStatusAsync(capture, trailingQueuedRequest.RequestId!, ChatStatusCodes.Done, TimeSpan.FromSeconds(20));

        Assert.False(
            HasRequestStatus(capture.Snapshot(), canceledQueuedRequest.RequestId!, ChatStatusCodes.Done),
            "Canceled queued request unexpectedly reached done status.");
        Assert.False(
            HasRequestErrorCode(capture.Snapshot(), canceledQueuedRequest.RequestId!, "chat_canceled"),
            "Queued cancellation should remove the queued run without emitting a terminal chat_canceled frame.");
    }

    private static async Task InvokeHandleChatRequestAsync(
        ChatServiceSession session,
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        CancellationToken cancellationToken) {
        var taskObj = HandleChatRequestAsyncMethod.Invoke(session, new object?[] { client, writer, request, cancellationToken });
        var task = Assert.IsAssignableFrom<Task>(taskObj);
        await task;
    }

    private static async Task InvokeHandleCancelChatAsync(
        ChatServiceSession session,
        StreamWriter writer,
        CancelChatRequest request,
        CancellationToken cancellationToken) {
        var taskObj = HandleCancelChatAsyncMethod.Invoke(session, new object?[] { writer, request, cancellationToken });
        var task = Assert.IsAssignableFrom<Task>(taskObj);
        await task;
    }

    private static async Task WaitForRequestStatusAsync(
        SynchronizedCaptureStream stream,
        string requestId,
        string status,
        TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            if (HasRequestStatus(stream.Snapshot(), requestId, status)) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var snapshot = stream.Snapshot();
        var requestFrames = ParseCapturedFrames(snapshot)
            .Where(frame => string.Equals(frame.RequestId, requestId, StringComparison.Ordinal))
            .ToList();
        var observedStatuses = requestFrames
            .Where(frame => string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase))
            .Select(frame => frame.Status ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var terminalSummaries = requestFrames
            .Where(IsTerminalResponseFrame)
            .Select(frame => $"{frame.Type}:{frame.Code ?? frame.Text ?? frame.Error ?? string.Empty}")
            .ToArray();

        Assert.True(
            HasRequestStatus(snapshot, requestId, status),
            $"Timed out waiting for status '{status}' for request '{requestId}'. Observed statuses=[{string.Join(", ", observedStatuses)}]; terminal=[{string.Join(", ", terminalSummaries)}].");
    }

    private static async Task WaitForRequestStatusCountAsync(
        SynchronizedCaptureStream stream,
        string requestId,
        string status,
        int minimumCount,
        TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            var count = CountRequestStatuses(stream.Snapshot(), requestId, status);
            if (count >= minimumCount) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        var finalCount = CountRequestStatuses(stream.Snapshot(), requestId, status);
        Assert.True(
            finalCount >= minimumCount,
            $"Timed out waiting for {minimumCount} '{status}' statuses for request '{requestId}'. Final count: {finalCount}.");
    }

    private static async Task WaitForRequestErrorCodeAsync(
        SynchronizedCaptureStream stream,
        string requestId,
        string errorCode,
        TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            if (HasRequestErrorCode(stream.Snapshot(), requestId, errorCode)) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(
            HasRequestErrorCode(stream.Snapshot(), requestId, errorCode),
            $"Timed out waiting for error code '{errorCode}' for request '{requestId}'.");
    }

    private static async Task WaitForAckAsync(
        SynchronizedCaptureStream stream,
        string requestId,
        TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            if (HasAck(stream.Snapshot(), requestId)) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(HasAck(stream.Snapshot(), requestId), $"Timed out waiting for ack frame for request '{requestId}'.");
    }

    private static async Task WaitForRequestTerminalFrameAsync(
        SynchronizedCaptureStream stream,
        string requestId,
        TimeSpan timeout) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout) {
            if (HasTerminalFrame(stream.Snapshot(), requestId)) {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(HasTerminalFrame(stream.Snapshot(), requestId), $"Timed out waiting for terminal frame for request '{requestId}'.");
    }

    private static bool HasRequestStatus(byte[] snapshotBytes, string requestId, string status) {
        return ParseCapturedFrames(snapshotBytes).Any(frame =>
            string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase)
            && string.Equals(frame.RequestId, requestId, StringComparison.Ordinal)
            && string.Equals(frame.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountRequestStatuses(byte[] snapshotBytes, string requestId, string status) {
        return ParseCapturedFrames(snapshotBytes).Count(frame =>
            string.Equals(frame.Type, "chat_status", StringComparison.OrdinalIgnoreCase)
            && string.Equals(frame.RequestId, requestId, StringComparison.Ordinal)
            && string.Equals(frame.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasRequestErrorCode(byte[] snapshotBytes, string requestId, string errorCode) {
        return ParseCapturedFrames(snapshotBytes).Any(frame =>
            string.Equals(frame.Type, "error", StringComparison.OrdinalIgnoreCase)
            && string.Equals(frame.RequestId, requestId, StringComparison.Ordinal)
            && string.Equals(frame.Code, errorCode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAck(byte[] snapshotBytes, string requestId) {
        return ParseCapturedFrames(snapshotBytes).Any(frame =>
            string.Equals(frame.Type, "ack", StringComparison.OrdinalIgnoreCase)
            && string.Equals(frame.RequestId, requestId, StringComparison.Ordinal));
    }

    private static bool HasTerminalFrame(byte[] snapshotBytes, string requestId) {
        return ParseCapturedFrames(snapshotBytes).Any(frame =>
            string.Equals(frame.RequestId, requestId, StringComparison.Ordinal)
            && IsTerminalResponseFrame(frame));
    }

    private static bool IsTerminalResponseFrame(CapturedFrame frame) {
        if (string.Equals(frame.Type, "chat_result", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(frame.Type, "error", StringComparison.OrdinalIgnoreCase)) {
            return !string.IsNullOrWhiteSpace(frame.Error) || !string.IsNullOrWhiteSpace(frame.Code);
        }

        return false;
    }

    private static List<CapturedFrame> ParseCapturedFrames(byte[] snapshotBytes) {
        using var snapshot = new MemoryStream(snapshotBytes, writable: false);
        using var reader = new StreamReader(snapshot, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var frames = new List<CapturedFrame>();
        while (!reader.EndOfStream) {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            frames.Add(new CapturedFrame(
                Type: TryGetStringProperty(root, "type"),
                RequestId: TryGetStringProperty(root, "requestId"),
                Status: TryGetStringProperty(root, "status"),
                Message: TryGetStringProperty(root, "message"),
                Code: TryGetStringProperty(root, "code"),
                Error: TryGetStringProperty(root, "error"),
                Text: TryGetStringProperty(root, "text")));
        }

        return frames;
    }

    private static string? TryGetStringProperty(JsonElement obj, string name) {
        if (!TryGetPropertyIgnoreCase(obj, name, out var value)) {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static ServiceOptions CreateCompatibleHttpServiceOptions(string baseUrl, int globalExecutionLaneConcurrency = 0) {
        return new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = baseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 1,
            GlobalExecutionLaneConcurrency = Math.Max(0, globalExecutionLaneConcurrency),
            DisabledPackIds = { "testimox", "officeimo" }
        };
    }

    private static async Task<IntelligenceXClient> ConnectCompatibleClientAsync(string baseUrl) {
        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = "mock-local-model"
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = baseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = OpenAICompatibleHttpAuthMode.None;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = true;
        return await IntelligenceXClient.ConnectAsync(clientOptions);
    }

    private static string BuildTextOnlyCompletion(string text) {
        return JsonSerializer.Serialize(new {
            id = "chatcmpl-lifecycle-status",
            @object = "chat.completion",
            choices = new[] {
                new {
                    index = 0,
                    message = new {
                        role = "assistant",
                        content = text
                    },
                    finish_reason = "stop"
                }
            }
        });
    }

    private sealed record CapturedFrame(
        string? Type,
        string? RequestId,
        string? Status,
        string? Message,
        string? Code,
        string? Error,
        string? Text);
}
