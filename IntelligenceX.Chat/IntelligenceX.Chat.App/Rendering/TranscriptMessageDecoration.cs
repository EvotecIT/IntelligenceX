using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Optional per-message transcript decorations (live/provisional + timeline details).
/// </summary>
internal sealed class TranscriptMessageDecoration {
    public bool IsProvisional { get; init; }
    public IReadOnlyList<string> Timeline { get; init; } = Array.Empty<string>();
}
