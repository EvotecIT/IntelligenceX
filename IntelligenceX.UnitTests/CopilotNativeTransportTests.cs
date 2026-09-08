using System.Net;
using System.Text;
using System.Text.Json;
using IntelligenceX.Copilot.Native;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotNativeTransportTests {
    [Theory]
    [InlineData("models", 429)]
    [InlineData("models", 503)]
    [InlineData("chat", 429)]
    [InlineData("chat", 503)]
    [InlineData("responses", 429)]
    [InlineData("responses", 503)]
    [InlineData("chat", 401)]
    [InlineData("responses", 400)]
    public async Task ProviderFailuresPreserveHttpStatus(string operation, int status) {
        using var client = await Connect(new Handler((request, _) => Task.FromResult(
            operation != "models" && request.Method == HttpMethod.Get ? Catalog(operation == "responses")
                : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private provider error") })), false);
        var error = await Assert.ThrowsAsync<HttpRequestException>(async () => {
            if (operation == "models") await client.ListModelsAsync();
            else await client.ChatAsync("question");
        });
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain("private provider error", error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveredModelValueSelectsItsAdvertisedProtocol(bool responses) {
        using var client = await Connect(new Handler(async (request, _) => {
            if (request.Method == HttpMethod.Get) return Json(JsonSerializer.Serialize(new {
                data = new[] { new { id = "catalog-id", model = "request-model", supported_endpoints = new[] { responses ? "/responses" : "/chat/completions" } } }
            }));
            Assert.Equal(responses ? "/responses" : "/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Contains("\"model\":\"request-model\"", await request.Content!.ReadAsStringAsync());
            return Answer(responses, false);
        }), false);
        var model = Assert.Single((await client.ListModelsAsync()).Models);
        Assert.Equal("request-model", model.Model);
        var turn = await client.ChatAsync("question", model.Model);
        Assert.Equal("answer", Assert.Single(turn.Outputs).Text);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task RefusalsRemainVisibleInBothProtocols(bool responses, bool streaming) {
        object result = new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "refusal", refusal = "Cannot help with that." } } } } };
        using var client = await Connect(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Catalog(responses)
            : responses ? streaming
                ? Sse(Event(new { type = "response.refusal.delta", delta = "Cannot help with that." }) + Event(new { type = "response.completed", response = result }))
                : Json(JsonSerializer.Serialize(result))
            : streaming ? Sse(Event(new { choices = new[] { new { delta = new { refusal = "Cannot help with that." }, finish_reason = "stop" } } }) + "data: [DONE]\n\n")
                : Json("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"refusal\":\"Cannot help with that.\"},\"finish_reason\":\"stop\"}]}"))), streaming);
        var deltas = new StringBuilder();
        client.DeltaReceived += (_, delta) => deltas.Append(delta);
        var turn = await client.ChatAsync("question");
        Assert.Equal("Cannot help with that.", Assert.Single(turn.Outputs).Text);
        if (streaming) Assert.Equal("Cannot help with that.", deltas.ToString());
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("error")]
    [InlineData("malformed")]
    public async Task FailedChatStreamsDoNotCommitPartialHistory(string termination) {
        int turns = 0;
        var handler = new Handler(async (request, _) => {
            if (request.Method == HttpMethod.Get) return Catalog(false);
            if (++turns > 1) {
                string body = await request.Content!.ReadAsStringAsync();
                Assert.DoesNotContain("failed private question", body);
                Assert.DoesNotContain("partial response", body);
                return Answer(false, true);
            }
            return Sse(Event(new { choices = new[] { new { delta = new { content = "partial response" } } } }) + (termination switch {
                "error" => Event(new { error = new { message = "provider failed" } }),
                "malformed" => "data: {broken\n\n",
                _ => ""
            }));
        });
        using var client = await Connect(handler, true);
        await Assert.ThrowsAnyAsync<Exception>(() => client.ChatAsync("failed private question"));
        Assert.Equal("answer", Assert.Single((await client.ChatAsync("retry" )).Outputs).Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SharedClientSupportsBothProtocolsAndStreaming(bool responses, bool streaming) {
        var requests = new List<string>();
        var handler = new Handler(async (request, _) => {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("host-token", request.Headers.Authorization.Parameter);
            Assert.Contains("IntelligenceX", request.Headers.UserAgent.ToString());
            if (request.Method == HttpMethod.Get) return Catalog(responses);
            Assert.Equal(responses ? "/responses" : "/chat/completions", request.RequestUri!.AbsolutePath);
            string body = await request.Content!.ReadAsStringAsync();
            requests.Add(body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(streaming, json.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal("model", json.RootElement.GetProperty("model").GetString());
            if (responses) Assert.False(json.RootElement.GetProperty("store").GetBoolean());
            return Answer(responses, streaming);
        });
        using var client = await Connect(handler, streaming);
        int deltas = 0;
        client.DeltaReceived += (_, _) => throw new InvalidOperationException("observer failure");
        client.DeltaReceived += (_, _) => deltas++;
        var first = await client.ChatAsync("first turn");
        Assert.Equal("completed", first.Status);
        Assert.Equal("answer", Assert.Single(first.Outputs).Text);
        Assert.Equal(11, first.Usage!.InputTokens);
        await client.ChatAsync("second turn");
        Assert.Contains("first turn", requests[1]);
        Assert.Contains("second turn", requests[1]);
        Assert.Contains("answer", requests[1]);
        if (streaming) Assert.True(deltas > 0);
    }

    [Fact]
    public async Task ResponsesToolRoundTripRetainsEncryptedReasoningAndCallIdentity() {
        int turns = 0;
        var handler = new Handler(async (request, _) => {
            if (request.Method == HttpMethod.Get) return Catalog(true);
            string body = await request.Content!.ReadAsStringAsync();
            if (++turns == 1) return Json("""
                {"id":"r1","status":"completed","output":[
                {"type":"reasoning","id":"reason1","encrypted_content":"opaque-state","summary":[]},
                {"type":"function_call","id":"fc1","call_id":"call1","name":"lookup","arguments":"{\"key\":1}"}]}
                """);
            using var parsed = JsonDocument.Parse(body);
            var input = parsed.RootElement.GetProperty("input").EnumerateArray().ToArray();
            Assert.Equal("opaque-state", input.Single(x => x.GetProperty("type").GetString() == "reasoning").GetProperty("encrypted_content").GetString());
            Assert.Equal("call1", input.Single(x => x.GetProperty("type").GetString() == "function_call").GetProperty("call_id").GetString());
            var output = input.Single(x => x.GetProperty("type").GetString() == "function_call_output");
            Assert.Equal("call1", output.GetProperty("call_id").GetString());
            Assert.Equal("lookup result", output.GetProperty("output").GetString());
            return Answer(true, false);
        });
        using var client = await Connect(handler, false);
        var first = await client.ChatAsync("lookup a value");
        var call = Assert.Single(ToolCallParser.Extract(first));
        Assert.Equal("lookup", call.Name);
        var next = await client.ChatAsync(new ChatInput().AddToolOutput("call1", "lookup result"));
        Assert.Equal("answer", Assert.Single(next.Outputs).Text);
        Assert.Equal(2, turns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedBodiesFailWithoutCommittingConversationHistory(bool responses) {
        int turns = 0;
        var handler = new Handler(async (request, _) => {
            if (request.Method == HttpMethod.Get) return Catalog(responses);
            if (++turns == 1) return Json(new string('x', 2048));
            Assert.DoesNotContain("private oversized turn", await request.Content!.ReadAsStringAsync());
            return Answer(responses, false);
        });
        using var client = await Connect(handler, false);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ChatAsync(ChatInput.FromText("private oversized turn"), new ChatOptions { MaxResponseBytes = 1024 }));
        await client.ChatAsync("next turn");
        Assert.Equal(2, turns);
    }

    [Theory]
    [InlineData("response.failed")]
    [InlineData("eof")]
    public async Task FailedOrTruncatedResponseStreamsCannotReportSuccess(string termination) {
        using var client = await Connect(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? Catalog(true) : Sse(Event(new { type = "response.output_text.delta", delta = "partial" }) +
                (termination == "eof" ? "" : Event(new { type = termination }))))), true);
        if (termination == "eof") await Assert.ThrowsAsync<InvalidDataException>(() => client.ChatAsync("question"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => client.ChatAsync("question"));
    }

    [Fact]
    public async Task TerminalResponseReturnsWithoutWaitingForServerEof() {
        using var client = await Connect(new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Catalog(true)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new OpenEndedStream(Encoding.UTF8.GetBytes(
                Event(new { type = "response.completed", response = new { status = "completed", output = Array.Empty<object>() } })))) {
                Headers = { ContentType = new("text/event-stream") }
            } })), true);
        var turn = await client.ChatAsync("question").WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("completed", turn.Status);
    }

    private static Task<IntelligenceXClient> Connect(HttpMessageHandler handler, bool streaming) => IntelligenceXClient.ConnectAsync(new() {
        TransportKind = OpenAITransportKind.CopilotNative, DefaultModel = "model",
        CopilotOptions = new() { GitHubToken = "host-token", Streaming = streaming, HttpMessageHandler = handler, RequestTimeout = TimeSpan.FromSeconds(3) }
    });
    private static HttpResponseMessage Catalog(bool responses) => Json(JsonSerializer.Serialize(new {
        data = new[] { new { id = "model", supported_endpoints = new[] { responses ? "/responses" : "/chat/completions" } } }
    }));
    private static HttpResponseMessage Answer(bool responses, bool streaming) {
        object response = new { id = "r1", status = "completed", output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "answer" } } } }, usage = new { input_tokens = 11, output_tokens = 2, total_tokens = 13 } };
        if (responses) return streaming ? Sse(Event(new { type = "response.output_text.delta", delta = "answer" }) + Event(new { type = "response.completed", response })) : Json(JsonSerializer.Serialize(response));
        if (streaming) return Sse("data: {\"choices\":[{\"delta\":{\"content\":\"answer\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":2,\"total_tokens\":13}}\n\n" + "data: [DONE]\n\n");
        return Json("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":2,\"total_tokens\":13}}");
    }
    private static string Event(object value) => "data: " + JsonSerializer.Serialize(value) + "\n\n";
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Sse(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/event-stream") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class OpenEndedStream(byte[] initial) : MemoryStream(initial) {
        private readonly TaskCompletionSource<int> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Position < Length
            ? base.ReadAsync(buffer, offset, count, cancellationToken) : _pending.Task;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Position < Length
            ? base.ReadAsync(buffer, cancellationToken) : new(_pending.Task);
        protected override void Dispose(bool disposing) { _pending.TrySetCanceled(); base.Dispose(disposing); }
    }
}
