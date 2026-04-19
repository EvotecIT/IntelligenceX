using System;
using IntelligenceX.Copilot;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    private static void ApplyEnvironmentCopilotAndAzureSettings(ReviewSettings settings) {
        var copilotCliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(copilotCliPath)) {
            settings.CopilotCliPath = copilotCliPath;
        }

        var copilotCliUrl = Environment.GetEnvironmentVariable("COPILOT_CLI_URL");
        if (!string.IsNullOrWhiteSpace(copilotCliUrl)) {
            settings.CopilotCliUrl = copilotCliUrl;
        }

        var copilotWorkingDirectory = Environment.GetEnvironmentVariable("COPILOT_CLI_WORKDIR");
        if (!string.IsNullOrWhiteSpace(copilotWorkingDirectory)) {
            settings.CopilotWorkingDirectory = copilotWorkingDirectory;
        }

        var copilotLauncher = GetInput("copilot_launcher", "COPILOT_LAUNCHER");
        if (!string.IsNullOrWhiteSpace(copilotLauncher)) {
            settings.CopilotLauncher = NormalizeCopilotLauncher(copilotLauncher, settings.CopilotLauncher);
        }

        var copilotAutoInstall = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL");
        if (!string.IsNullOrWhiteSpace(copilotAutoInstall)) {
            settings.CopilotAutoInstall = ParseBoolean(copilotAutoInstall, settings.CopilotAutoInstall);
        }

        var copilotAutoInstallMethod = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL_METHOD");
        if (!string.IsNullOrWhiteSpace(copilotAutoInstallMethod)) {
            settings.CopilotAutoInstallMethod = copilotAutoInstallMethod;
        }

        var copilotAutoInstallPrerelease = Environment.GetEnvironmentVariable("COPILOT_AUTO_INSTALL_PRERELEASE");
        if (!string.IsNullOrWhiteSpace(copilotAutoInstallPrerelease)) {
            settings.CopilotAutoInstallPrerelease =
                ParseBoolean(copilotAutoInstallPrerelease, settings.CopilotAutoInstallPrerelease);
        }

        var copilotEnvAllowlist = GetInput("copilot_env_allowlist", "COPILOT_ENV_ALLOWLIST");
        if (!string.IsNullOrWhiteSpace(copilotEnvAllowlist)) {
            settings.CopilotEnvAllowlist = ParseList(copilotEnvAllowlist, settings.CopilotEnvAllowlist);
        }

        var copilotInheritEnvironment = GetInput("copilot_inherit_environment", "COPILOT_INHERIT_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(copilotInheritEnvironment)) {
            settings.CopilotInheritEnvironment =
                ParseBoolean(copilotInheritEnvironment, settings.CopilotInheritEnvironment);
        }

        var copilotTransport = GetInput("copilot_transport", "COPILOT_TRANSPORT");
        if (!string.IsNullOrWhiteSpace(copilotTransport)) {
            settings.CopilotTransport = ParseCopilotTransport(copilotTransport, settings.CopilotTransport);
        }

        var copilotDirectUrl = GetInput("copilot_direct_url", "COPILOT_DIRECT_URL");
        if (!string.IsNullOrWhiteSpace(copilotDirectUrl)) {
            settings.CopilotDirectUrl = copilotDirectUrl;
        }

        var copilotDirectToken = GetInput("copilot_direct_token", "COPILOT_DIRECT_TOKEN");
        if (!string.IsNullOrWhiteSpace(copilotDirectToken)) {
            settings.CopilotDirectToken = copilotDirectToken;
        }

        var copilotDirectTokenEnv = GetInput("copilot_direct_token_env", "COPILOT_DIRECT_TOKEN_ENV");
        if (!string.IsNullOrWhiteSpace(copilotDirectTokenEnv)) {
            settings.CopilotDirectTokenEnv = copilotDirectTokenEnv;
        }

        var copilotDirectTimeout = GetInput("copilot_direct_timeout_seconds", "COPILOT_DIRECT_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(copilotDirectTimeout)) {
            settings.CopilotDirectTimeoutSeconds =
                ParsePositiveInt(copilotDirectTimeout, settings.CopilotDirectTimeoutSeconds);
        }

        var azureOrg = GetInput("azure_org", "AZURE_DEVOPS_ORG");
        if (!string.IsNullOrWhiteSpace(azureOrg)) {
            settings.AzureOrganization = azureOrg;
        }

        var azureProject = GetInput("azure_project", "AZURE_DEVOPS_PROJECT");
        if (!string.IsNullOrWhiteSpace(azureProject)) {
            settings.AzureProject = azureProject;
        }

        var azureRepo = GetInput("azure_repo", "AZURE_DEVOPS_REPO");
        if (!string.IsNullOrWhiteSpace(azureRepo)) {
            settings.AzureRepository = azureRepo;
        }

        var azureBaseUrl = GetInput("azure_base_url", "AZURE_DEVOPS_BASE_URL");
        if (!string.IsNullOrWhiteSpace(azureBaseUrl)) {
            settings.AzureBaseUrl = azureBaseUrl;
        }

        var azureTokenEnv = GetInput("azure_token_env", "AZURE_DEVOPS_TOKEN_ENV");
        if (!string.IsNullOrWhiteSpace(azureTokenEnv)) {
            settings.AzureTokenEnv = azureTokenEnv;
        }

        var azureAuthScheme = GetInput("azure_auth_scheme", "AZURE_DEVOPS_AUTH_SCHEME");
        if (!string.IsNullOrWhiteSpace(azureAuthScheme)) {
            settings.AzureAuthScheme = ParseAzureAuthScheme(azureAuthScheme);
            settings.AzureAuthSchemeSpecified = true;
        }
    }

    private static void ApplyEnvironmentCommentsAndCleanupSettings(ReviewSettings settings) {
        var length = GetInput("length", "REVIEW_LENGTH");
        if (!string.IsNullOrWhiteSpace(length)) {
            settings.Length = length.Trim().ToLowerInvariant() switch {
                "short" => ReviewLength.Short,
                "medium" => ReviewLength.Medium,
                _ => ReviewLength.Long
            };
        }

        var includeNextSteps = GetInput("include_next_steps", "REVIEW_INCLUDE_NEXT_STEPS");
        if (!string.IsNullOrWhiteSpace(includeNextSteps)) {
            settings.IncludeNextSteps = ParseBoolean(includeNextSteps, settings.IncludeNextSteps);
        }

        var languageHints = GetInput("language_hints", "REVIEW_LANGUAGE_HINTS");
        if (!string.IsNullOrWhiteSpace(languageHints)) {
            settings.IncludeLanguageHints = ParseBoolean(languageHints, settings.IncludeLanguageHints);
        }

        var budgetSummary = GetInput("review_budget_summary", "REVIEW_BUDGET_SUMMARY");
        if (!string.IsNullOrWhiteSpace(budgetSummary)) {
            settings.ReviewBudgetSummary = ParseBoolean(budgetSummary, settings.ReviewBudgetSummary);
        }

        var commentMode = GetInput("comment_mode", "REVIEW_COMMENT_MODE");
        if (!string.IsNullOrWhiteSpace(commentMode)) {
            settings.CommentMode = commentMode.Trim().ToLowerInvariant() switch {
                "fresh" => ReviewCommentMode.Fresh,
                _ => ReviewCommentMode.Sticky
            };
        }

        var includeIssueComments = GetInput("include_issue_comments", "REVIEW_INCLUDE_ISSUE_COMMENTS");
        if (!string.IsNullOrWhiteSpace(includeIssueComments)) {
            settings.IncludeIssueComments = ParseBoolean(includeIssueComments, settings.IncludeIssueComments);
        }

        var includeReviewComments = GetInput("include_review_comments", "REVIEW_INCLUDE_REVIEW_COMMENTS");
        if (!string.IsNullOrWhiteSpace(includeReviewComments)) {
            settings.IncludeReviewComments = ParseBoolean(includeReviewComments, settings.IncludeReviewComments);
        }

        var includeReviewThreads = GetInput("include_review_threads", "REVIEW_INCLUDE_REVIEW_THREADS");
        if (!string.IsNullOrWhiteSpace(includeReviewThreads)) {
            settings.IncludeReviewThreads = ParseBoolean(includeReviewThreads, settings.IncludeReviewThreads);
        }

        var triageOnly = GetInput("triage_only", "REVIEW_TRIAGE_ONLY");
        if (!string.IsNullOrWhiteSpace(triageOnly)) {
            settings.TriageOnly = ParseBoolean(triageOnly, settings.TriageOnly);
        }

        var reviewThreadsIncludeBots = GetInput(
            "review_threads_include_bots",
            "REVIEW_THREADS_INCLUDE_BOTS",
            "REVIEW_REVIEW_THREADS_INCLUDE_BOTS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsIncludeBots)) {
            settings.ReviewThreadsIncludeBots =
                ParseBoolean(reviewThreadsIncludeBots, settings.ReviewThreadsIncludeBots);
        }

        var reviewThreadsIncludeResolved = GetInput(
            "review_threads_include_resolved",
            "REVIEW_THREADS_INCLUDE_RESOLVED",
            "REVIEW_REVIEW_THREADS_INCLUDE_RESOLVED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsIncludeResolved)) {
            settings.ReviewThreadsIncludeResolved =
                ParseBoolean(reviewThreadsIncludeResolved, settings.ReviewThreadsIncludeResolved);
        }

        var reviewThreadsIncludeOutdated = GetInput(
            "review_threads_include_outdated",
            "REVIEW_THREADS_INCLUDE_OUTDATED",
            "REVIEW_REVIEW_THREADS_INCLUDE_OUTDATED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsIncludeOutdated)) {
            settings.ReviewThreadsIncludeOutdated =
                ParseBoolean(reviewThreadsIncludeOutdated, settings.ReviewThreadsIncludeOutdated);
        }

        var reviewThreadsMax = GetInput("review_threads_max", "REVIEW_THREADS_MAX", "REVIEW_REVIEW_THREADS_MAX");
        if (!string.IsNullOrWhiteSpace(reviewThreadsMax)) {
            settings.ReviewThreadsMax = ParseNonNegativeInt(reviewThreadsMax, settings.ReviewThreadsMax);
        }

        var reviewThreadsMaxComments = GetInput(
            "review_threads_max_comments",
            "REVIEW_THREADS_MAX_COMMENTS",
            "REVIEW_REVIEW_THREADS_MAX_COMMENTS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsMaxComments)) {
            settings.ReviewThreadsMaxComments =
                ParseNonNegativeInt(reviewThreadsMaxComments, settings.ReviewThreadsMaxComments);
        }

        var reviewThreadsAutoResolveStale = GetInput(
            "review_threads_auto_resolve_stale",
            "REVIEW_THREADS_AUTO_RESOLVE_STALE",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_STALE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveStale)) {
            settings.ReviewThreadsAutoResolveStale =
                ParseBoolean(reviewThreadsAutoResolveStale, settings.ReviewThreadsAutoResolveStale);
        }

        var reviewThreadsAutoResolveMissingInline = GetInput(
            "review_threads_auto_resolve_missing_inline",
            "REVIEW_THREADS_AUTO_RESOLVE_MISSING_INLINE",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_MISSING_INLINE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveMissingInline)) {
            settings.ReviewThreadsAutoResolveMissingInline =
                ParseBoolean(reviewThreadsAutoResolveMissingInline, settings.ReviewThreadsAutoResolveMissingInline);
        }

        var reviewThreadsAutoResolveBotsOnly = GetInput(
            "review_threads_auto_resolve_bots_only",
            "REVIEW_THREADS_AUTO_RESOLVE_BOTS_ONLY",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_BOTS_ONLY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveBotsOnly)) {
            settings.ReviewThreadsAutoResolveBotsOnly =
                ParseBoolean(reviewThreadsAutoResolveBotsOnly, settings.ReviewThreadsAutoResolveBotsOnly);
        }

        var reviewThreadsAutoResolveBotLogins = GetInput(
            "review_threads_auto_resolve_bot_logins",
            "REVIEW_THREADS_AUTO_RESOLVE_BOT_LOGINS",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_BOT_LOGINS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveBotLogins)) {
            settings.ReviewThreadsAutoResolveBotLogins =
                ParseList(reviewThreadsAutoResolveBotLogins, settings.ReviewThreadsAutoResolveBotLogins);
        }

        var reviewThreadsAutoResolveDiffRange = GetInput(
            "review_threads_auto_resolve_diff_range",
            "REVIEW_THREADS_AUTO_RESOLVE_DIFF_RANGE",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_DIFF_RANGE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveDiffRange)) {
            settings.ReviewThreadsAutoResolveDiffRange =
                NormalizeDiffRange(reviewThreadsAutoResolveDiffRange, settings.ReviewThreadsAutoResolveDiffRange);
        }

        var reviewThreadsAutoResolveMax = GetInput(
            "review_threads_auto_resolve_max",
            "REVIEW_THREADS_AUTO_RESOLVE_MAX",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_MAX");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveMax)) {
            settings.ReviewThreadsAutoResolveMax =
                ParseNonNegativeInt(reviewThreadsAutoResolveMax, settings.ReviewThreadsAutoResolveMax);
        }

        var reviewThreadsAutoResolveAi = GetInput(
            "review_threads_auto_resolve_ai",
            "REVIEW_THREADS_AUTO_RESOLVE_AI",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAi)) {
            settings.ReviewThreadsAutoResolveAI =
                ParseBoolean(reviewThreadsAutoResolveAi, settings.ReviewThreadsAutoResolveAI);
        }

        var reviewThreadsAutoResolveRequireEvidence = GetInput(
            "review_threads_auto_resolve_require_evidence",
            "REVIEW_THREADS_AUTO_RESOLVE_REQUIRE_EVIDENCE",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_REQUIRE_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveRequireEvidence)) {
            settings.ReviewThreadsAutoResolveRequireEvidence =
                ParseBoolean(reviewThreadsAutoResolveRequireEvidence, settings.ReviewThreadsAutoResolveRequireEvidence);
        }

        var reviewThreadsAutoResolveSweepNoBlockers = GetInput(
            "review_threads_auto_resolve_sweep_no_blockers",
            "REVIEW_THREADS_AUTO_RESOLVE_SWEEP_NO_BLOCKERS",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SWEEP_NO_BLOCKERS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveSweepNoBlockers)) {
            settings.ReviewThreadsAutoResolveSweepNoBlockers =
                ParseBoolean(reviewThreadsAutoResolveSweepNoBlockers, settings.ReviewThreadsAutoResolveSweepNoBlockers);
        }

        var reviewThreadsAutoResolveAiPost = GetInput(
            "review_threads_auto_resolve_ai_post_comment",
            "REVIEW_THREADS_AUTO_RESOLVE_AI_POST_COMMENT",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_POST_COMMENT");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiPost)) {
            settings.ReviewThreadsAutoResolveAIPostComment =
                ParseBoolean(reviewThreadsAutoResolveAiPost, settings.ReviewThreadsAutoResolveAIPostComment);
        }

        var reviewThreadsAutoResolveAiEmbed = GetInput(
            "review_threads_auto_resolve_ai_embed",
            "REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiEmbed)) {
            settings.ReviewThreadsAutoResolveAIEmbed =
                ParseBoolean(reviewThreadsAutoResolveAiEmbed, settings.ReviewThreadsAutoResolveAIEmbed);
        }

        var reviewThreadsAutoResolveEmbedPlacement = GetInput(
            "review_threads_auto_resolve_ai_embed_placement",
            "REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED_PLACEMENT",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED_PLACEMENT");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveEmbedPlacement)) {
            settings.ReviewThreadsAutoResolveAIEmbedPlacement =
                NormalizeEmbedPlacement(
                    reviewThreadsAutoResolveEmbedPlacement,
                    settings.ReviewThreadsAutoResolveAIEmbedPlacement);
        }

        var reviewThreadsAutoResolveAiSummary = GetInput(
            "review_threads_auto_resolve_ai_summary",
            "REVIEW_THREADS_AUTO_RESOLVE_AI_SUMMARY",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_SUMMARY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiSummary)) {
            settings.ReviewThreadsAutoResolveAISummary =
                ParseBoolean(reviewThreadsAutoResolveAiSummary, settings.ReviewThreadsAutoResolveAISummary);
        }

        var reviewThreadsAutoResolveSummaryAlways = GetInput(
            "review_threads_auto_resolve_summary_always",
            "REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_ALWAYS",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_ALWAYS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveSummaryAlways)) {
            settings.ReviewThreadsAutoResolveSummaryAlways =
                ParseBoolean(reviewThreadsAutoResolveSummaryAlways, settings.ReviewThreadsAutoResolveSummaryAlways);
        }

        var reviewThreadsAutoResolveAiReply = GetInput(
            "review_threads_auto_resolve_ai_reply",
            "REVIEW_THREADS_AUTO_RESOLVE_AI_REPLY",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_REPLY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiReply)) {
            settings.ReviewThreadsAutoResolveAIReply =
                ParseBoolean(reviewThreadsAutoResolveAiReply, settings.ReviewThreadsAutoResolveAIReply);
        }

        var reviewThreadsAutoResolveSummaryComment = GetInput(
            "review_threads_auto_resolve_summary_comment",
            "REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_COMMENT",
            "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_COMMENT");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveSummaryComment)) {
            settings.ReviewThreadsAutoResolveSummaryComment =
                ParseBoolean(reviewThreadsAutoResolveSummaryComment, settings.ReviewThreadsAutoResolveSummaryComment);
        }

        var contextDenyEnabled = GetInput("context_deny_enabled", "REVIEW_CONTEXT_DENY_ENABLED");
        if (!string.IsNullOrWhiteSpace(contextDenyEnabled)) {
            settings.ContextDenyEnabled = ParseBoolean(contextDenyEnabled, settings.ContextDenyEnabled);
        }

        var contextDenyPatterns = GetInput("context_deny_patterns", "REVIEW_CONTEXT_DENY_PATTERNS");
        if (!string.IsNullOrWhiteSpace(contextDenyPatterns)) {
            settings.ContextDenyPatterns = ParseList(contextDenyPatterns, settings.ContextDenyPatterns);
        }

        var maxCommentChars = GetInput("max_comment_chars", "REVIEW_MAX_COMMENT_CHARS");
        if (!string.IsNullOrWhiteSpace(maxCommentChars)) {
            settings.MaxCommentChars = ParseNonNegativeInt(maxCommentChars, settings.MaxCommentChars);
        }

        var maxComments = GetInput("max_comments", "REVIEW_MAX_COMMENTS");
        if (!string.IsNullOrWhiteSpace(maxComments)) {
            settings.MaxComments = ParseNonNegativeInt(maxComments, settings.MaxComments);
        }

        var commentSearchLimit = GetInput("comment_search_limit", "REVIEW_COMMENT_SEARCH_LIMIT");
        if (!string.IsNullOrWhiteSpace(commentSearchLimit)) {
            settings.CommentSearchLimit = ParsePositiveInt(commentSearchLimit, settings.CommentSearchLimit);
        }

        var githubConcurrency = GetInput("github_max_concurrency", "REVIEW_GITHUB_MAX_CONCURRENCY");
        if (!string.IsNullOrWhiteSpace(githubConcurrency)) {
            settings.GitHubMaxConcurrency = ParsePositiveInt(githubConcurrency, settings.GitHubMaxConcurrency);
        }

        var includeRelatedPrs = GetInput("include_related_prs", "REVIEW_INCLUDE_RELATED_PRS");
        if (!string.IsNullOrWhiteSpace(includeRelatedPrs)) {
            settings.IncludeRelatedPrs = ParseBoolean(includeRelatedPrs, settings.IncludeRelatedPrs);
        }

        var relatedPrsQuery = GetInput("related_prs_query", "REVIEW_RELATED_PRS_QUERY");
        if (!string.IsNullOrWhiteSpace(relatedPrsQuery)) {
            settings.RelatedPrsQuery = relatedPrsQuery;
        }

        var maxRelatedPrs = GetInput("max_related_prs", "REVIEW_MAX_RELATED_PRS");
        if (!string.IsNullOrWhiteSpace(maxRelatedPrs)) {
            settings.MaxRelatedPrs = ParsePositiveInt(maxRelatedPrs, settings.MaxRelatedPrs);
        }

        var cleanupEnabled = GetInput("cleanup_enabled", "REVIEW_CLEANUP_ENABLED");
        if (!string.IsNullOrWhiteSpace(cleanupEnabled)) {
            settings.Cleanup.Enabled = ParseBoolean(cleanupEnabled, settings.Cleanup.Enabled);
        }

        var cleanupMode = GetInput("cleanup_mode", "REVIEW_CLEANUP_MODE");
        if (!string.IsNullOrWhiteSpace(cleanupMode)) {
            settings.Cleanup.Mode = CleanupSettings.ParseMode(cleanupMode, settings.Cleanup.Mode);
        }

        var cleanupScope = GetInput("cleanup_scope", "REVIEW_CLEANUP_SCOPE");
        if (!string.IsNullOrWhiteSpace(cleanupScope)) {
            settings.Cleanup.Scope = cleanupScope!.Trim();
        }

        var cleanupRequireLabel = GetInput("cleanup_require_label", "REVIEW_CLEANUP_REQUIRE_LABEL");
        if (!string.IsNullOrWhiteSpace(cleanupRequireLabel)) {
            settings.Cleanup.RequireLabel = cleanupRequireLabel;
        }

        var cleanupAllowedEdits = GetInput("cleanup_allowed_edits", "REVIEW_CLEANUP_ALLOWED_EDITS");
        if (!string.IsNullOrWhiteSpace(cleanupAllowedEdits)) {
            settings.Cleanup.AllowedEdits = CleanupSettings.NormalizeAllowedEdits(ParseList(cleanupAllowedEdits));
        }

        var cleanupMinConfidence = GetInput("cleanup_min_confidence", "REVIEW_CLEANUP_MIN_CONFIDENCE");
        if (!string.IsNullOrWhiteSpace(cleanupMinConfidence)) {
            settings.Cleanup.MinConfidence =
                CleanupSettings.ParseConfidence(cleanupMinConfidence, settings.Cleanup.MinConfidence);
        }

        var cleanupTemplate = GetInput("cleanup_template", "REVIEW_CLEANUP_TEMPLATE");
        if (!string.IsNullOrWhiteSpace(cleanupTemplate)) {
            settings.Cleanup.Template = cleanupTemplate;
        }

        var cleanupTemplatePath = GetInput("cleanup_template_path", "REVIEW_CLEANUP_TEMPLATE_PATH");
        if (!string.IsNullOrWhiteSpace(cleanupTemplatePath)) {
            settings.Cleanup.TemplatePath = cleanupTemplatePath;
        }

        var cleanupPostEdit = GetInput("cleanup_post_edit_comment", "REVIEW_CLEANUP_POST_EDIT_COMMENT");
        if (!string.IsNullOrWhiteSpace(cleanupPostEdit)) {
            settings.Cleanup.PostEditComment = ParseBoolean(cleanupPostEdit, settings.Cleanup.PostEditComment);
        }
    }
}
