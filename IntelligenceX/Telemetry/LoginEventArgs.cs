using System;

namespace IntelligenceX.Telemetry;

/// <summary>
/// Event arguments raised during authentication flows.
/// </summary>
public sealed class LoginEventArgs : EventArgs {
    /// <summary>
    /// Initializes a new login event args instance.
    /// </summary>
    /// <param name="loginType">Login type identifier.</param>
    /// <param name="loginId">Optional login identifier.</param>
    /// <param name="authUrl">Optional authorization URL.</param>
    public LoginEventArgs(string loginType, string? loginId = null, string? authUrl = null) {
        LoginType = loginType;
        LoginId = loginId;
        AuthUrl = authUrl;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the login type identifier.
    /// </summary>
    public string LoginType { get; }
    /// <summary>
    /// Gets the login identifier when available.
    /// </summary>
    public string? LoginId { get; }
    /// <summary>
    /// Gets the authorization URL when available.
    /// </summary>
    public string? AuthUrl { get; }
    /// <summary>
    /// Gets the timestamp in UTC when the event was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}
