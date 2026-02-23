using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private const string DefaultActionsRepo = "evotecit/intelligencex";
    private const string DefaultActionsRef = "fb72170966841eb32cfcb00c46461bbf8f46233f";
    private const string DefaultRunsOn =
        "${{ github.event.repository.private && vars.IX_FORCE_GITHUB_HOSTED != 'true' && '[\"self-hosted\",\"ubuntu\"]' || '[\"ubuntu-latest\"]' }}";

    private sealed class SetupOptions {
        public string? RepoFullName { get; set; }
        public string? GitHubClientId { get; set; }
        public string? GitHubToken { get; set; }
        public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";
        public string GitHubAuthBaseUrl { get; set; } = "https://github.com";
        public string GitHubScopes { get; set; } = "repo workflow read:org";
        public string? ActionsRepo { get; set; } = DefaultActionsRepo;
        public string? ActionsRef { get; set; } = DefaultActionsRef;
        public string? RunsOn { get; set; } = DefaultRunsOn;
        public string? Provider { get; set; } = "openai";
        public string? OpenAIModel { get; set; } = "gpt-5.3-codex";
        public string? OpenAITransport { get; set; } = "native";
        public string? OpenAIAccountId { get; set; }
        public bool OpenAIAccountIdSet { get; set; }
        public string? OpenAIAccountIds { get; set; }
        public bool OpenAIAccountIdsSet { get; set; }
        public string? OpenAIAccountRotation { get; set; } = "first-available";
        public bool OpenAIAccountRotationSet { get; set; }
        public bool OpenAIAccountFailover { get; set; } = true;
        public bool OpenAIAccountFailoverSet { get; set; }
        public string? ReviewerSource { get; set; } = "release";
        public string? ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string? ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public string? ReviewIntent { get; set; }
        public string? ReviewStrictness { get; set; }
        public string? ReviewProfile { get; set; }
        public string? ReviewLoopPolicy { get; set; }
        public string? ReviewVisionPath { get; set; }
        public string? MergeBlockerSections { get; set; }
        public bool MergeBlockerRequireAllSections { get; set; } = true;
        public bool MergeBlockerRequireAllSectionsSet { get; set; }
        public bool MergeBlockerRequireSectionMatch { get; set; } = true;
        public bool MergeBlockerRequireSectionMatchSet { get; set; }
        public string? ReviewMode { get; set; }
        public string? ReviewCommentMode { get; set; }
        public string? ConfigPath { get; set; }
        public string? ConfigJson { get; set; }
        public string? AuthB64 { get; set; }
        public string? AuthB64Path { get; set; }
        public bool Diagnostics { get; set; }
        public bool Preflight { get; set; }
        public int PreflightTimeoutSeconds { get; set; } = 15;
        public bool AnalysisEnabled { get; set; }
        public bool AnalysisEnabledSet { get; set; }
        public bool AnalysisGateEnabled { get; set; }
        public bool AnalysisGateEnabledSet { get; set; }
        public bool AnalysisRunStrict { get; set; }
        public bool AnalysisRunStrictSet { get; set; }
        public string? AnalysisPacks { get; set; }
        public bool AnalysisPacksSet { get; set; }
        public string? AnalysisExportPath { get; set; }
        public bool AnalysisExportPathSet { get; set; }
        public bool CleanupEnabled { get; set; }
        public string? CleanupMode { get; set; } = "comment";
        public string? CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string? CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;
        public string? BranchName { get; set; }
        public bool WithConfig { get; set; }
        public bool TriageBootstrap { get; set; }
        public bool Upgrade { get; set; }
        public bool Cleanup { get; set; }
        public bool UpdateSecret { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool ManualSecretStdout { get; set; }
        public bool KeepSecret { get; set; }
        public bool Force { get; set; }
        public bool DryRun { get; set; }
        public bool ShowHelp { get; set; }
        public bool ActionsRepoSet { get; set; }
        public bool ActionsRefSet { get; set; }
        public bool RunsOnSet { get; set; }
        public bool ProviderSet { get; set; }
        public bool OpenAIModelSet { get; set; }
        public bool OpenAITransportSet { get; set; }
        public bool ReviewerSourceSet { get; set; }
        public bool ReviewerReleaseRepoSet { get; set; }
        public bool ReviewerReleaseTagSet { get; set; }
        public bool ReviewerReleaseAssetSet { get; set; }
        public bool ReviewerReleaseUrlSet { get; set; }
        public bool IncludeIssueCommentsSet { get; set; }
        public bool IncludeReviewCommentsSet { get; set; }
        public bool IncludeRelatedPullRequestsSet { get; set; }
        public bool ProgressUpdatesSet { get; set; }
        public bool ReviewIntentSet { get; set; }
        public bool ReviewStrictnessSet { get; set; }
        public bool ReviewProfileSet { get; set; }
        public bool ReviewLoopPolicySet { get; set; }
        public bool ReviewVisionPathSet { get; set; }
        public bool MergeBlockerSectionsSet { get; set; }
        public bool ReviewModeSet { get; set; }
        public bool ReviewCommentModeSet { get; set; }
        public bool DiagnosticsSet { get; set; }
        public bool PreflightSet { get; set; }
        public bool PreflightTimeoutSecondsSet { get; set; }
        public bool CleanupEnabledSet { get; set; }
        public bool CleanupModeSet { get; set; }
        public bool CleanupScopeSet { get; set; }
        public bool CleanupRequireLabelSet { get; set; }
        public bool CleanupMinConfidenceSet { get; set; }
        public bool CleanupAllowedEditsSet { get; set; }
        public bool CleanupPostEditCommentSet { get; set; }
        public bool ExplicitSecrets { get; set; } = true;

        public static SetupOptions Parse(string[] args) {
            var options = new SetupOptions {
                // Prefer non-interactive tokens when available; otherwise fall back to device flow via default Client ID.
                GitHubClientId = IntelligenceXDefaults.GetEffectiveGitHubClientId(),
                GitHubToken = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN"),
                OpenAIAccountId = Environment.GetEnvironmentVariable("INTELLIGENCEX_OPENAI_ACCOUNT_ID"),
                OpenAIAccountIds = Environment.GetEnvironmentVariable("INTELLIGENCEX_OPENAI_ACCOUNT_IDS"),
                OpenAIAccountRotation = Environment.GetEnvironmentVariable("INTELLIGENCEX_OPENAI_ACCOUNT_ROTATION")
                    ?? "first-available",
                OpenAIAccountFailover = ParseBool(
                    Environment.GetEnvironmentVariable("INTELLIGENCEX_OPENAI_ACCOUNT_FAILOVER"), true),
                GitHubApiBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_API_BASE_URL") ?? "https://api.github.com",
                GitHubAuthBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_AUTH_BASE_URL") ?? "https://github.com"
            };

            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                    continue;
                }
                var key = arg.Substring(2);
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";

                switch (key) {
                    case "repo":
                        options.RepoFullName = value;
                        break;
                    case "github-client-id":
                        options.GitHubClientId = value;
                        break;
                    case "github-token":
                        options.GitHubToken = value;
                        break;
                    case "github-api-base-url":
                        options.GitHubApiBaseUrl = value;
                        break;
                    case "github-auth-base-url":
                        options.GitHubAuthBaseUrl = value;
                        break;
                    case "actions-repo":
                        options.ActionsRepo = value;
                        options.ActionsRepoSet = true;
                        break;
                    case "actions-ref":
                        options.ActionsRef = value;
                        options.ActionsRefSet = true;
                        break;
                    case "runs-on":
                        options.RunsOn = value;
                        options.RunsOnSet = true;
                        break;
                    case "provider":
                        options.Provider = value;
                        options.ProviderSet = true;
                        break;
                    case "openai-model":
                        options.OpenAIModel = value;
                        options.OpenAIModelSet = true;
                        break;
                    case "openai-transport":
                        options.OpenAITransport = value;
                        options.OpenAITransportSet = true;
                        break;
                    case "openai-account-id":
                        options.OpenAIAccountId = value;
                        options.OpenAIAccountIdSet = true;
                        break;
                    case "openai-account-ids":
                        options.OpenAIAccountIds = value;
                        options.OpenAIAccountIdsSet = true;
                        break;
                    case "openai-account-rotation":
                        options.OpenAIAccountRotation = value;
                        options.OpenAIAccountRotationSet = true;
                        break;
                    case "openai-account-failover":
                        options.OpenAIAccountFailover = ParseBool(value, options.OpenAIAccountFailover);
                        options.OpenAIAccountFailoverSet = true;
                        break;
                    case "reviewer-source":
                        options.ReviewerSource = value;
                        options.ReviewerSourceSet = true;
                        break;
                    case "reviewer-release-repo":
                        options.ReviewerReleaseRepo = value;
                        options.ReviewerReleaseRepoSet = true;
                        break;
                    case "reviewer-release-tag":
                        options.ReviewerReleaseTag = value;
                        options.ReviewerReleaseTagSet = true;
                        break;
                    case "reviewer-release-asset":
                        options.ReviewerReleaseAsset = value;
                        options.ReviewerReleaseAssetSet = true;
                        break;
                    case "reviewer-release-url":
                        options.ReviewerReleaseUrl = value;
                        options.ReviewerReleaseUrlSet = true;
                        break;
                    case "include-issue-comments":
                        options.IncludeIssueComments = ParseBool(value, options.IncludeIssueComments);
                        options.IncludeIssueCommentsSet = true;
                        break;
                    case "include-review-comments":
                        options.IncludeReviewComments = ParseBool(value, options.IncludeReviewComments);
                        options.IncludeReviewCommentsSet = true;
                        break;
                    case "include-related-prs":
                        options.IncludeRelatedPullRequests = ParseBool(value, options.IncludeRelatedPullRequests);
                        options.IncludeRelatedPullRequestsSet = true;
                        break;
                    case "progress-updates":
                        options.ProgressUpdates = ParseBool(value, options.ProgressUpdates);
                        options.ProgressUpdatesSet = true;
                        break;
                    case "review-profile":
                        options.ReviewProfile = value;
                        options.ReviewProfileSet = true;
                        break;
                    case "review-intent":
                        options.ReviewIntent = value;
                        options.ReviewIntentSet = true;
                        break;
                    case "review-strictness":
                        options.ReviewStrictness = value;
                        options.ReviewStrictnessSet = true;
                        break;
                    case "review-loop-policy":
                        options.ReviewLoopPolicy = value;
                        options.ReviewLoopPolicySet = true;
                        break;
                    case "review-vision-path":
                        options.ReviewVisionPath = value;
                        options.ReviewVisionPathSet = true;
                        break;
                    case "merge-blocker-sections":
                        options.MergeBlockerSections = value;
                        options.MergeBlockerSectionsSet = true;
                        break;
                    case "merge-blocker-require-all-sections":
                        options.MergeBlockerRequireAllSections =
                            ParseBool(value, options.MergeBlockerRequireAllSections);
                        options.MergeBlockerRequireAllSectionsSet = true;
                        break;
                    case "merge-blocker-require-section-match":
                        options.MergeBlockerRequireSectionMatch =
                            ParseBool(value, options.MergeBlockerRequireSectionMatch);
                        options.MergeBlockerRequireSectionMatchSet = true;
                        break;
                    case "review-mode":
                        options.ReviewMode = value;
                        options.ReviewModeSet = true;
                        break;
                    case "review-comment-mode":
                        options.ReviewCommentMode = value;
                        options.ReviewCommentModeSet = true;
                        break;
                    case "analysis-enabled":
                        options.AnalysisEnabled = ParseBool(value, options.AnalysisEnabled);
                        options.AnalysisEnabledSet = true;
                        break;
                    case "analysis-gate":
                        options.AnalysisGateEnabled = ParseBool(value, options.AnalysisGateEnabled);
                        options.AnalysisGateEnabledSet = true;
                        break;
                    case "analysis-run-strict":
                        options.AnalysisRunStrict = ParseBool(value, options.AnalysisRunStrict);
                        options.AnalysisRunStrictSet = true;
                        break;
                    case "analysis-packs":
                        options.AnalysisPacks = value;
                        options.AnalysisPacksSet = true;
                        break;
                    case "analysis-export-path":
                        options.AnalysisExportPath = value;
                        options.AnalysisExportPathSet = true;
                        break;
                    case "config-path":
                        options.ConfigPath = value;
                        options.WithConfig = true;
                        break;
                    case "config-json":
                        options.ConfigJson = value;
                        options.WithConfig = true;
                        break;
                    case "auth-b64":
                        options.AuthB64 = value;
                        break;
                    case "auth-b64-path":
                        options.AuthB64Path = value;
                        break;
                    case "cleanup-enabled":
                        options.CleanupEnabled = ParseBool(value, options.CleanupEnabled);
                        options.CleanupEnabledSet = true;
                        break;
                    case "cleanup-mode":
                        options.CleanupMode = value;
                        options.CleanupModeSet = true;
                        break;
                    case "cleanup-scope":
                        options.CleanupScope = value;
                        options.CleanupScopeSet = true;
                        break;
                    case "cleanup-require-label":
                        options.CleanupRequireLabel = value;
                        options.CleanupRequireLabelSet = true;
                        break;
                    case "cleanup-min-confidence":
                        options.CleanupMinConfidence = ParseDouble(value, options.CleanupMinConfidence);
                        options.CleanupMinConfidenceSet = true;
                        break;
                    case "cleanup-allowed-edits":
                        options.CleanupAllowedEdits = value;
                        options.CleanupAllowedEditsSet = true;
                        break;
                    case "cleanup-post-edit-comment":
                        options.CleanupPostEditComment = ParseBool(value, options.CleanupPostEditComment);
                        options.CleanupPostEditCommentSet = true;
                        break;
                    case "with-config":
                        options.WithConfig = ParseBool(value, true);
                        break;
                    case "triage-bootstrap":
                        options.TriageBootstrap = ParseBool(value, true);
                        break;
                    case "upgrade":
                        options.Upgrade = ParseBool(value, true);
                        break;
                    case "cleanup":
                        options.Cleanup = ParseBool(value, true);
                        break;
                    case "update-secret":
                        options.UpdateSecret = ParseBool(value, true);
                        break;
                    case "skip-secret":
                        options.SkipSecret = ParseBool(value, true);
                        break;
                    case "manual-secret":
                        options.ManualSecret = ParseBool(value, true);
                        break;
                    case "manual-secret-stdout":
                        options.ManualSecretStdout = ParseBool(value, true);
                        break;
                    case "keep-secret":
                        options.KeepSecret = ParseBool(value, true);
                        break;
                    case "branch":
                        options.BranchName = value;
                        break;
                    case "force":
                        options.Force = ParseBool(value, true);
                        break;
                    case "dry-run":
                        options.DryRun = ParseBool(value, true);
                        break;
                    case "explicit-secrets":
                        options.ExplicitSecrets = ParseBool(value, true);
                        break;
                    case "help":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }
    }

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

    private sealed class WorkflowSettings {
        public string ActionsRepo { get; set; } = DefaultActionsRepo;
        public string ActionsRef { get; set; } = DefaultActionsRef;
        public string RunsOn { get; set; } = DefaultRunsOn;
        public string ReviewerSource { get; set; } = "release";
        public string ReviewerReleaseRepo { get; set; } = "EvotecIT/github-actions";
        public string ReviewerReleaseTag { get; set; } = "latest";
        public string? ReviewerReleaseAsset { get; set; }
        public string? ReviewerReleaseUrl { get; set; }
        public string Provider { get; set; } = "openai";
        public string Model { get; set; } = "gpt-5.3-codex";
        public string OpenAITransport { get; set; } = "native";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool ExplicitSecrets { get; set; } = true;
        public bool Diagnostics { get; set; }
        public bool Preflight { get; set; }
        public int PreflightTimeoutSeconds { get; set; } = 15;
        public bool CleanupEnabled { get; set; }
        public string CleanupMode { get; set; } = "comment";
        public string CleanupScope { get; set; } = "pr";
        public string? CleanupRequireLabel { get; set; } = string.Empty;
        public double CleanupMinConfidence { get; set; } = 0.85;
        public string CleanupAllowedEdits { get; set; } = "formatting,grammar,title,sections";
        public bool CleanupPostEditComment { get; set; } = true;

        public static WorkflowSettings FromOptions(SetupOptions options) {
            return new WorkflowSettings {
                ActionsRepo = NormalizeActionsRepo(options.ActionsRepo ?? DefaultActionsRepo),
                ActionsRef = options.ActionsRef ?? DefaultActionsRef,
                RunsOn = options.RunsOn ?? DefaultRunsOn,
                ReviewerSource = options.ReviewerSource ?? "release",
                ReviewerReleaseRepo = options.ReviewerReleaseRepo ?? "EvotecIT/github-actions",
                ReviewerReleaseTag = options.ReviewerReleaseTag ?? "latest",
                ReviewerReleaseAsset = options.ReviewerReleaseAsset,
                ReviewerReleaseUrl = options.ReviewerReleaseUrl,
                Provider = options.Provider ?? "openai",
                Model = options.OpenAIModel ?? "gpt-5.3-codex",
                OpenAITransport = options.OpenAITransport ?? "native",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates,
                ExplicitSecrets = options.ExplicitSecrets,
                Diagnostics = options.Diagnostics,
                Preflight = options.Preflight,
                PreflightTimeoutSeconds = options.PreflightTimeoutSeconds,
                CleanupEnabled = options.CleanupEnabled,
                CleanupMode = options.CleanupMode ?? "comment",
                CleanupScope = options.CleanupScope ?? "pr",
                CleanupRequireLabel = options.CleanupRequireLabel ?? string.Empty,
                CleanupMinConfidence = options.CleanupMinConfidence,
                CleanupAllowedEdits = options.CleanupAllowedEdits ?? "formatting,grammar,title,sections",
                CleanupPostEditComment = options.CleanupPostEditComment
            };
        }
    }

    private sealed class ConfigSettings {
        public string Provider { get; set; } = "openai";
        public string OpenAITransport { get; set; } = "native";
        public string OpenAIModel { get; set; } = "gpt-5.3-codex";
        public string? OpenAIAccountId { get; set; }
        public bool OpenAIAccountIdsSet { get; set; }
        public string[] OpenAIAccountIds { get; set; } = Array.Empty<string>();
        public string OpenAIAccountRotation { get; set; } = "first-available";
        public bool OpenAIAccountFailover { get; set; } = true;
        public string? Intent { get; set; }
        public bool IntentSet { get; set; }
        public string? Strictness { get; set; }
        public bool StrictnessSet { get; set; }
        public string? VisionPath { get; set; }
        public bool VisionPathSet { get; set; }
        public string Profile { get; set; } = "balanced";
        public string[] MergeBlockerSections { get; set; } = Array.Empty<string>();
        public bool MergeBlockerSectionsSet { get; set; }
        public bool MergeBlockerRequireAllSections { get; set; } = true;
        public bool MergeBlockerRequireAllSectionsSet { get; set; }
        public bool MergeBlockerRequireSectionMatch { get; set; } = true;
        public bool MergeBlockerRequireSectionMatchSet { get; set; }
        public string Mode { get; set; } = "hybrid";
        public string CommentMode { get; set; } = "sticky";
        public bool IncludeIssueComments { get; set; } = true;
        public bool IncludeReviewComments { get; set; } = true;
        public bool IncludeRelatedPullRequests { get; set; } = true;
        public bool ProgressUpdates { get; set; } = true;
        public bool Diagnostics { get; set; }
        public bool Preflight { get; set; }
        public int PreflightTimeoutSeconds { get; set; } = 15;
        public bool AnalysisEnabled { get; set; }
        public bool AnalysisEnabledSet { get; set; }
        public bool AnalysisGateEnabled { get; set; }
        public bool AnalysisGateEnabledSet { get; set; }
        public bool AnalysisRunStrict { get; set; }
        public bool AnalysisRunStrictSet { get; set; }
        public string[] AnalysisPacks { get; set; } = Array.Empty<string>();
        public bool AnalysisPacksSet { get; set; }

        public static ConfigSettings FromOptions(SetupOptions options) {
            var settings = new ConfigSettings {
                Provider = options.Provider ?? "openai",
                OpenAITransport = options.OpenAITransport ?? "native",
                OpenAIModel = options.OpenAIModel ?? "gpt-5.3-codex",
                OpenAIAccountId = string.IsNullOrWhiteSpace(options.OpenAIAccountId) ? null : options.OpenAIAccountId!.Trim(),
                OpenAIAccountIdsSet = options.OpenAIAccountIdsSet,
                OpenAIAccountIds = SplitCsv(options.OpenAIAccountIds),
                OpenAIAccountRotation = NormalizeOpenAiAccountRotation(options.OpenAIAccountRotation, strict: true),
                OpenAIAccountFailover = options.OpenAIAccountFailover,
                Intent = NormalizeOptionalValue(options.ReviewIntent),
                IntentSet = options.ReviewIntentSet,
                Strictness = NormalizeOptionalValue(options.ReviewStrictness),
                StrictnessSet = options.ReviewStrictnessSet,
                VisionPath = NormalizeOptionalValue(options.ReviewVisionPath),
                VisionPathSet = options.ReviewVisionPathSet,
                Profile = options.ReviewProfile ?? "balanced",
                MergeBlockerSections = SplitCsv(options.MergeBlockerSections),
                MergeBlockerSectionsSet = options.MergeBlockerSectionsSet,
                MergeBlockerRequireAllSections = options.MergeBlockerRequireAllSections,
                MergeBlockerRequireAllSectionsSet = options.MergeBlockerRequireAllSectionsSet,
                MergeBlockerRequireSectionMatch = options.MergeBlockerRequireSectionMatch,
                MergeBlockerRequireSectionMatchSet = options.MergeBlockerRequireSectionMatchSet,
                Mode = options.ReviewMode ?? "hybrid",
                CommentMode = options.ReviewCommentMode ?? "sticky",
                IncludeIssueComments = options.IncludeIssueComments,
                IncludeReviewComments = options.IncludeReviewComments,
                IncludeRelatedPullRequests = options.IncludeRelatedPullRequests,
                ProgressUpdates = options.ProgressUpdates,
                Diagnostics = options.Diagnostics,
                Preflight = options.Preflight,
                PreflightTimeoutSeconds = options.PreflightTimeoutSeconds,
                AnalysisEnabled = options.AnalysisEnabled,
                AnalysisEnabledSet = options.AnalysisEnabledSet,
                AnalysisGateEnabled = options.AnalysisGateEnabled,
                AnalysisGateEnabledSet = options.AnalysisGateEnabledSet,
                AnalysisRunStrict = options.AnalysisRunStrict,
                AnalysisRunStrictSet = options.AnalysisRunStrictSet,
                AnalysisPacks = SplitCsv(options.AnalysisPacks),
                AnalysisPacksSet = options.AnalysisPacksSet
            };
            ValidateReviewVisionPathContext(options);
            ApplyReviewLoopPolicy(settings,
                options.ReviewLoopPolicy,
                options.ReviewLoopPolicySet,
                options.ReviewVisionPath,
                options.ReviewVisionPathSet);
            ValidateMergeBlockerSections(settings);
            return settings;
        }

        private static string[] SplitCsv(string? raw) {
            if (string.IsNullOrWhiteSpace(raw)) {
                return Array.Empty<string>();
            }
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string? NormalizeOptionalValue(string? value) {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void ValidateReviewVisionPathContext(SetupOptions options) {
            if (!options.ReviewVisionPathSet) {
                return;
            }
            if (!options.ReviewLoopPolicySet) {
                throw new InvalidOperationException(
                    "--review-vision-path requires --review-loop-policy vision.");
            }
            if (!SetupReviewLoopPolicy.TryNormalize(options.ReviewLoopPolicy, out var normalizedPolicy)) {
                throw new InvalidOperationException(
                    $"Invalid --review-loop-policy value. Use {SetupReviewLoopPolicy.AllowedValuesMessage()}.");
            }
            if (!string.Equals(normalizedPolicy, SetupReviewLoopPolicy.Vision, StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    "--review-vision-path is only supported with --review-loop-policy vision.");
            }
        }

        private static void ValidateMergeBlockerSections(ConfigSettings settings) {
            if (!settings.MergeBlockerSectionsSet) {
                return;
            }
            if (settings.MergeBlockerSections.Length > 0) {
                return;
            }
            throw new InvalidOperationException(
                "--merge-blocker-sections requires at least one non-empty section name.");
        }

        private static void ApplyReviewLoopPolicy(
            ConfigSettings settings,
            string? value,
            bool policySet,
            string? visionPath,
            bool visionPathSet) {
            if (!policySet) {
                return;
            }
            if (!SetupReviewLoopPolicy.TryNormalize(value, out var normalized)) {
                throw new InvalidOperationException(
                    $"Invalid --review-loop-policy value. Use {SetupReviewLoopPolicy.AllowedValuesMessage()}.");
            }
            switch (normalized) {
                case SetupReviewLoopPolicy.Strict:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: true,
                        requireSectionMatch: true);
                    return;
                case SetupReviewLoopPolicy.Balanced:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: false,
                        requireSectionMatch: true);
                    return;
                case SetupReviewLoopPolicy.Lenient:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: false,
                        requireSectionMatch: false);
                    return;
                case SetupReviewLoopPolicy.TodoOnly:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list" },
                        requireAllSections: true,
                        requireSectionMatch: true);
                    return;
                case SetupReviewLoopPolicy.Vision:
                    ApplyMergeBlockerPolicy(settings,
                        sections: new[] { "todo list", "critical issues" },
                        requireAllSections: false,
                        requireSectionMatch: true);
                    if (!settings.VisionPathSet && !string.IsNullOrWhiteSpace(visionPath)) {
                        settings.VisionPath = visionPath.Trim();
                        settings.VisionPathSet = true;
                    }
                    var visionDefaults = ResolveReviewDefaultsFromVisionDocument(settings.VisionPath, visionPathSet);
                    if (!settings.IntentSet) {
                        settings.Intent = visionDefaults.Intent ?? "maintainability";
                        settings.IntentSet = true;
                    }
                    if (!settings.StrictnessSet) {
                        settings.Strictness = visionDefaults.Strictness ?? "balanced";
                        settings.StrictnessSet = true;
                    }
                    return;
            }
        }

        private static void ApplyMergeBlockerPolicy(
            ConfigSettings settings,
            IReadOnlyList<string> sections,
            bool requireAllSections,
            bool requireSectionMatch) {
            if (!settings.MergeBlockerSectionsSet) {
                settings.MergeBlockerSections = sections.ToArray();
                settings.MergeBlockerSectionsSet = true;
            }
            if (!settings.MergeBlockerRequireAllSectionsSet) {
                settings.MergeBlockerRequireAllSections = requireAllSections;
                settings.MergeBlockerRequireAllSectionsSet = true;
            }
            if (!settings.MergeBlockerRequireSectionMatchSet) {
                settings.MergeBlockerRequireSectionMatch = requireSectionMatch;
                settings.MergeBlockerRequireSectionMatchSet = true;
            }
        }

        private static string NormalizeOpenAiAccountRotation(string? value, bool strict) {
            if (string.IsNullOrWhiteSpace(value)) {
                return "first-available";
            }
            var normalized = value.Trim().ToLowerInvariant() switch {
                "first" or "first-available" or "first_available" or "ordered" => "first-available",
                "round-robin" or "round_robin" or "rr" or "rotate" => "round-robin",
                "sticky" or "pin" or "pinned" => "sticky",
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(normalized)) {
                return normalized;
            }
            if (strict) {
                throw new InvalidOperationException(
                    "Invalid --openai-account-rotation value. Use first-available, round-robin, or sticky.");
            }
            return "first-available";
        }
    }

    private sealed class WorkflowSnapshot {
        public string? ActionsRepo { get; set; }
        public string? ActionsRef { get; set; }
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
