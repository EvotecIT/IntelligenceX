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
    /// Favorite model names for the active profile when available.
    /// </summary>
    public string[] FavoriteModels { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Recent model names for the active profile when available (most recent first).
    /// </summary>
    public string[] RecentModels { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// When true, the service is returning a cached response because live model discovery failed.
    /// </summary>
    public bool IsStale { get; init; }

    /// <summary>
    /// Optional warning text when results are partial or stale.
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// Optional pagination cursor.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// Response message for <see cref="ListModelFavoritesRequest"/>.
/// </summary>
public sealed record ModelFavoritesMessage : ChatServiceMessage {
    /// <summary>
    /// Favorite model names for the active profile.
    /// </summary>
    public string[] Models { get; init; } = System.Array.Empty<string>();
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
    /// Provider owner identifier (for example <c>owned_by</c>) when exposed.
    /// </summary>
    public string? OwnedBy { get; init; }
    /// <summary>
    /// Publisher identifier when exposed by the runtime.
    /// </summary>
    public string? Publisher { get; init; }
    /// <summary>
    /// Architecture identifier when known.
    /// </summary>
    public string? Architecture { get; init; }
    /// <summary>
    /// Quantization identifier when known.
    /// </summary>
    public string? Quantization { get; init; }
    /// <summary>
    /// Compatibility format (for example gguf) when known.
    /// </summary>
    public string? CompatibilityType { get; init; }
    /// <summary>
    /// Runtime load state (for example loaded/not-loaded) when known.
    /// </summary>
    public string? RuntimeState { get; init; }
    /// <summary>
    /// Model category/type when known.
    /// </summary>
    public string? ModelType { get; init; }
    /// <summary>
    /// Maximum context length when exposed by the runtime.
    /// </summary>
    public long? MaxContextLength { get; init; }
    /// <summary>
    /// Active loaded context length when exposed by the runtime.
    /// </summary>
    public long? LoadedContextLength { get; init; }
    /// <summary>
    /// Capability tags advertised by the runtime.
    /// </summary>
    public string[] Capabilities { get; init; } = System.Array.Empty<string>();
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
