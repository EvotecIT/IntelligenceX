using System.Collections.Generic;
using IntelligenceX.Cli.Setup.Wizard;

namespace IntelligenceX.Cli.Setup;

internal enum SetupApplyOperation {
    Setup,
    Cleanup,
    UpdateSecret
}

internal sealed class SetupPostApplyContext {
    public string Repo { get; set; } = string.Empty;
    public SetupApplyOperation Operation { get; set; }
    public bool WithConfig { get; set; }
    public bool SkipSecret { get; set; }
    public bool ManualSecret { get; set; }
    public bool KeepSecret { get; set; }
    public bool DryRun { get; set; }
    public bool ExitSuccess { get; set; }
    public bool ExpectOrgSecret { get; set; }
    public string? SecretOrg { get; set; }
    public string Provider { get; set; } = "openai";
    public string Output { get; set; } = string.Empty;
    public string? PullRequestUrl { get; set; }
}

internal sealed class SetupPostApplyObservedState {
    public string? DefaultBranch { get; set; }
    public string? CheckRef { get; set; }
    public string? CheckRefSource { get; set; }
    public bool? WorkflowExists { get; set; }
    public bool? WorkflowManaged { get; set; }
    public bool? ConfigExists { get; set; }
    public GitHubRepoClient.SecretLookupResult? RepoSecretLookup { get; set; }
    public GitHubRepoClient.SecretLookupResult? OrgSecretLookup { get; set; }
    public GitHubRepoClient.WorkflowRunInfo? LatestWorkflowRun { get; set; }
    public string? WorkflowRunLookupStatus { get; set; }
    public string? WorkflowRunLookupNote { get; set; }
}

internal sealed class SetupPostApplyCheck {
    public string Name { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public bool Skipped { get; set; }
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public string? Note { get; set; }
}

internal sealed class SetupPostApplyVerification {
    public string Repo { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public bool Skipped { get; set; }
    public string? Note { get; set; }
    public string? PullRequestUrl { get; set; }
    public string? CheckedRef { get; set; }
    public string? CheckedRefSource { get; set; }
    public List<SetupPostApplyCheck> Checks { get; set; } = new();
}

internal static class SetupPostApplyCheckNames {
    public const string PullRequest = "Pull request";
    public const string Workflow = "Workflow";
    public const string ReviewerConfig = "Reviewer config";
    public const string RepoSecret = "Repo secret";
    public const string OrgSecret = "Org secret";
    public const string LatestWorkflowRun = "Latest workflow run";
}

internal static class SetupPostApplyCheckValues {
    public const string Ok = "ok";
    public const string Unknown = "unknown";
    public const string NotChecked = "not checked";
    public const string Observed = "observed";
    public const string None = "none";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string RateLimited = "rate_limited";
}
