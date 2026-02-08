using System;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Exception thrown when a user explicitly cancels an interactive login flow.
/// </summary>
public sealed class OpenAIUserCanceledLoginException : OperationCanceledException {
    /// <summary>
    /// Default error message used when the user cancels login.
    /// </summary>
    public const string DefaultMessage = "Login canceled by user.";

    /// <summary>
    /// Initializes a new instance with the default message.
    /// </summary>
    public OpenAIUserCanceledLoginException() : base(DefaultMessage) { }

    /// <summary>
    /// Initializes a new instance with a custom message.
    /// </summary>
    public OpenAIUserCanceledLoginException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance with a custom message and inner exception.
    /// </summary>
    public OpenAIUserCanceledLoginException(string message, Exception innerException) : base(message, innerException) { }
}

