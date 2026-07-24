using System;

namespace IntelligenceX.OpenAI.Realtime;

/// <summary>
/// A short-lived credential that an app can use to establish one OpenAI Realtime connection.
/// </summary>
public sealed class OpenAIRealtimeClientSecret {
    /// <summary>
    /// Initializes a short-lived credential received from a trusted backend.
    /// </summary>
    public OpenAIRealtimeClientSecret(string value, DateTimeOffset expiresAt, string model) {
        Value = NormalizeRequired(value, nameof(value));
        ExpiresAt = expiresAt;
        Model = NormalizeRequired(model, nameof(model));
    }

    /// <summary>
    /// Gets the sensitive short-lived credential value. Do not log or persist it.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the credential expiry time.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets the Realtime model associated with the session.
    /// </summary>
    public string Model { get; }

    private static string NormalizeRequired(string? value, string parameterName) {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) {
            throw new ArgumentException(parameterName + " cannot be null or whitespace.", parameterName);
        }
        return normalized!;
    }
}
