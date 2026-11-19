#if NET8_0_OR_GREATER
// GitHub App sign-in reads PEM keys, which needs .NET 8 or newer; older targets keep user tokens only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// A GitHub App installation to sign in as instead of a user token. Its installation tokens have their own rate limit,
/// separate from the user's. The private key stays in a local file named here; nothing in this record is a secret.
/// </summary>
public sealed class GitHubAppSettings {
    /// <summary>Creates settings for one App installation.</summary>
    public GitHubAppSettings(long appId, long installationId, string privateKeyPath, string? apiBaseUrl = null, bool enabled = true) {
        AppId = appId;
        InstallationId = installationId;
        PrivateKeyPath = privateKeyPath;
        ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.github.com/" : apiBaseUrl!.TrimEnd('/') + "/";
        Enabled = enabled;
    }

    /// <summary>The App's numeric ID.</summary>
    public long AppId { get; }

    /// <summary>The installation's numeric ID on the account or organisation.</summary>
    public long InstallationId { get; }

    /// <summary>Path to the App's private key (PEM).</summary>
    public string PrivateKeyPath { get; }

    /// <summary>The REST API root; GitHub.com unless set.</summary>
    public string ApiBaseUrl { get; }

    /// <summary>False keeps the file but signs in with the user token again.</summary>
    public bool Enabled { get; }

    /// <summary>
    /// The settings file: <c>INTELLIGENCEX_GITHUB_APP</c> when set, otherwise <c>github-app.json</c> in the local
    /// IntelligenceX folder.
    /// </summary>
    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_APP") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelligenceX", "github-app.json");

    /// <summary>
    /// Reads the settings, or null when the file is missing, disabled or incomplete, so the user token stays in use.
    /// </summary>
    public static GitHubAppSettings? Load(string? path = null) {
        path ??= DefaultPath();
        try {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var enabled = !root.TryGetProperty("enabled", out var flag) || flag.ValueKind != JsonValueKind.False;
            if (!enabled || !root.TryGetProperty("appId", out var app) || !app.TryGetInt64(out var appId) ||
                !root.TryGetProperty("installationId", out var installation) || !installation.TryGetInt64(out var installationId) ||
                !root.TryGetProperty("privateKeyPath", out var key) || key.GetString() is not { Length: > 0 } keyPath) return null;
            var api = root.TryGetProperty("apiBaseUrl", out var url) ? url.GetString() : null;
            return new GitHubAppSettings(appId, installationId, keyPath, api);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) {
            return null;
        }
    }
}

/// <summary>
/// Mints and caches the installation token of a GitHub App: a ten-minute JWT signed with the App's key buys a token
/// that lasts an hour, renewed five minutes before it expires.
/// </summary>
public sealed class GitHubAppTokenSource : IDisposable {
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);
    private readonly GitHubAppSettings _settings;
    private readonly HttpClient _client;
    private readonly bool _disposeClient;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    /// <summary>Creates a token source; <paramref name="client"/> is for tests.</summary>
    public GitHubAppTokenSource(GitHubAppSettings settings, HttpClient? client = null, Func<DateTimeOffset>? clock = null) {
        _settings = settings;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _disposeClient = client is null;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The installation ID tokens are minted for.</summary>
    public long InstallationId => _settings.InstallationId;

    /// <summary>The current installation token, minted or renewed when needed.</summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var now = _clock();
            if (_token is not null && now < _expires - RenewBefore) return _token;
            using var request = new HttpRequestMessage(HttpMethod.Post,
                _settings.ApiBaseUrl + "app/installations/" + _settings.InstallationId + "/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(now));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("IntelligenceX/1");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("GitHub App token request failed: " + (int)response.StatusCode, null, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            // An answer without a token is a failed sign-in like any other, so callers fall back the same way.
            if (body.RootElement.ValueKind != JsonValueKind.Object || !body.RootElement.TryGetProperty("token", out var issued) ||
                issued.ValueKind != JsonValueKind.String || issued.GetString() is not { Length: > 0 } value)
                throw new HttpRequestException("GitHub App token response had no token.");
            _token = value;
            _expires = body.RootElement.TryGetProperty("expires_at", out var expires) && expires.TryGetDateTimeOffset(out var at)
                ? at : now.AddHours(1);
            return _token;
        } finally {
            _gate.Release();
        }
    }

    /// <summary>
    /// Whether <paramref name="error"/> means the App couldn't sign in (unreachable, timed out, refused, bad key or
    /// answer), as opposed to the caller cancelling.
    /// </summary>
    public static bool IsSignInFailure(Exception error, CancellationToken cancellationToken) => error switch {
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        HttpRequestException or IOException or UnauthorizedAccessException or CryptographicException or ArgumentException
            or JsonException or InvalidOperationException or KeyNotFoundException => true,
        _ => false
    };

    /// <summary>The App's JWT: issued a minute back to allow for clock drift, valid for nine minutes after.</summary>
    internal string CreateJwt(DateTimeOffset now) {
        var header = Encode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"u8.ToArray());
        var payload = Encode(Encoding.UTF8.GetBytes(
            "{\"iat\":" + now.AddSeconds(-60).ToUnixTimeSeconds() + ",\"exp\":" + now.AddMinutes(9).ToUnixTimeSeconds() + ",\"iss\":\"" + _settings.AppId + "\"}"));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(_settings.PrivateKeyPath));
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + payload + "." + Encode(signature);
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <inheritdoc />
    public void Dispose() {
        if (_disposeClient) _client.Dispose();
        _gate.Dispose();
    }
}

/// <summary>
/// Signs each GitHub request with the App's current installation token, renewing it as it ages. When renewal fails, the
/// request goes out with the user token instead, and the App is tried again after <see cref="RetryAfter"/>.
/// </summary>
public sealed class GitHubAppAuthHandler : DelegatingHandler {
    /// <summary>How long requests use the user token after the App failed to renew.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    private readonly GitHubAppTokenSource _tokens;
    private readonly string? _fallback;
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset _appFailedUntil;

    /// <summary>Creates the handler in front of <paramref name="inner"/>, falling back to <paramref name="fallbackToken"/>.</summary>
    public GitHubAppAuthHandler(GitHubAppTokenSource tokens, HttpMessageHandler inner, string? fallbackToken = null, Func<DateTimeOffset>? clock = null)
        : base(inner) {
        _tokens = tokens;
        _fallback = fallbackToken;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(cancellationToken).ConfigureAwait(false));
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken) {
        if (_fallback is not null && _clock() < _appFailedUntil) return _fallback;
        try {
            return await _tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        } catch (Exception error) when (_fallback is not null && GitHubAppTokenSource.IsSignInFailure(error, cancellationToken)) {
            _appFailedUntil = _clock() + RetryAfter;
            return _fallback;
        }
    }
}
#endif
