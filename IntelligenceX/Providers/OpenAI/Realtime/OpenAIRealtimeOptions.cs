using System;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// Configures OpenAI Realtime client-secret minting and WebSocket connections.
/// </summary>
public sealed class OpenAIRealtimeOptions {
    /// <summary>
    /// Endpoint used to mint short-lived Realtime client credentials.
    /// </summary>
    public string ClientSecretUrl { get; set; } = "https://api.openai.com/v1/realtime/client_secrets";

    /// <summary>
    /// WebSocket endpoint used for Realtime sessions.
    /// </summary>
    public string WebSocketUrl { get; set; } = "wss://api.openai.com/v1/realtime";

    /// <summary>
    /// Optional OpenAI API key. When omitted, the client uses the configured ChatGPT OAuth bundle.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Maximum accepted Realtime event size in bytes.
    /// </summary>
    public int MaxEventBytes { get; set; } = 4 * 1024 * 1024;

    internal void Validate() {
        if (!Uri.TryCreate(ClientSecretUrl, UriKind.Absolute, out var clientSecretUri) ||
            !string.Equals(clientSecretUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("ClientSecretUrl must be an absolute HTTPS URL.", nameof(ClientSecretUrl));
        }

        if (!Uri.TryCreate(WebSocketUrl, UriKind.Absolute, out var webSocketUri) ||
            !string.Equals(webSocketUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("WebSocketUrl must be an absolute WSS URL.", nameof(WebSocketUrl));
        }

        if (MaxEventBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxEventBytes), "MaxEventBytes must be positive.");
        }

        ApiKey = NormalizeOptional(ApiKey);
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
