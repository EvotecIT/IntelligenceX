using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Utils;

namespace IntelligenceX.Authentication.GitHub;

/// <summary>Reusable GitHub device authorization and token renewal over HTTP. Never launches a process or browser.</summary>
public sealed class GitHubDeviceFlowClient : IDisposable {
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _clientId;
    private readonly Uri _authBase;
    private readonly TimeSpan _requestTimeout;

    /// <summary>Creates an authorization client for a registered GitHub app with device flow enabled.</summary>
    /// <param name="clientId">The host product's registered public client ID.</param>
    /// <param name="authBaseUrl">GitHub login root, or an explicitly trusted enterprise login root.</param>
    /// <param name="httpClient">Optional host HTTP client. The caller retains ownership.</param>
    public GitHubDeviceFlowClient(string clientId, string authBaseUrl = "https://github.com/", HttpClient? httpClient = null)
        : this(clientId, authBaseUrl, httpClient, TimeSpan.FromSeconds(30)) { }

    /// <summary>Creates an authorization client with a timeout for each HTTP operation, independently of the user's approval window.</summary>
    /// <param name="clientId">The host product's registered public client ID.</param>
    /// <param name="authBaseUrl">GitHub login root, or an explicitly trusted enterprise login root.</param>
    /// <param name="httpClient">Optional host HTTP client. The caller retains ownership.</param>
    /// <param name="requestTimeout">Maximum duration of an HTTP request and its response body.</param>
    public GitHubDeviceFlowClient(string clientId, string authBaseUrl, HttpClient? httpClient, TimeSpan requestTimeout) {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("A registered GitHub client ID is required.", nameof(clientId));
        if (requestTimeout <= TimeSpan.Zero || requestTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        if (!Uri.TryCreate(authBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("GitHub login root must use HTTPS without credentials, query or fragment.", nameof(authBaseUrl));
        _clientId = clientId; _authBase = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        _requestTimeout = requestTimeout;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = httpClient is null;
    }

    /// <summary>Requests a sign-in code. This does not authorize the application.</summary>
    public async Task<GitHubDeviceAuthorization> RequestCodeAsync(string? scopes = null, CancellationToken cancellationToken = default) {
        var values = new Dictionary<string, string> { ["client_id"] = _clientId };
        if (!string.IsNullOrWhiteSpace(scopes)) values["scope"] = scopes!;
        JsonObject result = await PostAsync("login/device/code", values, cancellationToken).ConfigureAwait(false);
        string device = Required(result, "device_code"), code = Required(result, "user_code");
        if (!Uri.TryCreate(Required(result, "verification_uri"), UriKind.Absolute, out var verification)
            || verification.Scheme != Uri.UriSchemeHttps || verification.Authority != _authBase.Authority || verification.UserInfo.Length != 0)
            throw new InvalidOperationException("GitHub returned an unexpected verification URL.");
        int expiry = checked((int)(result.GetInt64("expires_in") ?? 900));
        int interval = checked((int)(result.GetInt64("interval") ?? 5));
        if (expiry < 1 || expiry > 3600 || interval < 1 || interval > 300)
            throw new InvalidOperationException("GitHub returned invalid device authorization timing.");
        return new GitHubDeviceAuthorization(device, code, verification, interval, DateTimeOffset.UtcNow.AddSeconds(expiry));
    }

    /// <summary>Waits for the user to approve a pending sign-in, honoring server polling intervals and cancellation.</summary>
    public async Task<AuthBundle> CompleteAsync(GitHubDeviceAuthorization authorization, string provider = "github", CancellationToken cancellationToken = default) {
        if (authorization is null) throw new ArgumentNullException(nameof(authorization));
        TimeSpan remaining = authorization.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new TimeoutException("GitHub device authorization expired.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(remaining);
        int interval = authorization.IntervalSeconds;
        try {
            while (true) {
                await Task.Delay(TimeSpan.FromSeconds(interval), deadline.Token).ConfigureAwait(false);
                var result = await PostAsync("login/oauth/access_token", new Dictionary<string, string> {
                    ["client_id"] = _clientId, ["device_code"] = authorization.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }, deadline.Token, allowPending: true).ConfigureAwait(false);
                string? error = result.GetString("error");
                if (error == "authorization_pending") continue;
                if (error == "slow_down") { interval = Math.Min(interval + 5, 300); continue; }
                if (error is not null) throw new InvalidOperationException("GitHub device authorization was denied or expired. Start sign-in again.");
                return ParseBundle(result, provider);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        } catch (OperationCanceledException) {
            throw new TimeoutException("GitHub device authorization expired.");
        }
    }

    /// <summary>Renews an expiring GitHub credential. Some registered app types require the host to supply its client secret.</summary>
    public async Task<AuthBundle> RefreshAsync(AuthBundle bundle, string? clientSecret = null, CancellationToken cancellationToken = default) {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        if (string.IsNullOrWhiteSpace(bundle.RefreshToken)) throw new InvalidOperationException("The GitHub credential cannot be refreshed; sign in again.");
        var values = new Dictionary<string, string> { ["client_id"] = _clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = bundle.RefreshToken };
        if (!string.IsNullOrWhiteSpace(clientSecret)) values["client_secret"] = clientSecret!;
        var result = ParseBundle(await PostAsync("login/oauth/access_token", values, cancellationToken).ConfigureAwait(false), bundle.Provider);
        result.AccountId = bundle.AccountId;
        return result;
    }

    private async Task<JsonObject> PostAsync(string path, Dictionary<string, string> values, CancellationToken cancellationToken, bool allowPending = false) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        try {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_authBase, path)) { Content = new FormUrlEncodedContent(values) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("IntelligenceX/0.1.1");
            using var response = await TaskCancellation.WaitAsync(_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token),
                deadline.Token, abandoned => abandoned.Dispose()).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"GitHub authorization failed (HTTP {(int)response.StatusCode}).");
            var json = await ResponseBudgetStream.ReadTextAsync(response.Content, 65_536, deadline.Token).ConfigureAwait(false);
            var obj = JsonLite.Parse(json)?.AsObject() ?? throw new InvalidOperationException("GitHub returned an invalid authorization response.");
            if (!allowPending && obj.GetString("error") is not null) throw new InvalidOperationException("GitHub rejected authorization. Check the registered app configuration or sign in again.");
            return obj;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        } catch (OperationCanceledException) {
            throw new TimeoutException("GitHub authorization HTTP request exceeded its configured timeout.");
        }
    }

    private static AuthBundle ParseBundle(JsonObject obj, string provider) {
        long? expires = obj.GetInt64("expires_in");
        if (expires.HasValue && (expires < 1 || expires > 31_536_000)) throw new InvalidOperationException("GitHub returned an invalid token lifetime.");
        return new AuthBundle(provider, Required(obj, "access_token"), obj.GetString("refresh_token") ?? string.Empty,
            expires.HasValue ? DateTimeOffset.UtcNow.AddSeconds(expires.Value) : null) {
            TokenType = obj.GetString("token_type"), Scope = obj.GetString("scope")
        };
    }

    private static string Required(JsonObject obj, string name) => !string.IsNullOrWhiteSpace(obj.GetString(name))
        ? obj.GetString(name)! : throw new InvalidOperationException("GitHub returned an incomplete authorization response.");

    /// <summary>Releases only HTTP resources created by this instance.</summary>
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
