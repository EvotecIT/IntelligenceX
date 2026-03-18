namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Prefillable request argument hint for a blocked background scheduler continuation.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
    /// <summary>
    /// Request-field name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Best-effort string representation of the suggested value.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Stable scalar kind for the suggested value (string|boolean|number).
    /// </summary>
    public string ValueKind { get; init; } = string.Empty;
}
