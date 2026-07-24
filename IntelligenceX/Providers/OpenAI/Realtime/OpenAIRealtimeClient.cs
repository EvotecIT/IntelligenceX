using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// Mints short-lived OpenAI Realtime credentials and opens Realtime WebSocket sessions.
/// </summary>
/// <remarks>
/// When no API key is supplied, this client reuses the existing ChatGPT/Codex OAuth bundle.
/// User-owned apps can keep that bundle in device-protected storage and call OpenAI directly;
/// no IntelligenceX or Evotec relay is required.
/// </remarks>
public sealed class OpenAIRealtimeClient : IDisposable {
    private readonly OpenAIRealtimeOptions _options;
    private readonly OpenAINativeOptions _chatGptOptions;
    private readonly OpenAINativeAuthManager _authManager;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a Realtime client.
    /// </summary>
    /// <param name="options">Realtime endpoint and API-key options.</param>
    /// <param name="chatGptOptions">ChatGPT OAuth bundle options used when no API key is supplied.</param>
    public OpenAIRealtimeClient(
        OpenAIRealtimeOptions? options = null,
        OpenAINativeOptions? chatGptOptions = null)
        : this(options, chatGptOptions, new HttpClient(), ownsHttpClient: true) {
    }

    internal OpenAIRealtimeClient(
        OpenAIRealtimeOptions? options,
        OpenAINativeOptions? chatGptOptions,
        HttpClient httpClient,
        bool ownsHttpClient) {
        _options = options ?? new OpenAIRealtimeOptions();
        _chatGptOptions = chatGptOptions ?? new OpenAINativeOptions {
            PreferCurrentCodexSession = true
        };
        _options.Validate();
        _chatGptOptions.Validate();
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _authManager = new OpenAINativeAuthManager(_chatGptOptions);
    }

