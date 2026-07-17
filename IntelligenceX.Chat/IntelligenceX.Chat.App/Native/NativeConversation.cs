using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Conversation;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native presentation model over one persisted chat conversation.
/// </summary>
internal sealed class NativeConversation {
    public NativeConversation(
        string id,
        string title,
        string? threadId = null,
        DateTime updatedUtc = default,
        IEnumerable<NativeChatTranscriptItem>? messages = null) {
        Id = string.IsNullOrWhiteSpace(id) ? CreateId() : id.Trim();
        Title = NormalizeTitle(title);
        ThreadId = string.IsNullOrWhiteSpace(threadId) ? null : threadId.Trim();
        UpdatedUtc = updatedUtc == default ? DateTime.UtcNow : EnsureUtc(updatedUtc);
        Messages = messages?.ToList() ?? new List<NativeChatTranscriptItem>();
    }

    public string Id { get; }

    public string Title { get; set; }

    public string? ThreadId { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public List<NativeChatTranscriptItem> Messages { get; }

    public bool IsEmptyDraft => Messages.Count == 0
                                && string.IsNullOrWhiteSpace(ThreadId)
                                && string.Equals(Title, ChatConversationIdentity.DefaultTitle, StringComparison.OrdinalIgnoreCase);

    public string Subtitle => Messages.Count == 0
        ? "No messages yet"
        : UpdatedUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);

    public string Badge => Messages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool Matches(string? query) {
        var value = (query ?? string.Empty).Trim();
        return value.Length == 0
               || Title.Contains(value, StringComparison.OrdinalIgnoreCase)
               || Messages.Any(message => message.Text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    public void UpdateTitleFromFirstUserMessage() {
        if (!string.Equals(Title, ChatConversationIdentity.DefaultTitle, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        var message = Messages.FirstOrDefault(static item => item.IsUser && !string.IsNullOrWhiteSpace(item.Text));
        if (message is null) {
            return;
        }

        Title = ChatConversationIdentity.BuildTitleFromText(message.Text);
    }

    public static NativeConversation CreateNew() => new(CreateId(), ChatConversationIdentity.DefaultTitle);

    private static string CreateId() => ChatConversationIdentity.CreateId();

    private static string NormalizeTitle(string? title) =>
        ChatConversationIdentity.NormalizeTitle(title);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

internal sealed class NativeConversationWorkspace {
    public NativeConversationWorkspace(
        IReadOnlyList<NativeConversation> conversations,
        string activeConversationId) {
        Conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        ActiveConversationId = activeConversationId ?? string.Empty;
    }

    public IReadOnlyList<NativeConversation> Conversations { get; }

    public string ActiveConversationId { get; set; }
}
