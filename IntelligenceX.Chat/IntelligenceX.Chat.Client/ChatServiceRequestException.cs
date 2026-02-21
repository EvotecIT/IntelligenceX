using System;

namespace IntelligenceX.Chat.Client;

/// <summary>
/// Exception raised when the chat service returns an explicit error response frame.
/// </summary>
public sealed class ChatServiceRequestException : InvalidOperationException {
    /// <summary>
    /// Initializes a new exception for a service error response.
    /// </summary>
    /// <param name="message">Service-provided error message.</param>
    /// <param name="code">Optional structured service error code.</param>
    public ChatServiceRequestException(string message, string? code = null) : base(message) {
        Code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
    }

    /// <summary>
    /// Gets the service error code when provided.
    /// </summary>
    public string? Code { get; }
}