    /// <summary>
    /// Mints a short-lived Realtime client credential. The returned value is sensitive and must not be persisted.
    /// </summary>
    public async Task<OpenAIRealtimeClientSecret> CreateClientSecretAsync(
        OpenAIRealtimeSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        var session = sessionOptions ?? new OpenAIRealtimeSessionOptions();
        session.Validate();

        var authorization = await ResolveAuthorizationAsync(cancellationToken).ConfigureAwait(false);
        var payload = BuildClientSecretPayload(session);
        var response = await SendClientSecretRequestAsync(payload, authorization, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized && authorization.Bundle is not null) {
            var refreshed = await _authManager.RefreshAsync(authorization.Bundle, cancellationToken).ConfigureAwait(false);
            authorization = new RealtimeAuthorization(refreshed.AccessToken, refreshed.AccountId, refreshed);
            response = await SendClientSecretRequestAsync(payload, authorization, cancellationToken).ConfigureAwait(false);
        }
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"OpenAI Realtime client-secret request failed with HTTP {(int)response.StatusCode}: {response.Content}");
        }

        return ParseClientSecret(response.Content, session.Model);
    }

    /// <summary>
    /// Mints a short-lived credential and opens an authenticated Realtime WebSocket.
    /// </summary>
    public async Task<OpenAIRealtimeSession> ConnectAsync(
        OpenAIRealtimeSessionOptions? sessionOptions = null,
        CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        var session = sessionOptions ?? new OpenAIRealtimeSessionOptions();
        session.Validate();
        var secret = await CreateClientSecretAsync(session, cancellationToken).ConfigureAwait(false);
        return await ConnectAsync(secret, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a Realtime WebSocket with an existing short-lived credential.
    /// This overload is useful when another local component owns credential minting.
    /// </summary>
    public async Task<OpenAIRealtimeSession> ConnectAsync(
        OpenAIRealtimeClientSecret secret,
        CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        if (secret is null) {
            throw new ArgumentNullException(nameof(secret));
        }
        if (secret.ExpiresAt <= DateTimeOffset.UtcNow) {
            throw new InvalidOperationException("The OpenAI Realtime client credential has expired.");
        }

        var webSocket = new ClientWebSocket();
        webSocket.Options.SetRequestHeader("Authorization", "Bearer " + secret.Value);
        var endpoint = BuildWebSocketUri(secret.Model);
        try {
            await webSocket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new OpenAIRealtimeSession(webSocket, secret.Model, _options.MaxEventBytes);
        } catch {
            webSocket.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        if (_ownsHttpClient) {
            _httpClient.Dispose();
        }
    }

    internal static string BuildClientSecretPayload(OpenAIRealtimeSessionOptions session) {
        var outputModalities = new JsonArray();
        foreach (var modality in session.OutputModalities) {
            outputModalities.Add(modality);
        }
        var sessionPayload = new JsonObject()
            .Add("type", "realtime")
            .Add("model", session.Model)
            .Add("output_modalities", outputModalities);
        if (!string.IsNullOrWhiteSpace(session.Instructions)) {
            sessionPayload.Add("instructions", session.Instructions);
        }
        if (!string.IsNullOrWhiteSpace(session.Voice)) {
            sessionPayload.Add("audio", new JsonObject()
                .Add("output", new JsonObject()
                    .Add("voice", session.Voice)));
        }

        var lifetimeSeconds = Math.Max(1, (int)Math.Ceiling(session.ClientSecretLifetime.TotalSeconds));
        var payload = new JsonObject()
            .Add("expires_after", new JsonObject()
                .Add("anchor", "created_at")
                .Add("seconds", lifetimeSeconds))
            .Add("session", sessionPayload);
        return JsonLite.Serialize(JsonValue.From(payload));
    }

    internal static OpenAIRealtimeClientSecret ParseClientSecret(string responseJson, string fallbackModel) {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        var value = ReadRequiredString(root, "value");
        if (!root.TryGetProperty("expires_at", out var expiresAtProperty) ||
            !expiresAtProperty.TryGetInt64(out var expiresAtUnix)) {
            throw new FormatException("OpenAI Realtime client-secret response did not contain expires_at.");
        }

        var model = fallbackModel;
        if (root.TryGetProperty("session", out var session) &&
            session.ValueKind == System.Text.Json.JsonValueKind.Object &&
            session.TryGetProperty("model", out var modelProperty) &&
            modelProperty.ValueKind == System.Text.Json.JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(modelProperty.GetString())) {
            model = modelProperty.GetString()!;
        }

        return new OpenAIRealtimeClientSecret(
            value,
            DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix),
            model);
    }

    private async Task<RealtimeAuthorization> ResolveAuthorizationAsync(CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) {
            return new RealtimeAuthorization(_options.ApiKey!, null, null);
        }

        var bundle = await _authManager.TryGetValidBundleAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.AccessToken)) {
            throw new InvalidOperationException(
                "ChatGPT OAuth is required for Realtime when no OpenAI API key is configured. Sign in with ChatGPT first.");
        }
        return new RealtimeAuthorization(bundle.AccessToken, bundle.AccountId, bundle);
    }

    private async Task<ClientSecretResponse> SendClientSecretRequestAsync(
        string payload,
        RealtimeAuthorization authorization,
        CancellationToken cancellationToken) {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.ClientSecretUrl) {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization.Token);
        if (!string.IsNullOrWhiteSpace(authorization.AccountId)) {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-ID", authorization.AccountId);
        }
        if (!string.IsNullOrWhiteSpace(_chatGptOptions.UserAgent)) {
            request.Headers.TryAddWithoutValidation("User-Agent", _chatGptOptions.UserAgent);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return new ClientSecretResponse(response.IsSuccessStatusCode, response.StatusCode, responseText);
    }

    private Uri BuildWebSocketUri(string model) {
        var separator = _options.WebSocketUrl.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
        return new Uri(_options.WebSocketUrl + separator + "model=" + Uri.EscapeDataString(model), UriKind.Absolute);
    }

    private static string ReadRequiredString(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != System.Text.Json.JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString())) {
            throw new FormatException($"OpenAI Realtime client-secret response did not contain {propertyName}.");
        }
        return property.GetString()!;
    }

    private static async Task<string> ReadContentAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return await content.ReadAsStringAsync().ConfigureAwait(false);
#else
        return await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#endif
    }

    private void ThrowIfDisposed() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(OpenAIRealtimeClient));
        }
    }

    private sealed class RealtimeAuthorization {
        internal RealtimeAuthorization(string token, string? accountId, AuthBundle? bundle) {
            Token = token;
            AccountId = accountId;
            Bundle = bundle;
        }

        internal string Token { get; }
        internal string? AccountId { get; }
        internal AuthBundle? Bundle { get; }
    }

    private sealed class ClientSecretResponse {
        internal ClientSecretResponse(bool isSuccessStatusCode, HttpStatusCode statusCode, string content) {
            IsSuccessStatusCode = isSuccessStatusCode;
            StatusCode = statusCode;
            Content = content;
        }

        internal bool IsSuccessStatusCode { get; }
        internal HttpStatusCode StatusCode { get; }
        internal string Content { get; }
    }
}
