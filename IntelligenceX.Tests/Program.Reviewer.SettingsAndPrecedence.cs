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

    private static void TestReviewSettingsLoadConfigThenEnvPrecedenceForCiContextAndSwarm() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var previousCiContextEnabled = Environment.GetEnvironmentVariable("REVIEW_CI_CONTEXT_ENABLED");
        var previousCiContextMaxFailedRuns = Environment.GetEnvironmentVariable("REVIEW_CI_CONTEXT_MAX_FAILED_RUNS");
        var previousHistoryEnabled = Environment.GetEnvironmentVariable("REVIEW_HISTORY_ENABLED");
        var previousHistoryArtifacts = Environment.GetEnvironmentVariable("REVIEW_HISTORY_ARTIFACTS");
        var previousHistoryIncludeExternal = Environment.GetEnvironmentVariable("REVIEW_HISTORY_INCLUDE_EXTERNAL_BOT_SUMMARIES");
        var previousHistoryExternalLogins = Environment.GetEnvironmentVariable("REVIEW_HISTORY_EXTERNAL_BOT_LOGINS");
        var previousHistoryMaxRounds = Environment.GetEnvironmentVariable("REVIEW_HISTORY_MAX_ROUNDS");
        var previousHistoryMaxItems = Environment.GetEnvironmentVariable("REVIEW_HISTORY_MAX_ITEMS");
        var previousSwarmEnabled = Environment.GetEnvironmentVariable("REVIEW_SWARM_ENABLED");
        var previousSwarmMaxParallel = Environment.GetEnvironmentVariable("REVIEW_SWARM_MAX_PARALLEL");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-review-ci-swarm-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(configPath, """
{
  "review": {
    "ciContext": {
      "enabled": true,
      "includeCheckSummary": false,
      "includeFailedRuns": false,
        "includeFailureSnippets": "always",
        "maxFailedRuns": 7,
        "maxSnippetCharsPerRun": 900,
        "classifyInfraFailures": false
      },
      "history": {
        "enabled": true,
        "includeIxSummaryHistory": false,
        "includeReviewThreads": false,
        "includeExternalBotSummaries": false,
        "externalBotLogins": ["claude"],
        "artifacts": false,
        "maxRounds": 9,
        "maxItems": 9
      },
      "swarm": {
        "enabled": true,
        "shadowMode": true,
        "reviewers": ["security", "tests", "security"],
        "maxParallel": 0,
      "publishSubreviews": true,
      "aggregatorModel": "gpt-test",
      "failOpenOnPartial": false,
      "metrics": false
    }
  }
}
""");

            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
            Environment.SetEnvironmentVariable("REVIEW_CI_CONTEXT_ENABLED", "false");
            Environment.SetEnvironmentVariable("REVIEW_CI_CONTEXT_MAX_FAILED_RUNS", "2");
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_ENABLED", "false");
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_ARTIFACTS", "true");
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_INCLUDE_EXTERNAL_BOT_SUMMARIES", "true");
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_EXTERNAL_BOT_LOGINS", "claude,copilot-pull-request-reviewer");
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_MAX_ROUNDS", "4");
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_MAX_ITEMS", "4");
            Environment.SetEnvironmentVariable("REVIEW_SWARM_ENABLED", "false");
            Environment.SetEnvironmentVariable("REVIEW_SWARM_MAX_PARALLEL", "3");

            var settings = ReviewSettings.Load();

            AssertEqual(false, settings.CiContext.Enabled, "review settings env ciContext enabled precedence");
            AssertEqual(false, settings.CiContext.IncludeCheckSummary, "review settings ciContext config includeCheckSummary");
            AssertEqual(false, settings.CiContext.IncludeFailedRuns, "review settings ciContext config includeFailedRuns");
            AssertEqual("always", settings.CiContext.IncludeFailureSnippets,
                "review settings ciContext config includeFailureSnippets");
            AssertEqual(2, settings.CiContext.MaxFailedRuns, "review settings env ciContext maxFailedRuns precedence");
            AssertEqual(900, settings.CiContext.MaxSnippetCharsPerRun, "review settings ciContext config maxSnippetChars");
            AssertEqual(false, settings.CiContext.ClassifyInfraFailures,
                "review settings ciContext config classifyInfraFailures");

            AssertEqual(false, settings.History.Enabled, "review settings env history enabled precedence");
            AssertEqual(false, settings.History.IncludeIxSummaryHistory,
                "review settings history config includeIxSummaryHistory");
            AssertEqual(false, settings.History.IncludeReviewThreads,
                "review settings history config includeReviewThreads");
            AssertEqual(true, settings.History.IncludeExternalBotSummaries,
                "review settings env history include external summaries precedence");
            AssertSequenceEqual(new[] { "claude", "copilot-pull-request-reviewer" },
                settings.History.ExternalBotLogins.ToArray(), "review settings env history external bot logins");
            AssertEqual(true, settings.History.Artifacts, "review settings env history artifacts precedence");
            AssertEqual(4, settings.History.MaxRounds, "review settings env history maxRounds precedence");
            AssertEqual(4, settings.History.MaxItems, "review settings env history maxItems precedence");

            AssertEqual(false, settings.Swarm.Enabled, "review settings env swarm enabled precedence");
            AssertEqual(true, settings.Swarm.ShadowMode, "review settings swarm config shadowMode");
            AssertSequenceEqual(new[] { "security", "tests" }, settings.Swarm.Reviewers.ToArray(),
                "review settings swarm reviewers normalization");
            AssertEqual(2, settings.Swarm.ReviewerSettings.Count, "review settings swarm reviewer settings count");
            AssertEqual("security", settings.Swarm.ReviewerSettings[0].Id, "review settings swarm reviewer settings first id");
            AssertEqual(3, settings.Swarm.MaxParallel, "review settings env swarm maxParallel precedence");
            AssertEqual(true, settings.Swarm.PublishSubreviews, "review settings swarm config publishSubreviews");
            AssertEqual("gpt-test", settings.Swarm.AggregatorModel ?? string.Empty,
                "review settings swarm config aggregatorModel");
            AssertEqual("gpt-test", settings.Swarm.Aggregator.Model ?? string.Empty,
                "review settings swarm aggregator model sync");
            AssertEqual(false, settings.Swarm.FailOpenOnPartial, "review settings swarm config failOpenOnPartial");
            AssertEqual(false, settings.Swarm.Metrics, "review settings swarm config metrics");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            Environment.SetEnvironmentVariable("REVIEW_CI_CONTEXT_ENABLED", previousCiContextEnabled);
            Environment.SetEnvironmentVariable("REVIEW_CI_CONTEXT_MAX_FAILED_RUNS", previousCiContextMaxFailedRuns);
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_ENABLED", previousHistoryEnabled);
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_ARTIFACTS", previousHistoryArtifacts);
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_INCLUDE_EXTERNAL_BOT_SUMMARIES", previousHistoryIncludeExternal);
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_EXTERNAL_BOT_LOGINS", previousHistoryExternalLogins);
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_MAX_ROUNDS", previousHistoryMaxRounds);
            Environment.SetEnvironmentVariable("REVIEW_HISTORY_MAX_ITEMS", previousHistoryMaxItems);
            Environment.SetEnvironmentVariable("REVIEW_SWARM_ENABLED", previousSwarmEnabled);
            Environment.SetEnvironmentVariable("REVIEW_SWARM_MAX_PARALLEL", previousSwarmMaxParallel);
            if (File.Exists(configPath)) {
                File.Delete(configPath);
            }
        }
    }

    private static void TestReviewSettingsLoadSwarmReviewerObjects() {
        var previousConfigPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-review-swarm-objects-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(configPath, """
{
  "review": {
    "swarm": {
      "enabled": true,
      "reviewers": [
        {
          "id": "correctness",
          "provider": "openai",
          "model": "gpt-5.4",
          "reasoningEffort": "high"
        },
        {
          "id": "tests",
          "provider": "copilot",
          "model": "gpt-5.2"
        }
      ],
      "aggregator": {
        "provider": "openai",
        "model": "gpt-5.4",
        "reasoningEffort": "medium"
      }
    }
  }
}
""");

            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);

            var settings = ReviewSettings.Load();

            AssertSequenceEqual(new[] { "correctness", "tests" }, settings.Swarm.Reviewers.ToArray(),
                "review settings swarm object reviewers ids");
            AssertEqual(2, settings.Swarm.ReviewerSettings.Count, "review settings swarm object reviewer settings count");
            AssertEqual(ReviewProvider.OpenAI, settings.Swarm.ReviewerSettings[0].Provider ?? ReviewProvider.Copilot,
                "review settings swarm object first provider");
            AssertEqual("gpt-5.4", settings.Swarm.ReviewerSettings[0].Model ?? string.Empty,
                "review settings swarm object first model");
            AssertEqual(ReasoningEffort.High, settings.Swarm.ReviewerSettings[0].ReasoningEffort ?? ReasoningEffort.Low,
                "review settings swarm object first reasoning effort");
            AssertEqual(ReviewProvider.Copilot, settings.Swarm.ReviewerSettings[1].Provider ?? ReviewProvider.OpenAI,
                "review settings swarm object second provider");
            AssertEqual("gpt-5.2", settings.Swarm.ReviewerSettings[1].Model ?? string.Empty,
                "review settings swarm object second model");
            AssertEqual(ReviewProvider.OpenAI, settings.Swarm.Aggregator.Provider ?? ReviewProvider.Copilot,
                "review settings swarm object aggregator provider");
            AssertEqual("gpt-5.4", settings.Swarm.Aggregator.Model ?? string.Empty,
                "review settings swarm object aggregator model");
            AssertEqual("gpt-5.4", settings.Swarm.AggregatorModel ?? string.Empty,
                "review settings swarm object aggregatorModel sync");
            AssertEqual(ReasoningEffort.Medium, settings.Swarm.Aggregator.ReasoningEffort ?? ReasoningEffort.Low,
                "review settings swarm object aggregator reasoning effort");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousConfigPath);
            if (File.Exists(configPath)) {
                File.Delete(configPath);
            }
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
