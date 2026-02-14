using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Analysis;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;


namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal static void ApplyEnvironment(ReviewSettings settings) {
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

        var style = GetInput("style", "REVIEW_STYLE");
        if (!string.IsNullOrWhiteSpace(style)) {
            settings.Style = style;
            ReviewStyles.Apply(style!, settings);
        }

        var tone = GetInput("tone", "REVIEW_TONE");
        if (!string.IsNullOrWhiteSpace(tone)) {
            settings.Tone = tone;
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
        var usageBudgetAllowWeekly = GetInput("usage_budget_allow_weekly_limit", "REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT");
        if (!string.IsNullOrWhiteSpace(usageBudgetAllowWeekly)) {
            settings.ReviewUsageBudgetAllowWeeklyLimit =
                ParseBoolean(usageBudgetAllowWeekly, settings.ReviewUsageBudgetAllowWeeklyLimit);
        }
        var structuredFindings = GetInput("structured_findings", "REVIEW_STRUCTURED_FINDINGS");
        if (!string.IsNullOrWhiteSpace(structuredFindings)) {
            settings.StructuredFindings = ParseBoolean(structuredFindings, settings.StructuredFindings);
        }

        var transport = GetInput("openai_transport", "OPENAI_TRANSPORT");
        if (!string.IsNullOrWhiteSpace(transport)) {
            settings.OpenAITransport = ParseTransport(transport);
        }
        var openAiAccountId = GetInput("openai_account_id", "REVIEW_OPENAI_ACCOUNT_ID", "INTELLIGENCEX_OPENAI_ACCOUNT_ID");
        if (!string.IsNullOrWhiteSpace(openAiAccountId)) {
            settings.OpenAiAccountId = openAiAccountId;
        }
        var openAiAccountIds = GetInput("openai_account_ids", "REVIEW_OPENAI_ACCOUNT_IDS", "INTELLIGENCEX_OPENAI_ACCOUNT_IDS");
        if (!string.IsNullOrWhiteSpace(openAiAccountIds)) {
            settings.OpenAiAccountIds = NormalizeAccountIdList(ParseList(openAiAccountIds));
        }
        var openAiAccountRotation = GetInput("openai_account_rotation", "REVIEW_OPENAI_ACCOUNT_ROTATION");
        if (!string.IsNullOrWhiteSpace(openAiAccountRotation)) {
            settings.OpenAiAccountRotation =
                NormalizeOpenAiAccountRotation(openAiAccountRotation, settings.OpenAiAccountRotation);
        }
        var openAiAccountFailover = GetInput("openai_account_failover", "REVIEW_OPENAI_ACCOUNT_FAILOVER");
        if (!string.IsNullOrWhiteSpace(openAiAccountFailover)) {
            settings.OpenAiAccountFailover = ParseBoolean(openAiAccountFailover, settings.OpenAiAccountFailover);
        }

        var overwriteSummary = GetInput("overwrite_summary", "OVERWRITE_SUMMARY");
        if (!string.IsNullOrWhiteSpace(overwriteSummary)) {
            settings.OverwriteSummary = ParseBoolean(overwriteSummary, settings.OverwriteSummary);
        }
        var overwriteSummaryOnNewCommit = GetInput("overwrite_summary_on_new_commit", "OVERWRITE_SUMMARY_ON_NEW_COMMIT");
        if (!string.IsNullOrWhiteSpace(overwriteSummaryOnNewCommit)) {
            settings.OverwriteSummaryOnNewCommit = ParseBoolean(overwriteSummaryOnNewCommit, settings.OverwriteSummaryOnNewCommit);
        }
        var summaryStability = GetInput("summary_stability", "REVIEW_SUMMARY_STABILITY");
        if (!string.IsNullOrWhiteSpace(summaryStability)) {
            settings.SummaryStability = ParseBoolean(summaryStability, settings.SummaryStability);
        }

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
        var policyRulePreviewItems = GetInput("analysis_policy_rule_preview_items",
            "REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS");
        if (!string.IsNullOrWhiteSpace(policyRulePreviewItems) &&
            int.TryParse(policyRulePreviewItems, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPreviewItems)) {
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
            settings.RetryBackoffMultiplier = ParsePositiveDouble(retryBackoffMultiplier, settings.RetryBackoffMultiplier);
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
            settings.RetryExtraOnResponseEnded = ParseBoolean(retryExtraResponseEnded, settings.RetryExtraOnResponseEnded);
        }
        var providerHealthChecks = GetInput("provider_health_checks", "REVIEW_PROVIDER_HEALTH_CHECKS");
        if (!string.IsNullOrWhiteSpace(providerHealthChecks)) {
            settings.ProviderHealthChecks = ParseBoolean(providerHealthChecks, settings.ProviderHealthChecks);
        }
        var providerHealthCheckTimeoutSeconds = GetInput("provider_health_check_timeout_seconds",
            "REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(providerHealthCheckTimeoutSeconds)) {
            settings.ProviderHealthCheckTimeoutSeconds =
                ParsePositiveInt(providerHealthCheckTimeoutSeconds, settings.ProviderHealthCheckTimeoutSeconds);
        }
        var providerCircuitBreakerFailures = GetInput("provider_circuit_breaker_failures",
            "REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES");
        if (!string.IsNullOrWhiteSpace(providerCircuitBreakerFailures)) {
            settings.ProviderCircuitBreakerFailures =
                ParseNonNegativeInt(providerCircuitBreakerFailures, settings.ProviderCircuitBreakerFailures);
        }
        var providerCircuitBreakerOpenSeconds = GetInput("provider_circuit_breaker_open_seconds",
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
            settings.PreflightTimeoutSeconds = ParsePositiveInt(preflightTimeoutSeconds, settings.PreflightTimeoutSeconds);
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
            settings.CopilotAutoInstallPrerelease = ParseBoolean(copilotAutoInstallPrerelease, settings.CopilotAutoInstallPrerelease);
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
        var reviewThreadsIncludeBots = GetInput("review_threads_include_bots", "REVIEW_THREADS_INCLUDE_BOTS", "REVIEW_REVIEW_THREADS_INCLUDE_BOTS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsIncludeBots)) {
            settings.ReviewThreadsIncludeBots = ParseBoolean(reviewThreadsIncludeBots, settings.ReviewThreadsIncludeBots);
        }
        var reviewThreadsIncludeResolved = GetInput("review_threads_include_resolved", "REVIEW_THREADS_INCLUDE_RESOLVED", "REVIEW_REVIEW_THREADS_INCLUDE_RESOLVED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsIncludeResolved)) {
            settings.ReviewThreadsIncludeResolved = ParseBoolean(reviewThreadsIncludeResolved, settings.ReviewThreadsIncludeResolved);
        }
        var reviewThreadsIncludeOutdated = GetInput("review_threads_include_outdated", "REVIEW_THREADS_INCLUDE_OUTDATED", "REVIEW_REVIEW_THREADS_INCLUDE_OUTDATED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsIncludeOutdated)) {
            settings.ReviewThreadsIncludeOutdated = ParseBoolean(reviewThreadsIncludeOutdated, settings.ReviewThreadsIncludeOutdated);
        }
        var reviewThreadsMax = GetInput("review_threads_max", "REVIEW_THREADS_MAX", "REVIEW_REVIEW_THREADS_MAX");
        if (!string.IsNullOrWhiteSpace(reviewThreadsMax)) {
            settings.ReviewThreadsMax = ParseNonNegativeInt(reviewThreadsMax, settings.ReviewThreadsMax);
        }
        var reviewThreadsMaxComments = GetInput("review_threads_max_comments", "REVIEW_THREADS_MAX_COMMENTS", "REVIEW_REVIEW_THREADS_MAX_COMMENTS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsMaxComments)) {
            settings.ReviewThreadsMaxComments = ParseNonNegativeInt(reviewThreadsMaxComments, settings.ReviewThreadsMaxComments);
        }
        var reviewThreadsAutoResolveStale = GetInput("review_threads_auto_resolve_stale", "REVIEW_THREADS_AUTO_RESOLVE_STALE", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_STALE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveStale)) {
            settings.ReviewThreadsAutoResolveStale = ParseBoolean(reviewThreadsAutoResolveStale, settings.ReviewThreadsAutoResolveStale);
        }
        var reviewThreadsAutoResolveMissingInline = GetInput("review_threads_auto_resolve_missing_inline", "REVIEW_THREADS_AUTO_RESOLVE_MISSING_INLINE", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_MISSING_INLINE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveMissingInline)) {
            settings.ReviewThreadsAutoResolveMissingInline = ParseBoolean(reviewThreadsAutoResolveMissingInline, settings.ReviewThreadsAutoResolveMissingInline);
        }
        var reviewThreadsAutoResolveBotsOnly = GetInput("review_threads_auto_resolve_bots_only", "REVIEW_THREADS_AUTO_RESOLVE_BOTS_ONLY", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_BOTS_ONLY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveBotsOnly)) {
            settings.ReviewThreadsAutoResolveBotsOnly = ParseBoolean(reviewThreadsAutoResolveBotsOnly, settings.ReviewThreadsAutoResolveBotsOnly);
        }
        var reviewThreadsAutoResolveBotLogins = GetInput("review_threads_auto_resolve_bot_logins", "REVIEW_THREADS_AUTO_RESOLVE_BOT_LOGINS", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_BOT_LOGINS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveBotLogins)) {
            settings.ReviewThreadsAutoResolveBotLogins = ParseList(reviewThreadsAutoResolveBotLogins, settings.ReviewThreadsAutoResolveBotLogins);
        }
        var reviewThreadsAutoResolveDiffRange = GetInput("review_threads_auto_resolve_diff_range", "REVIEW_THREADS_AUTO_RESOLVE_DIFF_RANGE", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_DIFF_RANGE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveDiffRange)) {
            settings.ReviewThreadsAutoResolveDiffRange = NormalizeDiffRange(reviewThreadsAutoResolveDiffRange, settings.ReviewThreadsAutoResolveDiffRange);
        }
        var reviewThreadsAutoResolveMax = GetInput("review_threads_auto_resolve_max", "REVIEW_THREADS_AUTO_RESOLVE_MAX", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_MAX");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveMax)) {
            settings.ReviewThreadsAutoResolveMax = ParseNonNegativeInt(reviewThreadsAutoResolveMax, settings.ReviewThreadsAutoResolveMax);
        }
        var reviewThreadsAutoResolveAi = GetInput("review_threads_auto_resolve_ai", "REVIEW_THREADS_AUTO_RESOLVE_AI", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAi)) {
            settings.ReviewThreadsAutoResolveAI = ParseBoolean(reviewThreadsAutoResolveAi, settings.ReviewThreadsAutoResolveAI);
        }
        var reviewThreadsAutoResolveRequireEvidence = GetInput("review_threads_auto_resolve_require_evidence", "REVIEW_THREADS_AUTO_RESOLVE_REQUIRE_EVIDENCE", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_REQUIRE_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveRequireEvidence)) {
            settings.ReviewThreadsAutoResolveRequireEvidence = ParseBoolean(reviewThreadsAutoResolveRequireEvidence, settings.ReviewThreadsAutoResolveRequireEvidence);        }
        var reviewThreadsAutoResolveAiPost = GetInput("review_threads_auto_resolve_ai_post_comment", "REVIEW_THREADS_AUTO_RESOLVE_AI_POST_COMMENT", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_POST_COMMENT");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiPost)) {
            settings.ReviewThreadsAutoResolveAIPostComment = ParseBoolean(reviewThreadsAutoResolveAiPost, settings.ReviewThreadsAutoResolveAIPostComment);
        }
        var reviewThreadsAutoResolveAiEmbed = GetInput("review_threads_auto_resolve_ai_embed", "REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiEmbed)) {
            settings.ReviewThreadsAutoResolveAIEmbed = ParseBoolean(reviewThreadsAutoResolveAiEmbed, settings.ReviewThreadsAutoResolveAIEmbed);
        }
        var reviewThreadsAutoResolveEmbedPlacement = GetInput("review_threads_auto_resolve_ai_embed_placement",
            "REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED_PLACEMENT", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED_PLACEMENT");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveEmbedPlacement)) {
            settings.ReviewThreadsAutoResolveAIEmbedPlacement =
                NormalizeEmbedPlacement(reviewThreadsAutoResolveEmbedPlacement, settings.ReviewThreadsAutoResolveAIEmbedPlacement);
        }
        var reviewThreadsAutoResolveAiSummary = GetInput("review_threads_auto_resolve_ai_summary", "REVIEW_THREADS_AUTO_RESOLVE_AI_SUMMARY", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_SUMMARY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiSummary)) {
            settings.ReviewThreadsAutoResolveAISummary = ParseBoolean(reviewThreadsAutoResolveAiSummary, settings.ReviewThreadsAutoResolveAISummary);
        }
        var reviewThreadsAutoResolveSummaryAlways = GetInput("review_threads_auto_resolve_summary_always",
            "REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_ALWAYS", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_ALWAYS");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveSummaryAlways)) {
            settings.ReviewThreadsAutoResolveSummaryAlways =
                ParseBoolean(reviewThreadsAutoResolveSummaryAlways, settings.ReviewThreadsAutoResolveSummaryAlways);
        }
        var reviewThreadsAutoResolveAiReply = GetInput("review_threads_auto_resolve_ai_reply", "REVIEW_THREADS_AUTO_RESOLVE_AI_REPLY", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_REPLY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiReply)) {
            settings.ReviewThreadsAutoResolveAIReply = ParseBoolean(reviewThreadsAutoResolveAiReply, settings.ReviewThreadsAutoResolveAIReply);
        }
        var reviewThreadsAutoResolveSummaryComment = GetInput("review_threads_auto_resolve_summary_comment",
            "REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_COMMENT", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_COMMENT");
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
            settings.Cleanup.MinConfidence = CleanupSettings.ParseConfidence(cleanupMinConfidence, settings.Cleanup.MinConfidence);
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
