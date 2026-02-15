namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Response message for <see cref="ListProfilesRequest"/>.
/// </summary>
public sealed record ProfileListMessage : ChatServiceMessage {
    /// <summary>
    /// Available profile names.
    /// </summary>
    public string[] Profiles { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Active profile name for this connection/session when known.
    /// </summary>
    public string? ActiveProfile { get; init; }
}

