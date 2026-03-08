using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Setup;


internal static partial class SetupRunner {
    private sealed class FilePlan {
        public FilePlan(string path, string action, string? content, string? reason = null) {
            Path = path;
            Action = action;
            Content = content;
            Reason = reason;
        }

        public string Path { get; }
        public string Action { get; }
        public string? Content { get; }
        public string? Reason { get; }
        public bool IsWrite => Action is "create" or "update" or "overwrite";

        public static FilePlan Skip(string path, string? reason = null) => new(path, "skip", null, reason);
    }

    private sealed class RepoFile {
        public RepoFile(string sha, string content) {
            Sha = sha;
            Content = content;
        }

        public string Sha { get; }
        public string Content { get; }
    }

    private sealed class WorkflowSnapshot {
        public string? ActionsRepo { get; set; }
        public string? ActionsRef { get; set; }
        public bool UseLocalReusableWorkflow { get; set; }
        public string? RunsOn { get; set; }
        public string? ReviewerSource { get; set; }
        public string? ReviewerReleaseRepo { get; set; }
        public string? ReviewerReleaseTag { get; set; }
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string? Provider { get; set; }
        public string? Model { get; set; }
        public string? OpenAITransport { get; set; }
        public bool? IncludeIssueComments { get; set; }
        public bool? IncludeReviewComments { get; set; }
        public bool? IncludeRelatedPullRequests { get; set; }
        public bool? ProgressUpdates { get; set; }
        public bool? Diagnostics { get; set; }
        public bool? Preflight { get; set; }
        public int? PreflightTimeoutSeconds { get; set; }
        public bool? CleanupEnabled { get; set; }
        public string? CleanupMode { get; set; }
        public string? CleanupScope { get; set; }
        public string? CleanupRequireLabel { get; set; }
        public double? CleanupMinConfidence { get; set; }
        public string? CleanupAllowedEdits { get; set; }
        public bool? CleanupPostEditComment { get; set; }

        public bool HasAny =>
            ActionsRepo is not null ||
            ActionsRef is not null ||
            UseLocalReusableWorkflow ||
            RunsOn is not null ||
            ReviewerSource is not null ||
            ReviewerReleaseRepo is not null ||
            ReviewerReleaseTag is not null ||
            ReviewerReleaseAsset is not null ||
            ReviewerReleaseUrl is not null ||
            Provider is not null ||
            Model is not null ||
            OpenAITransport is not null ||
            IncludeIssueComments.HasValue ||
            IncludeReviewComments.HasValue ||
            IncludeRelatedPullRequests.HasValue ||
            ProgressUpdates.HasValue ||
            Diagnostics.HasValue ||
            Preflight.HasValue ||
            PreflightTimeoutSeconds.HasValue ||
            CleanupEnabled.HasValue ||
            CleanupMode is not null ||
            CleanupScope is not null ||
            CleanupRequireLabel is not null ||
            CleanupMinConfidence.HasValue ||
            CleanupAllowedEdits is not null ||
            CleanupPostEditComment.HasValue;
    }

    private sealed class ConfigSnapshot {
        public string? Provider { get; set; }
        public string? OpenAITransport { get; set; }
        public string? OpenAIModel { get; set; }
        public string? OpenAIAccountId { get; set; }
        public string[] OpenAIAccountIds { get; set; } = Array.Empty<string>();
        public string? OpenAIAccountRotation { get; set; }
        public bool? OpenAIAccountFailover { get; set; }
        public bool? SummaryStability { get; set; }
        public string? ReviewDiffRange { get; set; }
        public string? Intent { get; set; }
        public string? Strictness { get; set; }
        public string? VisionPath { get; set; }
        public string? Profile { get; set; }
        public string[] MergeBlockerSections { get; set; } = Array.Empty<string>();
        public bool? MergeBlockerRequireAllSections { get; set; }
        public bool? MergeBlockerRequireSectionMatch { get; set; }
        public string? Mode { get; set; }
        public string? CommentMode { get; set; }
        public bool? IncludeIssueComments { get; set; }
        public bool? IncludeReviewComments { get; set; }
        public bool? IncludeReviewThreads { get; set; }
        public bool? ReviewThreadsIncludeBots { get; set; }
        public int? ReviewThreadsMax { get; set; }
        public int? ReviewThreadsMaxComments { get; set; }
        public bool? ReviewThreadsAutoResolveStale { get; set; }
        public string? ReviewThreadsAutoResolveDiffRange { get; set; }
        public int? ReviewThreadsAutoResolveMax { get; set; }
        public bool? ReviewThreadsAutoResolveSweepNoBlockers { get; set; }
        public bool? ReviewThreadsAutoResolveAIReply { get; set; }
        public bool? ReviewUsageSummary { get; set; }
        public int? ReviewUsageSummaryCacheMinutes { get; set; }
        public int? ReviewUsageSummaryTimeoutSeconds { get; set; }
        public bool? ReviewUsageBudgetGuard { get; set; }
        public bool? ReviewUsageBudgetAllowCredits { get; set; }
        public bool? ReviewUsageBudgetAllowWeeklyLimit { get; set; }
        public bool? IncludeRelatedPullRequests { get; set; }
        public bool? ProgressUpdates { get; set; }
        public bool? Diagnostics { get; set; }
        public bool? Preflight { get; set; }
        public int? PreflightTimeoutSeconds { get; set; }
        public bool? AnalysisEnabled { get; set; }
        public bool? AnalysisGateEnabled { get; set; }
        public bool? AnalysisRunStrict { get; set; }
        public string[] AnalysisPacks { get; set; } = Array.Empty<string>();

