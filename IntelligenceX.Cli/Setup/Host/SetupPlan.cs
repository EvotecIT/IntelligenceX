using System;

namespace IntelligenceX.Cli.Setup.Host;

internal sealed class SetupPlan {
    public SetupPlan(string repoFullName) {
        if (string.IsNullOrWhiteSpace(repoFullName)) {
            throw new ArgumentException("Repository name is required.", nameof(repoFullName));
        }
        RepoFullName = repoFullName;
    }

    public string RepoFullName { get; }
    public string? GitHubClientId { get; init; }
    public string? GitHubToken { get; init; }
    public bool WithConfig { get; init; }
    public string? ConfigPath { get; init; }
    public string? ConfigJson { get; init; }
    public string? AuthB64 { get; init; }
    public string? AuthB64Path { get; init; }
    public string? Provider { get; init; }
    public string? OpenAIAccountId { get; init; }
    public string? OpenAIAccountIds { get; init; }
    public string? OpenAIAccountRotation { get; init; }
    public bool? OpenAIAccountFailover { get; init; }
    public string? ReviewProfile { get; init; }
    public string? ReviewMode { get; init; }
    public string? ReviewCommentMode { get; init; }
    public bool SkipSecret { get; init; }
    public bool ManualSecret { get; init; }
    public bool ExplicitSecrets { get; init; }
    public bool Upgrade { get; init; }
    public bool Force { get; init; }
    public bool UpdateSecret { get; init; }
    public bool Cleanup { get; init; }
    public bool KeepSecret { get; init; }
    public bool DryRun { get; init; }
    public string? BranchName { get; init; }

    // Reviewer config extras (written into .intelligencex/reviewer.json when WithConfig=true).
    public bool? AnalysisEnabled { get; init; }
    public bool? AnalysisGateEnabled { get; init; }
    public bool? AnalysisRunStrict { get; init; }
    public string? AnalysisPacks { get; init; }
    public string? AnalysisExportPath { get; init; }
}
