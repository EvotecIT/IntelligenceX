using System;

namespace IntelligenceX.Authentication.GitHub;

/// <summary>A pending GitHub sign-in. Display only the user code and verification URL to the user.</summary>
public sealed class GitHubDeviceAuthorization {
    internal GitHubDeviceAuthorization(string deviceCode, string userCode, Uri verificationUri, int intervalSeconds, DateTimeOffset expiresAt) {
        DeviceCode = deviceCode; UserCode = userCode; VerificationUri = verificationUri;
        IntervalSeconds = intervalSeconds; ExpiresAt = expiresAt;
    }
    internal string DeviceCode { get; }
    internal int IntervalSeconds { get; }
    /// <summary>The short code the user enters on GitHub.</summary>
    public string UserCode { get; }
    /// <summary>The GitHub page where the user grants access.</summary>
    public Uri VerificationUri { get; }
    /// <summary>When this pending authorization expires.</summary>
    public DateTimeOffset ExpiresAt { get; }
}
