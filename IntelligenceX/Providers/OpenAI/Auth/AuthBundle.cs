using System;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Stores access and refresh tokens for an authenticated account.
/// </summary>
public sealed class AuthBundle {
    /// <summary>
    /// Creates an auth bundle with token values and optional expiry.
    /// </summary>
    public AuthBundle(string provider, string accessToken, string refreshToken, DateTimeOffset? expiresAt) {
        Provider = provider;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Provider identifier (e.g., "openai").
    /// </summary>
    public string Provider { get; }
    /// <summary>
    /// Access token value.
    /// </summary>
    public string AccessToken { get; set; }
    /// <summary>
    /// Refresh token value.
    /// </summary>
    public string RefreshToken { get; set; }
    /// <summary>
    /// Expiration time for the access token.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>
    /// Token type (e.g., "bearer") if provided.
    /// </summary>
    public string? TokenType { get; set; }
    /// <summary>
    /// Scope string if provided by the auth server.
    /// </summary>
    public string? Scope { get; set; }
    /// <summary>
    /// Account identifier if available.
    /// </summary>
    public string? AccountId { get; set; }
    /// <summary>
    /// ID token if provided by the auth server.
    /// </summary>
    public string? IdToken { get; set; }

    /// <summary>
    /// Returns true if the bundle is expired at the given time.
    /// </summary>
    public bool IsExpired(DateTimeOffset? now = null) {
        if (!ExpiresAt.HasValue) {
            return false;
        }
        var instant = now ?? DateTimeOffset.UtcNow;
        return instant >= ExpiresAt.Value;
    }
}