        public bool HasAny =>
            Provider is not null ||
            OpenAITransport is not null ||
            OpenAIModel is not null ||
            OpenAIAccountId is not null ||
            OpenAIAccountIds.Length > 0 ||
            OpenAIAccountRotation is not null ||
            OpenAIAccountFailover.HasValue ||
            SummaryStability.HasValue ||
            ReviewDiffRange is not null ||
            Intent is not null ||
            Strictness is not null ||
            VisionPath is not null ||
            Profile is not null ||
            MergeBlockerSections.Length > 0 ||
            MergeBlockerRequireAllSections.HasValue ||
            MergeBlockerRequireSectionMatch.HasValue ||
            Mode is not null ||
            CommentMode is not null ||
            IncludeIssueComments.HasValue ||
            IncludeReviewComments.HasValue ||
            IncludeReviewThreads.HasValue ||
            ReviewThreadsIncludeBots.HasValue ||
            ReviewThreadsMax.HasValue ||
            ReviewThreadsMaxComments.HasValue ||
            ReviewThreadsAutoResolveStale.HasValue ||
            ReviewThreadsAutoResolveDiffRange is not null ||
            ReviewThreadsAutoResolveMax.HasValue ||
            ReviewThreadsAutoResolveSweepNoBlockers.HasValue ||
            ReviewThreadsAutoResolveAIReply.HasValue ||
            ReviewUsageSummary.HasValue ||
            ReviewUsageSummaryCacheMinutes.HasValue ||
            ReviewUsageSummaryTimeoutSeconds.HasValue ||
            ReviewUsageBudgetGuard.HasValue ||
            ReviewUsageBudgetAllowCredits.HasValue ||
            ReviewUsageBudgetAllowWeeklyLimit.HasValue ||
            IncludeRelatedPullRequests.HasValue ||
            ProgressUpdates.HasValue ||
            Diagnostics.HasValue ||
            Preflight.HasValue ||
            PreflightTimeoutSeconds.HasValue ||
            AnalysisEnabled.HasValue ||
            AnalysisGateEnabled.HasValue ||
            AnalysisRunStrict.HasValue ||
            AnalysisPacks.Length > 0;
    }

    private sealed class SetupState {
        public SetupState(SetupOptions options) {
            Options = options;
        }

        public SetupOptions Options { get; }
        public GitHubState GitHub { get; } = new();
        public OpenAiState OpenAI { get; } = new();
        public CopilotState Copilot { get; } = new();
        public Dictionary<string, object?> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class GitHubState {
        public string? ClientId { get; set; }
        public string? Token { get; set; }
        public string? RepositoryFullName { get; set; }
        public string? Owner { get; set; }
        public string? Repo { get; set; }
        public List<RepositoryInfo> Repositories { get; set; } = new();
    }

    private sealed class OpenAiState {
        public AuthBundle? AuthBundle { get; set; }
        public string? AuthJson { get; set; }
        public string? AuthB64 { get; set; }
    }

    private sealed class CopilotState {
        public string? Status { get; set; }
    }

    private sealed class RepositoryInfo {
        public RepositoryInfo(string fullName, bool isPrivate, DateTimeOffset? updatedAt) {
            FullName = fullName;
            Private = isPrivate;
            UpdatedAt = updatedAt;
        }

        public string FullName { get; }
        public bool Private { get; }
        public DateTimeOffset? UpdatedAt { get; }
    }

}
