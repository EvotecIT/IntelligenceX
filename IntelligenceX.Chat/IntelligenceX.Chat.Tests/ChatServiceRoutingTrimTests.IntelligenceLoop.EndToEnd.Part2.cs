using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public async Task RunChatOnCurrentThreadAsync_EmitsToolRoundCapApplied_WhenRequestedMaxToolRoundsExceedsSupportedLimit() {
        using var server = new DeterministicCompatibleHttpServer();

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 24,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        RegistryField.SetValue(session, registry);

        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = "mock-local-model"
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = server.BaseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = OpenAICompatibleHttpAuthMode.None;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = true;

        using var client = await IntelligenceXClient.ConnectAsync(clientOptions);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var request = new ChatRequest {
            RequestId = "req-tool-round-cap",
            ThreadId = thread.Id,
            Text = "Run the diagnostics workflow to completion.",
            Options = new ChatRequestOptions {
                MaxToolRounds = 500,
                WeightedToolRouting = false,
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer,
            request,
            thread.Id,
            CancellationToken.None);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Contains(statuses, static s => string.Equals(s, "tool_round_cap_applied", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolCallsCount"));
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RetriesAfterTransportDropPostToolRound_WithoutReexecutingTool() {
        using var server = new DeterministicCompatibleHttpServer(dropChatCompletionResponseOnRequestIndices: new[] { 2 });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();

        var invokedSteps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var invokedStepsSync = new object();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                lock (invokedStepsSync) {
                    if (invokedSteps.TryGetValue(step, out var current)) {
                        invokedSteps[step] = current + 1;
                    } else {
                        invokedSteps[step] = 1;
                    }
                }

                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        RegistryField.SetValue(session, registry);

        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = "mock-local-model"
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = server.BaseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = OpenAICompatibleHttpAuthMode.None;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = true;

        using var client = await IntelligenceXClient.ConnectAsync(clientOptions);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var request = new ChatRequest {
            RequestId = "req-e2e-transport-drop",
            ThreadId = thread.Id,
            Text = "Run the diagnostics workflow to completion.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer,
            request,
            thread.Id,
            CancellationToken.None);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(4, server.ChatCompletionRequestCount);
        Assert.Equal(1, server.DroppedChatCompletionRequestCount);
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(1), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(2), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(3), "call_round_2"));

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Final answer after two tool rounds.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(2, resultMessage.Tools!.Calls.Count);
        Assert.Equal(2, resultMessage.Tools.Outputs.Count);

        var normalizedCallIds = resultMessage.Tools.Calls
            .Select(static call => (call.CallId ?? string.Empty).Trim())
            .Where(static callId => callId.Length > 0)
            .ToArray();
        Assert.Equal(
            normalizedCallIds.Length,
            normalizedCallIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var normalizedOutputIds = resultMessage.Tools.Outputs
            .Select(static output => (output.CallId ?? string.Empty).Trim())
            .Where(static callId => callId.Length > 0)
            .ToArray();
        Assert.Equal(
            normalizedOutputIds.Length,
            normalizedOutputIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var normalizedCallIdSet = new HashSet<string>(normalizedCallIds, StringComparer.OrdinalIgnoreCase);
        Assert.All(normalizedOutputIds, outputCallId => Assert.Contains(outputCallId, normalizedCallIdSet));

        lock (invokedStepsSync) {
            Assert.Equal(2, invokedSteps.Values.Sum());
            Assert.True(invokedSteps.TryGetValue("one", out var oneCount) && oneCount == 1);
            Assert.True(invokedSteps.TryGetValue("two", out var twoCount) && twoCount == 1);
        }
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RetriesAfterTransportDropBeforeFinalResponse_WithoutExtraToolRounds() {
        using var server = new DeterministicCompatibleHttpServer(dropChatCompletionResponseOnRequestIndices: new[] { 3 });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();

        var invokedSteps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var invokedStepsSync = new object();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                lock (invokedStepsSync) {
                    if (invokedSteps.TryGetValue(step, out var current)) {
                        invokedSteps[step] = current + 1;
                    } else {
                        invokedSteps[step] = 1;
                    }
                }

                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        RegistryField.SetValue(session, registry);

        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = "mock-local-model"
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = server.BaseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = OpenAICompatibleHttpAuthMode.None;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = true;

        using var client = await IntelligenceXClient.ConnectAsync(clientOptions);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var request = new ChatRequest {
            RequestId = "req-e2e-final-transport-drop",
            ThreadId = thread.Id,
            Text = "Run the diagnostics workflow to completion.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer,
            request,
            thread.Id,
            CancellationToken.None);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(4, server.ChatCompletionRequestCount);
        Assert.Equal(1, server.DroppedChatCompletionRequestCount);
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(2), "call_round_2"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(3), "call_round_2"));

        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Final answer after two tool rounds.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(2, resultMessage.Tools!.Calls.Count);
        Assert.Equal(2, resultMessage.Tools.Outputs.Count);

        var normalizedCallIds = resultMessage.Tools.Calls
            .Select(static call => (call.CallId ?? string.Empty).Trim())
            .Where(static callId => callId.Length > 0)
            .ToArray();
        var normalizedOutputIds = resultMessage.Tools.Outputs
            .Select(static output => (output.CallId ?? string.Empty).Trim())
            .Where(static callId => callId.Length > 0)
            .ToArray();

        Assert.Equal(
            normalizedCallIds.Length,
            normalizedCallIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            normalizedOutputIds.Length,
            normalizedOutputIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var normalizedCallIdSet = new HashSet<string>(normalizedCallIds, StringComparer.OrdinalIgnoreCase);
        Assert.All(normalizedOutputIds, outputCallId => Assert.Contains(outputCallId, normalizedCallIdSet));

        lock (invokedStepsSync) {
            Assert.Equal(2, invokedSteps.Values.Sum());
            Assert.True(invokedSteps.TryGetValue("one", out var oneCount) && oneCount == 1);
            Assert.True(invokedSteps.TryGetValue("two", out var twoCount) && twoCount == 1);
        }
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ReusesPriorOutput_WhenTransportRetryReplaysCompletedToolCallId() {
        using var server = new DeterministicCompatibleHttpServer(
            dropChatCompletionResponseOnRequestIndices: new[] { 2 },
            emitReplayDuplicateToolCallAfterDrop: true);

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            EnableTestimoXPack = false,
            EnableOfficeImoPack = false
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();

        var invokedSteps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var invokedStepsSync = new object();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                lock (invokedStepsSync) {
                    if (invokedSteps.TryGetValue(step, out var current)) {
                        invokedSteps[step] = current + 1;
                    } else {
                        invokedSteps[step] = 1;
                    }
                }

                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        RegistryField.SetValue(session, registry);

        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.CompatibleHttp,
            AutoInitialize = false,
            DefaultModel = "mock-local-model"
        };
        clientOptions.CompatibleHttpOptions.BaseUrl = server.BaseUrl;
        clientOptions.CompatibleHttpOptions.AuthMode = OpenAICompatibleHttpAuthMode.None;
        clientOptions.CompatibleHttpOptions.Streaming = false;
        clientOptions.CompatibleHttpOptions.AllowInsecureHttp = true;

        using var client = await IntelligenceXClient.ConnectAsync(clientOptions);
        var thread = await client.StartNewThreadAsync("mock-local-model");

        var request = new ChatRequest {
            RequestId = "req-e2e-replay-duplicate-call-id",
            ThreadId = thread.Id,
            Text = "Run the diagnostics workflow to completion.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture = new SynchronizedCaptureStream();
        using var writer = new StreamWriter(capture, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer,
            request,
            thread.Id,
            CancellationToken.None);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_call", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(5, server.ChatCompletionRequestCount);
        Assert.Equal(1, server.DroppedChatCompletionRequestCount);
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(1), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(2), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(3), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(4), "call_round_2"));

        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Final answer after two tool rounds.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(2, resultMessage.Tools!.Calls.Count);
        Assert.Equal(2, resultMessage.Tools.Outputs.Count);

        var normalizedCallIds = resultMessage.Tools.Calls
            .Select(static call => (call.CallId ?? string.Empty).Trim())
            .Where(static callId => callId.Length > 0)
            .ToArray();
        var normalizedOutputIds = resultMessage.Tools.Outputs
            .Select(static output => (output.CallId ?? string.Empty).Trim())
            .Where(static callId => callId.Length > 0)
            .ToArray();

        Assert.Equal(
            normalizedCallIds.Length,
            normalizedCallIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            normalizedOutputIds.Length,
            normalizedOutputIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var normalizedCallIdSet = new HashSet<string>(normalizedCallIds, StringComparer.OrdinalIgnoreCase);
        Assert.All(normalizedOutputIds, outputCallId => Assert.Contains(outputCallId, normalizedCallIdSet));

        lock (invokedStepsSync) {
            Assert.Equal(2, invokedSteps.Values.Sum());
            Assert.True(invokedSteps.TryGetValue("one", out var oneCount) && oneCount == 1);
            Assert.True(invokedSteps.TryGetValue("two", out var twoCount) && twoCount == 1);
        }
    }

}
