using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.OpenAI;
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
        public string? Provider { get; set; } = IntelligenceXDefaults.DefaultProvider;
        public string? OpenAIModel { get; set; }
        public string? OpenAITransport { get; set; } = "native";
        public string? AnthropicApiKey { get; set; }
        public string? AnthropicApiKeyPath { get; set; }
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
                    case "model":
                    case "openai-model":
                        options.OpenAIModel = value;
                        options.OpenAIModelSet = true;
                        break;
                    case "openai-transport":
                        options.OpenAITransport = value;
                        options.OpenAITransportSet = true;
                        break;
                    case "anthropic-api-key":
                        options.AnthropicApiKey = value;
                        break;
                    case "anthropic-api-key-path":
                        options.AnthropicApiKeyPath = value;
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

}
