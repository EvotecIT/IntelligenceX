using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>
/// One session in a tree of work: who started it, how it is doing and what it holds. Id is the provider's session or
/// thread id when known. StartedBy is the parent's id, StartedVia the evidence that links them: codex-parent-thread,
/// claude-parent-session, desktop-side-session or ix-ask. SubtreeVerdict is the most urgent verdict of this node and
/// everything under it, so a branch with one stuck session reads as stuck. Suggestion is take-over when the node is stuck,
/// usage-limited or archived with work still attached, archive when an accepted root's finished descendant can go, and
/// null otherwise.
/// </summary>
public sealed record SessionTreeNode {
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Machine { get; init; }
    public int Depth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StartedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StartedVia { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
    public string Origin { get; init; } = "unknown";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Verdict { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SubtreeVerdict { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? CreatedAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? LastActivityAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Archived { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool UsageLimited { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Repo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Branch { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? PullRequests { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? Leases { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TakenOverBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TookOverFrom { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Suggestion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SuggestionReason { get; init; }
}

/// <summary>
/// The tree a session belongs to, from its root down, depth first. State is found, not-found or ambiguous (Matches lists
/// the candidates). Archive lists the root's finished descendants that cleanup would archive once the root's outcome is
/// accepted; it is a plan only. Truncated is set when more than the node limit exist.
/// </summary>
public sealed record SessionTree {
    public required string State { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Root { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Query { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SubtreeVerdict { get; init; }
    public SessionTreeNode[] Nodes { get; init; } = [];
    public int Total { get; init; }
    public bool Truncated { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionCleanupEntry[]? Archive { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionCleanupEntry[]? ArchiveHeld { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionMatch[]? Matches { get; init; }
}

/// <summary>One step in a tree's history. Kind is started, asked, ask-&lt;stage&gt;, lease, lease-ended, took-over,
/// verdict, archived, branch or pull-request. AtUtc is null when the Ledger knows the step happened but not when.</summary>
public sealed record SessionTrailEntry(DateTimeOffset? AtUtc, string Kind, string Session,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null);

/// <summary>A root's history across its sessions, asks, leases, branches, pull requests and results, oldest first.
/// Verdict changes come from the Ledger's 30-day transition history.</summary>
public sealed record SessionTrail {
    public required string State { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Root { get; init; }
    public SessionTrailEntry[] Entries { get; init; } = [];
    public bool Truncated { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionMatch[]? Matches { get; init; }
}

/// <summary>
/// A live session (By) takes over a stuck, usage-limited, archived or long-idle one (From): its active reservations move
/// to By, and both sides record the takeover. Reason is a short note kept with the record.
/// </summary>
public sealed record SessionTakeoverCommand(string From, string By, string? Reason = null);

/// <summary>
/// The takeover's result and what By needs to continue: From's task, checkout, pull requests, children still running and
/// open asks. State is taken-over, already-taken-over, taken-over-by-other (By names the live session that already
/// continues it), take-over-successor (that session stopped too; take over By instead), not-eligible (EligibleAtUtc says when it will be), not-found, ambiguous, same-session or by-not-live.
/// </summary>
public sealed record SessionTakeoverReply(bool Ok, string State) {
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? From { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? By { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Trigger { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? AtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? EligibleAtUtc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Lease[]? Leases { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TaskScope? Task { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Verdict { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Repo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Branch { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Worktree { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Commit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? PullRequests { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? Children { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? OpenAsks { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionMatch[]? Matches { get; init; }
}

/// <summary>A parent-child link the reporter didn't see natively, such as an ask whose helper session was bound to
/// a request. Provider and session ids are the providers' own.</summary>
public sealed record SessionLink(string ParentProvider, string ParentSessionId, string ChildProvider, string ChildSessionId,
    string Via, DateTimeOffset AtUtc, string? RequestId = null);

/// <summary>One ask in a tree's trail: who asked, which session helped, and when each stage happened.</summary>
public sealed record SessionAskStep(string RequestId, string RequesterProvider, string RequesterSessionId,
    string? HelperProvider, string? HelperSessionId, string State, DateTimeOffset RequestedAtUtc,
    Dictionary<string, DateTimeOffset> Stages, string? ResultRef = null);
