using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Telemetry.GitHub;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class GitHubAppCredentialsTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-github-app-" + Guid.NewGuid().ToString("N"));
    private readonly RSA _key = RSA.Create(2048);

    public GitHubAppCredentialsTests() => Directory.CreateDirectory(_directory);

    // Protects GitHub App sign-in: the token request carries a JWT signed with the App's key and naming the App, and
    // the installation token is reused until five minutes before it expires, then renewed.
    [Fact]
    public async Task InstallationTokensAreMintedWithASignedJwtAndRenewedBeforeTheyExpire() {
        var now = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
        var stub = new TokenStub(now);
        using var source = new GitHubAppTokenSource(Settings(), new HttpClient(stub), () => now);

        Assert.Equal("token-1", await source.GetTokenAsync());
        now = now.AddMinutes(50);
        Assert.Equal("token-1", await source.GetTokenAsync());
        now = now.AddMinutes(6);
        Assert.Equal("token-2", await source.GetTokenAsync());

        Assert.Equal(2, stub.Requests.Count);
        var request = stub.Requests[0];
        Assert.Equal("https://api.github.com/app/installations/77/access_tokens", request.Url);
        var parts = request.Jwt.Split('.');
        Assert.True(_key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("42", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 19, 9, 9, 0, TimeSpan.Zero).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
    }

    // Protects requests sent as the App: each one carries the current installation token, replacing the default header.
    [Fact]
    public async Task TheAuthHandlerSignsEachRequestWithTheInstallationToken() {
        var now = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
        using var source = new GitHubAppTokenSource(Settings(), new HttpClient(new TokenStub(now)), () => now);
        var api = new EchoAuth();
        using var client = new HttpClient(new GitHubAppAuthHandler(source, api));
        client.DefaultRequestHeaders.Authorization = new("Bearer", "user-token");
        await client.GetAsync("https://api.github.com/user");
        Assert.Equal("Bearer token-1", api.Seen);
    }

    // Protects the fallback: when the App can't renew its token (refused, malformed answer or a timeout), requests go
    // out with the user token, the App is retried after five minutes, and the caller's own cancellation still wins.
    [Fact]
    public async Task RequestsFallBackToTheUserTokenWhileTheAppCannotRenew() {
        var now = new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
        var stub = new TokenStub(now) { Answer = HttpStatusCode.OK, Body = "{}" };
        using var source = new GitHubAppTokenSource(Settings(), new HttpClient(stub), () => now);
        var api = new EchoAuth();
        using var client = new HttpClient(new GitHubAppAuthHandler(source, api, "user-token"));

        await client.GetAsync("https://api.github.com/repos/a/b/pulls");
        Assert.Equal(("Bearer user-token", false), (api.Seen, api.SignedByApp));
        // Every caller shares the retry time, so a second request doesn't ask the App again.
        await client.GetAsync("https://api.github.com/repos/a/b/pulls");
        Assert.Single(stub.Requests);

        stub.Body = null;
        now += GitHubAppTokenSource.RetryAfter;
        await client.GetAsync("https://api.github.com/repos/a/b/pulls");
        // Signed as the App, so usage is counted under the App's own component.
        Assert.Equal(("Bearer token-2", true), (api.Seen, api.SignedByApp));

        Assert.True(GitHubAppTokenSource.IsSignInFailure(new TaskCanceledException(), CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False(GitHubAppTokenSource.IsSignInFailure(new OperationCanceledException(), cancelled.Token));
    }

    // Protects the switch: no file or "enabled": false keeps the user token; a complete file selects the App.
    [Fact]
    public void SettingsSelectTheAppOnlyWhenCompleteAndEnabled() {
        var file = Path.Combine(_directory, "github-app.json");
        Assert.Null(GitHubAppSettings.Load(file));
        File.WriteAllText(file, "{\"appId\":42,\"installationId\":77,\"privateKeyPath\":\"key.pem\",\"enabled\":false}");
        Assert.Null(GitHubAppSettings.Load(file));
        File.WriteAllText(file, "{\"appId\":42,\"privateKeyPath\":\"key.pem\"}");
        Assert.Null(GitHubAppSettings.Load(file));
        File.WriteAllText(file, "{\"appId\":42,\"installationId\":77,\"privateKeyPath\":\"key.pem\"}");
        var settings = GitHubAppSettings.Load(file)!;
        Assert.Equal((42L, 77L, "key.pem", "https://api.github.com/"), (settings.AppId, settings.InstallationId, settings.PrivateKeyPath, settings.ApiBaseUrl));
    }

    private GitHubAppSettings Settings() {
        var path = Path.Combine(_directory, "app.pem");
        File.WriteAllText(path, _key.ExportRSAPrivateKeyPem());
        return new GitHubAppSettings(42, 77, path);
    }

    private static byte[] Decode(string value) {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }

    private sealed class TokenStub(DateTimeOffset issued) : HttpMessageHandler {
        public List<(string Url, string Jwt)> Requests { get; } = [];

        public HttpStatusCode Answer { get; set; } = HttpStatusCode.Created;

        /// <summary>Replaces the token answer when set, as a misbehaving endpoint would.</summary>
        public string? Body { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization!.Parameter!));
            var expires = issued.AddHours(Requests.Count).ToString("O");
            return Task.FromResult(new HttpResponseMessage(Body is null ? HttpStatusCode.Created : Answer) {
                Content = new StringContent(Body ?? "{\"token\":\"token-" + Requests.Count + "\",\"expires_at\":\"" + expires + "\"}")
            });
        }
    }

    private sealed class EchoAuth : HttpMessageHandler {
        public string? Seen { get; private set; }

        public bool SignedByApp { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Seen = request.Headers.Authorization?.ToString();
            SignedByApp = request.Options.TryGetValue(GitHubAppAuthHandler.SignedByApp, out var app) && app;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    public void Dispose() {
        _key.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
