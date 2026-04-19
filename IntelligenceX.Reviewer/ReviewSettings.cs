using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Analysis;
using IntelligenceX.Copilot;
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

internal enum ReviewNarrativeMode {
    Structured,
    Freedom
}

internal enum ReviewCodeHost {
    GitHub,
    AzureDevOps
}

internal enum AzureDevOpsAuthScheme {
    Basic,
    Bearer
}

internal enum ReviewProvider {
    OpenAI,
    Copilot,
    OpenAICompatible,
    Claude
}

internal sealed class ReviewCiContextSettings {
    public bool Enabled { get; set; }
    public bool IncludeCheckSummary { get; set; } = true;
    public bool IncludeFailedRuns { get; set; } = true;
    public string IncludeFailureSnippets { get; set; } = "off";
    public int MaxFailedRuns { get; set; } = 3;
    public int MaxSnippetCharsPerRun { get; set; } = 4000;
    public bool ClassifyInfraFailures { get; set; } = true;
}

internal sealed class ReviewHistorySettings {
    private static readonly IReadOnlyList<string> DefaultExternalBotLogins = new[] {
        "claude",
        "claude[bot]",
        "copilot-pull-request-reviewer"
    };

    public bool Enabled { get; set; }
    public bool IncludeIxSummaryHistory { get; set; } = true;
    public bool IncludeReviewThreads { get; set; } = true;
    public bool IncludeExternalBotSummaries { get; set; }
    public IReadOnlyList<string> ExternalBotLogins { get; set; } = DefaultExternalBotLogins;
    public bool Artifacts { get; set; }
    public int MaxRounds { get; set; } = 6;
    public int MaxItems { get; set; } = 8;
}

internal sealed class ReviewSwarmReviewerSettings {
    public string Id { get; set; } = string.Empty;
    public string? AgentProfile { get; set; }
    public ReviewProvider? Provider { get; set; }
    public string? Model { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
}

internal sealed class ReviewSwarmAggregatorSettings {
    public string? AgentProfile { get; set; }
    public ReviewProvider? Provider { get; set; }
    public string? Model { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
}

internal sealed class ReviewSwarmSettings {
    public bool Enabled { get; set; }
    public bool ShadowMode { get; set; }
    public IReadOnlyList<string> Reviewers { get; set; } = new[] { "correctness", "security", "reliability", "tests" };
    public IReadOnlyList<ReviewSwarmReviewerSettings> ReviewerSettings { get; set; } = new[] {
        new ReviewSwarmReviewerSettings { Id = "correctness" },
        new ReviewSwarmReviewerSettings { Id = "security" },
        new ReviewSwarmReviewerSettings { Id = "reliability" },
        new ReviewSwarmReviewerSettings { Id = "tests" }
    };
    public int MaxParallel { get; set; } = 4;
    public bool PublishSubreviews { get; set; }
    public ReviewSwarmAggregatorSettings Aggregator { get; } = new();
    public string? AggregatorModel { get; set; }
    public bool FailOpenOnPartial { get; set; } = true;
    public bool Metrics { get; set; } = true;
}

internal sealed partial class ReviewSettings {
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
        "copilot-pull-request-reviewer",
        "chatgpt-codex-connector",
        "github-actions"
    };
    private static readonly IReadOnlyList<string> DefaultRedactionPatterns = new[] {
        "-----BEGIN [A-Z ]*PRIVATE KEY-----[\\s\\S]+?-----END [A-Z ]*PRIVATE KEY-----",
        "\\b(AKIA|ASIA)[0-9A-Z]{16}\\b",
        "\\bgh[pousr]_[A-Za-z0-9]{36}\\b",
        "\\bgithub_pat_[A-Za-z0-9_]{20,}\\b",
        "\\bxox[baprs]-[A-Za-z0-9-]+\\b",
        "\\beyJ[a-zA-Z0-9_-]{10,}\\.[a-zA-Z0-9_-]{10,}\\.[a-zA-Z0-9_-]{10,}\\b",
        "(?i)authorization\\s*:\\s*(bearer|basic)\\s+[^\\s]+",
        "(?i)\\b(api[_-]?key|secret|token|password|passwd|pwd)\\b\\s*[:=]\\s*['\\\"]?[^\\s'\\\"]+"
    };
    private static readonly IReadOnlyList<string> DefaultMergeBlockerSections = new[] {
        "todo list",
        "critical issues"
    };
    private static readonly IReadOnlyList<string> CompactMergeBlockerSections = new[] {
        "todo list"
    };

