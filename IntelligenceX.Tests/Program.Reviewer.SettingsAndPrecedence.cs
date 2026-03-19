namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestReviewSettingsLoadConfigThenEnvPrecedence() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var previousProvider = Environment.GetEnvironmentVariable("REVIEW_PROVIDER");
        var previousCodeHost = Environment.GetEnvironmentVariable("REVIEW_CODE_HOST");
        var previousMaxFiles = Environment.GetEnvironmentVariable("OPENAI_MAX_FILES");
        var previousSkipGenerated = Environment.GetEnvironmentVariable("SKIP_GENERATED_FILES");
        var previousDiffRange = Environment.GetEnvironmentVariable("REVIEW_DIFF_RANGE");
        var previousNarrativeMode = Environment.GetEnvironmentVariable("REVIEW_NARRATIVE_MODE");
        var previousPolicyRulePreviewItems = Environment.GetEnvironmentVariable("REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS");
        var previousUsageBudgetGuard = Environment.GetEnvironmentVariable("REVIEW_USAGE_BUDGET_GUARD");
        var previousUsageBudgetAllowCredits = Environment.GetEnvironmentVariable("REVIEW_USAGE_BUDGET_ALLOW_CREDITS");
        var previousUsageBudgetAllowWeekly = Environment.GetEnvironmentVariable("REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-review-settings-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(configPath, """
{
  "review": {
    "provider": "openai",
    "codeHost": "github",
    "maxFiles": 7,
    "skipGeneratedFiles": true,
    "reviewDiffRange": "pr-base",
    "narrativeMode": "structured",
    "reviewUsageBudgetGuard": false,
    "reviewUsageBudgetAllowCredits": false,
    "reviewUsageBudgetAllowWeeklyLimit": false
  },
  "analysis": {
    "configMode": "replace",
    "results": {
      "policyRulePreviewItems": 50
    }
  }
}
""");

            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", "copilot");
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", "azure");
            Environment.SetEnvironmentVariable("OPENAI_MAX_FILES", "42");
            Environment.SetEnvironmentVariable("SKIP_GENERATED_FILES", "false");
            Environment.SetEnvironmentVariable("REVIEW_DIFF_RANGE", "current");
            Environment.SetEnvironmentVariable("REVIEW_NARRATIVE_MODE", "flexible");
            Environment.SetEnvironmentVariable("REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS", "100");
            Environment.SetEnvironmentVariable("REVIEW_USAGE_BUDGET_GUARD", "true");
            Environment.SetEnvironmentVariable("REVIEW_USAGE_BUDGET_ALLOW_CREDITS", "true");
            Environment.SetEnvironmentVariable("REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT", "true");

            var settings = ReviewSettings.Load();
            AssertEqual(ReviewProvider.Copilot, settings.Provider, "review settings load env provider precedence");
            AssertEqual(ReviewCodeHost.AzureDevOps, settings.CodeHost, "review settings load env code host precedence");
            AssertEqual(42, settings.MaxFiles, "review settings load env max files precedence");
            AssertEqual(false, settings.SkipGeneratedFiles, "review settings load env skip generated precedence");
            AssertEqual("current", settings.ReviewDiffRange, "review settings load env diff range precedence");
            AssertEqual(ReviewNarrativeMode.Freedom, settings.NarrativeMode, "review settings load env narrative mode precedence");
            AssertEqual(true, settings.ReviewUsageBudgetGuard, "review settings usage budget guard precedence");
            AssertEqual(true, settings.ReviewUsageBudgetAllowCredits, "review settings usage budget credits precedence");
            AssertEqual(true, settings.ReviewUsageBudgetAllowWeeklyLimit, "review settings usage budget weekly precedence");
            AssertEqual(AnalysisConfigMode.Replace, settings.Analysis.ConfigMode, "review settings load config analysis mode");
            AssertEqual(100, settings.Analysis.Results.PolicyRulePreviewItems,
                "review settings load config analysis preview size");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("REVIEW_CODE_HOST", previousCodeHost);
            Environment.SetEnvironmentVariable("OPENAI_MAX_FILES", previousMaxFiles);
            Environment.SetEnvironmentVariable("SKIP_GENERATED_FILES", previousSkipGenerated);
            Environment.SetEnvironmentVariable("REVIEW_DIFF_RANGE", previousDiffRange);
            Environment.SetEnvironmentVariable("REVIEW_NARRATIVE_MODE", previousNarrativeMode);
            Environment.SetEnvironmentVariable("REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS", previousPolicyRulePreviewItems);
            Environment.SetEnvironmentVariable("REVIEW_USAGE_BUDGET_GUARD", previousUsageBudgetGuard);
            Environment.SetEnvironmentVariable("REVIEW_USAGE_BUDGET_ALLOW_CREDITS", previousUsageBudgetAllowCredits);
            Environment.SetEnvironmentVariable("REVIEW_USAGE_BUDGET_ALLOW_WEEKLY_LIMIT", previousUsageBudgetAllowWeekly);
            if (File.Exists(configPath)) {
                File.Delete(configPath);
            }
        }
    }

    private static void TestReviewSettingsPolicyRulePreviewConfigClampRange() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-review-policy-clamp-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(configPath, """
{
  "analysis": {
    "results": {
      "policyRulePreviewItems": 501
    }
  }
}
""");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
            var settings = ReviewSettings.Load();
            AssertEqual(500, settings.Analysis.Results.PolicyRulePreviewItems,
                "review settings policy preview clamp high");

            File.WriteAllText(configPath, """
{
  "analysis": {
    "results": {
      "policyRulePreviewItems": -1
    }
  }
}
""");
            settings = ReviewSettings.Load();
            AssertEqual(0, settings.Analysis.Results.PolicyRulePreviewItems,
                "review settings policy preview clamp negative");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            if (File.Exists(configPath)) {
                File.Delete(configPath);
            }
        }
    }

    private static void TestReviewSettingsLoadConfigAllowsZeroForNonNegativeLimits() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-review-zero-limits-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(configPath, """
{
  "review": {
    "maxFiles": 0,
    "maxPatchChars": 0,
    "maxInlineComments": 0,
    "maxCommentChars": 0,
    "maxComments": 0,
    "commentSearchLimit": 0
  }
}
""");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
            var settings = ReviewSettings.Load();
            AssertEqual(0, settings.MaxFiles, "review settings config maxFiles zero");
            AssertEqual(0, settings.MaxPatchChars, "review settings config maxPatchChars zero");
            AssertEqual(0, settings.MaxInlineComments, "review settings config maxInlineComments zero");
            AssertEqual(0, settings.MaxCommentChars, "review settings config maxCommentChars zero");
            AssertEqual(0, settings.MaxComments, "review settings config maxComments zero");
            AssertEqual(500, settings.CommentSearchLimit, "review settings config commentSearchLimit zero falls back");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            if (File.Exists(configPath)) {
                File.Delete(configPath);
            }
        }
    }

    private static void TestReviewSettingsFromEnvironmentAllowsZeroForNonNegativeLimits() {
        var previousMaxFiles = Environment.GetEnvironmentVariable("OPENAI_MAX_FILES");
        var previousMaxPatchChars = Environment.GetEnvironmentVariable("OPENAI_MAX_PATCH_CHARS");
        var previousMaxInlineComments = Environment.GetEnvironmentVariable("OPENAI_MAX_INLINE_COMMENTS");
        var previousMaxCommentChars = Environment.GetEnvironmentVariable("REVIEW_MAX_COMMENT_CHARS");
        var previousMaxComments = Environment.GetEnvironmentVariable("REVIEW_MAX_COMMENTS");
        var previousCommentSearchLimit = Environment.GetEnvironmentVariable("REVIEW_COMMENT_SEARCH_LIMIT");
        try {
            Environment.SetEnvironmentVariable("OPENAI_MAX_FILES", "0");
            Environment.SetEnvironmentVariable("OPENAI_MAX_PATCH_CHARS", "0");
            Environment.SetEnvironmentVariable("OPENAI_MAX_INLINE_COMMENTS", "0");
            Environment.SetEnvironmentVariable("REVIEW_MAX_COMMENT_CHARS", "0");
            Environment.SetEnvironmentVariable("REVIEW_MAX_COMMENTS", "0");
            Environment.SetEnvironmentVariable("REVIEW_COMMENT_SEARCH_LIMIT", "0");

            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(0, settings.MaxFiles, "review settings env maxFiles zero");
            AssertEqual(0, settings.MaxPatchChars, "review settings env maxPatchChars zero");
            AssertEqual(0, settings.MaxInlineComments, "review settings env maxInlineComments zero");
            AssertEqual(0, settings.MaxCommentChars, "review settings env maxCommentChars zero");
            AssertEqual(0, settings.MaxComments, "review settings env maxComments zero");
            AssertEqual(500, settings.CommentSearchLimit, "review settings env commentSearchLimit zero falls back");
        } finally {
            Environment.SetEnvironmentVariable("OPENAI_MAX_FILES", previousMaxFiles);
            Environment.SetEnvironmentVariable("OPENAI_MAX_PATCH_CHARS", previousMaxPatchChars);
            Environment.SetEnvironmentVariable("OPENAI_MAX_INLINE_COMMENTS", previousMaxInlineComments);
            Environment.SetEnvironmentVariable("REVIEW_MAX_COMMENT_CHARS", previousMaxCommentChars);
            Environment.SetEnvironmentVariable("REVIEW_MAX_COMMENTS", previousMaxComments);
            Environment.SetEnvironmentVariable("REVIEW_COMMENT_SEARCH_LIMIT", previousCommentSearchLimit);
        }
    }

    private static void TestSetupGeneratedReviewerConfigValidatesAndLoadsWithCanonicalRelatedPrs() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-setup-reviewer-config-{Guid.NewGuid():N}.json");
        try {
            var content = IntelligenceX.Cli.Setup.SetupRunner.BuildReviewerConfigJson(new[] {
                "--provider", "openai",
                "--review-profile", "security",
                "--review-mode", "summary",
                "--include-related-prs", "false",
                "--with-config"
            });
            AssertContainsText(content, "\"includeRelatedPrs\": false", "setup config emits canonical includeRelatedPrs");
            AssertEqual(false, content.Contains("includeRelatedPullRequests", StringComparison.Ordinal),
                "setup config omits legacy includeRelatedPullRequests");

            File.WriteAllText(configPath, content);
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);

            var validation = ReviewConfigValidator.ValidateCurrent();
            AssertNotNull(validation, "setup config validator result");
            AssertEqual(0, validation!.Errors.Count, "setup config validator has no errors");

            var settings = ReviewSettings.Load();
            AssertEqual("security", settings.Profile, "setup config runtime profile");
            AssertEqual("summary", settings.Mode, "setup config runtime mode");
            AssertEqual(true, settings.SummaryStability, "setup config runtime summary stability");
            AssertEqual("pr-base", settings.ReviewDiffRange, "setup config runtime diff range");
            AssertEqual(true, settings.IncludeReviewThreads, "setup config runtime include review threads");
            AssertEqual(true, settings.ReviewThreadsIncludeBots, "setup config runtime include bot threads");
            AssertEqual(25, settings.ReviewThreadsMax, "setup config runtime review threads max");
            AssertEqual(true, settings.ReviewThreadsAutoResolveStale, "setup config runtime auto-resolve stale");
            AssertEqual("pr-base", settings.ReviewThreadsAutoResolveDiffRange,
                "setup config runtime auto-resolve diff range");
            AssertEqual(25, settings.ReviewThreadsAutoResolveMax, "setup config runtime auto-resolve max");
            AssertEqual(true, settings.ReviewThreadsAutoResolveSweepNoBlockers,
                "setup config runtime auto-resolve sweep");
            AssertEqual(true, settings.ReviewThreadsAutoResolveAIReply, "setup config runtime auto-resolve ai reply");
            AssertEqual(true, settings.ReviewUsageSummary, "setup config runtime usage summary");
            AssertEqual(true, settings.ReviewUsageBudgetGuard, "setup config runtime usage budget guard");
            AssertEqual(false, settings.IncludeRelatedPrs, "setup config runtime includeRelatedPrs");
            AssertEqual(0, settings.Analysis.Results.MaxInline, "setup config runtime analysis inline default");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            if (File.Exists(configPath)) {
                File.Delete(configPath);
            }
        }
    }
}
#endif
