using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Chat;
using System.Net;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class NativeLogoutRaceTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStaleCredentialCannotRenewAfterItsAccountWasRemovedOrReplaced(bool replacement) {
        var store = new Store();
        int refreshes = 0;
        var options = new OpenAINativeOptions { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false };
        var first = new OpenAINativeAuthManager(options, (_, _, _) => {
            refreshes++;
            return Task.FromResult(new OAuthLoginResult(new("openai-codex", "renewed", "refresh", null) { AccountId = "selected" }, new()));
        });
        var selected = (await first.TryGetValidBundleAsync(default))!;
        await new OpenAINativeAuthManager(options).LogoutAsync(default);
        if (replacement) store.Bundle = new("openai-codex", "another-account", "refresh", null) { AccountId = "other" };
        await Assert.ThrowsAsync<OpenAIAuthenticationRequiredException>(() => first.RefreshAsync(selected, default));
        Assert.Equal(0, refreshes);
        Assert.Equal(replacement ? "other" : null, store.Bundle?.AccountId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADelayed401CannotReauthenticateAfterAnotherOwnerLogsOut(bool ambientFallback) {
        string directory = Path.Combine(Path.GetTempPath(), "ix-logout-401-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string ambient = Path.Combine(directory, "auth.json");
        try {
            if (ambientFallback) await File.WriteAllTextAsync(ambient,
                "{\"tokens\":{\"access_token\":\"ambient\",\"refresh_token\":\"refresh\",\"account_id\":\"selected\"}}");
            var store = new Store();
            var options = new OpenAINativeOptions { AuthStore = store, CodexHome = directory,
                LoadCodexAuthJson = ambientFallback, PersistCodexAuthJson = false };
            int refreshes = 0;
            var auth = new OpenAINativeAuthManager(options, (_, _, _) => {
                refreshes++;
                return Task.FromResult(new OAuthLoginResult(new("openai-codex", "renewed", "refresh", null) { AccountId = "selected" }, new()));
            });
            var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = new HttpClient(new DelayedHandler(() => { sent.TrySetResult(true); return response.Task; }));
            using var transport = new OpenAINativeTransport(options, http, auth);
            var thread = await transport.StartThreadAsync("model", null, null, null, default);
            var turn = transport.StartTurnAsync(thread.Id, ChatInput.FromText("question"), new() { Model = "model" }, null, null, null, default);
            await sent.Task;
            await new OpenAINativeAuthManager(options).LogoutAsync(default);
            response.SetResult(new(HttpStatusCode.Unauthorized));
            await Assert.ThrowsAsync<OpenAIAuthenticationRequiredException>(() => turn);
            Assert.Equal(0, refreshes);
            Assert.Null(store.Bundle);
        } finally {
            if (File.Exists(ambient)) File.Delete(ambient);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedStoreLogoutInvalidatesPendingLoginOnlyForTheRemovedAccount(bool otherAccount) {
        var complete = new TaskCompletionSource<OAuthLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Store();
        var first = new OpenAINativeAuthManager(new() { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false }, null, _ => complete.Task);
        var second = new OpenAINativeAuthManager(new() { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false });
        Task login = first.LoginAsync(null, null, false, TimeSpan.FromMinutes(1), default);
        await second.LogoutAsync(default);
        complete.SetResult(new(new("openai-codex", "approved", "refresh", null) { AccountId = otherAccount ? "other" : "selected" }, new()));
        if (otherAccount) {
            await login;
            Assert.Equal("other", store.Bundle!.AccountId);
        } else {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => login);
            Assert.Null(store.Bundle);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutWaitsForANonCooperativeSaveThenRemovesIt(bool login) {
        var saveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Store { BeforeSave = async () => { saveStarted.SetResult(true); await finishSave.Task; } };
        Task<OAuthLoginResult> Approved() => Task.FromResult(new OAuthLoginResult(
            new("openai-codex", "approved", "refresh", null) { AccountId = "selected" }, new()));
        var auth = new OpenAINativeAuthManager(new() { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false },
            (_, _, _) => Approved(), _ => Approved());
        Task operation = login ? auth.LoginAsync(null, null, false, TimeSpan.FromMinutes(1), default) : auth.RefreshAsync(store.Bundle!, default);
        await saveStarted.Task;
        Task logout = auth.LogoutAsync(default);
        Assert.False(logout.IsCompleted);
        finishSave.SetResult(true);
        await logout;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => operation);
        Assert.Null(store.Bundle);
        Assert.Null(await auth.TryGetValidBundleAsync(default));
        store.BeforeSave = null;
        await auth.LoginAsync(null, null, false, TimeSpan.FromMinutes(1), default);
        Assert.Equal("approved", (await auth.TryGetValidBundleAsync(default))!.AccessToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogoutSupersedesPendingOAuthWithoutRestoringCredentials(bool login) {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<OAuthLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Store();
        var auth = new OpenAINativeAuthManager(new() { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false },
            (_, _, _) => { started.SetResult(true); return complete.Task; },
            _ => { started.SetResult(true); return complete.Task; });
        Task operation = login ? auth.LoginAsync(null, null, false, TimeSpan.FromMinutes(1), default)
            : auth.RefreshAsync(store.Bundle!, default);
        await started.Task;
        Task logout = auth.LogoutAsync(default);
        complete.SetResult(new(new("openai-codex", "renewed", "refresh", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "selected" }, new()));
        await logout;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => operation);
        Assert.Null(store.Bundle);
        Assert.Null(await auth.TryGetValidBundleAsync(default));
        Assert.Equal(0, store.Saves);
    }

    private sealed class Store : IRemovableAuthBundleStore {
        internal AuthBundle? Bundle = new("openai-codex", "current", "refresh", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "selected" };
        internal int Saves;
        internal Func<Task>? BeforeSave;
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult(Bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(Bundle is null ? Array.Empty<AuthBundle>() : new[] { Bundle });
        public async Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) {
            if (BeforeSave is not null) await BeforeSave();
            Saves++; Bundle = bundle;
        }
        public Task RemoveAsync(string provider, string? accountId, CancellationToken cancellationToken = default) { Bundle = null; return Task.CompletedTask; }
    }
    private sealed class DelayedHandler(Func<Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send();
    }
}