    public string Mode { get; set; } = "hybrid";
    public ReviewProvider Provider { get; set; } = ReviewProvider.OpenAI;
    public ReviewProvider? ProviderFallback { get; set; }
    public ReviewCodeHost CodeHost { get; set; } = ReviewCodeHost.GitHub;
    public string? Profile { get; set; }
    public string? AgentProfile { get; set; }
    public IReadOnlyDictionary<string, ReviewAgentProfileSettings> AgentProfiles { get; set; } =
        new Dictionary<string, ReviewAgentProfileSettings>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Optional high-level review intent preset (e.g., security, performance, maintainability).
    /// </summary>
    public string? Intent { get; set; }
    public string? Strictness { get; set; }
    /// <summary>
    /// Optional section names used to detect merge blockers in generated review markdown.
    /// Matching is case-insensitive and based on header contains.
    /// </summary>
    public IReadOnlyList<string> MergeBlockerSections { get; set; } = Array.Empty<string>();
    /// <summary>
    /// When true, every configured merge-blocker section must be present in the review markdown.
    /// Missing required sections are treated as blocked for safety.
    /// </summary>
    public bool MergeBlockerRequireAllSections { get; set; } = true;
    /// <summary>
    /// When true, at least one configured merge-blocker section must be present.
    /// If no configured sections are found, the review is treated as blocked.
    /// </summary>
    public bool MergeBlockerRequireSectionMatch { get; set; } = true;
    public string? Tone { get; set; }
    public string? Style { get; set; }
    public string? OutputStyle { get; set; }
    public ReviewNarrativeMode NarrativeMode { get; set; } = ReviewNarrativeMode.Structured;
    public IReadOnlyList<string> Focus { get; set; } = Array.Empty<string>();
    public string? Persona { get; set; }
    public string? Notes { get; set; }
    public string Model { get; set; } = OpenAIModelCatalog.DefaultModel;
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public bool ReviewUsageSummary { get; set; }
    public int ReviewUsageSummaryCacheMinutes { get; set; } = 10;
    public int ReviewUsageSummaryTimeoutSeconds { get; set; } = 10;
    /// <summary>
    /// Enables an early fail-fast guard when configured usage budget sources are exhausted.
    /// </summary>
    public bool ReviewUsageBudgetGuard { get; set; } = true;
    /// <summary>
    /// Allows reviewer runs to proceed when ChatGPT credits are available.
    /// </summary>
    public bool ReviewUsageBudgetAllowCredits { get; set; } = true;
    /// <summary>
    /// Allows reviewer runs to proceed when weekly limit capacity is available.
    /// </summary>
    public bool ReviewUsageBudgetAllowWeeklyLimit { get; set; } = true;
    public bool StructuredFindings { get; set; }
    public OpenAITransportKind OpenAITransport { get; set; } = OpenAITransportKind.AppServer;
    /// <summary>
    /// Base URL for an OpenAI-compatible HTTP endpoint (for example a local gateway or other provider).
    /// When <see cref="Provider"/> is <see cref="ReviewProvider.OpenAICompatible"/>, this value must be set.
    /// </summary>
    public string? OpenAICompatibleBaseUrl { get; set; }
    /// <summary>
    /// Environment variable name holding the OpenAI-compatible API key.
    /// When set, this is preferred over <see cref="OpenAICompatibleApiKey"/>.
    /// </summary>
    public string? OpenAICompatibleApiKeyEnv { get; set; }
    /// <summary>
    /// API key for the OpenAI-compatible HTTP endpoint (not recommended; prefer env).
    /// </summary>
    public string? OpenAICompatibleApiKey { get; set; }
    /// <summary>
    /// Timeout for OpenAI-compatible HTTP requests.
    /// </summary>
    public int OpenAICompatibleTimeoutSeconds { get; set; } = 60;
    /// <summary>
    /// When true, allows non-loopback <c>http://</c> base URLs for the OpenAI-compatible provider.
    /// This is not recommended because the provider sends a bearer API key on every request.
    /// </summary>
    public bool OpenAICompatibleAllowInsecureHttp { get; set; }
    /// <summary>
    /// When true, allows non-loopback http:// base URLs for the OpenAI-compatible provider, but only when
    /// <see cref="OpenAICompatibleAllowInsecureHttp"/> is also enabled. This is strongly discouraged because
    /// bearer tokens may be sent in plaintext.
    /// </summary>
    public bool OpenAICompatibleAllowInsecureHttpNonLoopback { get; set; }
    /// <summary>
    /// When true, drop the Authorization header when following redirects for the OpenAI-compatible provider.
    /// Defaults to false since cross-host redirects are blocked; keeping auth enables common reverse-proxy setups.
    /// </summary>
    public bool OpenAICompatibleDropAuthorizationOnRedirect { get; set; }
    /// <summary>
    /// Optional ChatGPT account id to use when multiple OpenAI bundles are present in the auth store.
    /// </summary>
    public string? OpenAiAccountId { get; set; }
    /// <summary>
    /// Optional ordered list of ChatGPT account ids used for reviewer account rotation/failover.
    /// </summary>
    public IReadOnlyList<string> OpenAiAccountIds { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Anthropic Messages API base URL for native Claude review runs.
    /// </summary>
    public string AnthropicBaseUrl { get; set; } = "https://api.anthropic.com";
    /// <summary>
    /// Anthropic API version header sent with native Claude review runs.
    /// </summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
    /// <summary>
    /// Environment variable name holding the Anthropic API key.
    /// </summary>
    public string? AnthropicApiKeyEnv { get; set; }
    /// <summary>
    /// Anthropic API key value for native Claude review runs (prefer env).
    /// </summary>
    public string? AnthropicApiKey { get; set; }
    /// <summary>
    /// Timeout for Anthropic Messages API requests.
    /// </summary>
    public int AnthropicTimeoutSeconds { get; set; } = 60;
    /// <summary>
    /// Max tokens requested from the Anthropic Messages API.
    /// </summary>
    public int AnthropicMaxTokens { get; set; } = 8192;
    /// <summary>
    /// Account rotation policy when <see cref="OpenAiAccountIds"/> contains multiple entries.
    /// Supported values: first-available, round-robin, sticky.
    /// </summary>
    public string OpenAiAccountRotation { get; set; } = "first-available";
    /// <summary>
    /// When true, reviewer can fall back to the next account in <see cref="OpenAiAccountIds"/> when the selected account is unavailable.
    /// </summary>
    public bool OpenAiAccountFailover { get; set; } = true;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int RetryMaxDelaySeconds { get; set; } = 30;
    public double RetryBackoffMultiplier { get; set; } = 2.0;
    public int RetryJitterMinMs { get; set; } = 200;
    public int RetryJitterMaxMs { get; set; } = 800;
    public bool RetryExtraOnResponseEnded { get; set; } = true;
    public bool ProviderHealthChecks { get; set; } = true;
    public int ProviderHealthCheckTimeoutSeconds { get; set; } = 10;
    public int ProviderCircuitBreakerFailures { get; set; } = 3;
    public int ProviderCircuitBreakerOpenSeconds { get; set; } = 120;
    public bool FailOpen { get; set; } = true;
    /// <summary>
    /// When true, fail-open is limited to transient errors only.
    /// </summary>
    public bool FailOpenTransientOnly { get; set; } = true;
    public bool Diagnostics { get; set; }
    public bool Preflight { get; set; }
    public int PreflightTimeoutSeconds { get; set; } = 15;
    public ReviewLength Length { get; set; } = ReviewLength.Long;
    public bool IncludeNextSteps { get; set; } = true;
    /// <summary>
    /// Adds a short language-aware hints block to the review prompt.
    /// </summary>
    public bool IncludeLanguageHints { get; set; } = true;
    /// When enabled, include a summary note if review context was truncated by file or patch limits.
    /// </summary>
    public bool ReviewBudgetSummary { get; set; } = true;
    public string? PromptTemplate { get; set; }
    public string? PromptTemplatePath { get; set; }
    public string? SummaryTemplate { get; set; }
    public string? SummaryTemplatePath { get; set; }
    public bool OverwriteSummary { get; set; } = true;
    public bool OverwriteSummaryOnNewCommit { get; set; } = true;
    /// <summary>
    /// When enabled, include the previous summary (same commit) in the prompt to reduce noisy rewording on reruns.
    /// </summary>
    public bool SummaryStability { get; set; } = true;
    public bool SkipDraft { get; set; } = true;
    public IReadOnlyList<string> SkipTitles { get; set; } = new[] { "[WIP]", "[skip-review]" };
    public IReadOnlyList<string> SkipLabels { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Paths that, when matched by <b>all</b> changed files in a pull request, cause the entire PR to be skipped.
    /// This is evaluated before <see cref="IncludePaths"/> and <see cref="ExcludePaths"/>.
    /// </summary>
    public IReadOnlyList<string> SkipPaths { get; set; } = Array.Empty<string>();
    /// <summary>
    /// When true, skip binary assets (images, archives, executables) from the review context.
    /// Evaluated before <see cref="IncludePaths"/> and <see cref="ExcludePaths"/>.
    /// </summary>
    public bool SkipBinaryFiles { get; set; } = true;
    /// <summary>
    /// When true, skip generated files (build output and generated sources) from the review context.
    /// Evaluated before <see cref="IncludePaths"/> and <see cref="ExcludePaths"/>.
    /// </summary>
    public bool SkipGeneratedFiles { get; set; } = true;
    /// <summary>
    /// Additional glob patterns to treat as generated files. These are appended to the built-in defaults.
    /// </summary>
    public IReadOnlyList<string> GeneratedFileGlobs { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Glob-style patterns specifying which changed files should be considered for review.
    /// If non-empty, only files matching these patterns are eligible for review.
    /// </summary>
    public IReadOnlyList<string> IncludePaths { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Glob-style patterns specifying changed files that should be excluded from review.
    /// Matching files are removed from the review list but do not skip the entire PR.
    /// </summary>
    public IReadOnlyList<string> ExcludePaths { get; set; } = Array.Empty<string>();
    /// <summary>
    /// When false, pull requests that modify workflow files are skipped to prevent self-modifying workflow runs.
    /// </summary>
    public bool AllowWorkflowChanges { get; set; }
    /// <summary>
    /// When enabled, emits an audit log describing which secrets sources were accessed.
    /// </summary>
    public bool SecretsAudit { get; set; } = true;
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
    public IReadOnlyList<string> RedactionPatterns { get; set; } = DefaultRedactionPatterns;
    public string RedactionReplacement { get; set; } = "[REDACTED]";
    public bool UntrustedPrAllowSecrets { get; set; }
    public bool UntrustedPrAllowWrites { get; set; }
    public int WaitSeconds { get; set; } = 180;
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
    public int ReviewThreadsMax { get; set; } = 20;
    public int ReviewThreadsMaxComments { get; set; } = 6;
    public bool ReviewThreadsAutoResolveStale { get; set; }
    public bool ReviewThreadsAutoResolveMissingInline { get; set; }
    public bool ReviewThreadsAutoResolveBotsOnly { get; set; } = true;
    public IReadOnlyList<string> ReviewThreadsAutoResolveBotLogins { get; set; } = DefaultReviewThreadsBotLogins;
    /// <summary>
    /// Controls which diff range is used when assessing review threads for auto-resolve.
    /// Uses the same values as <see cref="ReviewDiffRange"/>.
    /// </summary>
    public string ReviewThreadsAutoResolveDiffRange { get; set; } = "current";
    public int ReviewThreadsAutoResolveMax { get; set; } = 20;
    public bool ReviewThreadsAutoResolveAI { get; set; } = true;
    /// <summary>
    /// Require explicit diff evidence to auto-resolve review threads.
    /// </summary>
    public bool ReviewThreadsAutoResolveRequireEvidence { get; set; } = true;
    /// <summary>
    /// When enabled, sweep and resolve remaining bot-only kept threads after a no-blockers review.
    /// Useful for repositories that enforce resolved review conversations before merge.
    /// </summary>
    public bool ReviewThreadsAutoResolveSweepNoBlockers { get; set; } = false;
    public bool ReviewThreadsAutoResolveAIPostComment { get; set; }
    public bool ReviewThreadsAutoResolveAIEmbed { get; set; } = true;
    /// <summary>
    /// Placement for embedded thread triage blocks in the main review comment.
    /// </summary>
    public string ReviewThreadsAutoResolveAIEmbedPlacement { get; set; } = "bottom";
    public bool ReviewThreadsAutoResolveAISummary { get; set; } = true;
    public bool ReviewThreadsAutoResolveAIReply { get; set; }
    /// <summary>
    /// When enabled, always append the auto-resolve summary line to the main review comment.
    /// </summary>
    public bool ReviewThreadsAutoResolveSummaryAlways { get; set; }
    /// <summary>
    /// Post a standalone summary comment listing auto-resolved and kept threads.
    /// </summary>
    public bool ReviewThreadsAutoResolveSummaryComment { get; set; }
    /// <summary>
    /// When enabled, only run thread triage/auto-resolve without generating a full review comment.
    /// </summary>
    public bool TriageOnly { get; set; }
    public int MaxCommentChars { get; set; } = 4000;
    public int MaxComments { get; set; } = 20;
    public int CommentSearchLimit { get; set; } = 500;
    public int GitHubMaxConcurrency { get; set; } = 4;
    public bool ContextDenyEnabled { get; set; } = true;
    public IReadOnlyList<string> ContextDenyPatterns { get; set; } = DefaultContextDenyPatterns;
    public bool IncludeRelatedPrs { get; set; }
    public string? RelatedPrsQuery { get; set; }
    public int MaxRelatedPrs { get; set; } = 5;
    public ReviewHistorySettings History { get; } = new();
    public ReviewCiContextSettings CiContext { get; } = new();
    public ReviewSwarmSettings Swarm { get; } = new();
    public AnalysisSettings Analysis { get; } = new AnalysisSettings();

    public string? CodexPath { get; set; }
    public string? CodexArgs { get; set; }
    public string? CodexWorkingDirectory { get; set; }

    public string? CopilotCliPath { get; set; }
    public string? CopilotCliUrl { get; set; }
    public string? CopilotWorkingDirectory { get; set; }
    /// <summary>
    /// Optional Copilot-specific model override. When unset, the CLI default model is used.
    /// </summary>
    public string? CopilotModel { get; set; }
    public string CopilotLauncher { get; set; } = "binary";
    public bool CopilotAutoInstall { get; set; }
    public string? CopilotAutoInstallMethod { get; set; }
    public bool CopilotAutoInstallPrerelease { get; set; }
    /// <summary>
    /// Environment variables to forward from the host into the Copilot CLI process.
    /// When <see cref="CopilotInheritEnvironment"/> is false, only these variables are forwarded.
    /// When true, these variables override any inherited values.
    /// </summary>
    public IReadOnlyList<string> CopilotEnvAllowlist { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Whether the Copilot CLI process should inherit the current environment.
    /// </summary>
    public bool CopilotInheritEnvironment { get; set; } = true;
    /// <summary>
    /// Additional environment variables to set for the Copilot CLI process.
    /// </summary>
    public IReadOnlyDictionary<string, string> CopilotEnv { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Copilot transport selection (CLI or direct HTTP).
    /// </summary>
    public CopilotTransportKind CopilotTransport { get; set; } = CopilotTransportKind.Cli;
    /// <summary>
    /// Copilot direct HTTP endpoint URL (experimental).
    /// </summary>
    public string? CopilotDirectUrl { get; set; }
    /// <summary>
    /// Copilot direct token value (experimental).
    /// </summary>
    public string? CopilotDirectToken { get; set; }
    /// <summary>
    /// Environment variable that holds the Copilot direct token (experimental).
    /// </summary>
    public string? CopilotDirectTokenEnv { get; set; }
    /// <summary>
    /// Additional headers to send with Copilot direct requests (experimental).
    /// </summary>
    public IReadOnlyDictionary<string, string> CopilotDirectHeaders { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Timeout for Copilot direct requests (seconds).
    /// </summary>
    public int CopilotDirectTimeoutSeconds { get; set; } = 60;

    public string? AzureOrganization { get; set; }
    public string? AzureProject { get; set; }
    public string? AzureRepository { get; set; }
    public string? AzureBaseUrl { get; set; }
    public string? AzureTokenEnv { get; set; }
    public AzureDevOpsAuthScheme AzureAuthScheme { get; set; } = AzureDevOpsAuthScheme.Bearer;
    public bool AzureAuthSchemeSpecified { get; set; }

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

    internal IReadOnlyList<string> ResolveMergeBlockerSections() {
        if (MergeBlockerSections.Count > 0) {
            return MergeBlockerSections;
        }
        return IsCompactOutputStyle(OutputStyle)
            ? CompactMergeBlockerSections
            : DefaultMergeBlockerSections;
    }

    internal static bool IsCompactOutputStyle(string? outputStyle) {
        if (string.IsNullOrWhiteSpace(outputStyle)) {
            return false;
        }
        var key = outputStyle.Trim().ToLowerInvariant();
        return key is "compact" or "compact-like" or "compact_style" or "compact-style";
    }
}
