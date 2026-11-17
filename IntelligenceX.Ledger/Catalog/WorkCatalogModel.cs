namespace IntelligenceX.Ledger.Catalog;

/// <summary>Machine-local catalog configuration. Owner names and board field identities stay out of source.</summary>
public sealed record WorkCatalogSettings {
    public string[] Organizations { get; init; } = [];
    public string[] Users { get; init; } = [];
    /// <summary>Open issues carrying a label with this prefix (for example <c>to:claude</c>) are tasks.</summary>
    public string TaskLabelPrefix { get; init; } = "to:";
    /// <summary>Repositories whose addressed issues are tasks. Empty means every catalogued repository.</summary>
    public string[] TaskRepositories { get; init; } = [];
    public WorkBoardSettings? Board { get; init; }
    public int RefreshMinutes { get; init; } = 10;
    public int StaleAfterDays { get; init; } = 7;
    /// <summary>Requests that count against GitHub's limit, per hour, for every catalog read together.</summary>
    public int HourlyRequestBudget { get; init; } = 1200;
    /// <summary>Catalog reads pause while GitHub reports fewer remaining requests than this, leaving room for other tools.</summary>
    public int MinimumRemaining { get; init; } = 1500;

    internal void Validate() {
        if (Organizations.Length + Users.Length == 0 || Organizations.Concat(Users).Any(owner => !ValidName(owner)) ||
            TaskRepositories.Any(repo => repo.Split('/') is not [var o, var r] || !ValidName(o) || !ValidName(r)) ||
            string.IsNullOrWhiteSpace(TaskLabelPrefix) || RefreshMinutes is < 2 or > 1440 || StaleAfterDays is < 1 or > 365 ||
            HourlyRequestBudget is < 50 or > 4000 || MinimumRemaining is < 0 or > 4900)
            throw new InvalidDataException("Invalid work catalog settings.");
        if (Board is { } board && (!ValidName(board.Organization) || board.ProjectNumber < 1 || board.StatusFieldId < 1 ||
            board.AgentFieldId < 1 || board.MachineFieldId < 1))
            throw new InvalidDataException("Invalid work board settings.");
    }

    internal static bool ValidName(string value) => value.Length is > 0 and <= 100 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && value is not ("." or "..");
}

/// <summary>Board identity and field ids. Status and actor values are the board's own option names.</summary>
public sealed record WorkBoardSettings {
    public required string Organization { get; init; }
    public required int ProjectNumber { get; init; }
    public required long StatusFieldId { get; init; }
    public required long AgentFieldId { get; init; }
    public required long MachineFieldId { get; init; }
    public long? WorkRepoFieldId { get; init; }
    public long? BranchFieldId { get; init; }
    public long? PullRequestFieldId { get; init; }
    public long? NextActorFieldId { get; init; }
    public long? HoldReasonFieldId { get; init; }
    /// <summary>Board machine option → Ledger machine id.</summary>
    public Dictionary<string, string> MachineAliases { get; init; } = [];
    public string[] DoneStatuses { get; init; } = ["Done"];
    public string[] PlannedStatuses { get; init; } = ["Planned"];
    public string[] WaitingStatuses { get; init; } = ["Waiting for owner"];
    /// <summary>Next-actor or hold-reason values that mean the task waits for Przemek.</summary>
    public string[] WaitingMarkers { get; init; } = ["Przemek", "Human decision", "Acceptance"];

    internal long[] FieldIds() => new long?[] { StatusFieldId, AgentFieldId, MachineFieldId, WorkRepoFieldId, BranchFieldId,
        PullRequestFieldId, NextActorFieldId, HoldReasonFieldId }.OfType<long>().Distinct().ToArray();
}

internal sealed record CatalogRepository(string Repo, string Owner, bool Archived, bool Fork, int OpenIssuesAndPullRequests,
    DateTimeOffset? PushedAtUtc);

/// <summary>Open issue or PR from the issues list: metadata only.</summary>
internal sealed record CatalogIssue(string Repo, int Number, string Title, string[] Labels, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, bool PullRequest, bool Draft) {
    internal string Key => Repo + "#" + Number;
    internal string Url => "https://github.com/" + Repo + "/" + (PullRequest ? "pull" : "issues") + "/" + Number;
}

internal sealed record CatalogPull(string Repo, int Number, string Title, string? HeadBranch, bool Draft, DateTimeOffset UpdatedAtUtc) {
    internal string Url => "https://github.com/" + Repo + "/pull/" + Number;
}

internal sealed record CatalogBoardItem(long Id, string ContentType, string Title, string? ContentUrl, string? ContentState,
    DateTimeOffset UpdatedAtUtc, bool Archived) {
    public string? Status { get; init; }
    public string? Agent { get; init; }
    public string? Machine { get; init; }
    public string? WorkRepo { get; init; }
    public string? Branch { get; init; }
    public string? PullRequestLinks { get; init; }
    public string? NextActor { get; init; }
    public string? HoldReason { get; init; }
}

/// <summary>Observed state of a PR that a task links to. Merged is terminal and never read again.</summary>
internal sealed record CatalogPullState(string State, DateTimeOffset? UpdatedAtUtc, DateTimeOffset CheckedAtUtc) {
    public string? Title { get; init; }
    public bool Draft { get; init; }
    public string? HeadBranch { get; init; }
}

internal sealed record WorkCatalogSnapshot {
    public DateTimeOffset CapturedAtUtc { get; init; }
    public CatalogRepository[] Repositories { get; init; } = [];
    public CatalogIssue[] Issues { get; init; } = [];
    public CatalogPull[] PullRequests { get; init; } = [];
    public CatalogBoardItem[] Board { get; init; } = [];
    public DateTimeOffset? BoardReadAtUtc { get; init; }
    /// <summary>Issue key → PR URLs that referenced it (timeline cross-references).</summary>
    public Dictionary<string, string[]> IssueReferences { get; init; } = new(StringComparer.Ordinal);
    /// <summary>PR URL → state, for PRs linked from tasks.</summary>
    public Dictionary<string, CatalogPullState> PullStates { get; init; } = new(StringComparer.Ordinal);
    public string[] Coverage { get; init; } = [];
    public int Requests { get; init; }
    public int NotModified { get; init; }
}
