using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.ToolCalling;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class OpenAICompatibleHttpTransportTests {
    [Fact]
    public async Task ListModelsAsync_LmStudioCatalogOverlay_OnlyEnrichesPrimaryModels() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "data": [
                    { "id": "google/gemma-3-4b", "object": "model", "owned_by": "organization_owner" }
                  ],
                  "object": "list"
                }
                """)
            .RespondJson(HttpStatusCode.OK, """
                {
                  "data": [
                    {
                      "id": "google/gemma-3-4b",
                      "object": "model",
                      "state": "loaded",
                      "arch": "gemma3",
                      "quantization": "Q4_K_M",
                      "max_context_length": 131072,
                      "loaded_context_length": 4096,
                      "capabilities": ["tool_use"]
                    },
                    {
                      "id": "openai/gpt-oss-20b",
                      "object": "model",
                      "state": "not-loaded",
                      "arch": "gpt-oss",
                      "quantization": "MXFP4"
                    }
                  ],
                  "object": "list"
                }
                """);

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://127.0.0.1:1234/v1",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        var result = await transport.ListModelsAsync(CancellationToken.None);
        Assert.Single(result.Models);

        var gemma = Assert.Single(result.Models, m => string.Equals(m.Model, "google/gemma-3-4b", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("loaded", gemma.RuntimeState);
        Assert.Equal("gemma3", gemma.Architecture);
        Assert.Equal("Q4_K_M", gemma.Quantization);
        Assert.Equal(131072, gemma.MaxContextLength);
        Assert.Equal(4096, gemma.LoadedContextLength);
        Assert.Contains("tool_use", gemma.Capabilities, StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(result.Models, m => string.Equals(m.Model, "openai/gpt-oss-20b", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains(handler.RequestUris, uri => string.Equals(uri.AbsolutePath, "/v1/models", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.RequestUris, uri => string.Equals(uri.AbsolutePath, "/api/v0/models", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListModelsAsync_NonLmStudioBaseUrl_DoesNotProbeLmStudioCatalog() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "data": [
                    { "id": "llama3.1", "object": "model", "owned_by": "ollama" }
                  ],
                  "object": "list"
                }
                """);

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://127.0.0.1:11434",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        var result = await transport.ListModelsAsync(CancellationToken.None);
        var only = Assert.Single(result.Models);
        Assert.Equal("llama3.1", only.Model);
        Assert.Single(handler.RequestUris);
        Assert.Equal("/v1/models", handler.RequestUris[0].AbsolutePath);
    }

    [Fact]
    public async Task ListModelsAsync_LmStudioCatalogOverlay_PropagatesCancellation() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "data": [
                    { "id": "google/gemma-3-4b", "object": "model" }
                  ],
                  "object": "list"
                }
                """)
            .RespondCanceled();

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://127.0.0.1:1234/v1",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.ListModelsAsync(CancellationToken.None));
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains(handler.RequestUris, uri => string.Equals(uri.AbsolutePath, "/v1/models", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.RequestUris, uri => string.Equals(uri.AbsolutePath, "/api/v0/models", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListModelsAsync_LmStudioCatalogOverlay_PropagatesCancellationDuringContentRead() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "data": [
                    { "id": "google/gemma-3-4b", "object": "model" }
                  ],
                  "object": "list"
                }
                """)
            .RespondCanceledContent();

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://127.0.0.1:1234/v1",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.ListModelsAsync(CancellationToken.None));
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains(handler.RequestUris, uri => string.Equals(uri.AbsolutePath, "/v1/models", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.RequestUris, uri => string.Equals(uri.AbsolutePath, "/api/v0/models", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ToolCalls_Are_Emitted_In_ToolCallParser_Shape() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "id": "chatcmpl_1",
                  "choices": [
                    {
                      "index": 0,
                      "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                          {
                            "id": "call_1",
                            "type": "function",
                            "function": {
                              "name": "get_weather",
                              "arguments": "{\"city\":\"Paris\"}"
                            }
                          }
                        ]
                      }
                    }
                  ],
                  "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
                }
                """);

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://127.0.0.1:11434",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        var thread = await transport.StartThreadAsync("local-model", null, null, null, CancellationToken.None);
        var turn = await transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"), new ChatOptions {
            Model = "local-model"
        }, null, null, null, CancellationToken.None);

        var calls = ToolCallParser.Extract(turn);
        Assert.Single(calls);
        Assert.Equal("call_1", calls[0].CallId);
        Assert.Equal("get_weather", calls[0].Name);
        Assert.NotNull(calls[0].Arguments);
        Assert.Equal("Paris", calls[0].Arguments!.GetString("city"));
    }

    [Fact]
    public async Task ToolOutputs_Are_Sent_As_RoleTool_With_ToolCallId() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "id": "chatcmpl_1",
                  "choices": [
                    {
                      "index": 0,
                      "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                          { "id": "call_1", "type": "function", "function": { "name": "do_it", "arguments": "{}" } }
                        ]
                      }
                    }
                  ]
                }
                """)
            .RespondJson(HttpStatusCode.OK, """
                {
                  "id": "chatcmpl_2",
                  "choices": [
                    { "index": 0, "message": { "role": "assistant", "content": "ok" } }
                  ]
                }
                """);

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://localhost:11434",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        var thread = await transport.StartThreadAsync("local-model", null, null, null, CancellationToken.None);
        var first = await transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"), new ChatOptions { Model = "local-model" }, null, null, null, CancellationToken.None);
        var call = Assert.Single(ToolCallParser.Extract(first));

        var input = new ChatInput()
            .AddToolOutput(call.CallId, "{\"ok\":true}")
            .AddText("continue");

        _ = await transport.StartTurnAsync(thread.Id, input, new ChatOptions { Model = "local-model" }, null, null, null, CancellationToken.None);

        Assert.True(handler.RequestBodies.Count >= 2);
        var secondBody = handler.RequestBodies[1];
        using var doc = JsonDocument.Parse(secondBody);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.True(messages.GetArrayLength() >= 2);
        var lastIndex = messages.GetArrayLength() - 1;
        var toolMsg = messages[lastIndex - 1];
        var userMsg = messages[lastIndex];
        Assert.Equal("tool", toolMsg.GetProperty("role").GetString());
        Assert.Equal(call.CallId, toolMsg.GetProperty("tool_call_id").GetString());
        Assert.Equal("{\"ok\":true}", toolMsg.GetProperty("content").GetString());
        Assert.Equal("user", userMsg.GetProperty("role").GetString());
        Assert.Equal("continue", userMsg.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Instructions_Are_Sent_As_System_Message_And_Stream_Flag_Respects_Options() {
        var handler = new StubHandler()
            .RespondJson(HttpStatusCode.OK, """
                {
                  "id": "chatcmpl_1",
                  "choices": [
                    { "index": 0, "message": { "role": "assistant", "content": "hi" } }
                  ]
                }
                """);

        using var http = new HttpClient(handler);
        using var transport = new OpenAICompatibleHttpTransport(new OpenAICompatibleHttpOptions {
            BaseUrl = "http://127.0.0.1:11434",
            AllowInsecureHttp = true,
            Streaming = false
        }, http);

        var thread = await transport.StartThreadAsync("local-model", null, null, null, CancellationToken.None);
        _ = await transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"), new ChatOptions {
            Model = "local-model",
            Instructions = "You are a test system."
        }, null, null, null, CancellationToken.None);

        var body = Assert.Single(handler.RequestBodies);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.True(messages.GetArrayLength() >= 2);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a test system.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("hello", messages[1].GetProperty("content").GetString());
    }

    private sealed class StubHandler : HttpMessageHandler {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();
        public List<Uri> RequestUris { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHandler RespondJson(HttpStatusCode status, string json) {
            _responses.Enqueue((req, ct) => Task.FromResult(new HttpResponseMessage(status) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }));
            return this;
        }

        public StubHandler RespondCanceled() {
            _responses.Enqueue((req, ct) => Task.FromException<HttpResponseMessage>(new OperationCanceledException(ct)));
            return this;
        }

        public StubHandler RespondCanceledContent() {
            _responses.Enqueue((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new CanceledReadContent()
            }));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            RequestUris.Add(request.RequestUri ?? new Uri("about:blank"));
            if (request.Content is not null) {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            if (_responses.Count == 0) {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) {
                    Content = new StringContent("{\"error\":\"no response queued\"}", Encoding.UTF8, "application/json")
                };
            }

            return await _responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed class CanceledReadContent : HttpContent {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) {
            return Task.FromException(new OperationCanceledException());
        }

        protected override bool TryComputeLength(out long length) {
            length = 0;
            return false;
        }
    }
}
