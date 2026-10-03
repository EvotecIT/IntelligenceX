using System.Net;
using System.Text;
using System.Text.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class NativeTerminalResponseTests {
    [Fact]
    public async Task IncompleteResponsePreservesFinalOutputUsageAndHistory() {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request => {
            requests.Add(await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) {
                Content = new StringContent(TerminalEvent("response.incomplete"), Encoding.UTF8, "text/event-stream")
            };
        }));
        using var transport = CreateTransport(http);
        var thread = await transport.StartThreadAsync("model", null, null, null, default);
        var turn = await transport.StartTurnAsync(thread.Id, ChatInput.FromText("question"), new() { Model = "model" }, null, null, null, default);

        Assert.Equal("incomplete", turn.Status);
        Assert.Equal("response-id", turn.ResponseId);
        Assert.Equal("partial answer", EasyChatResult.FromTurn(turn).Text);
        Assert.Equal(15, turn.Usage?.TotalTokens);
        Assert.Equal("max_output_tokens", turn.Raw.GetObject("response")?.GetObject("incomplete_details")?.GetString("reason"));

        await transport.StartTurnAsync(thread.Id, ChatInput.FromText("continue"), new() { Model = "model" }, null, null, null, default);
        Assert.Equal(2, requests.Count);
        using var requestJson = JsonDocument.Parse(requests[1]);
        var history = requestJson.RootElement.GetProperty("input").EnumerateArray();
        Assert.Contains(history, item => item.TryGetProperty("role", out var role) && role.GetString() == "assistant"
            && item.GetProperty("content")[0].GetProperty("text").GetString() == "partial answer");
    }

    [Theory]
    [InlineData("response.completed")]
    [InlineData("response.done")]
    [InlineData("response.incomplete")]
    [InlineData("response.failed")]
    [InlineData("error")]
    public async Task TerminalEventCompletesWithoutWaitingForHttpEof(string eventType) {
        using var body = new OpenEndedStream(TerminalEvent(eventType));
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(body)
        })));
        using var transport = CreateTransport(http);
        var thread = await transport.StartThreadAsync("model", null, null, null, default);
        using var cleanup = new CancellationTokenSource();
        var turn = transport.StartTurnAsync(thread.Id, ChatInput.FromText("question"), new() { Model = "model" }, null, null, null, cleanup.Token);
        try {
            if (eventType is "response.failed" or "error") {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => turn.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.Equal("provider failure", error.Message);
            } else {
                var result = await turn.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(eventType == "response.incomplete" ? "incomplete" : "completed", result.Status);
                Assert.Equal("partial answer", EasyChatResult.FromTurn(result).Text);
            }
            Assert.True(body.IsDisposed);
            Assert.False(body.ReadPastTerminalEvent);
        } finally {
            cleanup.Cancel();
            body.Dispose();
            await Record.ExceptionAsync(() => turn);
        }
    }

    private static string TerminalEvent(string type) => "data: " + (type == "error"
        ? JsonSerializer.Serialize(new { type, message = "provider failure" })
        : JsonSerializer.Serialize(new {
            type,
            response = new {
                id = "response-id",
                status = type == "response.incomplete" ? "incomplete" : type == "response.failed" ? "failed" : "completed",
                incomplete_details = type == "response.incomplete" ? new { reason = "max_output_tokens" } : null,
                error = type == "response.failed" ? new { message = "provider failure" } : null,
                output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "partial answer" } } } },
                usage = new { input_tokens = 10, output_tokens = 5, total_tokens = 15 }
            }
        })) + "\n\n";

    private static OpenAINativeTransport CreateTransport(HttpClient http) => new(new() {
        AuthStore = new Store(), LoadCodexAuthJson = false, PersistCodexAuthJson = false,
        EnableModelFallback = false, EnableToolSchemaFallback = false, AllowSensitiveDiagnostics = false,
        ImageGeneration = new() { Enabled = false }
    }, http);

    private sealed class Store : IAuthBundleStore {
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthBundle?>(new("openai-codex", "fixture-token", "", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "fixture-account" });
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class OpenEndedStream(string prefix) : MemoryStream(Encoding.UTF8.GetBytes(prefix)) {
        private readonly TaskCompletionSource<int> _tail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool IsDisposed { get; private set; }
        internal bool ReadPastTerminalEvent { get; private set; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            if (Position < Length) return base.ReadAsync(buffer, offset, count, cancellationToken);
            ReadPastTerminalEvent = true;
            return _tail.Task;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            if (Position < Length) return base.ReadAsync(buffer, cancellationToken);
            ReadPastTerminalEvent = true;
            return new(_tail.Task);
        }
        protected override void Dispose(bool disposing) {
            IsDisposed = true;
            _tail.TrySetResult(0);
            base.Dispose(disposing);
        }
    }
}
