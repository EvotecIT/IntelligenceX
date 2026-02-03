using System;
using IntelligenceX.Copilot.Direct;

namespace IntelligenceX.Copilot;

/// <summary>
/// Options for the high-level Copilot chat client that can select between CLI and direct transports.
/// </summary>
public sealed class CopilotChatClientOptions {
    /// <summary>
    /// Transport selection (CLI or direct HTTP).
    /// </summary>
    public CopilotTransportKind Transport { get; set; } = CopilotTransportKind.Cli;
    /// <summary>
    /// Options for the Copilot CLI transport.
    /// </summary>
    public CopilotClientOptions Cli { get; } = new();
    /// <summary>
    /// Options for the direct HTTP transport.
    /// </summary>
    /// <remarks>
    /// Direct transport is experimental and may change or be removed.
    /// </remarks>
    public CopilotDirectOptions Direct { get; } = new();
    /// <summary>
    /// Default model to use when none is provided to <c>ChatAsync</c>.
    /// </summary>
    public string? DefaultModel { get; set; }
    /// <summary>
    /// Timeout to apply to CLI <c>SendAndWaitAsync</c> calls.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Validates configuration values.
    /// </summary>
    public void Validate() {
        if (Timeout < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout cannot be negative.");
        }
        if (Transport == CopilotTransportKind.Direct) {
            Direct.Validate();
        }
    }
}
