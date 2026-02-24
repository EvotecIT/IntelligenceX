using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Rendering;

internal enum AssistantBubbleChannelKind {
    Final = 0,
    DraftThinking = 1,
    ToolActivity = 2
}

/// <summary>
/// Optional per-message transcript decorations (live/provisional + timeline details).
/// </summary>
internal sealed class TranscriptMessageDecoration {
    public bool IsProvisional { get; init; }
    public AssistantBubbleChannelKind Channel { get; init; } = AssistantBubbleChannelKind.Final;
    public IReadOnlyList<string> Timeline { get; init; } = Array.Empty<string>();
}
