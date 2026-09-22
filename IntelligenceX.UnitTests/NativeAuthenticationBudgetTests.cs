using System.Net;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class NativeAuthenticationBudgetTests {
    [Theory]
    [InlineData("oversized-error")]
    [InlineData("unreadable-error")]
    [InlineData("no-refresh-token")]
    [InlineData("unauthorized-again")]
    [InlineData("cancel-after-headers")]
    [InlineData("refresh-failure")]
    [InlineData("oversized-success")]
    public async Task AuthenticationRecoveryDoesNotDependOnReadingTheErrorBody(string scenario) {
        using var cancellation = new CancellationTokenSource();
        var store = new Store(scenario != "no-refresh-token");
        var options = new OpenAINativeOptions { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false,
            EnableModelFallback = false, EnableToolSchemaFallback = false, AllowSensitiveDiagnostics = false };
        int refreshes = 0;
        var cause = new IOException("refresh failed");
        var auth = new OpenAINativeAuthManager(options, (_, _, token) => {
            token.ThrowIfCancellationRequested(); refreshes++;
            if (scenario == "refresh-failure") throw cause;
            return Task.FromResult(new OAuthLoginResult(new AuthBundle("openai-codex", "fresh", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "fixture-account"
            }, new()));
        });
        var authorizations = new List<string?>();
        var bodies = new List<HttpContent>();
        using var http = new HttpClient(new Handler(request => {
            authorizations.Add(request.Headers.Authorization?.Parameter);
            bool unauthorized = authorizations.Count == 1 || scenario == "unauthorized-again";
            if (scenario == "cancel-after-headers") cancellation.Cancel();
            HttpContent content = unauthorized
                ? scenario == "unreadable-error" ? new UnreadableContent() : new StringContent(new string('x', 5000))
                : new StringContent(scenario == "oversized-success" ? new string('x', 5000)
                    : "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"output\":[]}}\n\n");
            bodies.Add(content);
            return new HttpResponseMessage(unauthorized ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = content };
        }));
        using var transport = new OpenAINativeTransport(options, http, auth);
        var thread = await transport.StartThreadAsync("model", null, null, null, CancellationToken.None);
        Task Run() => transport.StartTurnAsync(thread.Id, ChatInput.FromText("document"), new ChatOptions { Model = "model", MaxResponseBytes = 512 },
            null, null, null, cancellation.Token);
        switch (scenario) {
            case "cancel-after-headers":
                Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(Run)).CancellationToken); break;
            case "no-refresh-token":
            case "unauthorized-again": await Assert.ThrowsAsync<OpenAIAuthenticationRequiredException>(Run); break;
            case "refresh-failure": Assert.Same(cause, await Assert.ThrowsAsync<IOException>(Run)); break;
            case "oversized-success": await Assert.ThrowsAsync<InvalidDataException>(Run); break;
            default: await Run(); break;
        }
        bool refreshExpected = scenario is not ("no-refresh-token" or "cancel-after-headers");
        Assert.Equal(refreshExpected ? 1 : 0, refreshes);
        bool replayExpected = refreshExpected && scenario != "refresh-failure";
        Assert.Equal(replayExpected ? new[] { "stale", "fresh" } : new[] { "stale" }, authorizations);
        Assert.Equal(replayExpected ? 1 : 0, store.Saves);
        if (scenario == "unreadable-error") Assert.False(((UnreadableContent)bodies[0]).WasRead);
        Assert.All(bodies, body => Assert.Throws<ObjectDisposedException>(() => body.ReadAsStream()));
    }

    private sealed class Store(bool refreshable) : IRemovableAuthBundleStore {
        private AuthBundle _bundle = new("openai-codex", "stale", refreshable ? "refresh" : "", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "fixture-account" };
        public int Saves { get; private set; }
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult<AuthBundle?>(_bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(new[] { _bundle });
        public Task RemoveAsync(string provider, string? accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Logout is outside this fixture contract.");
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) { Saves++; _bundle = bundle; return Task.CompletedTask; }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    private sealed class UnreadableContent : HttpContent {
        internal bool WasRead { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) {
            WasRead = true; throw new IOException("Authentication must be recognized without reading this body.");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
