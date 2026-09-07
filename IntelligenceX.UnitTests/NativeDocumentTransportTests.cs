using System.Net;
using System.Text;
using System.Text.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class NativeDocumentTransportTests {
    [Fact]
    public async Task SchemaInlineImageAndWhitespaceSurviveNativeTransport() {
        string body = string.Empty;
        using var http = new HttpClient(new Handler(async request => {
            body = await request.Content!.ReadAsStringAsync();
            string events = Event(new { type = "response.output_text.delta", delta = "Total:" })
                + Event(new { type = "response.output_text.delta", delta = " " })
                + Event(new { type = "response.output_text.delta", delta = "42" })
                + Event(new { type = "response.completed", response = new { id = "result", status = "completed", output = Array.Empty<object>() } });
            return new(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };
        }));
        using var transport = new OpenAINativeTransport(Options(), http);
        var thread = await transport.StartThreadAsync("vision-model", null, null, null, CancellationToken.None);
        var input = ChatInput.FromText("Extract this image");
        input.AddImageBytes(new byte[] { 1, 2, 3 }, "image/png");
        var turn = await transport.StartTurnAsync(thread.Id, input, new ChatOptions {
            Model = "vision-model", MaxResponseBytes = 4096,
            ResponseFormat = new("document", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}")
        }, null, null, null, CancellationToken.None);
        Assert.Equal("Total: 42", EasyChatResult.FromTurn(turn).Text);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("json_schema", json.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.Contains("data:image/png;base64,AQID", body);
        Assert.False(json.RootElement.TryGetProperty("tools", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task NativeResponseBudgetCoversSuccessAndErrorBodies(HttpStatusCode status) {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(status) {
            Content = new StringContent(new string('x', 5000))
        })));
        using var transport = new OpenAINativeTransport(Options(), http);
        var thread = await transport.StartThreadAsync("model", null, null, null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"),
            new ChatOptions { Model = "model", MaxResponseBytes = 1024 }, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DisabledModelFallbackSendsOnlyOneRequest() {
        int calls = 0;
        using var http = new HttpClient(new Handler(_ => {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) {
                Content = new StringContent("{\"error\":{\"message\":\"The model is not supported when using Codex with a ChatGPT account.\"}}")
            });
        }));
        using var transport = new OpenAINativeTransport(Options(), http);
        var thread = await transport.StartThreadAsync("unsupported-model", null, null, null, CancellationToken.None);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"),
            new ChatOptions { Model = "unsupported-model" }, null, null, null, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    private static string Event(object value) => "data: " + JsonSerializer.Serialize(value) + "\n\n";
    [Fact]
    public async Task EphemeralClientForgetsSourceEvenIfItsOldThreadIdIsResumed() {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request => {
            requests.Add(await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent(Event(new {
                type = "response.completed", response = new { status = "completed", output = new[] { new {
                    type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "answer" } }
                } } }
            })) };
        }));
        var transport = new OpenAINativeTransport(Options(), http);
        using var client = new IntelligenceXClient(transport, "model", null, null, null);
        string? priorThread = null;
        client.TurnCompleted += (_, value) => priorThread = value.ThreadId;
        await client.ChatAsync(ChatInput.FromText("sensitive-first-document"), new ChatOptions { Ephemeral = true });
        Assert.NotNull(priorThread);
        await client.UseThreadAsync(priorThread!);
        await client.ChatAsync(ChatInput.FromText("second-document"));
        Assert.Equal(2, requests.Count);
        Assert.DoesNotContain("sensitive-first-document", requests[1]);
        Assert.Contains("second-document", requests[1]);
    }

    private static OpenAINativeOptions Options() => new() {
        AuthStore = new Store(), LoadCodexAuthJson = false, PersistCodexAuthJson = false,
        EnableModelFallback = false, EnableToolSchemaFallback = false, AllowSensitiveDiagnostics = false,
        ImageGeneration = new() { Enabled = false }
    };
    private sealed class Store : IAuthBundleStore {
        private readonly AuthBundle _bundle = new("openai-codex", "test-token", "", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "test-account" };
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult<AuthBundle?>(_bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(new[] { _bundle });
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture must not refresh credentials.");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
