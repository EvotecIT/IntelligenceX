namespace IntelligenceX.Ledger.Contracts;

/// <summary>One bounded assignment page. Continue with NextOrder; immutable request order survives holds and removal from the active set.</summary>
public sealed record CoordinationWorkPage(CoordinationWork[] Items, long? NextOrder);

/// <summary>One bounded result page, including held/submitted entries without hiding later results.</summary>
public sealed record CoordinationResultPage(CoordinationResultOutbox[] Items, long? NextOrder);

/// <summary>One bounded retirement page. Items and Holds together contain at most sixteen entries.
/// NextOrder advances over both collections so held jobs do not hide later cleanup.</summary>
public sealed record CoordinationRetirementPage(CoordinationRetirement[] Items, long? NextOrder) {
    /// <summary>Retained jobs without temporary-worker ownership, exposing only their request identity and hold reason.</summary>
    public CoordinationRetirementHold[] Holds { get; init; } = [];
}

/// <summary>A retirement discovery hold without native binding or cleanup action details.</summary>
public sealed record CoordinationRetirementHold(string RequestId, string Reason);
