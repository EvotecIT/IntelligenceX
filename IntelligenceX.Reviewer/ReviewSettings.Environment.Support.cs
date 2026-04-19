using System;
using System.Globalization;
using System.Linq;
using IntelligenceX.Analysis;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    private static void ApplyEnvironmentCoreReviewSettings(ReviewSettings settings) {
        var profile = GetInput("profile", "REVIEW_PROFILE");
        if (!string.IsNullOrWhiteSpace(profile)) {
            ReviewProfiles.Apply(profile!, settings);
            settings.Profile = profile;
        }

        var codeHost = GetInput("code_host", "REVIEW_CODE_HOST");
        if (!string.IsNullOrWhiteSpace(codeHost)) {
            settings.CodeHost = ParseCodeHost(codeHost);
        }

        var intent = GetInput("intent", "REVIEW_INTENT");
        if (!string.IsNullOrWhiteSpace(intent)) {
            ReviewIntents.Apply(intent!, settings);
            settings.Intent = intent;
        }

        var provider = GetInput("provider", "REVIEW_PROVIDER");
        if (!string.IsNullOrWhiteSpace(provider)) {
            settings.Provider = ParseProvider(provider);
        }

        var providerFallback = GetInput("provider_fallback", "REVIEW_PROVIDER_FALLBACK");
        if (!string.IsNullOrWhiteSpace(providerFallback)) {
            settings.ProviderFallback = ParseProviderNullable(providerFallback);
        }

        var mode = GetInput("mode", "REVIEW_MODE");
        if (!string.IsNullOrWhiteSpace(mode)) {
            settings.Mode = mode!;
        }

        var strictness = GetInput("strictness", "REVIEW_STRICTNESS");
        if (!string.IsNullOrWhiteSpace(strictness)) {
            settings.Strictness = strictness;
        }

        var mergeBlockerSections = GetInput("merge_blocker_sections", "REVIEW_MERGE_BLOCKER_SECTIONS");
        if (!string.IsNullOrWhiteSpace(mergeBlockerSections)) {
            settings.MergeBlockerSections = NormalizeMergeBlockerSections(ParseList(mergeBlockerSections));
        }

        var mergeBlockerRequireAllSections = GetInput(
            "merge_blocker_require_all_sections",
            "REVIEW_MERGE_BLOCKER_REQUIRE_ALL_SECTIONS");
        if (!string.IsNullOrWhiteSpace(mergeBlockerRequireAllSections)) {
            settings.MergeBlockerRequireAllSections =
                ParseBoolean(mergeBlockerRequireAllSections, settings.MergeBlockerRequireAllSections);
        }

        var mergeBlockerRequireSectionMatch = GetInput(
            "merge_blocker_require_section_match",
            "REVIEW_MERGE_BLOCKER_REQUIRE_SECTION_MATCH");
        if (!string.IsNullOrWhiteSpace(mergeBlockerRequireSectionMatch)) {
            settings.MergeBlockerRequireSectionMatch =
                ParseBoolean(mergeBlockerRequireSectionMatch, settings.MergeBlockerRequireSectionMatch);
        }

        var style = GetInput("style", "REVIEW_STYLE");
        if (!string.IsNullOrWhiteSpace(style)) {
            settings.Style = style;
            ReviewStyles.Apply(style!, settings);
        }

        var tone = GetInput("tone", "REVIEW_TONE");
        if (!string.IsNullOrWhiteSpace(tone)) {
            settings.Tone = tone;
        }

        var narrativeMode = GetInput("narrative_mode", "REVIEW_NARRATIVE_MODE");
        if (!string.IsNullOrWhiteSpace(narrativeMode)) {
            settings.NarrativeMode = NormalizeNarrativeMode(narrativeMode, settings.NarrativeMode);
        }

        var focus = GetInput("focus", "REVIEW_FOCUS");
        if (!string.IsNullOrWhiteSpace(focus)) {
            settings.Focus = ParseList(focus);
        }

        var persona = GetInput("persona", "REVIEW_PERSONA");
        if (!string.IsNullOrWhiteSpace(persona)) {
            settings.Persona = persona;
        }

        var outputStyle = GetInput("output_style", "REVIEW_OUTPUT_STYLE");
        if (!string.IsNullOrWhiteSpace(outputStyle)) {
            settings.OutputStyle = outputStyle;
        }

        var notes = GetInput("notes", "REVIEW_NOTES");
        if (!string.IsNullOrWhiteSpace(notes)) {
            settings.Notes = notes;
        }

        var model = GetInput("model", "OPENAI_MODEL");
        if (!string.IsNullOrWhiteSpace(model)) {
            settings.Model = model!;
        }

        var reasoningEffort = GetInput("reasoning_effort", "OPENAI_REASONING_EFFORT");
        if (!string.IsNullOrWhiteSpace(reasoningEffort)) {
            var parsed = ChatEnumParser.ParseReasoningEffort(reasoningEffort);
            if (parsed.HasValue) {
                settings.ReasoningEffort = parsed;
            }
        }

        var reasoningSummary = GetInput("reasoning_summary", "OPENAI_REASONING_SUMMARY");
        if (!string.IsNullOrWhiteSpace(reasoningSummary)) {
            var parsed = ChatEnumParser.ParseReasoningSummary(reasoningSummary);
            if (parsed.HasValue) {
                settings.ReasoningSummary = parsed;
            }
        }
    }

    private static void ApplyEnvironmentUsageAndSummarySettings(ReviewSettings settings) {
        var usageSummary = GetInput("usage_summary", "REVIEW_USAGE_SUMMARY");
        if (!string.IsNullOrWhiteSpace(usageSummary)) {
            settings.ReviewUsageSummary = ParseBoolean(usageSummary, settings.ReviewUsageSummary);
        }

        var usageCacheMinutes = GetInput("usage_summary_cache_minutes", "REVIEW_USAGE_SUMMARY_CACHE_MINUTES");
        if (!string.IsNullOrWhiteSpace(usageCacheMinutes)) {
            settings.ReviewUsageSummaryCacheMinutes =
                ParseNonNegativeInt(usageCacheMinutes, settings.ReviewUsageSummaryCacheMinutes);
        }

        var usageTimeoutSeconds = GetInput("usage_summary_timeout_seconds", "REVIEW_USAGE_SUMMARY_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(usageTimeoutSeconds)) {
            settings.ReviewUsageSummaryTimeoutSeconds =
                Math.Max(1, ParseNonNegativeInt(usageTimeoutSeconds, settings.ReviewUsageSummaryTimeoutSeconds));
        }

        var usageBudgetGuard = GetInput("usage_budget_guard", "REVIEW_USAGE_BUDGET_GUARD");
        if (!string.IsNullOrWhiteSpace(usageBudgetGuard)) {
            settings.ReviewUsageBudgetGuard = ParseBoolean(usageBudgetGuard, settings.ReviewUsageBudgetGuard);
        }

        var usageBudgetAllowCredits = GetInput("usage_budget_allow_credits", "REVIEW_USAGE_BUDGET_ALLOW_CREDITS");
        if (!string.IsNullOrWhiteSpace(usageBudgetAllowCredits)) {
            settings.ReviewUsageBudgetAllowCredits =
                ParseBoolean(usageBudgetAllowCredits, settings.ReviewUsageBudgetAllowCredits);
        }

        var usageBudgetAllowWeekly = GetInput(
            "usage_budget_allow_weekly_limit",
            "REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT");
        if (!string.IsNullOrWhiteSpace(usageBudgetAllowWeekly)) {
            settings.ReviewUsageBudgetAllowWeeklyLimit =
                ParseBoolean(usageBudgetAllowWeekly, settings.ReviewUsageBudgetAllowWeeklyLimit);
        }

        var structuredFindings = GetInput("structured_findings", "REVIEW_STRUCTURED_FINDINGS");
        if (!string.IsNullOrWhiteSpace(structuredFindings)) {
            settings.StructuredFindings = ParseBoolean(structuredFindings, settings.StructuredFindings);
        }

        ApplyOpenAiEnvironment(settings);
        ApplyOpenAiCompatibleEnvironment(settings);
        ApplyAnthropicEnvironment(settings);

        var overwriteSummary = GetInput("overwrite_summary", "OVERWRITE_SUMMARY");
        if (!string.IsNullOrWhiteSpace(overwriteSummary)) {
            settings.OverwriteSummary = ParseBoolean(overwriteSummary, settings.OverwriteSummary);
        }

        var overwriteSummaryOnNewCommit = GetInput("overwrite_summary_on_new_commit", "OVERWRITE_SUMMARY_ON_NEW_COMMIT");
        if (!string.IsNullOrWhiteSpace(overwriteSummaryOnNewCommit)) {
            settings.OverwriteSummaryOnNewCommit =
                ParseBoolean(overwriteSummaryOnNewCommit, settings.OverwriteSummaryOnNewCommit);
        }

        var summaryStability = GetInput("summary_stability", "REVIEW_SUMMARY_STABILITY");
        if (!string.IsNullOrWhiteSpace(summaryStability)) {
            settings.SummaryStability = ParseBoolean(summaryStability, settings.SummaryStability);
        }
    }

    private static void ApplyEnvironmentAgentProfileSettings(ReviewSettings settings) {
        var agentProfile = GetInput("agent_profile", "REVIEW_AGENT_PROFILE", "REVIEW_MODEL_PROFILE");
        if (!string.IsNullOrWhiteSpace(agentProfile)) {
            settings.AgentProfile = agentProfile;
        }
    }

    private static void ApplyEnvironmentScopeAndPromptSettings(ReviewSettings settings) {
        var skipDraft = GetInput("skip_draft", "SKIP_DRAFT");
        if (!string.IsNullOrWhiteSpace(skipDraft)) {
            settings.SkipDraft = ParseBoolean(skipDraft, settings.SkipDraft);
        }

        var skipTitles = GetInput("skip_titles", "SKIP_TITLES");
        if (!string.IsNullOrWhiteSpace(skipTitles)) {
            settings.SkipTitles = ParseList(skipTitles, settings.SkipTitles);
        }

        var skipLabels = GetInput("skip_labels", "SKIP_LABELS");
        if (!string.IsNullOrWhiteSpace(skipLabels)) {
            settings.SkipLabels = ParseList(skipLabels, settings.SkipLabels);
        }

        var skipPaths = GetInput("skip_paths", "SKIP_PATHS");
        if (!string.IsNullOrWhiteSpace(skipPaths)) {
            settings.SkipPaths = ParseList(skipPaths, settings.SkipPaths);
        }

        var skipBinaryFiles = GetInput("skip_binary_files", "SKIP_BINARY_FILES");
        if (!string.IsNullOrWhiteSpace(skipBinaryFiles)) {
            settings.SkipBinaryFiles = ParseBoolean(skipBinaryFiles, settings.SkipBinaryFiles);
        }

        var skipGeneratedFiles = GetInput("skip_generated_files", "SKIP_GENERATED_FILES");
        if (!string.IsNullOrWhiteSpace(skipGeneratedFiles)) {
            settings.SkipGeneratedFiles = ParseBoolean(skipGeneratedFiles, settings.SkipGeneratedFiles);
        }

        var generatedFileGlobs = GetInput("generated_file_globs", "GENERATED_FILE_GLOBS");
        if (!string.IsNullOrWhiteSpace(generatedFileGlobs)) {
            settings.GeneratedFileGlobs = ParseList(generatedFileGlobs, settings.GeneratedFileGlobs);
        }

        var includePaths = GetInput("include_paths", "INCLUDE_PATHS");
        if (!string.IsNullOrWhiteSpace(includePaths)) {
            settings.IncludePaths = ParseList(includePaths, settings.IncludePaths);
        }

        var excludePaths = GetInput("exclude_paths", "EXCLUDE_PATHS");
        if (!string.IsNullOrWhiteSpace(excludePaths)) {
            settings.ExcludePaths = ParseList(excludePaths, settings.ExcludePaths);
        }

        var allowWorkflowChanges = GetInput("allow_workflow_changes", "REVIEW_ALLOW_WORKFLOW_CHANGES");
        if (!string.IsNullOrWhiteSpace(allowWorkflowChanges)) {
            settings.AllowWorkflowChanges = ParseBoolean(allowWorkflowChanges, settings.AllowWorkflowChanges);
        }

        var secretsAudit = GetInput("secrets_audit", "REVIEW_SECRETS_AUDIT");
        if (!string.IsNullOrWhiteSpace(secretsAudit)) {
            settings.SecretsAudit = ParseBoolean(secretsAudit, settings.SecretsAudit);
        }

        var reviewDiffRange = GetInput("review_diff_range", "REVIEW_DIFF_RANGE");
        if (!string.IsNullOrWhiteSpace(reviewDiffRange)) {
            settings.ReviewDiffRange = NormalizeDiffRange(reviewDiffRange, settings.ReviewDiffRange);
        }

        var maxFiles = GetInput("max_files", "OPENAI_MAX_FILES");
        if (!string.IsNullOrWhiteSpace(maxFiles)) {
            settings.MaxFiles = ParseNonNegativeInt(maxFiles, settings.MaxFiles);
        }

        var maxPatchChars = GetInput("max_patch_chars", "OPENAI_MAX_PATCH_CHARS");
        if (!string.IsNullOrWhiteSpace(maxPatchChars)) {
            settings.MaxPatchChars = ParseNonNegativeInt(maxPatchChars, settings.MaxPatchChars);
        }

        var maxInlineComments = GetInput("max_inline_comments", "OPENAI_MAX_INLINE_COMMENTS");
        if (!string.IsNullOrWhiteSpace(maxInlineComments)) {
            settings.MaxInlineComments = ParseNonNegativeInt(maxInlineComments, settings.MaxInlineComments);
        }

        var policyRulePreviewItems = GetInput(
            "analysis_policy_rule_preview_items",
            "REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS");
        if (!string.IsNullOrWhiteSpace(policyRulePreviewItems)
            && int.TryParse(policyRulePreviewItems, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPreviewItems)) {
            settings.Analysis.Results.PolicyRulePreviewItems =
                AnalysisPolicyFormatting.NormalizeRulePreviewItems(parsedPreviewItems);
        }

        var severityThreshold = GetInput("severity_threshold", "REVIEW_SEVERITY_THRESHOLD");
        if (!string.IsNullOrWhiteSpace(severityThreshold)) {
            settings.SeverityThreshold = NormalizeSeverity(severityThreshold);
        }

        var redactPii = GetInput("redact_pii", "REVIEW_REDACT_PII");
        if (!string.IsNullOrWhiteSpace(redactPii)) {
            settings.RedactPii = ParseBoolean(redactPii, settings.RedactPii);
        }

        var redactionPatterns = GetInput("redaction_patterns", "REDACTION_PATTERNS");
        if (!string.IsNullOrWhiteSpace(redactionPatterns)) {
            settings.RedactionPatterns = ParseList(redactionPatterns);
        }

        var redactionReplacement = GetInput("redaction_replacement", "REDACTION_REPLACEMENT");
        if (!string.IsNullOrWhiteSpace(redactionReplacement)) {
            settings.RedactionReplacement = redactionReplacement!;
        }

        var untrustedAllowSecrets = GetInput("untrusted_pr_allow_secrets", "REVIEW_UNTRUSTED_PR_ALLOW_SECRETS");
        if (!string.IsNullOrWhiteSpace(untrustedAllowSecrets)) {
            settings.UntrustedPrAllowSecrets = ParseBoolean(untrustedAllowSecrets, settings.UntrustedPrAllowSecrets);
        }

        var untrustedAllowWrites = GetInput("untrusted_pr_allow_writes", "REVIEW_UNTRUSTED_PR_ALLOW_WRITES");
        if (!string.IsNullOrWhiteSpace(untrustedAllowWrites)) {
            settings.UntrustedPrAllowWrites = ParseBoolean(untrustedAllowWrites, settings.UntrustedPrAllowWrites);
        }

        var promptTemplate = GetInput("prompt_template", "REVIEW_PROMPT_TEMPLATE");
        if (!string.IsNullOrWhiteSpace(promptTemplate)) {
            settings.PromptTemplate = promptTemplate;
        }

        var promptTemplatePath = GetInput("prompt_template_path", "REVIEW_PROMPT_TEMPLATE_PATH");
        if (!string.IsNullOrWhiteSpace(promptTemplatePath)) {
            settings.PromptTemplatePath = promptTemplatePath;
        }

        var summaryTemplate = GetInput("summary_template", "REVIEW_SUMMARY_TEMPLATE");
        if (!string.IsNullOrWhiteSpace(summaryTemplate)) {
            settings.SummaryTemplate = summaryTemplate;
        }

        var summaryTemplatePath = GetInput("summary_template_path", "REVIEW_SUMMARY_TEMPLATE_PATH");
        if (!string.IsNullOrWhiteSpace(summaryTemplatePath)) {
            settings.SummaryTemplatePath = summaryTemplatePath;
        }
    }

    private static void ApplyEnvironmentCiContextSettings(ReviewSettings settings) {
        var enabled = GetInput("ci_context_enabled", "REVIEW_CI_CONTEXT_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabled)) {
            settings.CiContext.Enabled = ParseBoolean(enabled, settings.CiContext.Enabled);
        }

        var includeCheckSummary = GetInput(
            "ci_context_include_check_summary",
            "REVIEW_CI_CONTEXT_INCLUDE_CHECK_SUMMARY");
        if (!string.IsNullOrWhiteSpace(includeCheckSummary)) {
            settings.CiContext.IncludeCheckSummary =
                ParseBoolean(includeCheckSummary, settings.CiContext.IncludeCheckSummary);
        }

        var includeFailedRuns = GetInput(
            "ci_context_include_failed_runs",
            "REVIEW_CI_CONTEXT_INCLUDE_FAILED_RUNS");
        if (!string.IsNullOrWhiteSpace(includeFailedRuns)) {
            settings.CiContext.IncludeFailedRuns =
                ParseBoolean(includeFailedRuns, settings.CiContext.IncludeFailedRuns);
        }

        var includeFailureSnippets = GetInput(
            "ci_context_include_failure_snippets",
            "REVIEW_CI_CONTEXT_INCLUDE_FAILURE_SNIPPETS");
        if (!string.IsNullOrWhiteSpace(includeFailureSnippets)) {
            settings.CiContext.IncludeFailureSnippets =
                NormalizeCiContextFailureSnippets(includeFailureSnippets, settings.CiContext.IncludeFailureSnippets);
        }

        var maxFailedRuns = GetInput("ci_context_max_failed_runs", "REVIEW_CI_CONTEXT_MAX_FAILED_RUNS");
        if (!string.IsNullOrWhiteSpace(maxFailedRuns)) {
            settings.CiContext.MaxFailedRuns = ParseNonNegativeInt(maxFailedRuns, settings.CiContext.MaxFailedRuns);
        }

        var maxSnippetCharsPerRun = GetInput(
            "ci_context_max_snippet_chars_per_run",
            "REVIEW_CI_CONTEXT_MAX_SNIPPET_CHARS_PER_RUN");
        if (!string.IsNullOrWhiteSpace(maxSnippetCharsPerRun)) {
            settings.CiContext.MaxSnippetCharsPerRun =
                ParseNonNegativeInt(maxSnippetCharsPerRun, settings.CiContext.MaxSnippetCharsPerRun);
        }

        var classifyInfraFailures = GetInput(
            "ci_context_classify_infra_failures",
            "REVIEW_CI_CONTEXT_CLASSIFY_INFRA_FAILURES");
        if (!string.IsNullOrWhiteSpace(classifyInfraFailures)) {
            settings.CiContext.ClassifyInfraFailures =
                ParseBoolean(classifyInfraFailures, settings.CiContext.ClassifyInfraFailures);
        }
    }

    private static void ApplyEnvironmentHistorySettings(ReviewSettings settings) {
        var enabled = GetInput("history_enabled", "REVIEW_HISTORY_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabled)) {
            settings.History.Enabled = ParseBoolean(enabled, settings.History.Enabled);
        }

        var includeIxSummaryHistory = GetInput(
            "history_include_ix_summary_history",
            "REVIEW_HISTORY_INCLUDE_IX_SUMMARY_HISTORY");
        if (!string.IsNullOrWhiteSpace(includeIxSummaryHistory)) {
            settings.History.IncludeIxSummaryHistory =
                ParseBoolean(includeIxSummaryHistory, settings.History.IncludeIxSummaryHistory);
        }

        var includeReviewThreads = GetInput(
            "history_include_review_threads",
            "REVIEW_HISTORY_INCLUDE_REVIEW_THREADS");
        if (!string.IsNullOrWhiteSpace(includeReviewThreads)) {
            settings.History.IncludeReviewThreads =
                ParseBoolean(includeReviewThreads, settings.History.IncludeReviewThreads);
        }

        var includeExternalBotSummaries = GetInput(
            "history_include_external_bot_summaries",
            "REVIEW_HISTORY_INCLUDE_EXTERNAL_BOT_SUMMARIES");
        if (!string.IsNullOrWhiteSpace(includeExternalBotSummaries)) {
            settings.History.IncludeExternalBotSummaries =
                ParseBoolean(includeExternalBotSummaries, settings.History.IncludeExternalBotSummaries);
        }

        var externalBotLogins = GetInput("history_external_bot_logins", "REVIEW_HISTORY_EXTERNAL_BOT_LOGINS");
        if (!string.IsNullOrWhiteSpace(externalBotLogins)) {
            settings.History.ExternalBotLogins = ParseList(externalBotLogins, settings.History.ExternalBotLogins);
        }

        var artifacts = GetInput("history_artifacts", "REVIEW_HISTORY_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(artifacts)) {
            settings.History.Artifacts = ParseBoolean(artifacts, settings.History.Artifacts);
        }

        var maxRounds = GetInput("history_max_rounds", "REVIEW_HISTORY_MAX_ROUNDS");
        if (!string.IsNullOrWhiteSpace(maxRounds)) {
            settings.History.MaxRounds = ParseNonNegativeInt(maxRounds, settings.History.MaxRounds);
        }

        var maxItems = GetInput("history_max_items", "REVIEW_HISTORY_MAX_ITEMS");
        if (!string.IsNullOrWhiteSpace(maxItems)) {
            settings.History.MaxItems = ParseNonNegativeInt(maxItems, settings.History.MaxItems);
        }
    }

    private static void ApplyEnvironmentSwarmSettings(ReviewSettings settings) {
        var enabled = GetInput("swarm_enabled", "REVIEW_SWARM_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabled)) {
            settings.Swarm.Enabled = ParseBoolean(enabled, settings.Swarm.Enabled);
        }

        var shadowMode = GetInput("swarm_shadow_mode", "REVIEW_SWARM_SHADOW_MODE");
        if (!string.IsNullOrWhiteSpace(shadowMode)) {
            settings.Swarm.ShadowMode = ParseBoolean(shadowMode, settings.Swarm.ShadowMode);
        }

        var reviewers = GetInput("swarm_reviewers", "REVIEW_SWARM_REVIEWERS");
        if (!string.IsNullOrWhiteSpace(reviewers)) {
            settings.Swarm.ReviewerSettings =
                ParseSwarmReviewerSettingsInput(reviewers, settings.Swarm.ReviewerSettings);
            settings.Swarm.Reviewers = NormalizeSwarmReviewers(
                settings.Swarm.ReviewerSettings.Select(static reviewer => reviewer.Id),
                settings.Swarm.Reviewers);
        }

        var maxParallel = GetInput("swarm_max_parallel", "REVIEW_SWARM_MAX_PARALLEL");
        if (!string.IsNullOrWhiteSpace(maxParallel)) {
            settings.Swarm.MaxParallel = Math.Max(1, ParsePositiveInt(maxParallel, settings.Swarm.MaxParallel));
        }

        var publishSubreviews = GetInput("swarm_publish_subreviews", "REVIEW_SWARM_PUBLISH_SUBREVIEWS");
        if (!string.IsNullOrWhiteSpace(publishSubreviews)) {
            settings.Swarm.PublishSubreviews =
                ParseBoolean(publishSubreviews, settings.Swarm.PublishSubreviews);
        }

        var aggregatorModel = GetInput("swarm_aggregator_model", "REVIEW_SWARM_AGGREGATOR_MODEL");
        if (!string.IsNullOrWhiteSpace(aggregatorModel)) {
            settings.Swarm.AggregatorModel = aggregatorModel;
            settings.Swarm.Aggregator.Model = aggregatorModel.Trim();
        }

        var failOpenOnPartial = GetInput("swarm_fail_open_on_partial", "REVIEW_SWARM_FAIL_OPEN_ON_PARTIAL");
        if (!string.IsNullOrWhiteSpace(failOpenOnPartial)) {
            settings.Swarm.FailOpenOnPartial =
                ParseBoolean(failOpenOnPartial, settings.Swarm.FailOpenOnPartial);
        }

        var metrics = GetInput("swarm_metrics", "REVIEW_SWARM_METRICS");
        if (!string.IsNullOrWhiteSpace(metrics)) {
            settings.Swarm.Metrics = ParseBoolean(metrics, settings.Swarm.Metrics);
        }
    }

    private static void ApplyEnvironmentRetryAndDiagnosticsSettings(ReviewSettings settings) {
        var waitSeconds = GetInput("wait_seconds", "REVIEW_WAIT_SECONDS");
        if (!string.IsNullOrWhiteSpace(waitSeconds)) {
            settings.WaitSeconds = ParsePositiveInt(waitSeconds, settings.WaitSeconds);
        }

        var idleSeconds = GetInput("idle_seconds", "REVIEW_IDLE_SECONDS");
        if (!string.IsNullOrWhiteSpace(idleSeconds)) {
            settings.IdleSeconds = ParsePositiveInt(idleSeconds, settings.IdleSeconds);
        }

        var retryCount = GetInput("retry_count", "REVIEW_RETRY_COUNT");
        if (!string.IsNullOrWhiteSpace(retryCount)) {
            settings.RetryCount = ParsePositiveInt(retryCount, settings.RetryCount);
        }

        var retryDelaySeconds = GetInput("retry_delay_seconds", "REVIEW_RETRY_DELAY_SECONDS");
        if (!string.IsNullOrWhiteSpace(retryDelaySeconds)) {
            settings.RetryDelaySeconds = ParsePositiveInt(retryDelaySeconds, settings.RetryDelaySeconds);
        }

        var retryMaxDelaySeconds = GetInput("retry_max_delay_seconds", "REVIEW_RETRY_MAX_DELAY_SECONDS");
        if (!string.IsNullOrWhiteSpace(retryMaxDelaySeconds)) {
            settings.RetryMaxDelaySeconds = ParsePositiveInt(retryMaxDelaySeconds, settings.RetryMaxDelaySeconds);
        }

        var retryBackoffMultiplier = GetInput("retry_backoff_multiplier", "REVIEW_RETRY_BACKOFF_MULTIPLIER");
        if (!string.IsNullOrWhiteSpace(retryBackoffMultiplier)) {
            settings.RetryBackoffMultiplier =
                ParsePositiveDouble(retryBackoffMultiplier, settings.RetryBackoffMultiplier);
        }

        var retryJitterMinMs = GetInput("retry_jitter_min_ms", "REVIEW_RETRY_JITTER_MIN_MS");
        if (!string.IsNullOrWhiteSpace(retryJitterMinMs)) {
            settings.RetryJitterMinMs = ParseNonNegativeInt(retryJitterMinMs, settings.RetryJitterMinMs);
        }

        var retryJitterMaxMs = GetInput("retry_jitter_max_ms", "REVIEW_RETRY_JITTER_MAX_MS");
        if (!string.IsNullOrWhiteSpace(retryJitterMaxMs)) {
            settings.RetryJitterMaxMs = ParseNonNegativeInt(retryJitterMaxMs, settings.RetryJitterMaxMs);
        }

        var retryExtraResponseEnded = GetInput("retry_extra_response_ended", "REVIEW_RETRY_EXTRA_RESPONSE_ENDED");
        if (!string.IsNullOrWhiteSpace(retryExtraResponseEnded)) {
            settings.RetryExtraOnResponseEnded =
                ParseBoolean(retryExtraResponseEnded, settings.RetryExtraOnResponseEnded);
        }

        var providerHealthChecks = GetInput("provider_health_checks", "REVIEW_PROVIDER_HEALTH_CHECKS");
        if (!string.IsNullOrWhiteSpace(providerHealthChecks)) {
            settings.ProviderHealthChecks = ParseBoolean(providerHealthChecks, settings.ProviderHealthChecks);
        }

        var providerHealthCheckTimeoutSeconds = GetInput(
            "provider_health_check_timeout_seconds",
            "REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(providerHealthCheckTimeoutSeconds)) {
            settings.ProviderHealthCheckTimeoutSeconds =
                ParsePositiveInt(providerHealthCheckTimeoutSeconds, settings.ProviderHealthCheckTimeoutSeconds);
        }

        var providerCircuitBreakerFailures = GetInput(
            "provider_circuit_breaker_failures",
            "REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES");
        if (!string.IsNullOrWhiteSpace(providerCircuitBreakerFailures)) {
            settings.ProviderCircuitBreakerFailures =
                ParseNonNegativeInt(providerCircuitBreakerFailures, settings.ProviderCircuitBreakerFailures);
        }

        var providerCircuitBreakerOpenSeconds = GetInput(
            "provider_circuit_breaker_open_seconds",
            "REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS");
        if (!string.IsNullOrWhiteSpace(providerCircuitBreakerOpenSeconds)) {
            settings.ProviderCircuitBreakerOpenSeconds =
                ParsePositiveInt(providerCircuitBreakerOpenSeconds, settings.ProviderCircuitBreakerOpenSeconds);
        }

        var failOpen = GetInput("fail_open", "REVIEW_FAIL_OPEN");
        if (!string.IsNullOrWhiteSpace(failOpen)) {
            settings.FailOpen = ParseBoolean(failOpen, settings.FailOpen);
        }

        var failOpenTransientOnly = GetInput("fail_open_transient_only", "REVIEW_FAIL_OPEN_TRANSIENT_ONLY");
        if (!string.IsNullOrWhiteSpace(failOpenTransientOnly)) {
            settings.FailOpenTransientOnly = ParseBoolean(failOpenTransientOnly, settings.FailOpenTransientOnly);
        }

        var diagnostics = GetInput("diagnostics", "REVIEW_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(diagnostics)) {
            diagnostics = Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_DIAGNOSTICS");
        }
        if (!string.IsNullOrWhiteSpace(diagnostics)) {
            settings.Diagnostics = ParseBoolean(diagnostics, settings.Diagnostics);
        }

        var preflight = GetInput("preflight", "REVIEW_PREFLIGHT");
        if (string.IsNullOrWhiteSpace(preflight)) {
            preflight = Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_PREFLIGHT");
        }
        if (!string.IsNullOrWhiteSpace(preflight)) {
            settings.Preflight = ParseBoolean(preflight, settings.Preflight);
        }

        var preflightTimeoutSeconds = GetInput("preflight_timeout_seconds", "REVIEW_PREFLIGHT_TIMEOUT_SECONDS");
        if (string.IsNullOrWhiteSpace(preflightTimeoutSeconds)) {
            preflightTimeoutSeconds = Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_PREFLIGHT_TIMEOUT_SECONDS");
        }
        if (!string.IsNullOrWhiteSpace(preflightTimeoutSeconds)) {
            settings.PreflightTimeoutSeconds =
                ParsePositiveInt(preflightTimeoutSeconds, settings.PreflightTimeoutSeconds);
        }

        var progressUpdates = GetInput("progress_updates", "REVIEW_PROGRESS_UPDATES");
        if (!string.IsNullOrWhiteSpace(progressUpdates)) {
            settings.ProgressUpdates = ParseBoolean(progressUpdates, settings.ProgressUpdates);
        }

        var progressUpdateSeconds = GetInput("progress_update_seconds", "REVIEW_PROGRESS_UPDATE_SECONDS");
        if (!string.IsNullOrWhiteSpace(progressUpdateSeconds)) {
            settings.ProgressUpdateSeconds = ParsePositiveInt(progressUpdateSeconds, settings.ProgressUpdateSeconds);
        }

        var progressPreviewChars = GetInput("progress_preview_chars", "REVIEW_PROGRESS_PREVIEW_CHARS");
        if (!string.IsNullOrWhiteSpace(progressPreviewChars)) {
            settings.ProgressPreviewChars = ParsePositiveInt(progressPreviewChars, settings.ProgressPreviewChars);
        }

        var codexPath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH");
        if (!string.IsNullOrWhiteSpace(codexPath)) {
            settings.CodexPath = codexPath;
        }

        var codexArgs = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS");
        if (!string.IsNullOrWhiteSpace(codexArgs)) {
            settings.CodexArgs = codexArgs;
        }

        var codexCwd = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_CWD");
        if (!string.IsNullOrWhiteSpace(codexCwd)) {
            settings.CodexWorkingDirectory = codexCwd;
        }
    }

}
