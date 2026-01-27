using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Auth;

public sealed class OAuthLoginService {
    private readonly HttpClient _httpClient = new();

    public async Task<OAuthLoginResult> LoginAsync(OAuthLoginOptions options) {
        options.Config.Validate();
        var config = options.Config;
        var ct = options.CancellationToken;

        var verifier = OAuthPkce.CreateCodeVerifier();
        var challenge = OAuthPkce.CreateCodeChallenge(verifier);
        var state = CreateState();

        var redirectUri = BuildRedirectUri(config);

        var authUrl = BuildAuthorizeUrl(config, redirectUri, state, challenge);

        if (options.OnAuthUrl is not null) {
            await options.OnAuthUrl(authUrl).ConfigureAwait(false);
        }

        string? code = null;
        if (options.UseLocalListener) {
            code = await TryListenForCodeAsync(redirectUri, state, options.Timeout, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(code)) {
            if (options.OnPrompt is null) {
                throw new InvalidOperationException("OAuth code not received and no prompt handler configured.");
            }
            var input = await options.OnPrompt("Paste the redirect URL or authorization code:").ConfigureAwait(false);
            code = ExtractCode(input, state);
        }

        if (string.IsNullOrWhiteSpace(code)) {
            throw new InvalidOperationException("Authorization code was not provided.");
        }

        var tokenResponse = await ExchangeCodeAsync(config, redirectUri, verifier, code!, ct).ConfigureAwait(false);
        var bundle = BuildBundle(tokenResponse);
        return new OAuthLoginResult(bundle, tokenResponse);
    }

    public async Task<OAuthLoginResult> RefreshAsync(OAuthConfig config, AuthBundle bundle, CancellationToken cancellationToken = default) {
        config.Validate();
        var tokenResponse = await RefreshTokenAsync(config, bundle.RefreshToken, cancellationToken).ConfigureAwait(false);
        bundle.AccessToken = tokenResponse["access_token"];
        if (tokenResponse.TryGetValue("refresh_token", out var refresh) && !string.IsNullOrWhiteSpace(refresh)) {
            bundle.RefreshToken = refresh;
        }
        if (tokenResponse.TryGetValue("expires_in", out var expiresRaw) && int.TryParse(expiresRaw, out var seconds)) {
            bundle.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }
        if (tokenResponse.TryGetValue("token_type", out var tokenType)) {
            bundle.TokenType = tokenType;
        }
        if (tokenResponse.TryGetValue("scope", out var scope)) {
            bundle.Scope = scope;
        }
        if (tokenResponse.TryGetValue("id_token", out var idToken) && !string.IsNullOrWhiteSpace(idToken)) {
            bundle.IdToken = idToken;
        }
        bundle.AccountId = ResolveAccountId(bundle.AccessToken, tokenResponse, bundle.AccountId);
        return new OAuthLoginResult(bundle, tokenResponse);
    }

    private static string BuildAuthorizeUrl(OAuthConfig config, string redirectUri, string state, string challenge) {
        var query = new List<string> {
            $"response_type=code",
            $"client_id={Uri.EscapeDataString(config.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            $"code_challenge_method=S256",
            $"state={Uri.EscapeDataString(state)}"
        };
        if (!string.IsNullOrWhiteSpace(config.Scopes)) {
            query.Add($"scope={Uri.EscapeDataString(config.Scopes)}");
        }
        if (config.AddOrganizations) {
            query.Add("id_token_add_organizations=true");
        }
        if (config.CodexCliSimplifiedFlow) {
            query.Add("codex_cli_simplified_flow=true");
        }
        if (!string.IsNullOrWhiteSpace(config.Originator)) {
            query.Add($"originator={Uri.EscapeDataString(config.Originator)}");
        }
        return config.AuthorizeUrl + (config.AuthorizeUrl.Contains("?") ? "&" : "?") + string.Join("&", query);
    }

    private async Task<Dictionary<string, string>> ExchangeCodeAsync(OAuthConfig config, string redirectUri, string verifier, string code,
        CancellationToken cancellationToken) {
        var body = new Dictionary<string, string> {
            ["grant_type"] = "authorization_code",
            ["client_id"] = config.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier
        };
        return await PostFormAsync(config.TokenUrl, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> RefreshTokenAsync(OAuthConfig config, string refreshToken, CancellationToken cancellationToken) {
        var body = new Dictionary<string, string> {
            ["grant_type"] = "refresh_token",
            ["client_id"] = config.ClientId,
            ["refresh_token"] = refreshToken
        };
        return await PostFormAsync(config.TokenUrl, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, string>> PostFormAsync(string url, Dictionary<string, string> body, CancellationToken cancellationToken) {
        using var content = new FormUrlEncodedContent(body);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadAsStringAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"OAuth token request failed ({(int)response.StatusCode}): {responseText}");
        }
        var value = JsonLite.Parse(responseText);
        var obj = value?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid OAuth token response.");
        }
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in obj) {
            var raw = entry.Value?.AsString() ?? entry.Value?.ToString() ?? string.Empty;
            dict[entry.Key] = raw;
        }
        return dict;
    }

    private static AuthBundle BuildBundle(Dictionary<string, string> tokenResponse) {
        if (!tokenResponse.TryGetValue("access_token", out var access) || string.IsNullOrWhiteSpace(access)) {
            throw new InvalidOperationException("OAuth response missing access_token.");
        }
        if (!tokenResponse.TryGetValue("refresh_token", out var refresh) || string.IsNullOrWhiteSpace(refresh)) {
            throw new InvalidOperationException("OAuth response missing refresh_token.");
        }
        DateTimeOffset? expiresAt = null;
        if (tokenResponse.TryGetValue("expires_in", out var expiresRaw) && int.TryParse(expiresRaw, out var seconds)) {
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds);
        }
        var bundle = new AuthBundle(OpenAICodexDefaults.Provider, access, refresh, expiresAt) {
            TokenType = tokenResponse.TryGetValue("token_type", out var tokenType) ? tokenType : null,
            Scope = tokenResponse.TryGetValue("scope", out var scope) ? scope : null,
            AccountId = ResolveAccountId(access, tokenResponse, null),
            IdToken = tokenResponse.TryGetValue("id_token", out var idToken) ? idToken : null
        };
        return bundle;
    }

