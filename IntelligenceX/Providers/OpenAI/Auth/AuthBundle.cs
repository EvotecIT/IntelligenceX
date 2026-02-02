using System;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Represents an authentication bundle for OpenAI providers.
/// </summary>
public sealed class AuthBundle {
    /// <summary>
    /// Initializes a new authentication bundle.
    /// </summary>
    /// <param name="provider">Provider identifier.</param>
    /// <param name="accessToken">Access token.</param>
    /// <param name="refreshToken">Refresh token.</param>
    /// <param name="expiresAt">Expiration timestamp.</param>
    public AuthBundle(string provider, string accessToken, string refreshToken, DateTimeOffset? expiresAt) {
        Provider = provider;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Gets the provider identifier.
    /// </summary>
    public string Provider { get; }
    /// <summary>
    /// Gets or sets the access token.
    /// </summary>
    public string AccessToken { get; set; }
    /// <summary>
    /// Gets or sets the refresh token.
    /// </summary>
    public string RefreshToken { get; set; }
    /// <summary>
    /// Gets or sets the expiration time.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>
    /// Gets or sets the token type.
    /// </summary>
    public string? TokenType { get; set; }
    /// <summary>
    /// Gets or sets the scopes string.
    /// </summary>
    public string? Scope { get; set; }
    /// <summary>
    /// Gets or sets the account id when known.
    /// </summary>
    public string? AccountId { get; set; }
    /// <summary>
    /// Gets or sets the ID token when present.
    /// </summary>
    public string? IdToken { get; set; }

    /// <summary>
    /// Returns whether the bundle is expired at the given instant.
    /// </summary>
    /// <param name="now">Optional clock override.</param>
    public bool IsExpired(DateTimeOffset? now = null) {
        if (!ExpiresAt.HasValue) {
            return false;
        }
        var instant = now ?? DateTimeOffset.UtcNow;
        return instant >= ExpiresAt.Value;
    }
}
