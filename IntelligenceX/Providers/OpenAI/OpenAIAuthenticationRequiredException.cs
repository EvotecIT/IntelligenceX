using System;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Exception thrown when an operation requires an authenticated ChatGPT session.
/// </summary>
public sealed class OpenAIAuthenticationRequiredException : InvalidOperationException {
    /// <summary>
    /// Default error message used when authentication is missing.
    /// </summary>
    public const string DefaultMessage = "Not logged in. Run ChatGPT login first.";

    /// <summary>
    /// Initializes a new instance with the default message.
    /// </summary>
    public OpenAIAuthenticationRequiredException() : base(DefaultMessage) { }

    /// <summary>
    /// Initializes a new instance with a custom message.
    /// </summary>
    public OpenAIAuthenticationRequiredException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance with a custom message and inner exception.
    /// </summary>
    public OpenAIAuthenticationRequiredException(string message, Exception innerException) : base(message, innerException) { }
}

