using System;

namespace IntelligenceX.Auth;

public sealed class AuthBundle {
    public AuthBundle(string provider, string accessToken, string refreshToken, DateTimeOffset? expiresAt) {
        Provider = provider;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
    }

    public string Provider { get; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? TokenType { get; set; }
    public string? Scope { get; set; }
    public string? AccountId { get; set; }
    public string? IdToken { get; set; }

    public bool IsExpired(DateTimeOffset? now = null) {
        if (!ExpiresAt.HasValue) {
            return false;
        }
        var instant = now ?? DateTimeOffset.UtcNow;
        return instant >= ExpiresAt.Value;
    }
}
