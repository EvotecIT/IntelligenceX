namespace IntelligenceX.Ledger.Context;

/// <summary>The directory cannot establish whether a requested project exists.</summary>
public sealed class ProjectDirectoryUnavailableException : IOException {
    public ProjectDirectoryUnavailableException() : base("The project directory is unavailable.") { }
}
