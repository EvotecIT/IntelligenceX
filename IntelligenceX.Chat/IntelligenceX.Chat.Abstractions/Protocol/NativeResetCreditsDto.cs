using System;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>Read-only provider reset evidence, distinct from local manual records.</summary>
public sealed record NativeResetCreditsDto {
    /// <summary>Available grant count, if reported.</summary>
    public long? AvailableCount { get; init; }
    /// <summary>Grants applicable now, if reported.</summary>
    public long? ApplicableAvailableCount { get; init; }
    /// <summary>Whether individual grants were returned.</summary>
    public bool DetailsAvailable { get; init; }
    /// <summary>Provider grants and exact lifecycle timestamps.</summary>
    public NativeResetCreditDto[] Credits { get; init; } = Array.Empty<NativeResetCreditDto>();
    /// <summary>Whether a history page was returned.</summary>
    public bool HistoryAvailable { get; init; }
    /// <summary>Events in the retained page, not lifetime history.</summary>
    public NativeResetCreditEventDto[] History { get; init; } = Array.Empty<NativeResetCreditEventDto>();
    /// <summary>Start of the provider retention window.</summary>
    public DateTimeOffset? HistoryWindowStart { get; init; }
    /// <summary>End of the provider reading.</summary>
    public DateTimeOffset? HistoryAsOf { get; init; }
    /// <summary>Cursor when additional history remains.</summary>
    public string? HistoryNextCursor { get; init; }
}

/// <summary>A grant with provider state and exact times; this conveys no authority to redeem it.</summary>
public sealed record NativeResetCreditDto {
    /// <summary>Provider grant identity.</summary>
    public string? Id { get; init; }
    /// <summary>Provider reset type.</summary>
    public string? ResetType { get; init; }
    /// <summary>Display title.</summary>
    public string? Title { get; init; }
    /// <summary>Original provider state.</summary>
    public string? Status { get; init; }
    /// <summary>Whether the plan supports the grant, if reported.</summary>
    public bool? IsSupportedByPlan { get; init; }
    /// <summary>Exact grant time.</summary>
    public DateTimeOffset? GrantedAt { get; init; }
    /// <summary>Exact expiry time.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Time redemption began, if reported.</summary>
    public DateTimeOffset? RedeemStartedAt { get; init; }
    /// <summary>Provider-confirmed use time.</summary>
    public DateTimeOffset? RedeemedAt { get; init; }
}

/// <summary>An event in the provider's retained history page.</summary>
public sealed record NativeResetCreditEventDto {
    /// <summary>Event identity.</summary>
    public string? Id { get; init; }
    /// <summary>Original event kind.</summary>
    public string? Kind { get; init; }
    /// <summary>Exact occurrence time.</summary>
    public DateTimeOffset? OccurredAt { get; init; }
}
