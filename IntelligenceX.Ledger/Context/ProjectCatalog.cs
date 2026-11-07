using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Context;

/// <summary>Explicit machine-local enrollment of maintained project sources.</summary>
public sealed record ProjectCatalog {
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset ObservedAtUtc { get; init; }
    public ProjectDefinition[] Projects { get; init; } = [];
    public WorkLabel[] WorkLabels { get; init; } = [];
}

/// <summary>One enrolled project and its independently observed directory facts.</summary>
public sealed record ProjectDefinition {
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Repository { get; init; }
    public string Revision { get; init; } = "unknown";
    public ResponsibilityView[] Responsibilities { get; init; } = [];
    public DependencyView[] Dependencies { get; init; } = [];
    public KnowledgeSource[] Sources { get; init; } = [];
    public string[] Coverage { get; init; } = [];
}

/// <summary>A single reviewed file, with local access and public/private source references kept separate.</summary>
public sealed record KnowledgeSource {
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Path { get; init; }
    public required string SourceRef { get; init; }
    public string Outcome { get; init; } = "reference";
    public DateTimeOffset? VerifiedAtUtc { get; init; }
    public string? SupersededBy { get; init; }
}

/// <summary>Locally curated display labels for exact opaque work identities.</summary>
public sealed record WorkLabel(string ItemId, string Label, string? ProjectId = null);
