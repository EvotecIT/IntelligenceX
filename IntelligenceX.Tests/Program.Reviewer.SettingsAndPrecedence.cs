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
        var previousPolicyRulePreviewItems = Environment.GetEnvironmentVariable("REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS");
        var configPath = Path.Combine(Path.GetTempPath(), $"intelligencex-review-settings-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(configPath, """
{
  "review": {
    "provider": "openai",
    "codeHost": "github",
    "maxFiles": 7,
    "skipGeneratedFiles": true,
    "reviewDiffRange": "pr-base"
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
            Environment.SetEnvironmentVariable("REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS", "100");

            var settings = ReviewSettings.Load();
            AssertEqual(ReviewProvider.Copilot, settings.Provider, "review settings load env provider precedence");
            AssertEqual(ReviewCodeHost.AzureDevOps, settings.CodeHost, "review settings load env code host precedence");
            AssertEqual(42, settings.MaxFiles, "review settings load env max files precedence");
            AssertEqual(false, settings.SkipGeneratedFiles, "review settings load env skip generated precedence");
            AssertEqual("current", settings.ReviewDiffRange, "review settings load env diff range precedence");
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
            Environment.SetEnvironmentVariable("REVIEW_ANALYSIS_POLICY_RULE_PREVIEW_ITEMS", previousPolicyRulePreviewItems);
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
}
#endif
