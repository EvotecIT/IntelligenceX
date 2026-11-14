namespace IntelligenceX.Ledger.Contracts;

/// <summary>An answer of at most 8192 UTF-8 bytes for one native assignment. Content is stored outside the event journal;
/// credentials, transcripts and arbitrary local file paths are not part of this contract.</summary>
public sealed record CoordinationArtifactCommand(long Generation, string GrantId, string BootId, string Content);

/// <summary>Immutable UTF-8 result content. The service assigns its content digest; task authorization controls retrieval.</summary>
public sealed record CoordinationArtifact(string ResultRef, string Content);
