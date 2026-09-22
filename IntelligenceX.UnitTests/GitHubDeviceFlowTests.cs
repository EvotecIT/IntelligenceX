using System.Diagnostics;
using System.Net;
using IntelligenceX.Authentication.GitHub;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class GitHubDeviceFlowTests {
    [Fact]
    public async Task PendingAndSlowDownAreHonoredBeforeReturningTheApprovedCredential() {
        var elapsed = Stopwatch.StartNew();
        var pollTimes = new List<TimeSpan>();
        using var http = new HttpClient(new Handler(async (request, token) => {
            string body = await request.Content!.ReadAsStringAsync(token);
            Assert.Contains("client_id=host-app", body);
            if (request.RequestUri!.AbsolutePath == "/login/device/code") {
                Assert.Contains("scope=read%3Auser", body);
                return Json("{\"device_code\":\"private-code\",\"user_code\":\"USER-CODE\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":60,\"interval\":1}");
            }
            Assert.Contains("device_code=private-code", body);
            Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", body);
            pollTimes.Add(elapsed.Elapsed);
            return Json(pollTimes.Count switch {
                1 => "{\"error\":\"authorization_pending\"}",
                2 => "{\"error\":\"slow_down\"}",
                _ => "{\"access_token\":\"approved\",\"token_type\":\"bearer\",\"scope\":\"read:user\"}"
            });
        }));
        using var flow = new GitHubDeviceFlowClient("host-app", httpClient: http);
        var code = await flow.RequestCodeAsync("read:user");
        Assert.Equal("USER-CODE", code.UserCode);
        Assert.Equal("https://github.com/login/device", code.VerificationUri.AbsoluteUri);
        var credential = await flow.CompleteAsync(code, "copilot");
        Assert.Equal("approved", credential.AccessToken);
        Assert.Equal("copilot", credential.Provider);
        Assert.Empty(credential.RefreshToken);
        Assert.Null(credential.ExpiresAt);
        Assert.Equal(3, pollTimes.Count);
        Assert.True(pollTimes[1] - pollTimes[0] >= TimeSpan.FromMilliseconds(900));
        Assert.True(pollTimes[2] - pollTimes[1] >= TimeSpan.FromMilliseconds(5900));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("expired_token")]
    public async Task DeniedOrExpiredAuthorizationDoesNotReturnACredential(string error) {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"error\":\"" + error + "\"}"))));
        using var flow = new GitHubDeviceFlowClient("host-app", httpClient: http);
        var code = Code(DateTimeOffset.UtcNow.AddMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.CompleteAsync(code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryAndCallerCancellationStopPendingAuthorization(bool cancel) {
        int calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; throw new Exception("No request is due yet."); }));
        using var flow = new GitHubDeviceFlowClient("host-app", httpClient: http);
        using var cancellation = new CancellationTokenSource();
        var code = Code(DateTimeOffset.UtcNow.AddMilliseconds(cancel ? 60000 : 50));
        var operation = flow.CompleteAsync(code, cancellationToken: cancellation.Token);
        if (cancel) {
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation)).CancellationToken);
        } else await Assert.ThrowsAsync<TimeoutException>(() => operation);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("http://github.com/login/device")]
    [InlineData("https://other.example/login/device")]
    public async Task DeviceCodeRejectsAnUnexpectedVerificationSite(string uri) {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"device_code\":\"private\",\"user_code\":\"code\",\"verification_uri\":\"" + uri + "\"}"))));
        using var flow = new GitHubDeviceFlowClient("host-app", httpClient: http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.RequestCodeAsync());
    }

    private static GitHubDeviceAuthorization Code(DateTimeOffset expires) => new("private", "public", new Uri("https://github.com/login/device"), 1, expires);
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
