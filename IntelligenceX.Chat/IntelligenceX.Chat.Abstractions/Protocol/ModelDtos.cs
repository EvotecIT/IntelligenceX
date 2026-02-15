namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Response message for <see cref="ListModelsRequest"/>.
/// </summary>
public sealed record ModelListMessage : ChatServiceMessage {
    /// <summary>
    /// Models returned by the provider.
    /// </summary>
    public ModelInfoDto[] Models { get; init; } = System.Array.Empty<ModelInfoDto>();

    /// <summary>
    /// Optional pagination cursor.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// Model info DTO suitable for UI display.
/// </summary>
public sealed record ModelInfoDto {
    /// <summary>
    /// Provider-specific stable identifier for the model entry.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Model name used in chat requests.
    /// </summary>
    public required string Model { get; init; }
    /// <summary>
    /// Optional display name suitable for UI.
    /// </summary>
    public string? DisplayName { get; init; }
    /// <summary>
    /// Optional description suitable for UI.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Indicates whether the provider considers this the default model.
    /// </summary>
    public bool? IsDefault { get; init; }
    /// <summary>
    /// Default reasoning effort identifier when the provider exposes it.
    /// </summary>
    public string? DefaultReasoningEffort { get; init; }
    /// <summary>
    /// Supported reasoning-effort options for this model when known.
    /// </summary>
    public ReasoningEffortOptionDto[] SupportedReasoningEfforts { get; init; } = System.Array.Empty<ReasoningEffortOptionDto>();
}

/// <summary>
/// Reasoning effort option for a model.
/// </summary>
public sealed record ReasoningEffortOptionDto {
    /// <summary>
    /// Reasoning effort identifier (provider-specific).
    /// </summary>
    public required string ReasoningEffort { get; init; }
    /// <summary>
    /// Optional description suitable for UI.
    /// </summary>
    public string? Description { get; init; }
}
