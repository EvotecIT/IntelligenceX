using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {    private static async Task<object> InvokeRunChatOnCurrentThreadAsync(ChatServiceSession session, IntelligenceXClient client, StreamWriter writer,
        ChatRequest request, string threadId, CancellationToken cancellationToken) {
        var taskObj = RunChatOnCurrentThreadAsyncMethod.Invoke(session, new object?[] { client, writer, request, threadId, cancellationToken });
        var task = Assert.IsAssignableFrom<Task>(taskObj);
        await task.ConfigureAwait(false);

        var resultProperty = taskObj!.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)
                             ?? throw new InvalidOperationException("Task result property not found.");
        return resultProperty.GetValue(taskObj)
               ?? throw new InvalidOperationException("RunChatOnCurrentThreadAsync returned null.");
    }

    private static T GetPropertyValue<T>(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        return Assert.IsType<T>(property.GetValue(instance));
    }

    private static void AssertStatusSubsequence(IReadOnlyList<string> statuses, params string[] expectedSequence) {
        var currentIndex = 0;
        for (var i = 0; i < expectedSequence.Length; i++) {
            var expected = expectedSequence[i];
            var found = false;
            for (; currentIndex < statuses.Count; currentIndex++) {
                if (!string.Equals(statuses[currentIndex], expected, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                found = true;
                currentIndex++;
                break;
            }

            if (!found) {
                throw new Xunit.Sdk.XunitException($"Expected status subsequence item '{expected}' was not found in order.");
            }
        }
    }

    private static int CountRoleMessages(string requestBody, string role) {
        using var doc = JsonDocument.Parse(requestBody);
        if (!doc.RootElement.TryGetProperty("messages", out var messages)
            || messages.ValueKind != System.Text.Json.JsonValueKind.Array) {
            return 0;
        }

        var count = 0;
        foreach (var message in messages.EnumerateArray()) {
            if (!message.TryGetProperty("role", out var roleEl)) {
                continue;
            }

            if (string.Equals(roleEl.GetString(), role, StringComparison.OrdinalIgnoreCase)) {
                count++;
            }
        }

        return count;
    }

    private static bool ContainsToolMessageForCallId(string requestBody, string callId) {
        using var doc = JsonDocument.Parse(requestBody);
        if (!doc.RootElement.TryGetProperty("messages", out var messages)
            || messages.ValueKind != System.Text.Json.JsonValueKind.Array) {
            return false;
        }

        foreach (var message in messages.EnumerateArray()) {
            if (!message.TryGetProperty("role", out var roleEl)
                || !string.Equals(roleEl.GetString(), "tool", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (message.TryGetProperty("tool_call_id", out var callIdEl)
                && string.Equals(callIdEl.GetString(), callId, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private sealed class RoundTripStubTool : ITool {
        private readonly Func<JsonObject?, CancellationToken, Task<string>> _invoke;

        public RoundTripStubTool(string name, Func<JsonObject?, CancellationToken, Task<string>> invoke) {
            Definition = new ToolDefinition(name, description: "roundtrip stub");
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }
    }

    private sealed class DeterministicCompatibleHttpServer : IDisposable {
        private readonly TcpListener _listener;
        private readonly Task _acceptLoop;
        private readonly object _sync = new();
        private readonly List<string> _chatCompletionRequestBodies = new();
        private readonly Func<int, string>? _chatCompletionResponder;
        private readonly HashSet<int> _dropChatCompletionResponseOnRequestIndices;
        private readonly bool _emitReplayDuplicateToolCallAfterDrop;
        private readonly bool _emitReplayMixedToolCallsAfterDrop;
        private readonly bool _emitReplayMixedToolCallsAfterDropReordered;
        private readonly bool _emitReplayDelayedMixedToolCallsAfterDrop;
        private readonly bool _emitReplayMismatchedToolCallArgumentsAfterDrop;
        private readonly bool _emitReplayMixedMismatchedAndFreshToolCallsAfterDrop;
        private readonly List<int> _droppedChatCompletionRequests = new();
        private volatile bool _disposed;
        private int _successfulChatCompletionResponses;

        public DeterministicCompatibleHttpServer(
            Func<int, string>? chatCompletionResponder = null,
            IEnumerable<int>? dropChatCompletionResponseOnRequestIndices = null,
            bool emitReplayDuplicateToolCallAfterDrop = false,
            bool emitReplayMixedToolCallsAfterDrop = false,
            bool emitReplayMixedToolCallsAfterDropReordered = false,
            bool emitReplayDelayedMixedToolCallsAfterDrop = false,
            bool emitReplayMismatchedToolCallArgumentsAfterDrop = false,
            bool emitReplayMixedMismatchedAndFreshToolCallsAfterDrop = false) {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}/v1";
            _chatCompletionResponder = chatCompletionResponder;
            _dropChatCompletionResponseOnRequestIndices = dropChatCompletionResponseOnRequestIndices is null
                ? new HashSet<int>()
                : new HashSet<int>(dropChatCompletionResponseOnRequestIndices.Where(static index => index > 0));
            _emitReplayDuplicateToolCallAfterDrop = emitReplayDuplicateToolCallAfterDrop;
            _emitReplayMixedToolCallsAfterDrop = emitReplayMixedToolCallsAfterDrop;
            _emitReplayMixedToolCallsAfterDropReordered = emitReplayMixedToolCallsAfterDropReordered;
            _emitReplayDelayedMixedToolCallsAfterDrop = emitReplayDelayedMixedToolCallsAfterDrop;
            _emitReplayMismatchedToolCallArgumentsAfterDrop = emitReplayMismatchedToolCallArgumentsAfterDrop;
            _emitReplayMixedMismatchedAndFreshToolCallsAfterDrop = emitReplayMixedMismatchedAndFreshToolCallsAfterDrop;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        public int ChatCompletionRequestCount {
            get {
                lock (_sync) {
                    return _chatCompletionRequestBodies.Count;
                }
            }
        }

        public int DroppedChatCompletionRequestCount {
            get {
                lock (_sync) {
                    return _droppedChatCompletionRequests.Count;
                }
            }
        }

        public string GetChatRequestBody(int index) {
            lock (_sync) {
                return _chatCompletionRequestBodies[index];
            }
        }

        private async Task AcceptLoopAsync() {
            while (!_disposed) {
                TcpClient? client = null;
                try {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                } catch (ObjectDisposedException) {
                    break;
                } catch (SocketException) when (_disposed) {
                    break;
                }

                if (client is null) {
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private async Task HandleClientAsync(TcpClient client) {
            using var _ = client;
            using var stream = client.GetStream();

            var headerBytes = new List<byte>(1024);
            var delimiter = new byte[] { 13, 10, 13, 10 };
            var matched = 0;
            var singleByte = new byte[1];

            while (true) {
                var read = await stream.ReadAsync(singleByte, 0, 1).ConfigureAwait(false);
                if (read == 0) {
                    return;
                }

                var b = singleByte[0];
                headerBytes.Add(b);
                if (b == delimiter[matched]) {
                    matched++;
                    if (matched == delimiter.Length) {
                        break;
                    }
                } else {
                    matched = b == delimiter[0] ? 1 : 0;
                }
            }

            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines.Length > 0 ? lines[0] : string.Empty;
            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                return;
            }

            var method = parts[0];
            var path = NormalizePath(parts[1]);

            var contentLength = 0;
            for (var i = 1; i < lines.Length; i++) {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) {
                    break;
                }

                var colon = line.IndexOf(':');
                if (colon <= 0) {
                    continue;
                }

                var key = line.Substring(0, colon).Trim();
                var value = line[(colon + 1)..].Trim();
                if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                    int.TryParse(value, out contentLength);
                }
            }

            var body = string.Empty;
            if (contentLength > 0) {
                var buffer = new byte[contentLength];
                var total = 0;
                while (total < contentLength) {
                    var read = await stream.ReadAsync(buffer, total, contentLength - total).ConfigureAwait(false);
                    if (read == 0) {
                        break;
                    }
                    total += read;
                }

                body = Encoding.UTF8.GetString(buffer, 0, total);
            }

            var responseBody = HandleRequest(method, path, body, out var responseCode, out var responseStatus, out var closeWithoutResponse);
            if (closeWithoutResponse) {
                return;
            }

            var responsePayloadBytes = Encoding.UTF8.GetBytes(responseBody);
            var responseHeader = $"HTTP/1.1 {responseCode} {responseStatus}\r\n"
                                 + "Content-Type: application/json\r\n"
                                 + $"Content-Length: {responsePayloadBytes.Length}\r\n"
                                 + "Connection: close\r\n\r\n";
            var responseHeaderBytes = Encoding.ASCII.GetBytes(responseHeader);
            await stream.WriteAsync(responseHeaderBytes, 0, responseHeaderBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(responsePayloadBytes, 0, responsePayloadBytes.Length).ConfigureAwait(false);
        }

        private string HandleRequest(string method, string path, string body, out int code, out string status, out bool closeWithoutResponse) {
            closeWithoutResponse = false;
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path, "/v1/models", StringComparison.OrdinalIgnoreCase)) {
                code = 200;
                status = "OK";
                return JsonSerializer.Serialize(new {
                    @object = "list",
                    data = new[] {
                        new { id = "mock-local-model", @object = "model" }
                    }
                });
            }

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path, "/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                int requestIndex;
                var shouldDrop = false;
                int responseIndex = 0;
                lock (_sync) {
                    _chatCompletionRequestBodies.Add(body);
                    requestIndex = _chatCompletionRequestBodies.Count;
                    shouldDrop = _dropChatCompletionResponseOnRequestIndices.Contains(requestIndex);
                    if (shouldDrop) {
                        _droppedChatCompletionRequests.Add(requestIndex);
                    } else {
                        _successfulChatCompletionResponses++;
                        responseIndex = _successfulChatCompletionResponses;
                    }
                }

                if (shouldDrop) {
                    closeWithoutResponse = true;
                    code = 0;
                    status = string.Empty;
                    return string.Empty;
                }

                code = 200;
                status = "OK";
                if (_chatCompletionResponder is not null) {
                    return _chatCompletionResponder(responseIndex);
                }

                if (_emitReplayMixedToolCallsAfterDropReordered) {
                    return responseIndex switch {
                        1 => BuildToolCallCompletionBody("call_round_1", "one"),
                        2 => BuildMultiToolCallCompletionBody(
                            ("call_round_2", "two"),
                            ("call_round_1", "one")),
                        3 => BuildTextCompletionBody("Final answer after two tool rounds."),
                        _ => BuildTextCompletionBody("Unexpected extra chat request.")
                    };
                }

                if (_emitReplayMixedToolCallsAfterDrop) {
                    return responseIndex switch {
                        1 => BuildToolCallCompletionBody("call_round_1", "one"),
                        2 => BuildMultiToolCallCompletionBody(
                            ("call_round_1", "one"),
                            ("call_round_2", "two")),
                        3 => BuildTextCompletionBody("Final answer after two tool rounds."),
                        _ => BuildTextCompletionBody("Unexpected extra chat request.")
                    };
                }

                if (_emitReplayDelayedMixedToolCallsAfterDrop) {
                    return responseIndex switch {
                        1 => BuildToolCallCompletionBody("call_round_1", "one"),
                        2 => BuildToolCallCompletionBody("call_round_2", "two"),
                        3 => BuildMultiToolCallCompletionBody(
                            ("call_round_1", "one"),
                            ("call_round_3", "three")),
                        4 => BuildTextCompletionBody("Final answer after delayed mixed replay recovery."),
                        _ => BuildTextCompletionBody("Unexpected extra chat request.")
                    };
                }

                if (_emitReplayDuplicateToolCallAfterDrop) {
                    return responseIndex switch {
                        1 => BuildToolCallCompletionBody("call_round_1", "one"),
                        2 => BuildToolCallCompletionBody("call_round_1", "one"),
                        3 => BuildToolCallCompletionBody("call_round_2", "two"),
                        4 => BuildTextCompletionBody("Final answer after two tool rounds."),
                        _ => BuildTextCompletionBody("Unexpected extra chat request.")
                    };
                }

                if (_emitReplayMismatchedToolCallArgumentsAfterDrop) {
                    return responseIndex switch {
                        1 => BuildToolCallCompletionBody("call_round_1", "one"),
                        2 => BuildToolCallCompletionBody("call_round_1", "two"),
                        3 => BuildTextCompletionBody("Final answer after replay mismatch recovery."),
                        _ => BuildTextCompletionBody("Unexpected extra chat request.")
                    };
                }

                if (_emitReplayMixedMismatchedAndFreshToolCallsAfterDrop) {
                    return responseIndex switch {
                        1 => BuildToolCallCompletionBody("call_round_1", "one"),
                        2 => BuildMultiToolCallCompletionBody(
                            ("call_round_1", "two"),
                            ("call_round_2", "three")),
                        3 => BuildTextCompletionBody("Final answer after mixed replay mismatch recovery."),
                        _ => BuildTextCompletionBody("Unexpected extra chat request.")
                    };
                }

                return responseIndex switch {
                    1 => BuildToolCallCompletionBody("call_round_1", "one"),
                    2 => BuildToolCallCompletionBody("call_round_2", "two"),
                    3 => BuildTextCompletionBody("Final answer after two tool rounds."),
                    _ => BuildTextCompletionBody("Unexpected extra chat request.")
                };
            }

            code = 404;
            status = "Not Found";
            return """{"error":"not_found"}""";
        }

        private static string BuildToolCallCompletionBody(string callId, string step) {
            return JsonSerializer.Serialize(new {
                id = $"chatcmpl-{callId}",
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

        private static string BuildMultiToolCallCompletionBody(params (string CallId, string Step)[] calls) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-multi-call",
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

        private static string BuildTextCompletionBody(string text) {
            return JsonSerializer.Serialize(new {
                id = "chatcmpl-final",
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

        private static string NormalizePath(string rawPath) {
            if (Uri.TryCreate(rawPath, UriKind.Absolute, out var absolute)) {
                return absolute.AbsolutePath;
            }

            var queryIndex = rawPath.IndexOf('?', StringComparison.Ordinal);
            return queryIndex < 0 ? rawPath : rawPath[..queryIndex];
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _listener.Stop();
            try {
                _acceptLoop.Wait(TimeSpan.FromSeconds(1));
            } catch {
                // Ignore loop teardown failures in tests.
            }
        }
    }
}

