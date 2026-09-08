using System.Net;
using IntelligenceX.OpenAI;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class EasySessionCopilotAuthenticationTests {
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
}
