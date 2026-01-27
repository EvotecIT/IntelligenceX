using System;

namespace IntelligenceX.Telemetry;

public sealed class LoginEventArgs : EventArgs {
    public LoginEventArgs(string loginType, string? loginId = null, string? authUrl = null) {
        LoginType = loginType;
        LoginId = loginId;
        AuthUrl = authUrl;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public string LoginType { get; }
    public string? LoginId { get; }
    public string? AuthUrl { get; }
    public DateTimeOffset Timestamp { get; }
}
