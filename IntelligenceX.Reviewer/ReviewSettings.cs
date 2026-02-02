using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal enum ReviewLength {
    Short,
    Medium,
    Long
}

internal enum ReviewCommentMode {
    Sticky,
    Fresh
}

internal enum ReviewProvider {
    OpenAI,
    Copilot
}

internal sealed class ReviewSettings {
    private static readonly IReadOnlyList<string> DefaultContextDenyPatterns = new[] {
        "\\bpoem(s)?\\b",
        "\\bpoetry\\b",
        "\\bhaiku\\b",
        "\\blyrics\\b",
        "\\bsong\\b",
        "\\bjoke\\b",
        "\\blife advice\\b",
        "\\brelationship\\b",
        "\\bdating\\b",
        "\\bmedical\\b",
        "\\bdiagnos(e|is)\\b",
        "\\btherapy\\b",
        "\\blegal\\b",
        "\\blawsuit\\b",
        "\\bfinancial\\b",
        "\\binvestment\\b",
        "\\btax\\b"
    };
    private static readonly IReadOnlyList<string> DefaultReviewThreadsBotLogins = new[] {
        "intelligencex-review",
        "copilot-pull-request-reviewer"
    };

    public string Mode { get; set; } = "hybrid";
    public ReviewProvider Provider { get; set; } = ReviewProvider.OpenAI;
    public string? Profile { get; set; }
    public string? Strictness { get; set; }
    public string? Tone { get; set; }
    public string? Style { get; set; }
    public string? OutputStyle { get; set; }
    public IReadOnlyList<string> Focus { get; set; } = Array.Empty<string>();
    public string? Persona { get; set; }
    public string? Notes { get; set; }
    public string Model { get; set; } = "gpt-5.2-codex";
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public bool ReviewUsageSummary { get; set; }
    public int ReviewUsageSummaryCacheMinutes { get; set; } = 10;
    public int ReviewUsageSummaryTimeoutSeconds { get; set; } = 10;
    public OpenAITransportKind OpenAITransport { get; set; } = OpenAITransportKind.AppServer;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int RetryMaxDelaySeconds { get; set; } = 30;
    public double RetryBackoffMultiplier { get; set; } = 2.0;
    public int RetryJitterMinMs { get; set; } = 200;
    public int RetryJitterMaxMs { get; set; } = 800;
    public bool RetryExtraOnResponseEnded { get; set; } = true;
    public bool FailOpen { get; set; } = true;
    public bool Diagnostics { get; set; }
    public bool Preflight { get; set; }
    public int PreflightTimeoutSeconds { get; set; } = 15;
    public ReviewLength Length { get; set; } = ReviewLength.Long;
    public bool IncludeNextSteps { get; set; } = true;
    public string? PromptTemplate { get; set; }
    public string? PromptTemplatePath { get; set; }
    public string? SummaryTemplate { get; set; }
    public string? SummaryTemplatePath { get; set; }
    public bool OverwriteSummary { get; set; } = true;
    public bool OverwriteSummaryOnNewCommit { get; set; } = true;
    public bool SkipDraft { get; set; } = true;
    public IReadOnlyList<string> SkipTitles { get; set; } = new[] { "[WIP]", "[skip-review]" };
    public IReadOnlyList<string> SkipLabels { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SkipPaths { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Controls which diff range is used to build the review context.
    /// <list type="bullet">
    /// <item><description><c>current</c>: use the current PR files.</description></item>
    /// <item><description><c>pr-base</c>: compare the PR base to head.</description></item>
    /// <item><description><c>first-review</c>: compare the oldest review summary commit to head, falling back to PR base.</description></item>
    /// </list>
    /// </summary>
    public string ReviewDiffRange { get; set; } = "current";
    public int MaxFiles { get; set; } = 20;
    public int MaxPatchChars { get; set; } = 4000;
    public int MaxInlineComments { get; set; } = 10;
    public string? SeverityThreshold { get; set; }
    public bool RedactPii { get; set; }
    public IReadOnlyList<string> RedactionPatterns { get; set; } = Array.Empty<string>();
    public string RedactionReplacement { get; set; } = "[REDACTED]";
    public int WaitSeconds { get; set; } = 60;
    public int IdleSeconds { get; set; } = 5;
    public bool ProgressUpdates { get; set; } = true;
    public int ProgressUpdateSeconds { get; set; } = 30;
    public int ProgressPreviewChars { get; set; } = 4000;
    public ReviewCommentMode CommentMode { get; set; } = ReviewCommentMode.Sticky;
    public CleanupSettings Cleanup { get; } = new CleanupSettings();

    public bool IncludeIssueComments { get; set; }
    public bool IncludeReviewComments { get; set; }
    public bool IncludeReviewThreads { get; set; }
    public bool ReviewThreadsIncludeBots { get; set; }
    public bool ReviewThreadsIncludeResolved { get; set; }
    public bool ReviewThreadsIncludeOutdated { get; set; } = true;
    public int ReviewThreadsMax { get; set; } = 10;
    public int ReviewThreadsMaxComments { get; set; } = 3;
    public bool ReviewThreadsAutoResolveStale { get; set; }
    public bool ReviewThreadsAutoResolveMissingInline { get; set; }
    public bool ReviewThreadsAutoResolveBotsOnly { get; set; } = true;
    public IReadOnlyList<string> ReviewThreadsAutoResolveBotLogins { get; set; } = DefaultReviewThreadsBotLogins;
    /// <summary>
    /// Controls which diff range is used when assessing review threads for auto-resolve.
    /// Uses the same values as <see cref="ReviewDiffRange"/>.
    /// </summary>
    public string ReviewThreadsAutoResolveDiffRange { get; set; } = "current";
    public int ReviewThreadsAutoResolveMax { get; set; } = 10;
    public bool ReviewThreadsAutoResolveAI { get; set; } = true;
    public bool ReviewThreadsAutoResolveAIPostComment { get; set; }
    public bool ReviewThreadsAutoResolveAIEmbed { get; set; } = true;
    public bool ReviewThreadsAutoResolveAISummary { get; set; } = true;
    public bool ReviewThreadsAutoResolveAIReply { get; set; }
    public int MaxCommentChars { get; set; } = 4000;
    public int MaxComments { get; set; } = 20;
    public int CommentSearchLimit { get; set; } = 500;
    public bool ContextDenyEnabled { get; set; } = true;
    public IReadOnlyList<string> ContextDenyPatterns { get; set; } = DefaultContextDenyPatterns;
    public bool IncludeRelatedPrs { get; set; }
    public string? RelatedPrsQuery { get; set; }
    public int MaxRelatedPrs { get; set; } = 5;

    public string? CodexPath { get; set; }
    public string? CodexArgs { get; set; }
    public string? CodexWorkingDirectory { get; set; }

    public string? CopilotCliPath { get; set; }
    public string? CopilotCliUrl { get; set; }
    public string? CopilotWorkingDirectory { get; set; }
    public bool CopilotAutoInstall { get; set; }
    public string? CopilotAutoInstallMethod { get; set; }
    public bool CopilotAutoInstallPrerelease { get; set; }

    public static ReviewSettings Load() {
        var settings = new ReviewSettings();
        ReviewConfigLoader.Apply(settings);
        ApplyEnvironment(settings);
        return settings;
    }

    public static ReviewSettings FromEnvironment() {
        var settings = new ReviewSettings();
        ApplyEnvironment(settings);
        return settings;
    }

    internal static void ApplyEnvironment(ReviewSettings settings) {
        var profile = GetInput("profile", "REVIEW_PROFILE");
        if (!string.IsNullOrWhiteSpace(profile)) {
            ReviewProfiles.Apply(profile!, settings);
            settings.Profile = profile;
        }

        var provider = GetInput("provider", "REVIEW_PROVIDER");
        if (!string.IsNullOrWhiteSpace(provider)) {
            settings.Provider = ParseProvider(provider);
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

        var transport = GetInput("openai_transport", "OPENAI_TRANSPORT");
        if (!string.IsNullOrWhiteSpace(transport)) {
            settings.OpenAITransport = ParseTransport(transport);
        }

        var overwriteSummary = GetInput("overwrite_summary", "OVERWRITE_SUMMARY");
        if (!string.IsNullOrWhiteSpace(overwriteSummary)) {
            settings.OverwriteSummary = ParseBoolean(overwriteSummary, settings.OverwriteSummary);
        }
        var overwriteSummaryOnNewCommit = GetInput("overwrite_summary_on_new_commit", "OVERWRITE_SUMMARY_ON_NEW_COMMIT");
        if (!string.IsNullOrWhiteSpace(overwriteSummaryOnNewCommit)) {
            settings.OverwriteSummaryOnNewCommit = ParseBoolean(overwriteSummaryOnNewCommit, settings.OverwriteSummaryOnNewCommit);
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

        var reviewDiffRange = GetInput("review_diff_range", "REVIEW_DIFF_RANGE");
        if (!string.IsNullOrWhiteSpace(reviewDiffRange)) {
            settings.ReviewDiffRange = NormalizeDiffRange(reviewDiffRange, settings.ReviewDiffRange);
        }

        var maxFiles = GetInput("max_files", "OPENAI_MAX_FILES");
        if (!string.IsNullOrWhiteSpace(maxFiles)) {
            settings.MaxFiles = ParsePositiveInt(maxFiles, settings.MaxFiles);
        }

        var maxPatchChars = GetInput("max_patch_chars", "OPENAI_MAX_PATCH_CHARS");
        if (!string.IsNullOrWhiteSpace(maxPatchChars)) {
            settings.MaxPatchChars = ParsePositiveInt(maxPatchChars, settings.MaxPatchChars);
        }

        var maxInlineComments = GetInput("max_inline_comments", "OPENAI_MAX_INLINE_COMMENTS");
        if (!string.IsNullOrWhiteSpace(maxInlineComments)) {
            settings.MaxInlineComments = ParsePositiveInt(maxInlineComments, settings.MaxInlineComments);
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
        var failOpen = GetInput("fail_open", "REVIEW_FAIL_OPEN");
        if (!string.IsNullOrWhiteSpace(failOpen)) {
            settings.FailOpen = ParseBoolean(failOpen, settings.FailOpen);
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
        var reviewThreadsAutoResolveAiPost = GetInput("review_threads_auto_resolve_ai_post_comment", "REVIEW_THREADS_AUTO_RESOLVE_AI_POST_COMMENT", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_POST_COMMENT");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiPost)) {
            settings.ReviewThreadsAutoResolveAIPostComment = ParseBoolean(reviewThreadsAutoResolveAiPost, settings.ReviewThreadsAutoResolveAIPostComment);
        }
        var reviewThreadsAutoResolveAiEmbed = GetInput("review_threads_auto_resolve_ai_embed", "REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_EMBED");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiEmbed)) {
            settings.ReviewThreadsAutoResolveAIEmbed = ParseBoolean(reviewThreadsAutoResolveAiEmbed, settings.ReviewThreadsAutoResolveAIEmbed);
        }
        var reviewThreadsAutoResolveAiSummary = GetInput("review_threads_auto_resolve_ai_summary", "REVIEW_THREADS_AUTO_RESOLVE_AI_SUMMARY", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_SUMMARY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiSummary)) {
            settings.ReviewThreadsAutoResolveAISummary = ParseBoolean(reviewThreadsAutoResolveAiSummary, settings.ReviewThreadsAutoResolveAISummary);
        }
        var reviewThreadsAutoResolveAiReply = GetInput("review_threads_auto_resolve_ai_reply", "REVIEW_THREADS_AUTO_RESOLVE_AI_REPLY", "REVIEW_REVIEW_THREADS_AUTO_RESOLVE_AI_REPLY");
        if (!string.IsNullOrWhiteSpace(reviewThreadsAutoResolveAiReply)) {
            settings.ReviewThreadsAutoResolveAIReply = ParseBoolean(reviewThreadsAutoResolveAiReply, settings.ReviewThreadsAutoResolveAIReply);
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
            settings.MaxCommentChars = ParsePositiveInt(maxCommentChars, settings.MaxCommentChars);
        }
        var maxComments = GetInput("max_comments", "REVIEW_MAX_COMMENTS");
        if (!string.IsNullOrWhiteSpace(maxComments)) {
            settings.MaxComments = ParsePositiveInt(maxComments, settings.MaxComments);
        }
        var commentSearchLimit = GetInput("comment_search_limit", "REVIEW_COMMENT_SEARCH_LIMIT");
        if (!string.IsNullOrWhiteSpace(commentSearchLimit)) {
            settings.CommentSearchLimit = ParsePositiveInt(commentSearchLimit, settings.CommentSearchLimit);
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

    private static ReviewProvider ParseProvider(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "copilot" => ReviewProvider.Copilot,
            "openai" or "codex" => ReviewProvider.OpenAI,
            _ => ReviewProvider.OpenAI
        };
    }

    private static OpenAITransportKind ParseTransport(string value) {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => OpenAITransportKind.AppServer
        };
    }

    private static string? GetInput(string inputName, string? envName = null, string? altEnvName = null) {
        var value = Environment.GetEnvironmentVariable($"INPUT_{inputName.ToUpperInvariant()}");
        if (!string.IsNullOrWhiteSpace(value)) {
            return value.Trim();
        }
        if (!string.IsNullOrWhiteSpace(envName)) {
            value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }
        if (!string.IsNullOrWhiteSpace(altEnvName)) {
            value = Environment.GetEnvironmentVariable(altEnvName);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ParseList(string? value, IReadOnlyList<string>? fallback = null) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback ?? Array.Empty<string>();
        }
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? fallback ?? Array.Empty<string>() : parts;
    }

    internal static string NormalizeDiffRange(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "current" or "pr" or "pr-files" or "pr_files" => "current",
            "pr-base" or "pr_base" or "base" or "prbase" => "pr-base",
            "first-review" or "first_review" or "first-reviewed" or "firstreview" or "first" => "first-review",
            _ => fallback
        };
    }

    private static bool ParseBoolean(string? value, bool fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => fallback
        };
    }

    private static int ParsePositiveInt(string? value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) {
            return parsed;
        }
        return fallback;
    }

    private static int ParseNonNegativeInt(string? value, int fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0) {
            return parsed;
        }
        return fallback;
    }

    private static double ParsePositiveDouble(string? value, double fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1) {
            return parsed;
        }
        return fallback;
    }

    private static string? NormalizeSeverity(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "low" or "medium" or "high" or "critical" => normalized,
            _ => null
        };
    }
}
