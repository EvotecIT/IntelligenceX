using System;
using System.Collections.Generic;

namespace IntelligenceX.Copilot.Direct;

/// <summary>
/// Options for the experimental Copilot direct HTTP client.
/// </summary>
/// <remarks>
/// This transport is unsupported and intended for controlled environments.
/// </remarks>
public sealed class CopilotDirectOptions {
    /// <summary>
    /// Absolute URL for the Copilot HTTP endpoint.
    /// </summary>
    public string? Url { get; set; }
    /// <summary>
    /// Optional bearer token to send with requests.
    /// </summary>
    public string? Token { get; set; }
    /// <summary>
    /// Optional additional headers to attach to requests.
    /// </summary>
    /// <remarks>
    /// Use this to pass custom authorization headers when the endpoint does not accept bearer tokens.
    /// </remarks>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Request timeout.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Validates configuration values.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(Url)) {
            throw new ArgumentException("Copilot direct Url is required.", nameof(Url));
        }
        if (!Uri.TryCreate(Url, UriKind.Absolute, out _)) {
            throw new ArgumentException("Copilot direct Url must be absolute.", nameof(Url));
        }
        var hasAuthorizationHeader = false;
        string? authHeader = null;
        foreach (var entry in Headers) {
            if (string.Equals(entry.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) {
                hasAuthorizationHeader = true;
                authHeader = entry.Value;
                break;
            }
        }
        if (!string.IsNullOrWhiteSpace(Token) &&
            hasAuthorizationHeader &&
            !string.IsNullOrWhiteSpace(authHeader)) {
            throw new ArgumentException("Copilot direct options cannot specify both Token and Authorization header.", nameof(Headers));
        }
        if (Timeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be greater than zero.");
        }
    }
}
