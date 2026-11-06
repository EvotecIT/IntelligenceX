namespace IntelligenceX.Ledger.Contracts;

/// <summary>Directory observation and coverage remain visible even when no project can be returned.</summary>
public sealed record ProjectDirectoryView(DateTimeOffset AsOfUtc, ProjectContextView[] Projects, string[] Coverage);

/// <summary>A source-backed project directory entry returned to read-only clients.</summary>
public sealed record ProjectContextView(string Id, string Name, string Repository, DateTimeOffset AsOfUtc,
    string Revision, ResponsibilityView[] Responsibilities, DependencyView[] Dependencies,
    KnowledgeHit[] Knowledge, string[] Coverage);

/// <summary>Responsibility evidence distinguishes a stable role from its current execution owner.</summary>
public sealed record ResponsibilityView(string Role, string? WorkerId, string? MachineId, string State,
    string SourceRef, DateTimeOffset ObservedAtUtc);

/// <summary>A dependency describes the precise fact required by a consumer and its observation.</summary>
public sealed record DependencyView(string ProjectId, string Kind, string RequiredRef, string State,
    string SourceRef, DateTimeOffset ObservedAtUtc);

/// <summary>A bounded excerpt from an explicitly enrolled, maintained source.</summary>
public sealed record KnowledgeHit(string DocumentId, string ProjectId, string Title, string Section,
    string Excerpt, string SourceRef, string Revision, string Outcome, DateTimeOffset? VerifiedAtUtc,
    string? SupersededBy, int Line);

/// <summary>Knowledge retrieval always exposes its source coverage and evaluation time.</summary>
public sealed record KnowledgeSearchView(DateTimeOffset AsOfUtc, string ProjectId, string Query,
    KnowledgeHit[] Hits, string[] Coverage, bool Truncated);

/// <summary>A current-work row adds local display metadata without changing the underlying verdict.</summary>
public sealed record LedgerWorkRow(string Label, string? ProjectId, LedgerItemView Item);

/// <summary>A bounded current-work projection, separate from the all-history status counts.</summary>
public sealed record LedgerWorkView(DateTimeOffset AsOfUtc, int RulesVersion, MachineRow[] Machines,
    Dictionary<string, int> Counts, LedgerWorkRow[] Items, string[] Coverage, bool Truncated);

/// <summary>Qualified session availability is separate from unobserved application responsiveness.</summary>
public sealed record WorkerAvailabilityView(string State, bool? Archived, DateTimeOffset? ArchiveObservedAtUtc,
    bool UsageLimited, string ApplicationState, string[] Coverage);
