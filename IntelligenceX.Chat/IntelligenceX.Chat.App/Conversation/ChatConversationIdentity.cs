using System;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Owns conversation identity and default-title policy shared by every desktop shell.
/// </summary>
internal static class ChatConversationIdentity {
    /// <summary>
    /// Default title used until the first user message supplies a useful title.
    /// </summary>
    public const string DefaultTitle = "New Chat";

    public const string SystemConversationId = "chat-system";
    private const int MaximumTitleTextLength = 56;

    /// <summary>
    /// Creates a non-reserved conversation identifier.
    /// </summary>
    public static string CreateId() => "chat-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Returns whether an identifier is reserved for host-owned or legacy state.
    /// </summary>
    public static bool IsReservedCreationId(string? conversationId) {
        var normalized = (conversationId ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return true;
        }

        return string.Equals(normalized, SystemConversationId, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "system", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "chat-default", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Preserves valid client identifiers and replaces reserved values with a new identifier.
    /// </summary>
    public static string ResolveCreationId(string? conversationId) {
        var normalized = (conversationId ?? string.Empty).Trim();
        return IsReservedCreationId(normalized) ? CreateId() : normalized;
    }

    /// <summary>
    /// Normalizes an existing title without inventing shell-specific defaults.
    /// </summary>
    public static string NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();

    /// <summary>
    /// Builds the deterministic sidebar title used by every desktop shell.
    /// </summary>
    public static string BuildTitleFromText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return DefaultTitle;
        }

        normalized = normalized.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length > MaximumTitleTextLength) {
            normalized = normalized[..MaximumTitleTextLength].TrimEnd() + "...";
        }

        return normalized;
    }
}
