namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Identifies the provider runtime that is active inside the chat service.
/// </summary>
public sealed record SessionRuntimeIdentityDto {
    /// <summary>
    /// Optional persisted service profile that supplied the runtime settings.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Canonical provider transport token such as native or compatible-http.
    /// </summary>
    public required string Transport { get; init; }

    /// <summary>
    /// Effective service model, when the runtime exposes one.
    /// </summary>
    public string? Model { get; init; }
}
