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
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly FieldInfo RegistryField =
        typeof(ChatServiceSession).GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_registry not found.");
    private static readonly FieldInfo ToolOrchestrationCatalogField =
        typeof(ChatServiceSession).GetField("_toolOrchestrationCatalog", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolOrchestrationCatalog not found.");

    private static readonly MethodInfo RunChatOnCurrentThreadAsyncMethod =
        typeof(ChatServiceSession).GetMethod("RunChatOnCurrentThreadAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RunChatOnCurrentThreadAsync not found.");

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ChainsToolRoundsAndEmitsOrderedLifecycleStatuses() {
        using var server = new DeterministicCompatibleHttpServer();

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-e2e-rounds",
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
        AssertStatusSubsequence(
            statuses,
            "phase_plan",
            "tool_round_started",
            "phase_execute",
            "tool_round_completed",
            "phase_review",
            "tool_round_started",
            "phase_execute",
            "tool_round_completed",
            "phase_review");
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        Assert.InRange(server.ChatCompletionRequestCount, 3, 4);
        Assert.True(CountRoleMessages(server.GetChatRequestBody(0), "tool") == 0);
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(1), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(2), "call_round_2"));

        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Final answer after two tool rounds.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(2, resultMessage.Tools!.Calls.Count);
        Assert.Equal(2, resultMessage.Tools.Outputs.Count);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RecoversFromDroppedModelResponseDuringAutonomousToolLoop() {
        using var server = new DeterministicCompatibleHttpServer(
            dropChatCompletionResponseOnRequestIndices: new[] { 2 },
            emitReplayDuplicateToolCallAfterDrop: true);

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-autonomy-drop-recovery",
            ThreadId = thread.Id,
            Text = "Run diagnostics workflow and recover from transient transport issues.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
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

        Assert.Equal(1, server.DroppedChatCompletionRequestCount);
        Assert.Equal(5, server.ChatCompletionRequestCount);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(2), "call_round_1"));
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(4), "call_round_2"));

        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(2, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Final answer after two tool rounds.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(2, resultMessage.Tools!.Calls.Count);
        Assert.Equal(2, resultMessage.Tools.Outputs.Count);
        Assert.Equal("call_round_1", resultMessage.Tools.Calls[0].CallId);
        Assert.Equal("call_round_2", resultMessage.Tools.Calls[1].CallId);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_SustainsExtendedAutonomyWithDropAndReplayAnomalies() {
        using var server = new DeterministicCompatibleHttpServer(
            responseIndex => responseIndex switch {
                1 => BuildToolCallResponse("call_autonomy_1", "step_1"),
                2 => BuildMultiToolCallResponse(("call_autonomy_1", "step_1"), ("call_autonomy_2", "step_2")),
                3 => BuildToolCallResponse("call_autonomy_3", "step_3"),
                4 => BuildMultiToolCallResponse(("call_autonomy_2", "step_2"), ("call_autonomy_4", "step_4")),
                5 => BuildToolCallResponse("call_autonomy_5", "step_5"),
                6 => BuildToolCallResponse("call_autonomy_6", "step_6"),
                _ => BuildTextResponse("Long mixed-failure autonomy run completed.")
            },
            dropChatCompletionResponseOnRequestIndices: new[] { 2 });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 10,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-extended-autonomy-mixed-failures",
            ThreadId = thread.Id,
            Text = "Continue autonomous diagnostics through all planned rounds despite transient replay anomalies.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 10,
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

        Assert.Equal(1, server.DroppedChatCompletionRequestCount);
        Assert.InRange(server.ChatCompletionRequestCount, 8, 10);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(6, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(6, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        for (var round = 1; round <= 6; round++) {
            var callId = "call_autonomy_" + round;
            var callObserved = false;
            for (var requestIndex = 0; requestIndex < server.ChatCompletionRequestCount; requestIndex++) {
                if (ContainsToolMessageForCallId(server.GetChatRequestBody(requestIndex), callId)) {
                    callObserved = true;
                    break;
                }
            }

            Assert.True(callObserved, "Expected replay context to contain tool output for " + callId + ".");
        }

        Assert.Equal(6, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(6, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Long mixed-failure autonomy run completed.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(6, resultMessage.Tools!.Calls.Count);
        Assert.Equal(6, resultMessage.Tools.Outputs.Count);
        for (var round = 1; round <= 6; round++) {
            Assert.Contains(resultMessage.Tools.Calls, call => string.Equals(call.CallId, "call_autonomy_" + round, StringComparison.Ordinal));
        }

        static string BuildToolCallResponse(string callId, string step) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-" + callId,
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = callId,
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildMultiToolCallResponse(params (string CallId, string Step)[] calls) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-mixed-replay",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = calls.Select(call => new {
                                id = call.CallId,
                                type = "function",
                                function = new {
                                    name = "mock_round_tool",
                                    arguments = JsonSerializer.Serialize(new { step = call.Step })
                                }
                            }).ToArray()
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-extended-autonomy-final",
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
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ContinuesAutonomousLoopAfterMidRoundToolException() {
        using var server = new DeterministicCompatibleHttpServer(
            responseIndex => responseIndex switch {
                1 => BuildToolCallResponse("call_resilience_1", "step_1"),
                2 => BuildToolCallResponse("call_resilience_2", "step_2_fail"),
                3 => BuildToolCallResponse("call_resilience_3", "step_3"),
                _ => BuildTextResponse("Autonomous loop recovered from transient tool failure.")
            });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                if (string.Equals(step, "step_2_fail", StringComparison.Ordinal)) {
                    throw new InvalidOperationException("Injected transient failure for autonomy continuation coverage.");
                }

                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-autonomy-mid-round-tool-exception",
            ThreadId = thread.Id,
            Text = "Continue autonomous diagnostics even if one step fails and complete the remaining plan.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
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

        Assert.InRange(server.ChatCompletionRequestCount, 4, 5);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(3, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(3, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(2), "call_resilience_2"));
        Assert.Equal(3, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(3, GetPropertyValue<int>(runResult, "ToolCallsCount"));

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Autonomous loop recovered from transient tool failure.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(3, resultMessage.Tools!.Calls.Count);
        Assert.Equal(3, resultMessage.Tools.Outputs.Count);
        var failedOutput = Assert.Single(resultMessage.Tools.Outputs, static output =>
            string.Equals(output.CallId, "call_resilience_2", StringComparison.Ordinal));
        Assert.Equal("tool_exception", failedOutput.ErrorCode);
        Assert.NotEqual(true, failedOutput.Ok);

        static string BuildToolCallResponse(string callId, string step) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-" + callId,
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = callId,
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-mid-round-tool-exception-final",
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
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ContinuesAfterConsecutiveToolExceptionsAndCompletes() {
        using var server = new DeterministicCompatibleHttpServer(
            responseIndex => responseIndex switch {
                1 => BuildToolCallResponse("call_consecutive_fail_1", "step_fail_1"),
                2 => BuildToolCallResponse("call_consecutive_fail_2", "step_fail_2"),
                3 => BuildToolCallResponse("call_consecutive_success_3", "step_3"),
                _ => BuildTextResponse("Autonomy continued after consecutive soft failures.")
            });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                if (string.Equals(step, "step_fail_1", StringComparison.Ordinal)
                    || string.Equals(step, "step_fail_2", StringComparison.Ordinal)) {
                    throw new InvalidOperationException("Injected soft failure for consecutive-failure autonomy coverage.");
                }

                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-autonomy-consecutive-soft-failures",
            ThreadId = thread.Id,
            Text = "Continue autonomous troubleshooting even if two consecutive tool steps fail.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
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

        Assert.InRange(server.ChatCompletionRequestCount, 4, 6);

        var statuses = ParseStatuses(capture.Snapshot());
        Assert.Equal(3, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(3, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(3, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(3, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Autonomy continued after consecutive soft failures.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(3, resultMessage.Tools!.Calls.Count);
        Assert.Equal(3, resultMessage.Tools.Outputs.Count);

        Assert.Contains(resultMessage.Tools.Calls, static call => string.Equals(call.CallId, "call_consecutive_fail_1", StringComparison.Ordinal));
        Assert.Contains(resultMessage.Tools.Calls, static call => string.Equals(call.CallId, "call_consecutive_fail_2", StringComparison.Ordinal));
        Assert.Contains(resultMessage.Tools.Calls, static call => string.Equals(call.CallId, "call_consecutive_success_3", StringComparison.Ordinal));

        var outputFail1 = Assert.Single(resultMessage.Tools.Outputs, static output =>
            string.Equals(output.CallId, "call_consecutive_fail_1", StringComparison.Ordinal));
        var outputFail2 = Assert.Single(resultMessage.Tools.Outputs, static output =>
            string.Equals(output.CallId, "call_consecutive_fail_2", StringComparison.Ordinal));
        var outputSuccess = Assert.Single(resultMessage.Tools.Outputs, static output =>
            string.Equals(output.CallId, "call_consecutive_success_3", StringComparison.Ordinal));

        Assert.Equal("tool_exception", outputFail1.ErrorCode);
        Assert.Equal("tool_exception", outputFail2.ErrorCode);
        Assert.NotEqual(true, outputFail1.Ok);
        Assert.NotEqual(true, outputFail2.Ok);
        Assert.True(outputSuccess.Ok ?? false);

        static string BuildToolCallResponse(string callId, string step) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-" + callId,
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = callId,
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-consecutive-failures-final",
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
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_MixedChaosDropAndConsecutiveFailuresRemainIsolatedAcrossThreads() {
        using var server = new DeterministicCompatibleHttpServer(
            responseIndex => responseIndex switch {
                1 => BuildToolCallResponse("call_chaos_a_1", "packa_round_tool", "step_fail_1"),
                2 => BuildToolCallResponse("call_chaos_a_2", "packa_round_tool", "step_fail_2"),
                3 => BuildToolCallResponse("call_chaos_a_3", "packa_round_tool", "step_success_3"),
                _ => BuildTextResponse("Mixed chaos turn completed.")
            },
            dropChatCompletionResponseOnRequestIndices: new[] { 2 });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 8,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "packa_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                if (string.Equals(step, "step_fail_1", StringComparison.Ordinal)
                    || string.Equals(step, "step_fail_2", StringComparison.Ordinal)) {
                    throw new InvalidOperationException("Injected pack A chaos failure for mixed-thread chaos coverage.");
                }

                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        registry.Register(new RoundTripStubTool(
            "packb_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
        var threadA = await client.StartNewThreadAsync("mock-local-model");
        var threadB = await client.StartNewThreadAsync("mock-local-model");

        var requestA = new ChatRequest {
            RequestId = "req-chaos-thread-a",
            ThreadId = threadA.Id,
            Text = "Thread A chaos run with pack A should continue despite transient failures.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 8,
                EnabledPackIds = new[] { "packa" },
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureA = new SynchronizedCaptureStream();
        using var writerA = new StreamWriter(captureA, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResultA = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerA,
            requestA,
            threadA.Id,
            CancellationToken.None);

        var requestB = new ChatRequest {
            RequestId = "req-chaos-thread-b",
            ThreadId = threadB.Id,
            Text = "Thread B isolated run must stay on pack B only.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 8,
                EnabledPackIds = new[] { "packb" },
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureB = new SynchronizedCaptureStream();
        using var writerB = new StreamWriter(captureB, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResultB = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerB,
            requestB,
            threadB.Id,
            CancellationToken.None);

        Assert.Equal(1, server.DroppedChatCompletionRequestCount);
        Assert.InRange(server.ChatCompletionRequestCount, 6, 12);

        var statusesA = ParseStatuses(captureA.Snapshot());
        var startedA = statusesA.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase));
        var completedA = statusesA.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(startedA, 3, 5);
        Assert.InRange(completedA, 3, 5);
        Assert.True(startedA >= completedA);
        Assert.DoesNotContain(statusesA, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        var statusesB = ParseStatuses(captureB.Snapshot());
        Assert.Equal(0, statusesB.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(0, statusesB.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statusesB, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        var observedThreadARequests = 0;
        var observedThreadBRequests = 0;
        for (var requestIndex = 0; requestIndex < server.ChatCompletionRequestCount; requestIndex++) {
            var requestBody = server.GetChatRequestBody(requestIndex);
            var latestUserMessage = GetLatestUserMessageContent(requestBody);
            if (latestUserMessage.Contains("Thread A chaos run with pack A should continue despite transient failures.", StringComparison.Ordinal)) {
                observedThreadARequests++;
                Assert.True(HasToolSchemaForFunctionName(requestBody, "packa_round_tool"));
                Assert.False(HasToolSchemaForFunctionName(requestBody, "packb_round_tool"));
            } else if (latestUserMessage.Contains("Thread B isolated run must stay on pack B only.", StringComparison.Ordinal)) {
                observedThreadBRequests++;
                Assert.True(HasToolSchemaForFunctionName(requestBody, "packb_round_tool"));
                Assert.False(HasToolSchemaForFunctionName(requestBody, "packa_round_tool"));
            }
        }

        Assert.True(observedThreadARequests > 0);
        Assert.True(observedThreadBRequests > 0);

        Assert.InRange(GetPropertyValue<int>(runResultA, "ToolRounds"), 3, 5);
        Assert.InRange(GetPropertyValue<int>(runResultA, "ToolCallsCount"), 3, 5);
        var resultMessageA = GetPropertyValue<ChatResultMessage>(runResultA, "Result");
        Assert.Equal("Mixed chaos turn completed.", resultMessageA.Text);
        Assert.NotNull(resultMessageA.Tools);
        Assert.True(resultMessageA.Tools!.Calls.Count >= 3);
        Assert.True(resultMessageA.Tools.Outputs.Count >= 3);
        Assert.Contains(resultMessageA.Tools.Calls, static call => string.Equals(call.CallId, "call_chaos_a_1", StringComparison.Ordinal));
        Assert.Contains(resultMessageA.Tools.Calls, static call => string.Equals(call.CallId, "call_chaos_a_2", StringComparison.Ordinal));
        Assert.Contains(resultMessageA.Tools.Calls, static call => string.Equals(call.CallId, "call_chaos_a_3", StringComparison.Ordinal));
        Assert.Contains(resultMessageA.Tools.Outputs, static output => string.Equals(output.CallId, "call_chaos_a_1", StringComparison.Ordinal));
        Assert.Contains(resultMessageA.Tools.Outputs, static output => string.Equals(output.CallId, "call_chaos_a_2", StringComparison.Ordinal));
        Assert.Contains(resultMessageA.Tools.Outputs, static output => string.Equals(output.CallId, "call_chaos_a_3", StringComparison.Ordinal));
        var outputA1 = resultMessageA.Tools.Outputs.First(static output =>
            string.Equals(output.CallId, "call_chaos_a_1", StringComparison.Ordinal));
        var outputA2 = resultMessageA.Tools.Outputs.First(static output =>
            string.Equals(output.CallId, "call_chaos_a_2", StringComparison.Ordinal));
        var outputA3 = resultMessageA.Tools.Outputs.First(static output =>
            string.Equals(output.CallId, "call_chaos_a_3", StringComparison.Ordinal));
        Assert.Equal("tool_exception", outputA1.ErrorCode);
        Assert.Equal("tool_exception", outputA2.ErrorCode);
        Assert.NotEqual(true, outputA1.Ok);
        Assert.NotEqual(true, outputA2.Ok);
        Assert.True(outputA3.Ok ?? false);

        Assert.Equal(0, GetPropertyValue<int>(runResultB, "ToolRounds"));
        Assert.Equal(0, GetPropertyValue<int>(runResultB, "ToolCallsCount"));
        var resultMessageB = GetPropertyValue<ChatResultMessage>(runResultB, "Result");
        Assert.Equal("Mixed chaos turn completed.", resultMessageB.Text);
        Assert.Null(resultMessageB.Tools);

        static string BuildToolCallResponse(string callId, string toolName, string step) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-" + callId,
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = callId,
                                    type = "function",
                                    function = new {
                                        name = toolName,
                                        arguments = JsonSerializer.Serialize(new { step })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-chaos-final",
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
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_AutonomyParityBenchmark_TracksContinuationDepthAcrossSeededFailurePatterns() {
        var scenarios = new (string Name, Func<DeterministicCompatibleHttpServer> CreateServer)[] {
            (
                "baseline_clean",
                () => new DeterministicCompatibleHttpServer(
                    responseIndex => responseIndex switch {
                        1 => BuildToolCallResponse("call_baseline_1", "step_1"),
                        2 => BuildToolCallResponse("call_baseline_2", "step_2"),
                        _ => BuildTextResponse("Benchmark scenario baseline_clean completed.")
                    })
            ),
            (
                "drop_replay",
                () => new DeterministicCompatibleHttpServer(
                    responseIndex => responseIndex switch {
                        1 => BuildToolCallResponse("call_drop_1", "step_1"),
                        2 => BuildMultiToolCallResponse(("call_drop_1", "step_1"), ("call_drop_2", "step_2")),
                        3 => BuildToolCallResponse("call_drop_3", "step_3"),
                        _ => BuildTextResponse("Benchmark scenario drop_replay completed.")
                    },
                    dropChatCompletionResponseOnRequestIndices: new[] { 2 })
            ),
            (
                "consecutive_soft_failures",
                () => new DeterministicCompatibleHttpServer(
                    responseIndex => responseIndex switch {
                        1 => BuildToolCallResponse("call_fail_1", "step_fail_1"),
                        2 => BuildToolCallResponse("call_fail_2", "step_fail_2"),
                        3 => BuildToolCallResponse("call_fail_3", "step_3"),
                        _ => BuildTextResponse("Benchmark scenario consecutive_soft_failures completed.")
                    })
            )
        };

        var completedScenarios = 0;
        var observedDepths = new List<int>();
        var observedCalls = new List<int>();

        foreach (var scenario in scenarios) {
            using var server = scenario.CreateServer();

            var serviceOptions = new ServiceOptions {
                OpenAITransport = OpenAITransportKind.CompatibleHttp,
                OpenAIBaseUrl = server.BaseUrl,
                OpenAIAllowInsecureHttp = true,
                OpenAIStreaming = false,
                Model = "mock-local-model",
                MaxToolRounds = 8,
                DisabledPackIds = { "testimox", "officeimo" }
            };
            var session = new ChatServiceSession(serviceOptions, Stream.Null);
            var registry = new ToolRegistry();
            registry.Register(new RoundTripStubTool(
                "mock_round_tool",
                static (arguments, _) => {
                    var step = arguments?.GetString("step") ?? "unknown";
                    if (step.Contains("fail", StringComparison.Ordinal)) {
                        throw new InvalidOperationException("Injected benchmark soft failure.");
                    }

                    return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
                }));
            SetSessionRegistry(session, registry);

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
                RequestId = "req-autonomy-parity-" + scenario.Name,
                ThreadId = thread.Id,
                Text = "Run benchmark scenario " + scenario.Name + " and continue autonomously until completion.",
                Options = new ChatRequestOptions {
                    WeightedToolRouting = false,
                    MaxToolRounds = 8,
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
            var startedCount = statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase));
            var completedCount = statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));
            Assert.True(startedCount >= completedCount);
            Assert.True(startedCount >= 2);
            Assert.True(completedCount >= 2);

            var toolRounds = GetPropertyValue<int>(runResult, "ToolRounds");
            var toolCalls = GetPropertyValue<int>(runResult, "ToolCallsCount");
            Assert.InRange(toolRounds, 2, 8);
            Assert.InRange(toolCalls, 2, 8);
            observedDepths.Add(toolRounds);
            observedCalls.Add(toolCalls);

            var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
            Assert.Equal("Benchmark scenario " + scenario.Name + " completed.", resultMessage.Text);
            Assert.NotNull(resultMessage.Tools);
            var autonomyTelemetry = Assert.IsType<AutonomyTelemetryDto>(resultMessage.AutonomyTelemetry);
            Assert.Equal(toolRounds, autonomyTelemetry.AutonomyDepth);
            Assert.Equal(1.0d, autonomyTelemetry.CompletionRate);
            Assert.True(autonomyTelemetry.RecoveryEvents >= 0);

            if (string.Equals(scenario.Name, "drop_replay", StringComparison.Ordinal)) {
                Assert.Equal(1, server.DroppedChatCompletionRequestCount);
            } else if (string.Equals(scenario.Name, "consecutive_soft_failures", StringComparison.Ordinal)) {
                Assert.True(autonomyTelemetry.RecoveryEvents > 0);
            }

            completedScenarios++;
        }

        Assert.Equal(scenarios.Length, completedScenarios);
        var completionRate = (double)completedScenarios / scenarios.Length;
        Assert.Equal(1.0d, completionRate);
        Assert.True(observedDepths.Max() >= 3);
        Assert.True(observedDepths.Average() >= 2.0d);
        Assert.True(observedCalls.Average() >= 2.0d);

        static string BuildToolCallResponse(string callId, string step) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-" + callId,
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = callId,
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildMultiToolCallResponse(params (string CallId, string Step)[] calls) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-benchmark-multi",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = calls.Select(call => new {
                                id = call.CallId,
                                type = "function",
                                function = new {
                                    name = "mock_round_tool",
                                    arguments = JsonSerializer.Serialize(new { step = call.Step })
                                }
                            }).ToArray()
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }

        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-benchmark-final",
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
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_CompletesWithEmptyToolRegistryAndOmitsToolSchemas() {
        using var server = new DeterministicCompatibleHttpServer(_ => JsonSerializer.Serialize(new {
            id = "chatcmpl-plugin-isolated",
            @object = "chat.completion",
            choices = new[] {
                new {
                    index = 0,
                    message = new {
                        role = "assistant",
                        content = "Completed without any plugin tools loaded."
                    },
                    finish_reason = "stop"
                }
            }
        }));

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        SetSessionRegistry(session, new ToolRegistry());

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
            RequestId = "req-plugin-isolated-empty-registry",
            ThreadId = thread.Id,
            Text = "Summarize current session status in one short sentence.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = true,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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
        var toolStatuses = statuses.Where(IsToolRelatedStatusCode).ToArray();
        Assert.Empty(toolStatuses);

        Assert.InRange(server.ChatCompletionRequestCount, 1, 2);
        for (var requestIndex = 0; requestIndex < server.ChatCompletionRequestCount; requestIndex++) {
            var requestBody = server.GetChatRequestBody(requestIndex);
            Assert.False(HasToolsArrayProperty(requestBody));
            Assert.Equal(0, CountRoleMessages(requestBody, "tool"));
        }

        Assert.Equal(0, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(0, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Completed without any plugin tools loaded.", resultMessage.Text);
        Assert.Null(resultMessage.Tools);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_AppliesPackTogglesPerTurnWithoutCrossTurnLeakage() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-packa-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_packa_1",
                                    type = "function",
                                    function = new {
                                        name = "packa_echo",
                                        arguments = JsonSerializer.Serialize(new { value = "alpha" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-packa-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Pack A completed."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-packb-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_packb_1",
                                    type = "function",
                                    function = new {
                                        name = "packb_echo",
                                        arguments = JsonSerializer.Serialize(new { value = "beta" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-packb-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Pack B completed."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "packa_echo",
            static (arguments, _) => Task.FromResult(JsonSerializer.Serialize(new { ok = true, value = arguments?.GetString("value") }))));
        registry.Register(new RoundTripStubTool(
            "packb_echo",
            static (arguments, _) => Task.FromResult(JsonSerializer.Serialize(new { ok = true, value = arguments?.GetString("value") }))));
        SetSessionRegistry(session, registry);

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

        var turn1 = new ChatRequest {
            RequestId = "req-pack-toggle-turn-1",
            ThreadId = thread.Id,
            Text = "Use plugin pack A to echo alpha.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                EnabledPackIds = new[] { "packa" },
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureTurn1 = new SynchronizedCaptureStream();
        using var writerTurn1 = new StreamWriter(captureTurn1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var turn1Result = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerTurn1,
            turn1,
            thread.Id,
            CancellationToken.None);

        Assert.Equal(2, server.ChatCompletionRequestCount);
        for (var requestIndex = 0; requestIndex < 2; requestIndex++) {
            var requestBody = server.GetChatRequestBody(requestIndex);
            Assert.True(HasToolSchemaForFunctionName(requestBody, "packa_echo"));
            Assert.False(HasToolSchemaForFunctionName(requestBody, "packb_echo"));
        }

        var turn1Statuses = ParseStatuses(captureTurn1.Snapshot());
        Assert.Equal(1, turn1Statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, turn1Statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        var turn1Message = GetPropertyValue<ChatResultMessage>(turn1Result, "Result");
        Assert.Equal("Pack A completed.", turn1Message.Text);
        Assert.NotNull(turn1Message.Tools);
        Assert.Single(turn1Message.Tools!.Calls);
        Assert.Equal("packa_echo", turn1Message.Tools.Calls[0].Name);

        var turn2 = new ChatRequest {
            RequestId = "req-pack-toggle-turn-2",
            ThreadId = thread.Id,
            Text = "Use plugin pack B to echo beta.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                EnabledPackIds = new[] { "packb" },
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureTurn2 = new SynchronizedCaptureStream();
        using var writerTurn2 = new StreamWriter(captureTurn2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var turn2Result = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerTurn2,
            turn2,
            thread.Id,
            CancellationToken.None);

        Assert.Equal(4, server.ChatCompletionRequestCount);
        for (var requestIndex = 2; requestIndex < 4; requestIndex++) {
            var requestBody = server.GetChatRequestBody(requestIndex);
            Assert.True(HasToolSchemaForFunctionName(requestBody, "packb_echo"));
            Assert.False(HasToolSchemaForFunctionName(requestBody, "packa_echo"));
        }

        var turn2Statuses = ParseStatuses(captureTurn2.Snapshot());
        Assert.Equal(1, turn2Statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, turn2Statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        var turn2Message = GetPropertyValue<ChatResultMessage>(turn2Result, "Result");
        Assert.Equal("Pack B completed.", turn2Message.Text);
        Assert.NotNull(turn2Message.Tools);
        Assert.Single(turn2Message.Tools!.Calls);
        Assert.Equal("packb_echo", turn2Message.Tools.Calls[0].Name);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_DoesNotLeakPackSelectionAcrossThreadsInSameSession() {
        using var server = new DeterministicCompatibleHttpServer(_ => JsonSerializer.Serialize(new {
            id = "chatcmpl-cross-thread-pack-isolation",
            @object = "chat.completion",
            choices = new[] {
                new {
                    index = 0,
                    message = new {
                        role = "assistant",
                        content = "Thread response complete."
                    },
                    finish_reason = "stop"
                }
            }
        }));

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "packa_echo",
            static (arguments, _) => Task.FromResult(JsonSerializer.Serialize(new { ok = true, value = arguments?.GetString("value") }))));
        registry.Register(new RoundTripStubTool(
            "packb_echo",
            static (arguments, _) => Task.FromResult(JsonSerializer.Serialize(new { ok = true, value = arguments?.GetString("value") }))));
        SetSessionRegistry(session, registry);

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
        var threadA = await client.StartNewThreadAsync("mock-local-model");
        var threadB = await client.StartNewThreadAsync("mock-local-model");

        var requestThreadATurn1 = new ChatRequest {
            RequestId = "req-thread-a-turn-1",
            ThreadId = threadA.Id,
            Text = "Thread A turn 1 diagnostics for pack A.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                EnabledPackIds = new[] { "packa" },
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        var requestThreadBTurn1 = new ChatRequest {
            RequestId = "req-thread-b-turn-1",
            ThreadId = threadB.Id,
            Text = "Thread B turn 1 diagnostics for pack B.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                EnabledPackIds = new[] { "packb" },
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        var requestThreadATurn2 = new ChatRequest {
            RequestId = "req-thread-a-turn-2",
            ThreadId = threadA.Id,
            Text = "Thread A turn 2 diagnostics for pack A.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                EnabledPackIds = new[] { "packa" },
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureATurn1 = new SynchronizedCaptureStream();
        using var writerATurn1 = new StreamWriter(captureATurn1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var resultATurn1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerATurn1,
            requestThreadATurn1,
            threadA.Id,
            CancellationToken.None);

        using var captureBTurn1 = new SynchronizedCaptureStream();
        using var writerBTurn1 = new StreamWriter(captureBTurn1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var resultBTurn1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerBTurn1,
            requestThreadBTurn1,
            threadB.Id,
            CancellationToken.None);

        using var captureATurn2 = new SynchronizedCaptureStream();
        using var writerATurn2 = new StreamWriter(captureATurn2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var resultATurn2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerATurn2,
            requestThreadATurn2,
            threadA.Id,
            CancellationToken.None);

        Assert.True(server.ChatCompletionRequestCount >= 3);
        var observedThreadATurn1Requests = 0;
        var observedThreadBTurn1Requests = 0;
        var observedThreadATurn2Requests = 0;
        for (var requestIndex = 0; requestIndex < server.ChatCompletionRequestCount; requestIndex++) {
            var requestBody = server.GetChatRequestBody(requestIndex);
            var latestUserMessage = GetLatestUserMessageContent(requestBody);
            if (latestUserMessage.Contains("Thread A turn 1 diagnostics for pack A.", StringComparison.Ordinal)) {
                observedThreadATurn1Requests++;
                Assert.True(HasToolSchemaForFunctionName(requestBody, "packa_echo"));
                Assert.False(HasToolSchemaForFunctionName(requestBody, "packb_echo"));
            } else if (latestUserMessage.Contains("Thread B turn 1 diagnostics for pack B.", StringComparison.Ordinal)) {
                observedThreadBTurn1Requests++;
                Assert.True(HasToolSchemaForFunctionName(requestBody, "packb_echo"));
                Assert.False(HasToolSchemaForFunctionName(requestBody, "packa_echo"));
            } else if (latestUserMessage.Contains("Thread A turn 2 diagnostics for pack A.", StringComparison.Ordinal)) {
                observedThreadATurn2Requests++;
                Assert.True(HasToolSchemaForFunctionName(requestBody, "packa_echo"));
                Assert.False(HasToolSchemaForFunctionName(requestBody, "packb_echo"));
            }
        }

        Assert.True(observedThreadATurn1Requests > 0);
        Assert.True(observedThreadBTurn1Requests > 0);
        Assert.True(observedThreadATurn2Requests > 0);

        var messageATurn1 = GetPropertyValue<ChatResultMessage>(resultATurn1, "Result");
        Assert.Equal("Thread response complete.", messageATurn1.Text);
        Assert.Null(messageATurn1.Tools);

        var messageBTurn1 = GetPropertyValue<ChatResultMessage>(resultBTurn1, "Result");
        Assert.Equal("Thread response complete.", messageBTurn1.Text);
        Assert.Null(messageBTurn1.Tools);

        var messageATurn2 = GetPropertyValue<ChatResultMessage>(resultATurn2, "Result");
        Assert.Equal("Thread response complete.", messageATurn2.Text);
        Assert.Null(messageATurn2.Tools);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_SustainsFiveAutonomousToolRoundsWithoutUserReprompt() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => BuildAutonomySoakToolCallResponse("call_autonomy_1", "step_1"),
            2 => BuildAutonomySoakToolCallResponse("call_autonomy_2", "step_2"),
            3 => BuildAutonomySoakToolCallResponse("call_autonomy_3", "step_3"),
            4 => BuildAutonomySoakToolCallResponse("call_autonomy_4", "step_4"),
            5 => BuildAutonomySoakToolCallResponse("call_autonomy_5", "step_5"),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-autonomy-soak-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Autonomous 5-round diagnostics completed."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 8,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-autonomy-soak-5-rounds",
            ThreadId = thread.Id,
            Text = "Run the diagnostics workflow through all planned rounds to completion.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 8,
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
        Assert.Equal(5, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(5, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(statuses, static s => string.Equals(s, "tool_round_limit_reached", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(7, server.ChatCompletionRequestCount);
        Assert.Equal(1, CountRoleMessages(server.GetChatRequestBody(0), "user"));
        for (var round = 1; round <= 5; round++) {
            Assert.True(
                ContainsToolMessageForCallId(server.GetChatRequestBody(round), "call_autonomy_" + round),
                "Expected request " + (round + 1) + " to include tool output for call_autonomy_" + round + ".");
        }

        Assert.Equal(5, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(5, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Autonomous 5-round diagnostics completed.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(5, resultMessage.Tools!.Calls.Count);
        Assert.Equal(5, resultMessage.Tools.Outputs.Count);

        static string BuildAutonomySoakToolCallResponse(string callId, string step) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-" + callId,
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = callId,
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            });
        }
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_UsesStructuredContinuationContractForCompactFollowUpTurn() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => JsonSerializer.Serialize(new {
            id = "chatcmpl-continuation-contract",
            @object = "chat.completion",
            choices = new[] {
                new {
                    index = 0,
                    message = new {
                        role = "assistant",
                        content = "Continuation request accepted."
                    },
                    finish_reason = "stop"
                }
            }
        }));

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 3,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult("""{"ok":true}""")));
        SetSessionRegistry(session, registry);

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
        session.RememberUserIntentForTesting(thread.Id, "Run forest-wide replication and LDAP diagnostics.");

        var request = new ChatRequest {
            RequestId = "req-continuation-contract",
            ThreadId = thread.Id,
            Text = "run now",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 3,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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
            request.ThreadId,
            CancellationToken.None);

        Assert.Equal(1, server.ChatCompletionRequestCount);
        var userMessage = GetLatestUserMessageContent(server.GetChatRequestBody(0));
        Assert.Contains("ix:continuation:v1", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled: true", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent_anchor: Run forest-wide replication and LDAP diagnostics.", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("follow_up: run now", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Follow-up: run now", userMessage, StringComparison.OrdinalIgnoreCase);

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Continuation request accepted.", resultMessage.Text);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_UsesSameStructuredIntentForCommonContinuationPrompts() {
        var prompts = new[] { "continue", "keep going", "继续" };
        string? baselineIntentAnchor = null;
        string? baselineContractSignature = null;

        foreach (var followUpPrompt in prompts) {
            using var server = new DeterministicCompatibleHttpServer(responseIndex => JsonSerializer.Serialize(new {
                id = "chatcmpl-continuation-contract-regression",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Continuation regression prompt accepted."
                        },
                        finish_reason = "stop"
                    }
                }
            }));

            var serviceOptions = new ServiceOptions {
                OpenAITransport = OpenAITransportKind.CompatibleHttp,
                OpenAIBaseUrl = server.BaseUrl,
                OpenAIAllowInsecureHttp = true,
                OpenAIStreaming = false,
                Model = "mock-local-model",
                MaxToolRounds = 3,
                DisabledPackIds = { "testimox", "officeimo" }
            };
            var session = new ChatServiceSession(serviceOptions, Stream.Null);
            var registry = new ToolRegistry();
            registry.Register(new RoundTripStubTool(
                "mock_round_tool",
                static (_, _) => Task.FromResult("""{"ok":true}""")));
            SetSessionRegistry(session, registry);

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
            session.RememberUserIntentForTesting(thread.Id, "Run forest-wide replication and LDAP diagnostics.");

            var request = new ChatRequest {
                RequestId = "req-continuation-contract-regression-" + followUpPrompt,
                ThreadId = thread.Id,
                Text = followUpPrompt,
                Options = new ChatRequestOptions {
                    WeightedToolRouting = false,
                    MaxToolRounds = 3,
                    ParallelTools = false,
                    PlanExecuteReviewLoop = false,
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
                request.ThreadId,
                CancellationToken.None);

            Assert.Equal(1, server.ChatCompletionRequestCount);
            var userMessage = GetLatestUserMessageContent(server.GetChatRequestBody(0));
            Assert.Contains("ix:continuation:v1", userMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("enabled: true", userMessage, StringComparison.OrdinalIgnoreCase);

            var intentAnchor = ReadStructuredLineValue(userMessage, "intent_anchor");
            var followUp = ReadStructuredLineValue(userMessage, "follow_up");
            var contractSignature = BuildContinuationContractSignature(userMessage);
            Assert.Equal("Run forest-wide replication and LDAP diagnostics.", intentAnchor);
            Assert.Equal(followUpPrompt, followUp);
            if (baselineIntentAnchor is null) {
                baselineIntentAnchor = intentAnchor;
            } else {
                Assert.Equal(baselineIntentAnchor, intentAnchor);
            }
            if (baselineContractSignature is null) {
                baselineContractSignature = contractSignature;
            } else {
                Assert.Equal(baselineContractSignature, contractSignature);
            }

            var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
            Assert.Equal("Continuation regression prompt accepted.", resultMessage.Text);
        }

        static string ReadStructuredLineValue(string content, string key) {
            var normalized = (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n');
            for (var i = 0; i < lines.Length; i++) {
                var line = lines[i].Trim();
                if (!line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                var value = line[(key.Length + 1)..].Trim();
                return value.Trim('"', '\'');
            }

            throw new Xunit.Sdk.XunitException($"Missing structured field '{key}' in continuation envelope.");
        }

        static string BuildContinuationContractSignature(string content) {
            var normalized = (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = normalized.Split('\n');
            var builder = new StringBuilder();
            for (var i = 0; i < lines.Length; i++) {
                var line = lines[i].Trim();
                if (line.Length == 0) {
                    continue;
                }

                if (line.StartsWith("follow_up:", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (line.StartsWith("follow-up:", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (builder.Length > 0) {
                    builder.Append('\n');
                }

                builder.Append(line);
            }

            return builder.ToString();
        }
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_CarriesSkillsSnapshotIntoAutonomousContinuationLoop() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-skills-continuation-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_skills_continuation_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "skills_continuation" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-skills-continuation-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Skills-guided continuation completed."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 3,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        session.SetCapabilitySnapshotContextForTesting(
            new[] {
                new ToolPackAvailabilityInfo {
                    Id = "active_directory",
                    Name = "Active Directory",
                    SourceKind = "builtin",
                    Enabled = true
                },
                new ToolPackAvailabilityInfo {
                    Id = "eventlog",
                    Name = "Event Log",
                    SourceKind = "builtin",
                    Enabled = true
                }
            },
            new ToolRoutingCatalogDiagnostics {
                TotalTools = 12,
                RoutingAwareTools = 12,
                MissingRoutingContractTools = 0,
                DomainFamilyTools = 2,
                ExpectedDomainFamilyMissingTools = 0,
                DomainFamilyMissingActionTools = 0,
                ActionWithoutFamilyTools = 0,
                FamilyActionConflictFamilies = 0,
                FamilyActions = new[] {
                    new ToolRoutingFamilyActionSummary {
                        Family = "ad_domain",
                        ActionId = "scope_hosts",
                        ToolCount = 4
                    },
                    new ToolRoutingFamilyActionSummary {
                        Family = "public_domain",
                        ActionId = "query_whois",
                        ToolCount = 2
                    }
                }
            });

        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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
        session.RememberUserIntentForTesting(thread.Id, "Continue AD replication and event-log diagnostics across remaining controllers.");
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: thread.Id,
            intentAnchor: "Continue AD replication and event-log diagnostics across remaining controllers.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary", "eventlog_live_query" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: replication backlog remains on DC03." },
            enabledPackIds: new[] { "active_directory", "eventlog" },
            routingFamilies: new[] { "ad_domain", "public_domain" },
            skills: new[] { "ad_domain.scope_hosts", "public_domain.query_whois" },
            healthyToolNames: new[] { "ad_replication_summary", "eventlog_live_query" });

        var request = new ChatRequest {
            RequestId = "req-skills-continuation-loop",
            ThreadId = thread.Id,
            Text = "continue",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 3,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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
            request.ThreadId,
            CancellationToken.None);

        var statuses = ParseStatuses(capture.Snapshot());
        AssertStatusSubsequence(statuses, "tool_round_started", "tool_round_completed");
        Assert.Equal(1, statuses.Count(static s => string.Equals(s, "tool_round_started", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, statuses.Count(static s => string.Equals(s, "tool_round_completed", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal(2, server.ChatCompletionRequestCount);
        var firstRequestBody = server.GetChatRequestBody(0);
        var userMessage = GetLatestUserMessageContent(firstRequestBody);
        var systemMessage = GetLatestMessageContentByRole(firstRequestBody, "system");
        Assert.Contains("ix:continuation:v1", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enabled: true", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("follow_up: continue", userMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:skills:v1", systemMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skills: ad_domain.scope_hosts, public_domain.query_whois", systemMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(ContainsToolMessageForCallId(server.GetChatRequestBody(1), "call_skills_continuation_1"));

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Skills-guided continuation completed.", resultMessage.Text);
        Assert.NotNull(resultMessage.Tools);
        Assert.Single(resultMessage.Tools!.Calls);
        Assert.Single(resultMessage.Tools.Outputs);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_EmitsPhaseHeartbeatDuringLongToolExecution() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-long-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_long_tool_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "long" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-long-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Final answer after long execution."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static async (_, cancellationToken) => {
                await Task.Delay(TimeSpan.FromMilliseconds(1300), cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Serialize(new { ok = true, step = "long" });
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-e2e-long-execute-heartbeat",
            ThreadId = thread.Id,
            Text = "Run one long diagnostic step and summarize the result.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
                ParallelTools = false,
                PlanExecuteReviewLoop = true,
                MaxReviewPasses = 0,
                ModelHeartbeatSeconds = 1
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
        AssertStatusSubsequence(
            statuses,
            "phase_plan",
            "tool_round_started",
            "phase_execute",
            "phase_heartbeat",
            "tool_round_completed",
            "phase_review");

        var phaseExecuteIndex = statuses.IndexOf("phase_execute");
        var phaseHeartbeatIndex = statuses.IndexOf("phase_heartbeat");
        var roundCompletedIndex = statuses.IndexOf("tool_round_completed");
        Assert.True(phaseExecuteIndex >= 0);
        Assert.True(phaseHeartbeatIndex > phaseExecuteIndex);
        Assert.True(roundCompletedIndex > phaseHeartbeatIndex);

        Assert.InRange(server.ChatCompletionRequestCount, 2, 3);
        Assert.Equal(1, GetPropertyValue<int>(runResult, "ToolRounds"));
        Assert.Equal(1, GetPropertyValue<int>(runResult, "ToolCallsCount"));
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Equal("Final answer after long execution.", resultMessage.Text);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RecoversToolOutputSummaryWhenModelReturnsNoTextAfterToolExecution() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-call-1",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_no_text_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "cross_dc" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-empty",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "   "
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "   "
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult("{" +
                                              "\"ok\":true," +
                                              "\"summary_markdown\":\"Cross-DC matrix: AD1 healthy, AD2 has Event 41 signal.\"" +
                                              "}")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-no-text-tool-recovery",
            ThreadId = thread.Id,
            Text = "Check AD2 against other DCs and summarize similarities.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
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

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("Recovered findings from executed tools", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD2 has Event 41 signal", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No response text was produced", resultMessage.Text, StringComparison.OrdinalIgnoreCase);

        var autonomyCounters = GetPropertyValueAssignable<IEnumerable<TurnCounterMetricDto>>(runResult, "AutonomyCounters");
        Assert.Contains(
            autonomyCounters,
            counter => string.Equals(counter.Name, "no_text_tool_output_recovery_hits", StringComparison.Ordinal)
                       && counter.Count >= 1);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_PrefersToolOutputFallbackBeforeNoTextNarrativeRetry() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-call-retry-1",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_no_text_retry_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "cross_dc" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-empty-retry",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "   "
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-retry-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Cross-DC comparison completed. AD1 is healthy and AD2 shows Event 41 signal."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 8,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult("{" +
                                              "\"ok\":true," +
                                              "\"summary_markdown\":\"Cross-DC matrix: AD1 healthy, AD2 has Event 41 signal.\"" +
                                              "}")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-no-text-tool-retry",
            ThreadId = thread.Id,
            Text = "Compare AD1 and AD2 reboot evidence and summarize.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 8,
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

        Assert.InRange(server.ChatCompletionRequestCount, 2, 3);
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("Recovered findings from executed tools", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No response text was produced", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        var autonomyCounters = GetPropertyValueAssignable<IEnumerable<TurnCounterMetricDto>>(runResult, "AutonomyCounters");
        Assert.Contains(
            autonomyCounters,
            counter => string.Equals(counter.Name, "no_text_tool_output_recovery_hits", StringComparison.Ordinal)
                       && counter.Count >= 1);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RedactsToolOutputRecoveryFallbackWhenRedactionEnabled() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-call-redact-1",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_no_text_redact_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "cross_dc" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-empty-redact",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "   "
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-extra-redact",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 3,
            Redact = true,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult("{" +
                                              "\"ok\":true," +
                                              "\"summary_markdown\":\"Contact admin@contoso.local for cross-DC drill-down.\"" +
                                              "}")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-no-text-tool-recovery-redact",
            ThreadId = thread.Id,
            Text = "Check AD2 against other DCs and summarize similarities.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 3,
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

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("[redacted_email]", resultMessage.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("admin@contoso.local", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RecoversFromStructuredPromiseOnlyDraftAndExecutesToolsInSameTurn() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-plan-only",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      Inventory is in; target DCs:
                                      - AD0
                                      - AD1
                                      - AD2
                                      I will return a side-by-side reboot matrix.
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-call-2",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_cross_dc_matrix",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { scope = "cross_dc" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Cross-DC comparison complete: AD0/AD1 stable, AD2 shows recurring Event 41/6008 pairing."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult("{" +
                                              "\"ok\":true," +
                                              "\"summary_markdown\":\"Cross-DC matrix generated from AD0/AD1/AD2 sweep.\"" +
                                              "}")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-structured-promise-recovery",
            ThreadId = thread.Id,
            Text = """
                   Check AD0, AD1 and AD2 for a shared reboot-cause pattern and return one compact matrix.
                   Follow-up: go ahead?
                   """,
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
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

        Assert.InRange(server.ChatCompletionRequestCount, 3, 4);

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("Cross-DC comparison complete", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(resultMessage.Tools);
        Assert.Single(resultMessage.Tools!.Calls);
        Assert.Single(resultMessage.Tools.Outputs);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ReplaysCarryoverStructuredNextActionForCompactGoAheadFollowUp() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-carryover-seed-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_scope_discovery",
                                    type = "function",
                                    function = new {
                                        name = "mock_discover_tool",
                                        arguments = "{}"
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-carryover-seed-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Environment discovery completed for the requested scope. Do you want me to run the live follow-up now?"
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-carryover-followup-draft",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Confirmed. Scope evidence is sufficient."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            4 => JsonSerializer.Serialize(new {
                id = "chatcmpl-carryover-followup-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Follow-up execution completed from the queued read-only action."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_discover_tool",
            static (_, _) => Task.FromResult("""
                                             {"ok":true,"next_actions":[{"tool":"mock_followup_tool","mutating":false,"arguments":{"scope":"live"}}]}
                                             """)));
        registry.Register(new RoundTripStubTool(
            "mock_followup_tool",
            static (_, _) => Task.FromResult("""
                                             {"ok":true,"summary_markdown":"follow-up completed"}
                                             """)));
        SetSessionRegistry(session, registry);

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

        var turn1 = new ChatRequest {
            RequestId = "req-carryover-seed",
            ThreadId = thread.Id,
            Text = "Discover the environment scope and return baseline evidence.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        using var captureTurn1 = new SynchronizedCaptureStream();
        using var writerTurn1 = new StreamWriter(captureTurn1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResultTurn1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerTurn1,
            turn1,
            thread.Id,
            CancellationToken.None);
        var resultMessageTurn1 = GetPropertyValue<ChatResultMessage>(runResultTurn1, "Result");
        Assert.NotNull(resultMessageTurn1.Tools);
        Assert.Single(resultMessageTurn1.Tools!.Calls);
        Assert.Equal("mock_discover_tool", resultMessageTurn1.Tools.Calls[0].Name);

        var turn2 = new ChatRequest {
            RequestId = "req-carryover-followup",
            ThreadId = thread.Id,
            Text = "go ahead",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        using var captureTurn2 = new SynchronizedCaptureStream();
        using var writerTurn2 = new StreamWriter(captureTurn2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResultTurn2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerTurn2,
            turn2,
            thread.Id,
            CancellationToken.None);
        var turn2Statuses = ParseStatuses(captureTurn2.Snapshot());

        Assert.InRange(server.ChatCompletionRequestCount, 4, 6);

        var resultMessageTurn2 = GetPropertyValue<ChatResultMessage>(runResultTurn2, "Result");
        Assert.True(
            resultMessageTurn2.Tools is not null,
            "Expected carryover replay tool output, but no tools were attached. statuses=" + string.Join(",", turn2Statuses));
        Assert.Single(resultMessageTurn2.Tools!.Calls);
        Assert.Single(resultMessageTurn2.Tools.Outputs);
        Assert.Equal("mock_followup_tool", resultMessageTurn2.Tools.Calls[0].Name);
        Assert.StartsWith("host_carryover_next_action_", resultMessageTurn2.Tools.Calls[0].CallId, StringComparison.Ordinal);
        Assert.Equal(resultMessageTurn2.Tools.Calls[0].CallId, resultMessageTurn2.Tools.Outputs[0].CallId);
        var replayCallObservedInModelContext = false;
        for (var i = 0; i < server.ChatCompletionRequestCount; i++) {
            if (ContainsToolMessageForCallId(server.GetChatRequestBody(i), resultMessageTurn2.Tools.Calls[0].CallId)) {
                replayCallObservedInModelContext = true;
                break;
            }
        }

        Assert.True(replayCallObservedInModelContext);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_DoesNotReplaceCompactFollowUpToolQuestionWithCachedEvidenceFallback() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-cache-seed-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_dns_seed",
                                    type = "function",
                                    function = new {
                                        name = "dnsclientx_query",
                                        arguments = JsonSerializer.Serialize(new { query = "ad.evotec.xyz", type = "A" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-cache-seed-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "DNS evidence captured for ad.evotec.xyz."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-capability-question",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "In this runtime I do not have eventlog tools enabled for this session."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            4 => JsonSerializer.Serialize(new {
                id = "chatcmpl-capability-question-review",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "In this runtime I do not have eventlog tools enabled for this session."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-capability-question-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "In this runtime I do not have eventlog tools enabled for this session."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 3,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "dnsclientx_query",
            static (_, _) => Task.FromResult("""
                                             {"ok":true,"summary_markdown":"Resolved ad.evotec.xyz to 192.168.241.6."}
                                             """)));
        SetSessionRegistry(session, registry);

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

        var turn1 = new ChatRequest {
            RequestId = "req-cache-seed",
            ThreadId = thread.Id,
            Text = "Check DNS for ad.evotec.xyz and summarize quickly.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 3,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureTurn1 = new SynchronizedCaptureStream();
        using var writerTurn1 = new StreamWriter(captureTurn1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        _ = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerTurn1,
            turn1,
            thread.Id,
            CancellationToken.None);

        var turn2 = new ChatRequest {
            RequestId = "req-tool-capability-question",
            ThreadId = thread.Id,
            Text = "aale to chyba masz toole do event logow?",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 3,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                DisabledTools = new[] { "dnsclientx_query" },
                ModelHeartbeatSeconds = 0
            }
        };

        using var captureTurn2 = new SynchronizedCaptureStream();
        using var writerTurn2 = new StreamWriter(captureTurn2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResultTurn2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writerTurn2,
            turn2,
            thread.Id,
            CancellationToken.None);

        Assert.InRange(server.ChatCompletionRequestCount, 3, 6);
        var resultMessageTurn2 = GetPropertyValue<ChatResultMessage>(runResultTurn2, "Result");
        Assert.DoesNotContain("ix:cached-tool-evidence:v1", resultMessageTurn2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Resolved ad.evotec.xyz", resultMessageTurn2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not have eventlog tools enabled", resultMessageTurn2.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_AutoBootstrapsDomainEnvironmentAfterNoToolBlockerLoop() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-1",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      Szybki status AD replication forest:
                                      - brak wykrytej domeny/DC
                                      - podaj FQDN kontrolera lub nazwę domeny
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-2",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      AD replication forest - nadal bez punktu zaczepienia:
                                      - brak domeny z autodiscovery
                                      - potrzebny host/FQDN DC
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-3",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      AD replication forest:
                                      - discovery nie zwróciło pełnego scope
                                      - kontynuuję po bootstrapie
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            4 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Auto-scope completed: discovered contoso.local via dc01.contoso.local. Ready to run replication summary."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "ad_environment_discover",
            static (_, _) => Task.FromResult("""
                                             {"ok":true,"summary_markdown":"Auto-scope: contoso.local via dc01.contoso.local"}
                                             """)));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-domain-bootstrap-recovery",
            ThreadId = thread.Id,
            Text = "Check AD replication forest status and summarize what is available right now.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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

        Assert.Equal(4, server.ChatCompletionRequestCount);

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("Auto-scope completed", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(resultMessage.Tools);
        Assert.Single(resultMessage.Tools!.Calls);
        Assert.Single(resultMessage.Tools.Outputs);
        Assert.Equal("ad_environment_discover", resultMessage.Tools.Calls[0].Name);
        Assert.StartsWith("host_pack_preflight_environment_discover_", resultMessage.Tools.Calls[0].CallId, StringComparison.Ordinal);
        Assert.Equal(resultMessage.Tools.Calls[0].CallId, resultMessage.Tools.Outputs[0].CallId);
        Assert.True(resultMessage.Tools.Outputs[0].Ok ?? true);

        var finalRequestBody = server.GetChatRequestBody(3);
        Assert.True(ContainsToolMessageForCallId(finalRequestBody, resultMessage.Tools.Calls[0].CallId));
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ContinuesOperationalAdFlowAfterHostDomainBootstrapReplay() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-continue-1",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      Szybki status AD replication forest:
                                      - brak wykrytej domeny/DC
                                      - podaj FQDN kontrolera lub nazwę domeny
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-continue-2",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      AD replication forest - nadal bez punktu zaczepienia:
                                      - brak domeny z autodiscovery
                                      - potrzebny host/FQDN DC
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-continue-3",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      AD replication forest:
                                      - discovery nie zwróciło pełnego scope
                                      - kontynuuję po bootstrapie
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            4 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-continue-4",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_ad_replication_summary_1",
                                    type = "function",
                                    function = new {
                                        name = "ad_replication_summary",
                                        arguments = JsonSerializer.Serialize(new { scope = "current_forest", time_format = "utc" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            5 => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-continue-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Forest replication is healthy for contoso.local. UTC summary captured after auto-scope discovery."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-domain-bootstrap-continue-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 6,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "ad_environment_discover",
            static (_, _) => Task.FromResult("""
                                             {"ok":true,"summary_markdown":"Auto-scope: contoso.local via dc01.contoso.local"}
                                             """)));
        registry.Register(new RoundTripStubTool(
            "ad_replication_summary",
            static (_, _) => Task.FromResult("""
                                             {"ok":true,"summary_markdown":"UTC replication summary for contoso.local is healthy"}
                                             """)));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-domain-bootstrap-continue-operational",
            ThreadId = thread.Id,
            Text = "Check AD replication forest status and continue with UTC summary.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 6,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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

        Assert.Equal(5, server.ChatCompletionRequestCount);

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("UTC summary", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(2, resultMessage.Tools!.Calls.Count);
        Assert.Equal(2, resultMessage.Tools.Outputs.Count);
        Assert.Equal("ad_environment_discover", resultMessage.Tools.Calls[0].Name);
        Assert.Equal("ad_replication_summary", resultMessage.Tools.Calls[1].Name);
        Assert.StartsWith("host_pack_preflight_environment_discover_", resultMessage.Tools.Calls[0].CallId, StringComparison.Ordinal);
        Assert.Equal("call_ad_replication_summary_1", resultMessage.Tools.Calls[1].CallId);

        var continuationRequestBody = server.GetChatRequestBody(3);
        Assert.Contains("ix:host-domain-bootstrap-continuation:v1", continuationRequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("original_user_request", continuationRequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not stop after merely restating the bootstrap output.", continuationRequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.True(ContainsToolMessageForCallId(continuationRequestBody, resultMessage.Tools.Calls[0].CallId));

        var finalRequestBody = server.GetChatRequestBody(4);
        Assert.True(ContainsToolMessageForCallId(finalRequestBody, "call_ad_replication_summary_1"));
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RecomputesPendingActionsAfterRecoveredNoTextDraft() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-call-action-recovery",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_scope_inventory",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { scope = "dc_inventory" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-action-draft",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = """
                                      Next best read-only action is prepared.
                                      Use the action block below to continue.

                                      [Action]
                                      ix:action:v1
                                      id: act_scope_read
                                      title: Compare AD2 with AD0/AD1 reboot events
                                      request: {"query":{"scope":"read_only"}}
                                      reply: /act act_scope_read
                                      mutating: false
                                      """
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-empty-after-proactive",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "   "
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"Inventory ready.\"}")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-recover-pending-actions-no-text",
            ThreadId = thread.Id,
            Text = """
                   Inventory reboot evidence for AD0, AD1, AD2 and suggest one safe next check.
                   [Proactive execution mode]
                   ix:proactive-mode:v1
                   enabled: true
                   """,
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

        Assert.Equal(3, server.ChatCompletionRequestCount);
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Contains("ix:action:v1", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/act act_scope_read", resultMessage.Text, StringComparison.OrdinalIgnoreCase);

        var expandedSelection = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { thread.Id, "/act act_scope_read" });
        var expandedText = Assert.IsType<string>(expandedSelection);
        Assert.Contains("\"ix_action_selection\"", expandedText, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"act_scope_read\"", expandedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_InsertsPackPreflightBeforeOperationalToolCalls_WhenMissingInRound() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-insert-1",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_ad_search_1",
                                    type = "function",
                                    function = new {
                                        name = "ad_search",
                                        arguments = JsonSerializer.Serialize(new { query = "dc inventory" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-insert-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "AD scope complete."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-insert-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool("ad_pack_info", static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"ad pack ready\"}")));
        registry.Register(new RoundTripStubTool("ad_environment_discover", static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"scope discovered\"}")));
        registry.Register(new RoundTripStubTool("ad_search", static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"search complete\"}")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-pack-preflight-insert",
            ThreadId = thread.Id,
            Text = "Run AD search and summarize scope.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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

        Assert.Equal(2, server.ChatCompletionRequestCount);
        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.NotNull(resultMessage.Tools);
        Assert.Equal(3, resultMessage.Tools!.Calls.Count);
        Assert.Equal("ad_pack_info", resultMessage.Tools.Calls[0].Name);
        Assert.Equal("ad_environment_discover", resultMessage.Tools.Calls[1].Name);
        Assert.Equal("ad_search", resultMessage.Tools.Calls[2].Name);
        Assert.StartsWith("host_pack_preflight_", resultMessage.Tools.Calls[0].CallId, StringComparison.Ordinal);
        Assert.StartsWith("host_pack_preflight_", resultMessage.Tools.Calls[1].CallId, StringComparison.Ordinal);
        Assert.Equal("call_ad_search_1", resultMessage.Tools.Calls[2].CallId);

        var reviewRequestBody = server.GetChatRequestBody(1);
        Assert.True(ContainsToolMessageForCallId(reviewRequestBody, resultMessage.Tools.Calls[0].CallId));
        Assert.True(ContainsToolMessageForCallId(reviewRequestBody, resultMessage.Tools.Calls[1].CallId));
        Assert.True(ContainsToolMessageForCallId(reviewRequestBody, "call_ad_search_1"));
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_DoesNotRepeatPackPreflightOnSameThreadAfterSuccessfulExecution() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-turn1-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_ad_search_turn1",
                                    type = "function",
                                    function = new {
                                        name = "ad_search",
                                        arguments = JsonSerializer.Serialize(new { query = "turn1" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-turn1-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Turn 1 complete."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            3 => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-turn2-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_ad_search_turn2",
                                    type = "function",
                                    function = new {
                                        name = "ad_search",
                                        arguments = JsonSerializer.Serialize(new { query = "turn2" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            4 => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-turn2-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Turn 2 complete."
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-preflight-repeat-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool("ad_pack_info", static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"ad pack ready\"}")));
        registry.Register(new RoundTripStubTool("ad_environment_discover", static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"scope discovered\"}")));
        registry.Register(new RoundTripStubTool("ad_search", static (_, _) => Task.FromResult("{\"ok\":true,\"summary_markdown\":\"search complete\"}")));
        SetSessionRegistry(session, registry);

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

        var request1 = new ChatRequest {
            RequestId = "req-pack-preflight-turn1",
            ThreadId = thread.Id,
            Text = "Run AD search, turn 1.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        var request2 = new ChatRequest {
            RequestId = "req-pack-preflight-turn2",
            ThreadId = thread.Id,
            Text = "Run AD search, turn 2.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture1 = new SynchronizedCaptureStream();
        using var writer1 = new StreamWriter(capture1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer1,
            request1,
            thread.Id,
            CancellationToken.None);

        using var capture2 = new SynchronizedCaptureStream();
        using var writer2 = new StreamWriter(capture2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer2,
            request2,
            thread.Id,
            CancellationToken.None);

        Assert.Equal(4, server.ChatCompletionRequestCount);

        var resultMessage1 = GetPropertyValue<ChatResultMessage>(runResult1, "Result");
        Assert.NotNull(resultMessage1.Tools);
        Assert.Equal(3, resultMessage1.Tools!.Calls.Count);
        Assert.Contains(resultMessage1.Tools.Calls, call => call.CallId.StartsWith("host_pack_preflight_", StringComparison.Ordinal));

        var resultMessage2 = GetPropertyValue<ChatResultMessage>(runResult2, "Result");
        Assert.NotNull(resultMessage2.Tools);
        Assert.Single(resultMessage2.Tools!.Calls);
        Assert.Equal("ad_search", resultMessage2.Tools.Calls[0].Name);
        Assert.Equal("call_ad_search_turn2", resultMessage2.Tools.Calls[0].CallId);
        Assert.DoesNotContain(resultMessage2.Tools.Calls, call => call.CallId.StartsWith("host_pack_preflight_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_CarriesFocusedForestGapAcrossFollowUpThenClearsItAfterResolution() {
        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-forest-followup",
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

        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-forest-turn1-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_forest_replication_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "forest_replication" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: summarize the forest replication state in a table
                resolved_so_far: generated the forest replication table and topology overview
                unresolved_now: explain why ADRODC is absent from the forest replication rows
                carry_forward_unresolved_focus: true
                carry_forward_reason: the missing-controller explanation is still unresolved after showing the table
                primary_artifact: table
                requested_artifact_already_visible_above: false
                requested_artifact_visibility_reason: none
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: provides the requested forest table while keeping the missing-row explanation open

                | Server | Fails | Total Links | % Error |
                | --- | --- | --- | --- |
                | AD0.ad.evotec.xyz | 0 | 16 | 0% |
                | AD1.ad.evotec.xyz | 0 | 12 | 0% |
                | AD2.ad.evotec.xyz | 0 | 16 | 0% |

                ```mermaid
                flowchart TD
                  AD0["AD0"] --> AD1["AD1"]
                  AD1["AD1"] --> AD2["AD2"]
                ```
                """),
            3 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: explain why ADRODC is missing from the table above
                resolved_so_far: the forest replication table is already visible above
                unresolved_now: explain why ADRODC is absent from the forest replication rows
                carry_forward_unresolved_focus: false
                carry_forward_reason: the missing-controller explanation is fully resolved in this turn
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the table above already shows the returned rows, so repeating it adds no value
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: answers the missing-row follow-up without replaying the diagram or table

                The table above already shows the returned forest rows. ADRODC is missing because that run still surfaced domain-scoped replication rows from the upstream collector instead of the complete forest rowset.
                """),
            _ => BuildTextResponse("Unexpected extra chat request.")
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (arguments, _) => {
                var step = arguments?.GetString("step") ?? "unknown";
                return Task.FromResult(JsonSerializer.Serialize(new { ok = true, step }));
            }));
        SetSessionRegistry(session, registry);

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

        var request1 = new ChatRequest {
            RequestId = "req-forest-followup-turn1",
            ThreadId = thread.Id,
            Text = "go ahead and check full ad replication forest",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        var request2 = new ChatRequest {
            RequestId = "req-forest-followup-turn2",
            ThreadId = thread.Id,
            Text = "where is ADRODC in the full forest replication table above, and why are those rows still missing from it?",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        using var capture1 = new SynchronizedCaptureStream();
        using var writer1 = new StreamWriter(capture1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer1,
            request1,
            thread.Id,
            CancellationToken.None);

        var resultMessage1 = GetPropertyValue<ChatResultMessage>(runResult1, "Result");
        Assert.NotNull(resultMessage1.Tools);
        Assert.Single(resultMessage1.Tools!.Calls);
        Assert.Contains("| Server | Fails | Total Links | % Error |", resultMessage1.Text, StringComparison.Ordinal);
        Assert.Contains("```mermaid", resultMessage1.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ix:answer-plan:v1", resultMessage1.Text, StringComparison.OrdinalIgnoreCase);

        var foundInitialFocus = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            thread.Id,
            out var initialRememberedUserGoal,
            out var initialRememberedUnresolvedNow,
            out var initialRememberedPrimaryArtifact);
        Assert.True(foundInitialFocus);
        Assert.Equal("summarize the forest replication state in a table", initialRememberedUserGoal);
        Assert.Equal("explain why ADRODC is absent from the forest replication rows", initialRememberedUnresolvedNow);
        Assert.Equal("table", initialRememberedPrimaryArtifact);

        var followUpPrelude = session.ResolveRoutingPreludeForTesting(thread.Id, request2.Text);
        Assert.True(followUpPrelude.ContinuationExpandedFromContext);
        Assert.True(followUpPrelude.HasStructuredContinuationContext);
        Assert.Contains("ix:continuation-focus:v1", followUpPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last_unresolved_ask: explain why ADRODC is absent from the forest replication rows", followUpPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("follow_up: where is ADRODC in the full forest replication table above, and why are those rows still missing from it?", followUpPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);

        using var capture2 = new SynchronizedCaptureStream();
        using var writer2 = new StreamWriter(capture2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer2,
            request2,
            thread.Id,
            CancellationToken.None);

        Assert.Equal(3, server.ChatCompletionRequestCount);

        var resultMessage2 = GetPropertyValue<ChatResultMessage>(runResult2, "Result");
        Assert.Null(resultMessage2.Tools);
        Assert.Contains("The table above already shows the returned forest rows.", resultMessage2.Text, StringComparison.Ordinal);
        Assert.Contains("ADRODC is missing because that run still surfaced domain-scoped replication rows", resultMessage2.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("```mermaid", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("| Server |", resultMessage2.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ix:answer-plan:v1", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cached evidence fallback", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);

        var foundFocus = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            thread.Id,
            out var rememberedUserGoal,
            out var rememberedUnresolvedNow,
            out var rememberedPrimaryArtifact);
        Assert.True(foundFocus);
        Assert.Equal("explain why ADRODC is missing from the table above", rememberedUserGoal);
        Assert.Equal(string.Empty, rememberedUnresolvedNow);
        Assert.Equal("prose", rememberedPrimaryArtifact);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_DoesNotReplayCachedEvidenceForUnresolvedForestGapContinuation() {
        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-forest-cached-fallback",
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

        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-forest-cache-turn1-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_forest_cache_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "forest_cache" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: summarize the forest replication state in a table
                resolved_so_far: returned the visible forest replication table summary
                unresolved_now: explain why ADRODC is absent from the full replication table
                carry_forward_unresolved_focus: true
                carry_forward_reason: the missing-controller explanation is still unresolved after the summary turn
                primary_artifact: table
                requested_artifact_already_visible_above: false
                requested_artifact_visibility_reason: none
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: returns the visible summary while keeping the missing-row explanation active

                | Server | Health |
                | --- | --- |
                | AD0 | healthy |
                | AD1 | healthy |
                | AD2 | healthy |
                """),
            3 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: explain why ADRODC is absent from the full replication table
                resolved_so_far: the visible forest replication table is still available above
                unresolved_now: explain why ADRODC is absent from the full replication table
                carry_forward_unresolved_focus: true
                carry_forward_reason: the missing-controller explanation still requires a live rerun or deeper evidence
                prefer_cached_evidence_reuse: false
                cached_evidence_reuse_reason: none
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the forest replication table is already visible above
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: clarifies that the missing-row explanation still needs live evidence rather than replaying cached nearby output

                Done. Continuing with the same replication context.
                """),
            4 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: explain why ADRODC is absent from the full replication table
                resolved_so_far: the visible forest replication table is still available above
                unresolved_now: explain why ADRODC is absent from the full replication table
                carry_forward_unresolved_focus: true
                carry_forward_reason: the missing-controller explanation still requires a live rerun or deeper evidence
                prefer_cached_evidence_reuse: false
                cached_evidence_reuse_reason: none
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the forest replication table is already visible above
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: clarifies that the missing-row explanation still needs live evidence rather than replaying cached nearby output

                Done. Continuing with the same replication context.
                """),
            _ => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: explain why ADRODC is absent from the full replication table
                resolved_so_far: the visible forest replication table is still available above
                unresolved_now: explain why ADRODC is absent from the full replication table
                carry_forward_unresolved_focus: true
                carry_forward_reason: the missing-controller explanation still requires a live rerun or deeper evidence
                prefer_cached_evidence_reuse: false
                cached_evidence_reuse_reason: none
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the forest replication table is already visible above
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: clarifies that the missing-row explanation still needs live evidence rather than replaying cached nearby output

                Done. Continuing with the same replication context.
                """)
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult(
                """{"ok":true,"summary_markdown":"Full forest replication table shows AD0, AD1, AD2, and ADRODC is absent from the returned rows."}""")));
        SetSessionRegistry(session, registry);

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

        var request1 = new ChatRequest {
            RequestId = "req-forest-cache-turn1",
            ThreadId = thread.Id,
            Text = "go ahead and check full ad replication forest",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        var request2 = new ChatRequest {
            RequestId = "req-forest-cache-turn2",
            ThreadId = thread.Id,
            Text = "continue ADRODC absent",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture1 = new SynchronizedCaptureStream();
        using var writer1 = new StreamWriter(capture1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer1,
            request1,
            thread.Id,
            CancellationToken.None);

        var rememberedInitialFocus = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            thread.Id,
            out _,
            out var rememberedInitialUnresolvedNow,
            out _);
        Assert.True(rememberedInitialFocus);
        Assert.Equal("explain why ADRODC is absent from the full replication table", rememberedInitialUnresolvedNow);

        var initialFocusPrelude = session.ResolveRoutingPreludeForTesting(thread.Id, request2.Text);
        Assert.True(initialFocusPrelude.ContinuationExpandedFromContext);

        using var capture2 = new SynchronizedCaptureStream();
        using var writer2 = new StreamWriter(capture2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer2,
            request2,
            thread.Id,
            CancellationToken.None);

        Assert.InRange(server.ChatCompletionRequestCount, 3, 6);

        var resultMessage1 = GetPropertyValue<ChatResultMessage>(runResult1, "Result");
        Assert.NotNull(resultMessage1.Tools);
        Assert.Single(resultMessage1.Tools!.Calls);
        Assert.DoesNotContain("ix:answer-plan:v1", resultMessage1.Text, StringComparison.OrdinalIgnoreCase);

        var resultMessage2 = GetPropertyValue<ChatResultMessage>(runResult2, "Result");
        Assert.Null(resultMessage2.Tools);
        Assert.Contains("[Execution blocked]", resultMessage2.Text, StringComparison.Ordinal);
        Assert.Contains("Reason code:", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cached evidence fallback", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ix:cached-tool-evidence:v1", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ADRODC is absent from the returned rows.", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);

        var rememberedFocus = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            thread.Id,
            out _,
            out var rememberedUnresolvedNow,
            out _);
        Assert.True(rememberedFocus);
        Assert.Equal("explain why ADRODC is absent from the full replication table", rememberedUnresolvedNow);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_ReusesCachedEvidenceForResolvedForestContinuation() {
        static string BuildTextResponse(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-forest-cached-fallback-safe",
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

        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-forest-cache-safe-turn1-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_forest_cache_safe_1",
                                    type = "function",
                                    function = new {
                                        name = "mock_round_tool",
                                        arguments = JsonSerializer.Serialize(new { step = "forest_cache_safe" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: summarize the forest replication state in a table
                resolved_so_far: returned the visible forest replication table summary
                unresolved_now: none
                carry_forward_unresolved_focus: false
                carry_forward_reason: the summary turn fully answers the requested replication status check
                prefer_cached_evidence_reuse: false
                cached_evidence_reuse_reason: none
                primary_artifact: table
                requested_artifact_already_visible_above: false
                requested_artifact_visibility_reason: none
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: returns the requested forest summary and leaves no unresolved follow-up gap

                | Server | Health |
                | --- | --- |
                | AD0 | healthy |
                | AD1 | healthy |
                | AD2 | healthy |
                """),
            3 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: continue from the same forest replication evidence
                resolved_so_far: the forest replication table is already available above
                unresolved_now: none
                carry_forward_unresolved_focus: false
                carry_forward_reason: this continuation reuses the already-resolved evidence snapshot
                prefer_cached_evidence_reuse: true
                cached_evidence_reuse_reason: compact continuation should reuse the latest forest replication evidence snapshot
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the forest replication table is already visible above
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: confirms that the next step should reuse the same forest replication evidence without a rerun

                Reusing the latest forest replication evidence for AD0, AD1, and AD2.
                """),
            4 => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: continue from the same forest replication evidence
                resolved_so_far: the forest replication table is already available above
                unresolved_now: none
                carry_forward_unresolved_focus: false
                carry_forward_reason: this continuation reuses the already-resolved evidence snapshot
                prefer_cached_evidence_reuse: true
                cached_evidence_reuse_reason: compact continuation should reuse the latest forest replication evidence snapshot
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the forest replication table is already visible above
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: confirms that the next step should reuse the same forest replication evidence without a rerun

                Reusing the latest forest replication evidence for AD0, AD1, and AD2.
                """),
            _ => BuildTextResponse("""
                [Answer progression plan]
                ix:answer-plan:v1
                user_goal: continue from the same forest replication evidence
                resolved_so_far: the forest replication table is already available above
                unresolved_now: none
                carry_forward_unresolved_focus: false
                carry_forward_reason: this continuation reuses the already-resolved evidence snapshot
                prefer_cached_evidence_reuse: true
                cached_evidence_reuse_reason: compact continuation should reuse the latest forest replication evidence snapshot
                primary_artifact: prose
                requested_artifact_already_visible_above: true
                requested_artifact_visibility_reason: the forest replication table is already visible above
                repeats_prior_visible_content: false
                prior_visible_delta_reason: none
                reuse_prior_visuals: false
                reuse_reason: none
                repeat_adds_new_information: true
                repeat_novelty_reason: none
                advances_current_ask: true
                advance_reason: confirms that the next step should reuse the same forest replication evidence without a rerun

                Reusing the latest forest replication evidence for AD0, AD1, and AD2.
                """)
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool(
            "mock_round_tool",
            static (_, _) => Task.FromResult(
                """{"ok":true,"summary_markdown":"Full forest replication table shows AD0, AD1, and AD2 with healthy replication."}""")));
        SetSessionRegistry(session, registry);

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

        var request1 = new ChatRequest {
            RequestId = "req-forest-cache-safe-turn1",
            ThreadId = thread.Id,
            Text = "go ahead and check full ad replication forest",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };
        var request2 = new ChatRequest {
            RequestId = "req-forest-cache-safe-turn2",
            ThreadId = thread.Id,
            Text = "continue replication AD2",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
                ModelHeartbeatSeconds = 0
            }
        };

        using var capture1 = new SynchronizedCaptureStream();
        using var writer1 = new StreamWriter(capture1, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult1 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer1,
            request1,
            thread.Id,
            CancellationToken.None);

        var initialFocusPrelude = session.ResolveRoutingPreludeForTesting(thread.Id, request2.Text);
        Assert.DoesNotContain("ix:continuation-focus:v1", initialFocusPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last_unresolved_ask:", initialFocusPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);

        using var capture2 = new SynchronizedCaptureStream();
        using var writer2 = new StreamWriter(capture2, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
        var runResult2 = await InvokeRunChatOnCurrentThreadAsync(
            session,
            client,
            writer2,
            request2,
            thread.Id,
            CancellationToken.None);

        Assert.InRange(server.ChatCompletionRequestCount, 3, 6);

        var resultMessage1 = GetPropertyValue<ChatResultMessage>(runResult1, "Result");
        Assert.NotNull(resultMessage1.Tools);
        Assert.Single(resultMessage1.Tools!.Calls);
        Assert.DoesNotContain("ix:answer-plan:v1", resultMessage1.Text, StringComparison.OrdinalIgnoreCase);

        var resultMessage2 = GetPropertyValue<ChatResultMessage>(runResult2, "Result");
        Assert.Null(resultMessage2.Tools);
        Assert.Contains("[Cached evidence fallback]", resultMessage2.Text, StringComparison.Ordinal);
        Assert.Contains("ix:cached-tool-evidence:v1", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mock_round_tool", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD2", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Execution blocked]", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ix:answer-plan:v1", resultMessage2.Text, StringComparison.OrdinalIgnoreCase);

        var rememberedFocus = session.TryGetWorkingMemoryAnswerPlanFocusForTesting(
            thread.Id,
            out var rememberedUserGoal,
            out var rememberedUnresolvedNow,
            out var rememberedPrimaryArtifact);
        Assert.True(rememberedFocus);
        Assert.Equal("continue from the same forest replication evidence", rememberedUserGoal);
        Assert.Equal(string.Empty, rememberedUnresolvedNow);
        Assert.Equal("prose", rememberedPrimaryArtifact);

        var followOnPrelude = session.ResolveRoutingPreludeForTesting(thread.Id, "continue replication AD1");
        Assert.Contains("ix:continuation-focus:v1", followOnPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last_unresolved_ask:", followOnPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("last_prefer_cached_evidence_reuse: true", followOnPrelude.RoutedUserRequest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "last_cached_evidence_reuse_reason: compact continuation should reuse the latest forest replication evidence snapshot",
            followOnPrelude.RoutedUserRequest,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_DoesNotAutoSwitchPacksAfterToolFailure() {
        using var server = new DeterministicCompatibleHttpServer(responseIndex => responseIndex switch {
            1 => JsonSerializer.Serialize(new {
                id = "chatcmpl-no-cross-pack-fallback-call",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            tool_calls = new[] {
                                new {
                                    id = "call_ad_search_fail_1",
                                    type = "function",
                                    function = new {
                                        name = "ad_search",
                                        arguments = JsonSerializer.Serialize(new { query = "dc-2 reboot timeline" })
                                    }
                                }
                            }
                        },
                        finish_reason = "tool_calls"
                    }
                }
            }),
            2 => JsonSerializer.Serialize(new {
                id = "chatcmpl-no-cross-pack-fallback-final",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "   "
                        },
                        finish_reason = "stop"
                    }
                }
            }),
            _ => JsonSerializer.Serialize(new {
                id = "chatcmpl-no-cross-pack-fallback-extra",
                @object = "chat.completion",
                choices = new[] {
                    new {
                        index = 0,
                        message = new {
                            role = "assistant",
                            content = "Unexpected extra chat request."
                        },
                        finish_reason = "stop"
                    }
                }
            })
        });

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();

        var dnsClientXCalls = 0;
        registry.Register(new RoundTripStubTool(
            "ad_search",
            static (_, _) => throw new InvalidOperationException("Injected permanent failure for regression coverage.")));
        registry.Register(new RoundTripStubTool(
            "dnsclientx_query",
            (_, _) => {
                Interlocked.Increment(ref dnsClientXCalls);
                return Task.FromResult("""{"ok":true,"summary_markdown":"dns fallback executed"}""");
            }));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-no-cross-pack-fallback-after-failure",
            ThreadId = thread.Id,
            Text = "Run AD search for reboot anomalies and summarize what happened.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = false,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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

        Assert.Equal(2, server.ChatCompletionRequestCount);
        Assert.Equal(0, Volatile.Read(ref dnsClientXCalls));

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.NotNull(resultMessage.Tools);
        Assert.Single(resultMessage.Tools!.Calls);
        Assert.Single(resultMessage.Tools.Outputs);
        Assert.Equal("ad_search", resultMessage.Tools.Calls[0].Name);
        Assert.Equal("call_ad_search_fail_1", resultMessage.Tools.Calls[0].CallId);
        Assert.DoesNotContain(resultMessage.Tools.Calls, static call =>
            string.Equals(call.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("call_ad_search_fail_1", resultMessage.Tools.Outputs[0].CallId);
        Assert.Equal("tool_exception", resultMessage.Tools.Outputs[0].ErrorCode);

        var reviewRequestBody = server.GetChatRequestBody(1);
        Assert.True(ContainsToolMessageForCallId(reviewRequestBody, "call_ad_search_fail_1"));
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RequestsDomainScopeClarificationForAmbiguousMixedSignalsBeforeExecution() {
        using var server = new DeterministicCompatibleHttpServer();

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool("ad_scope_discovery", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new RoundTripStubTool("dnsclientx_query", static (_, _) => Task.FromResult("""{"ok":true}""")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-domain-intent-clarify-ambiguous",
            ThreadId = thread.Id,
            Text = "Please run AD LDAP and DNS MX checks together now.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = true,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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

        Assert.Equal(0, server.ChatCompletionRequestCount);

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Null(resultMessage.Tools);
        Assert.Contains("I can check that", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("which side", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.", resultMessage.Text, StringComparison.Ordinal);
        Assert.Contains("2.", resultMessage.Text, StringComparison.Ordinal);
        Assert.Contains("AD domain", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Public domain", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunChatOnCurrentThreadAsync_RequestsDomainScopeClarificationForParentChildDomainPairBeforeExecution() {
        using var server = new DeterministicCompatibleHttpServer();

        var serviceOptions = new ServiceOptions {
            OpenAITransport = OpenAITransportKind.CompatibleHttp,
            OpenAIBaseUrl = server.BaseUrl,
            OpenAIAllowInsecureHttp = true,
            OpenAIStreaming = false,
            Model = "mock-local-model",
            MaxToolRounds = 4,
            DisabledPackIds = { "testimox", "officeimo" }
        };
        var session = new ChatServiceSession(serviceOptions, Stream.Null);
        var registry = new ToolRegistry();
        registry.Register(new RoundTripStubTool("ad_scope_discovery", static (_, _) => Task.FromResult("""{"ok":true}""")));
        registry.Register(new RoundTripStubTool("dnsclientx_query", static (_, _) => Task.FromResult("""{"ok":true}""")));
        SetSessionRegistry(session, registry);

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
            RequestId = "req-domain-intent-clarify-parent-child",
            ThreadId = thread.Id,
            Text = "Check domain health for corp.contoso.com and contoso.com.",
            Options = new ChatRequestOptions {
                WeightedToolRouting = true,
                MaxToolRounds = 4,
                ParallelTools = false,
                PlanExecuteReviewLoop = false,
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

        Assert.Equal(0, server.ChatCompletionRequestCount);

        var resultMessage = GetPropertyValue<ChatResultMessage>(runResult, "Result");
        Assert.Null(resultMessage.Tools);
        Assert.Contains("I can check that", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("which side", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD domain", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Public domain", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetSessionRegistry(ChatServiceSession session, ToolRegistry registry) {
        RegistryField.SetValue(session, registry);
        var catalog = ToolOrchestrationCatalog.Build(registry.GetDefinitions());
        ToolOrchestrationCatalogField.SetValue(session, catalog);
    }
}
