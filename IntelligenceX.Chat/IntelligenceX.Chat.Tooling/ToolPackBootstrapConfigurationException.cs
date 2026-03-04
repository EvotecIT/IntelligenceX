using System;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Raised when runtime pack bootstrap configuration is invalid and startup should fail fast.
/// </summary>
public sealed class ToolPackBootstrapConfigurationException : InvalidOperationException {
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolPackBootstrapConfigurationException"/> class.
    /// </summary>
    /// <param name="message">Human-readable configuration failure details.</param>
    public ToolPackBootstrapConfigurationException(string message) : base(message) {
    }
}
