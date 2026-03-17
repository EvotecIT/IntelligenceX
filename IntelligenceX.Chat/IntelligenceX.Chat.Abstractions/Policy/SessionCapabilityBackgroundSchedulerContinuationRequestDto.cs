using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured request hint for continuing dependency-blocked background scheduler work.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerContinuationRequestDto {
    /// <summary>
    /// Chat service request kind that can advance the blocked continuation.
    /// </summary>
    public string RequestKind { get; init; } = string.Empty;

    /// <summary>
    /// Stable purpose label describing why the request is suggested.
    /// </summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>
    /// Required request-field names the client still needs before the request can be sent.
    /// </summary>
    public string[] RequiredArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Blocked tool/runtime input arguments this request is expected to satisfy.
    /// </summary>
    public string[] SatisfiesInputArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Suggested request argument values the client can prefill deterministically.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto[] SuggestedArguments { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto>();
}