    private static string CreateState() {
        var bytes = new byte[16];
        FillRandom(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

#if NETSTANDARD2_0
    private static Task<string?> TryListenForCodeAsync(string redirectUri, string state, TimeSpan timeout, CancellationToken cancellationToken) {
        return Task.FromResult<string?>(null);
    }
#else
    private static async Task<string?> TryListenForCodeAsync(string redirectUri, string state, TimeSpan timeout, CancellationToken cancellationToken) {
        if (!HttpListener.IsSupported) {
            return null;
        }
        var listener = new HttpListener();
        listener.Prefixes.Add(BuildListenerPrefix(redirectUri));
        try {
            listener.Start();
        } catch {
            listener.Close();
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try {
            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
            if (completed != contextTask) {
                return null;
            }
            var context = await contextTask.ConfigureAwait(false);
            var request = context.Request;
            var response = context.Response;
            var query = request.Url?.Query ?? string.Empty;
            var code = ExtractCode(query, state);
            var responseBody = Encoding.UTF8.GetBytes("<html><body>You can close this window.</body></html>");
            response.ContentType = "text/html";
            response.ContentLength64 = responseBody.Length;
            await response.OutputStream.WriteAsync(responseBody, 0, responseBody.Length, cancellationToken).ConfigureAwait(false);
            response.Close();
            return code;
        } catch {
            return null;
        } finally {
            listener.Close();
        }
    }
#endif

    private static string? ExtractCode(string input, string expectedState) {
        if (string.IsNullOrWhiteSpace(input)) {
            return null;
        }
        var url = input.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
            return ExtractCodeFromPlainInput(url, expectedState);
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return null;
        }
        var query = uri.Query;
        var fragment = uri.Fragment;
        var code = ExtractCodeFromQuery(query, expectedState) ?? ExtractCodeFromQuery(fragment, expectedState);
        return code;
    }

    private static string? ExtractCodeFromQuery(string query, string expectedState) {
        if (string.IsNullOrWhiteSpace(query)) {
            return null;
        }
        var trimmed = query.TrimStart('?', '#');
        var pairs = trimmed.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
        string? code = null;
        string? state = null;
        foreach (var pair in pairs) {
            var parts = pair.Split(new[] { '=' }, 2, StringSplitOptions.None);
            if (parts.Length != 2) {
                continue;
            }
            var key = WebUtility.UrlDecode(parts[0]);
            var value = WebUtility.UrlDecode(parts[1]);
            if (string.Equals(key, "code", StringComparison.OrdinalIgnoreCase)) {
                code = value;
            } else if (string.Equals(key, "state", StringComparison.OrdinalIgnoreCase)) {
                state = value;
            }
        }
        if (!string.IsNullOrWhiteSpace(expectedState) && state is not null && !string.Equals(state, expectedState, StringComparison.Ordinal)) {
            return null;
        }
        return code;
    }

    private static string BuildRedirectUri(OAuthConfig config) {
        if (!string.IsNullOrWhiteSpace(config.RedirectUri)) {
            return config.RedirectUri;
        }
        var path = string.IsNullOrWhiteSpace(config.RedirectPath) ? "/auth/callback" : config.RedirectPath.Trim();
        if (!path.StartsWith("/", StringComparison.Ordinal)) {
            path = "/" + path;
        }
        return $"http://127.0.0.1:{config.RedirectPort}{path}";
    }

    private static string BuildListenerPrefix(string redirectUri) {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) {
            return EnsureTrailingSlash(redirectUri);
        }
        var builder = new UriBuilder(uri) {
            Query = string.Empty,
            Fragment = string.Empty
        };
        var prefix = builder.Uri.ToString();
        return EnsureTrailingSlash(prefix);
    }

    private static string EnsureTrailingSlash(string value) {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static string? ExtractCodeFromPlainInput(string input, string expectedState) {
        if (input.StartsWith("?") || input.StartsWith("#")) {
            return ExtractCodeFromQuery(input, expectedState);
        }
        var hashIndex = input.IndexOf('#');
        if (hashIndex > 0) {
            var codePart = input.Substring(0, hashIndex);
            var statePart = input.Substring(hashIndex + 1);
            var code = ExtractBareValue(codePart);
            var state = ExtractBareValue(statePart);
            if (!string.IsNullOrWhiteSpace(expectedState) && !string.IsNullOrWhiteSpace(state) &&
                !string.Equals(state, expectedState, StringComparison.Ordinal)) {
                return null;
            }
            return code;
        }
        return input;
    }

    private static string ExtractBareValue(string input) {
        var trimmed = input.Trim();
        var equals = trimmed.IndexOf('=');
        if (equals < 0) {
            return trimmed;
        }
        return trimmed.Substring(equals + 1);
    }

    private static Task<string> ReadAsStringAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
#else
        return content.ReadAsStringAsync(cancellationToken);
#endif
    }

    private static void FillRandom(byte[] buffer) {
#if NETSTANDARD2_0 || NET472
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
#else
        RandomNumberGenerator.Fill(buffer);
#endif
    }

    private static string? ResolveAccountId(string accessToken, Dictionary<string, string> tokenResponse, string? fallback) {
        if (tokenResponse.TryGetValue("account_id", out var accountId) && !string.IsNullOrWhiteSpace(accountId)) {
            return accountId;
        }
        return JwtDecoder.TryGetAccountId(accessToken) ?? fallback;
    }
}
