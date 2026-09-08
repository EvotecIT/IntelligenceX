using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Authentication.GitHub;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Utils;

namespace IntelligenceX.Copilot.Native;

/// <summary>Owns Copilot credentials, optional GitHub device sign-in, and renewal independently of any product or CLI.</summary>
public sealed class CopilotNativeAuthentication : IDisposable {
    /// <summary>Provider key used in the shared IntelligenceX authentication store.</summary>
    public const string Provider = "copilot";
    private readonly CopilotNativeOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _gate;
    private readonly AuthStoreCoordination.CredentialEpoch _storeEpoch;
    private AuthBundle? _bundle;
    private volatile bool _signedOut;
    private int _logoutGeneration;
    private readonly object _stateLock = new();
    private string? _verifiedToken;

    /// <summary>Creates a credential owner. A supplied HTTP client takes precedence over the options handler and remains owned by the caller.</summary>
    public CopilotNativeAuthentication(CopilotNativeOptions options, HttpClient? httpClient = null) {
        if (options is null) throw new ArgumentNullException(nameof(options));
        options.Validate(); _options = options.Snapshot();
        _gate = AuthStoreCoordination.GetGate(_options.AuthStore);
        _storeEpoch = AuthStoreCoordination.GetEpoch(_gate, Provider);
        _http = httpClient ?? new HttpClient(_options.HttpMessageHandler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = httpClient is null;
    }

    /// <summary>Returns the current credential, renewing a stored expiring token when configured. Never invokes a CLI.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        try {
            // The underlying operation retains its gate until any non-cooperative store write finishes.
            string token = await TaskCancellation.WaitAsync(GetSelectedTokenAsync(deadline.Token), deadline.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(_options.AccountId) && token != _verifiedToken) {
                var identity = await ReadIdentityAsync(token, deadline.Token).ConfigureAwait(false);
                if (identity.AccountId != _options.AccountId)
                    throw new InvalidOperationException("The GitHub credential belongs to a different account.");
                _verifiedToken = token;
            }
            if (_signedOut) throw new InvalidOperationException("Copilot is signed out. Sign in before sending requests.");
            return token;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        } catch (OperationCanceledException) {
            throw new TimeoutException("Copilot authentication exceeded its configured timeout.");
        }
    }

    private async Task<string> GetSelectedTokenAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_signedOut) throw new InvalidOperationException("Copilot is signed out. Sign in before sending requests.");
        if (_options.TokenProvider is not null)
            return ValidateToken(await TaskCancellation.WaitAsync(_options.TokenProvider(cancellationToken), cancellationToken).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(_options.GitHubToken)) return ValidateToken(_options.GitHubToken!);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_signedOut) throw new InvalidOperationException("Copilot is signed out. Sign in before sending requests.");
            if (_options.AuthStore is not null)
                _bundle = await _options.AuthStore.GetAsync(Provider, _options.AccountId ?? _bundle?.AccountId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (_bundle is not null) {
                if (!string.Equals(_bundle.Provider, Provider, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(_options.AccountId) && _bundle.AccountId != _options.AccountId))
                    throw new InvalidOperationException("The authentication store returned a different provider or account.");
                if (_bundle.ExpiresAt.HasValue && _bundle.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1)) {
                    using var flow = CreateDeviceFlow();
                    var renewed = await flow.RefreshAsync(_bundle, _options.GitHubClientSecret, cancellationToken).ConfigureAwait(false);
                    AccountInfo identity = await ReadIdentityAsync(renewed.AccessToken, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(_bundle.AccountId) && _bundle.AccountId != identity.AccountId)
                        throw new InvalidOperationException("The refreshed GitHub credential belongs to a different account.");
                    renewed.AccountId = identity.AccountId;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_options.AuthStore is not null) await _options.AuthStore.SaveAsync(renewed, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    _bundle = renewed;
                }
                return ValidateToken(_bundle.AccessToken);
            }
            if (_options.GetEnvironmentToken() is { } ambientToken) return ValidateToken(ambientToken);
            throw new InvalidOperationException("Copilot authentication is required. Supply a GitHub credential, a token provider, or sign in using a registered GitHub app.");
        } finally { _gate.Release(); }
    }

    /// <summary>Runs device sign-in, verifies GitHub identity, and saves the credential only when an auth store was explicitly supplied.</summary>
    public async Task<AccountInfo> LoginAsync(Action<GitHubDeviceAuthorization> onCode, CancellationToken cancellationToken = default) {
        if (onCode is null) throw new ArgumentNullException(nameof(onCode));
        if (_options.TokenProvider is not null || !string.IsNullOrWhiteSpace(_options.GitHubToken))
            throw new InvalidOperationException("Device sign-in cannot replace an explicitly configured credential. Create a client using an authentication store or device sign-in instead.");
        int generation = Volatile.Read(ref _logoutGeneration);
        long storeEpoch = _storeEpoch.Capture();
        using var flow = CreateDeviceFlow();
        var pending = await flow.RequestCodeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        onCode(pending);
        var bundle = await flow.CompleteAsync(pending, Provider, cancellationToken).ConfigureAwait(false);
        var account = await ReadIdentityAsync(bundle.AccessToken, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_options.AccountId) && _options.AccountId != account.AccountId)
            throw new InvalidOperationException("GitHub signed in to a different account than the configured account.");
        bundle.AccountId = account.AccountId;
        await RunWithDeadlineAsync(async token => {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try {
                token.ThrowIfCancellationRequested();
                if (generation != Volatile.Read(ref _logoutGeneration) || !_storeEpoch.IsCurrent(storeEpoch, bundle.AccountId))
                    throw new InvalidOperationException("Sign-in was superseded by logout.");
                if (_options.AuthStore is not null) await _options.AuthStore.SaveAsync(bundle, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_stateLock) {
                    if (generation != _logoutGeneration || !_storeEpoch.IsCurrent(storeEpoch, bundle.AccountId))
                        throw new InvalidOperationException("Sign-in was superseded by logout.");
                    _bundle = bundle; _signedOut = false;
                }
            } finally { _gate.Release(); }
        }, cancellationToken).ConfigureAwait(false);
        return account;
    }

    /// <summary>Verifies the identity associated with the selected credential.</summary>
    public async Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) {
        string token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var account = await ReadIdentityAsync(token, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_options.AccountId) && account.AccountId != _options.AccountId)
            throw new InvalidOperationException("The GitHub credential belongs to a different account.");
        return account;
    }

    /// <summary>Removes this account's stored credential and prevents reuse in this instance. Does not revoke GitHub grants.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock) {
            _logoutGeneration++;
            _signedOut = true;
        }
        _verifiedToken = null;
        await RunWithDeadlineAsync(async token => {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try {
                if (_options.AuthStore is not null && _options.TokenProvider is null && string.IsNullOrWhiteSpace(_options.GitHubToken)) {
                    var selected = _bundle ?? await _options.AuthStore.GetAsync(Provider, _options.AccountId, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (selected is not null && string.Equals(selected.Provider, Provider, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(_options.AccountId) || selected.AccountId == _options.AccountId)) {
                        _storeEpoch.Invalidate(selected.AccountId);
                        await _options.AuthStore.RemoveAsync(Provider, selected.AccountId, token).ConfigureAwait(false);
                    } else if (selected is null) _storeEpoch.Invalidate(_options.AccountId);
                }
                _bundle = null;
            } finally { _gate.Release(); }
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunWithDeadlineAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        async Task<bool> Run() { await action(deadline.Token).ConfigureAwait(false); return true; }
        try { await TaskCancellation.WaitAsync(Run(), deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (OperationCanceledException) { throw new TimeoutException("Copilot authentication exceeded its configured timeout."); }
    }

    private GitHubDeviceFlowClient CreateDeviceFlow() => new(_options.GitHubClientId ?? throw new InvalidOperationException(
        "Configure GitHubClientId for your registered app before device sign-in or token renewal."), _options.GitHubAuthBaseUrl, _http, _options.RequestTimeout);

    private async Task<AccountInfo> ReadIdentityAsync(string token, CancellationToken cancellationToken) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.GitHubApiBaseUrl.TrimEnd('/') + "/"), "user"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidateToken(token));
        request.Headers.UserAgent.ParseAdd("IntelligenceX/0.1.1");
        using var response = await TaskCancellation.WaitAsync(_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token),
            deadline.Token, abandoned => abandoned.Dispose()).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"GitHub identity request failed (HTTP {(int)response.StatusCode}).");
        var json = await ResponseBudgetStream.ReadTextAsync(response.Content, 65_536, deadline.Token).ConfigureAwait(false);
        var obj = JsonLite.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Invalid GitHub identity response.");
        string? id = obj.GetInt64("id")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? obj.GetString("id");
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("GitHub did not return an account ID.");
        return AccountInfo.FromJson(new JsonObject().Add("id", id).Add("name", obj.GetString("login")).Add("type", "github"));
    }

    private static string ValidateToken(string value) {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new InvalidOperationException("Copilot requires a nonempty GitHub credential without newlines.");
        return value.Trim();
    }

    /// <summary>Releases HTTP resources created by this instance. Call after outstanding operations finish.</summary>
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
