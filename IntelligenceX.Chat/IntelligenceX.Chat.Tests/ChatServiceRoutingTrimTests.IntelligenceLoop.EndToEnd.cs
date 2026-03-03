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

        Assert.Equal(3, server.ChatCompletionRequestCount);
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
        Assert.Contains("Recovered findings from executed tools", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD2 has Event 41 signal", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No response text was produced", resultMessage.Text, StringComparison.OrdinalIgnoreCase);

        var autonomyCounters = GetPropertyValue<List<TurnCounterMetricDto>>(runResult, "AutonomyCounters");
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
            MaxToolRounds = 4,
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
            MaxToolRounds = 4,
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
                MaxToolRounds = 4,
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

        Assert.InRange(server.ChatCompletionRequestCount, 4, 6);

        var resultMessageTurn2 = GetPropertyValue<ChatResultMessage>(runResultTurn2, "Result");
        Assert.NotNull(resultMessageTurn2.Tools);
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
            MaxToolRounds = 4,
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
        Assert.Contains("I need a quick scope choice before continuing.", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.", resultMessage.Text, StringComparison.Ordinal);
        Assert.Contains("2.", resultMessage.Text, StringComparison.Ordinal);
        Assert.Contains("AD domain", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Public domain", resultMessage.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetSessionRegistry(ChatServiceSession session, ToolRegistry registry) {
        RegistryField.SetValue(session, registry);
        var catalog = ToolOrchestrationCatalog.Build(registry.GetDefinitions());
        ToolOrchestrationCatalogField.SetValue(session, catalog);
    }
}
