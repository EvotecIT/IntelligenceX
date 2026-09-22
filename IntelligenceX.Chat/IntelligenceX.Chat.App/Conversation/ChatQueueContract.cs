namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Defines the shared live and persisted turn-queue capacity.
/// </summary>
internal static class ChatQueueContract {
    /// <summary>
    /// Maximum number of turns retained in either operator queue.
    /// </summary>
    public const int MaxTurns = 8;
}
