using System.Net;
using IntelligenceX.OpenAI;
using Xunit;

namespace IntelligenceX.UnitTests;

[Collection("Copilot environment credentials")]
public sealed class EasySessionCopilotAuthenticationTests {
    [Theory]
    [InlineData("disabled")]
    [InlineData("account")]
    [InlineData("store")]
    public async Task AmbientCredentialsCannotBypassExplicitCredentialIsolation(string isolation) {
        string? previous = Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN");
        try {
            Environment.SetEnvironmentVariable("COPILOT_GITHUB_TOKEN", "host-token");
            var handler = new Handler(HttpStatusCode.ServiceUnavailable);
            var options = Options(handler, false);
            options.CopilotOptions.GitHubToken = null;
            options.CopilotOptions.UseEnvironmentCredentials = isolation != "disabled";
            if (isolation == "account") options.CopilotOptions.AccountId = "42";
            if (isolation == "store") options.CopilotOptions.AuthStore = new EmptyStore();
            await Assert.ThrowsAsync<InvalidOperationException>(() => EasySession.StartAsync(options));
            Assert.Equal(0, handler.Inferences);
            Assert.Equal(0, handler.DeviceRequests);
        } finally { Environment.SetEnvironmentVariable("COPILOT_GITHUB_TOKEN", previous); }
    }

    [Theory]
    [InlineData("COPILOT_GITHUB_TOKEN")]
    [InlineData("GH_TOKEN")]
    [InlineData("GITHUB_TOKEN")]
    public async Task AmbientCredentialsCanInferWhenTheOptionalIdentityEndpointIsUnavailable(string variable) {
        string[] names = { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, name == variable ? "host-token" : null);
            var handler = new Handler(HttpStatusCode.ServiceUnavailable);
            var options = Options(handler, false);
            options.CopilotOptions.GitHubToken = null;
            options.CopilotOptions.UseEnvironmentCredentials = true;
            options.ValidateLoginOnEachRequest = true;
            using var session = await EasySession.StartAsync(options);
            Assert.Equal("answer", (await session.AskAsync("question")).Text);
            Assert.Equal("answer", (await session.AskAsync("another question")).Text);
            Assert.Equal(2, handler.Inferences);
            Assert.Equal(0, handler.DeviceRequests);
        } finally {
            foreach (var item in previous) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }

    [Fact]
    public async Task MissingCredentialsStillUseTheConfiguredDeviceSignIn() {
        var handler = new Handler(HttpStatusCode.OK);
        var options = Options(handler, false);
        options.CopilotOptions.GitHubToken = null;
        options.CopilotOptions.GitHubClientId = "app";
        int codes = 0;
        options.OnCopilotLoginCode = _ => codes++;
        using var session = await EasySession.StartAsync(options);
        Assert.Equal("answer", (await session.AskAsync("question")).Text);
        Assert.Equal(1, codes);
        Assert.Equal(2, handler.DeviceRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCredentialsCanInferWhenTheOptionalIdentityEndpointIsUnavailable(bool callback) {
        var handler = new Handler(HttpStatusCode.ServiceUnavailable);
        var options = Options(handler, callback);
        options.ValidateLoginOnEachRequest = true;
        using var session = await EasySession.StartAsync(options);
        Assert.Equal("answer", (await session.AskAsync("question")).Text);
        Assert.Equal("answer", (await session.AskAsync("another question")).Text);
        Assert.Equal(2, handler.Inferences);
        Assert.Equal(0, handler.DeviceRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.OK)]
    public async Task PinnedAccountStillRequiresVerifiedMatchingIdentity(HttpStatusCode identityStatus) {
        var handler = new Handler(identityStatus);
        var options = Options(handler, false);
        options.CopilotOptions.AccountId = "different";
        await Assert.ThrowsAsync<InvalidOperationException>(() => EasySession.StartAsync(options));
        Assert.Equal(0, handler.Inferences);
        Assert.Equal(0, handler.DeviceRequests);
        Assert.True(handler.Disposed);
    }

    private static EasySessionOptions Options(Handler handler, bool callback) => new() {
        TransportKind = OpenAITransportKind.CopilotNative, DefaultModel = "model",
        CopilotOptions = new() { GitHubToken = callback ? null : "host-token",
            TokenProvider = callback ? _ => Task.FromResult("host-token") : null,
            HttpMessageHandler = handler, Streaming = false, UseEnvironmentCredentials = false },
        OnCopilotLoginCode = _ => throw new Xunit.Sdk.XunitException("Explicit credentials must not start interactive login.")
    };

    private sealed class Handler(HttpStatusCode identityStatus) : HttpMessageHandler {
        internal int Inferences, DeviceRequests;
        internal bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/user") return Task.FromResult(new HttpResponseMessage(identityStatus) { Content = new StringContent("{\"id\":42}") });
            if (path.Contains("login")) {
                DeviceRequests++;
                return Task.FromResult(Json(path.EndsWith("device/code", StringComparison.Ordinal)
                    ? "{\"device_code\":\"private\",\"user_code\":\"public\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":60,\"interval\":1}"
                    : "{\"access_token\":\"host-token\"}"));
            }
            Assert.Equal("host-token", request.Headers.Authorization!.Parameter);
            if (path == "/models") return Task.FromResult(Json("{\"data\":[{\"id\":\"model\",\"supported_endpoints\":[\"/chat/completions\"]}]}"));
            Inferences++;
            return Task.FromResult(Json("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"answer\"},\"finish_reason\":\"stop\"}]}"));
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }

    private sealed class EmptyStore : IntelligenceX.OpenAI.Auth.IAuthBundleStore {
        public Task<IntelligenceX.OpenAI.Auth.AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult<IntelligenceX.OpenAI.Auth.AuthBundle?>(null);
        public Task<IReadOnlyList<IntelligenceX.OpenAI.Auth.AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IntelligenceX.OpenAI.Auth.AuthBundle>>(Array.Empty<IntelligenceX.OpenAI.Auth.AuthBundle>());
        public Task SaveAsync(IntelligenceX.OpenAI.Auth.AuthBundle bundle, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No sign-in expected.");
    }
}

[CollectionDefinition("Copilot environment credentials", DisableParallelization = true)]
public sealed class CopilotEnvironmentCredentialCollection { }
