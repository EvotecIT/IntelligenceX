using System;
using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewHistorySnapshot {
    public string CurrentHeadSha { get; init; } = string.Empty;
    public IReadOnlyList<ReviewHistoryRound> Rounds { get; init; } = Array.Empty<ReviewHistoryRound>();
    public IReadOnlyList<ReviewHistoryFinding> OpenFindings { get; init; } = Array.Empty<ReviewHistoryFinding>();
    public IReadOnlyList<ReviewHistoryFinding> ResolvedSinceLastRound { get; init; } = Array.Empty<ReviewHistoryFinding>();
    public IReadOnlyList<ReviewHistoryExternalSummary> ExternalSummaries { get; init; } =
        Array.Empty<ReviewHistoryExternalSummary>();
    public ReviewHistoryThreadSnapshot? ThreadSnapshot { get; init; }
    public bool HasContent => Rounds.Count > 0 || ExternalSummaries.Count > 0 || ThreadSnapshot is not null;
}

internal sealed class ReviewHistoryRound {
    public int Sequence { get; init; }
    public string Source { get; init; } = "intelligencex";
    public long? SummaryCommentId { get; init; }
    public string ReviewedSha { get; init; } = string.Empty;
    public bool SameHeadAsCurrent { get; init; }
    public bool HasMergeBlockers { get; init; }
    public string MergeBlockerStatus { get; init; } = string.Empty;
    public bool FindingsHitLimit { get; init; }
    public bool FindingsParseIncomplete { get; init; }
    public IReadOnlyList<ReviewHistoryFinding> Findings { get; init; } = Array.Empty<ReviewHistoryFinding>();
}

internal sealed class ReviewHistoryFinding {
    public string Fingerprint { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Status { get; init; } = "open";
}

internal sealed class ReviewHistoryExternalSummary {
    public long? CommentId { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Source { get; init; } = "external";
    public string Excerpt { get; init; } = string.Empty;
}

internal sealed class ReviewHistoryThreadSnapshot {
    public int ActiveCount { get; init; }
    public int ResolvedCount { get; init; }
    public int StaleCount { get; init; }
    public IReadOnlyList<ReviewHistoryThreadExcerpt> Excerpts { get; init; } = Array.Empty<ReviewHistoryThreadExcerpt>();
}

internal sealed class ReviewHistoryThreadExcerpt {
    public string ThreadId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? Path { get; init; }
    public int? Line { get; init; }
}
