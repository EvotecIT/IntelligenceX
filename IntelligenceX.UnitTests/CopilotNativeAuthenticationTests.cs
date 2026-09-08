using System.Net;
using IntelligenceX.Copilot.Native;
using IntelligenceX.OpenAI.Auth;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotNativeAuthenticationTests {
    [Fact]
    public async Task StoreWithoutRemovalCapabilityStillSupportsAuthenticationAndReportsPersistentLogoutGap() {
        var store = new LegacyStore();
        using var auth = new CopilotNativeAuthentication(new() { AuthStore = store });
        Assert.Equal("stored-token", await auth.GetAccessTokenAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => auth.LogoutAsync());
        Assert.Equal("stored-token", (await store.GetAsync("copilot"))!.AccessToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
    }

    // Implements the pre-existing custom-store contract without an account-removal member.
    private sealed class LegacyStore : IAuthBundleStore {
        private AuthBundle _bundle = new("copilot", "stored-token", "", null);
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult<AuthBundle?>(_bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(new[] { _bundle });
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) { _bundle = bundle; return Task.CompletedTask; }
    }

    [Fact]
    public async Task StandaloneAuthenticationUsesAndDisposesConfiguredHandler() {
        var handler = new OwnedHandler();
        using (var auth = new CopilotNativeAuthentication(new() {
            GitHubToken = "host-token", AccountId = "42", HttpMessageHandler = handler,
            GitHubApiBaseUrl = "https://auth.invalid/"
        })) {
            Assert.Equal("host-token", await auth.GetAccessTokenAsync());
            Assert.Equal(1, handler.Requests);
            Assert.False(handler.Disposed);
        }
        Assert.True(handler.Disposed);
    }

    private sealed class OwnedHandler : HttpMessageHandler {
        internal int Requests;
        internal bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests++;
            Assert.Equal("auth.invalid", request.RequestUri!.Host);
            Assert.Equal("host-token", request.Headers.Authorization!.Parameter);
            return Task.FromResult(Json("{\"id\":42}"));
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCredentialLogoutDoesNotDeleteAnUnselectedStoredAccount(bool callback) {
        var store = new Store(new("copilot", "unrelated", "", null) { AccountId = "other" });
        using var auth = new CopilotNativeAuthentication(new() {
            AuthStore = store, GitHubToken = callback ? null : "explicit",
            TokenProvider = callback ? _ => Task.FromResult("explicit") : null
        });
        Assert.Equal("explicit", await auth.GetAccessTokenAsync());
        await auth.LogoutAsync();
        Assert.Equal("unrelated", store.Bundle?.AccessToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
    }

    [Fact]
    public async Task SharedFileStoresSerializeRefreshAndReloadTheRotatedToken() {
        string path = Path.Combine(Path.GetTempPath(), "ix-rotation-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            await new FileAuthBundleStore(path).SaveAsync(new("copilot", "expired", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(-1)) { AccountId = "42" });
            int refreshes = 0;
            using var http = new HttpClient(new Handler(async (request, _) => {
                if (request.RequestUri!.AbsolutePath == "/login/oauth/access_token") {
                    Interlocked.Increment(ref refreshes);
                    await Task.Delay(60);
                    return Json("{\"access_token\":\"renewed\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
                }
                return Json("{\"id\":42}");
            }));
            using var first = new CopilotNativeAuthentication(new() { AuthStore = new FileAuthBundleStore(path), GitHubClientId = "app" }, http);
            using var second = new CopilotNativeAuthentication(new() { AuthStore = new FileAuthBundleStore(path), GitHubClientId = "app" }, http);
            Assert.All(await Task.WhenAll(first.GetAccessTokenAsync(), second.GetAccessTokenAsync()), token => Assert.Equal("renewed", token));
            Assert.Equal(1, refreshes);
            await new FileAuthBundleStore(path).SaveAsync(new("copilot", "host-rotated", "", null) { AccountId = "42" });
            Assert.Equal("host-rotated", await first.GetAccessTokenAsync());
            await second.LogoutAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => first.GetAccessTokenAsync());
        } finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingStoreWriteIsBoundedAndCannotOvertakeLogout(bool timeout) {
        var store = new DelayedStore();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/login/oauth/access_token"
            ? "{\"access_token\":\"renewed\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}" : "{\"id\":42}"))));
        using var auth = new CopilotNativeAuthentication(new() { AuthStore = store, GitHubClientId = "app",
            RequestTimeout = TimeSpan.FromMilliseconds(150) }, http);
        using var cancellation = new CancellationTokenSource();
        var pending = auth.GetAccessTokenAsync(cancellation.Token);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (!timeout) cancellation.Cancel();
        try {
            Assert.Same(pending, await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2))));
            if (timeout) await Assert.ThrowsAsync<TimeoutException>(() => pending);
            else Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)))).CancellationToken);
            var logout = auth.LogoutAsync();
            await Task.Delay(20);
            Assert.False(logout.IsCompleted);
            store.Complete.TrySetResult(true);
            await logout.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(store.Bundle);
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
        } finally { store.Complete.TrySetResult(true); }
    }

    [Fact]
    public async Task LogoutTimesOutBehindAPendingWriteAndStillDisablesCredentialReuse() {
        var store = new DelayedStore();
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath == "/login/oauth/access_token"
            ? "{\"access_token\":\"renewed\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}" : "{\"id\":42}"))));
        using var auth = new CopilotNativeAuthentication(new() { AuthStore = store, GitHubClientId = "app",
            RequestTimeout = TimeSpan.FromMilliseconds(100) }, http);
        var refresh = auth.GetAccessTokenAsync();
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try {
            Assert.Same(refresh, await Task.WhenAny(refresh, Task.Delay(2000)));
            await Assert.ThrowsAsync<TimeoutException>(() => refresh);
            var logout = auth.LogoutAsync();
            Assert.Same(logout, await Task.WhenAny(logout, Task.Delay(2000)));
            await Assert.ThrowsAsync<TimeoutException>(() => logout);
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
            store.Complete.TrySetResult(true);
            // Retry removal after the non-cooperative write has completed; it cannot race past this logout.
            await auth.LogoutAsync();
            Assert.Null(store.Bundle);
        } finally { store.Complete.TrySetResult(true); }
    }

    private sealed class DelayedStore : IRemovableAuthBundleStore {
        internal AuthBundle? Bundle = new("copilot", "expired", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1)) { AccountId = "42" };
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult(Bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(Bundle is null ? [] : [Bundle]);
        public async Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) { Started.TrySetResult(true); await Complete.Task; Bundle = bundle; }
        public Task RemoveAsync(string provider, string? accountId, CancellationToken cancellationToken = default) { Bundle = null; return Task.CompletedTask; }
    }

    [Fact]
    public async Task PinnedAccountIsVerifiedAgainWhenTheHostChangesItsCredential() {
        string token = "first";
        int identities = 0;
        using var http = new HttpClient(new Handler((request, _) => {
            identities++;
            return Task.FromResult(Json(request.Headers.Authorization!.Parameter == "first"
                ? "{\"id\":42,\"login\":\"first\"}" : "{\"id\":99,\"login\":\"second\"}"));
        }));
        using var auth = new CopilotNativeAuthentication(new() {
            AccountId = "42", TokenProvider = _ => Task.FromResult(token)
        }, http);
        Assert.Equal("first", await auth.GetAccessTokenAsync());
        Assert.Equal("first", await auth.GetAccessTokenAsync());
        Assert.Equal(1, identities);
        token = "second";
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
        Assert.Equal(2, identities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonCooperativeCredentialCallbackHonorsCancellationAndDeadline(bool timeout) {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var auth = new CopilotNativeAuthentication(new() {
            TokenProvider = _ => pending.Task,
            RequestTimeout = timeout ? TimeSpan.FromMilliseconds(40) : TimeSpan.FromMinutes(1)
        });
        var operation = auth.GetAccessTokenAsync(cancellation.Token);
        if (timeout) await Assert.ThrowsAsync<TimeoutException>(() => operation.WaitAsync(TimeSpan.FromSeconds(3)));
        else {
            cancellation.Cancel();
            Assert.Equal(cancellation.Token,
                (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation)).CancellationToken);
        }
        pending.TrySetException(new IOException("late callback failure"));
    }

    [Fact]
    public async Task DeviceSignInCannotSilentlyLoseToAnExplicitCredential() {
        using var auth = new CopilotNativeAuthentication(new() { GitHubToken = "configured", GitHubClientId = "app" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.LoginAsync(_ => throw new Exception("must not request a code")));
    }

    [Fact]
    public async Task RefreshVerifiesIdentityAndSavesTheRotatedCredential() {
        var store = new Store(new("copilot", "expired", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(-1)) { AccountId = "42" });
        using var http = new HttpClient(new Handler(async (request, _) => {
            if (request.RequestUri!.AbsolutePath == "/login/oauth/access_token") {
                string form = await request.Content!.ReadAsStringAsync();
                Assert.Contains("client_id=host-app", form);
                Assert.Contains("refresh_token=old-refresh", form);
                return Json("{\"access_token\":\"renewed\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
            }
            Assert.Equal("renewed", request.Headers.Authorization!.Parameter);
            return Json("{\"id\":42,\"login\":\"account\"}");
        }));
        using var auth = new CopilotNativeAuthentication(new() {
            AuthStore = store, AccountId = "42", GitHubClientId = "host-app"
        }, http);
        Assert.Equal("renewed", await auth.GetAccessTokenAsync());
        Assert.Equal("new-refresh", store.Bundle!.RefreshToken);
        Assert.Equal("42", store.Bundle.AccountId);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task LogoutPreservesOtherProvidersAndAccounts() {
        string path = Path.Combine(Path.GetTempPath(), "ix-native-auth-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("openai-codex", "chatgpt", "", null) { AccountId = "chat" });
            await store.SaveAsync(new("copilot", "first", "", null) { AccountId = "42" });
            await store.SaveAsync(new("copilot", "second", "", null) { AccountId = "99" });
            using var auth = new CopilotNativeAuthentication(new() { AuthStore = store, AccountId = "42" });
            await auth.LogoutAsync();
            Assert.Null(await store.GetAsync("copilot", "42"));
            Assert.Equal("second", (await store.GetAsync("copilot", "99"))!.AccessToken);
            Assert.Equal("chatgpt", (await store.GetAsync("openai-codex", "chat"))!.AccessToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
        } finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ExplicitStoreDoesNotFallBackToAmbientCredentials() {
        using var auth = new CopilotNativeAuthentication(new() { AuthStore = new Store(null) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
    }

    private sealed class Store(AuthBundle? initial) : IRemovableAuthBundleStore {
        internal AuthBundle? Bundle = initial;
        internal int Saves;
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult(Bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(Bundle is null ? Array.Empty<AuthBundle>() : new[] { Bundle });
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) { Bundle = bundle; Saves++; return Task.CompletedTask; }
        public Task RemoveAsync(string provider, string? accountId, CancellationToken cancellationToken = default) { Bundle = null; return Task.CompletedTask; }
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
