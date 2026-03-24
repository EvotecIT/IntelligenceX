namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestSetupArgsIncludeReviewLoopPolicyAndStrictness() {
        var plan = new SetupPlan("owner/repo") {
            ReviewIntent = "maintainability",
            ReviewStrictness = "strict",
            ReviewLoopPolicy = "vision",
            ReviewVisionPath = "VISION.md",
            MergeBlockerSections = "Todo List,Critical Issues",
            MergeBlockerRequireAllSections = false,
            MergeBlockerRequireSectionMatch = false
        };
        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--review-intent", "maintainability",
            "--review-strictness", "strict",
            "--review-loop-policy", "vision",
            "--review-vision-path", "VISION.md",
            "--merge-blocker-sections", "Todo List,Critical Issues",
            "--merge-blocker-require-all-sections", "false",
            "--merge-blocker-require-section-match", "false"
        }, args, "setup args review loop policy and strictness");
    }

    private static void TestSetupArgsRejectsReviewVisionPathWithoutVisionPolicy() {
        var plan = new SetupPlan("owner/repo") {
            ReviewVisionPath = "VISION.md"
        };
        AssertThrows<InvalidOperationException>(() => SetupArgsBuilder.FromPlan(plan),
            "setup args review vision path requires vision policy");
    }

    private static void TestSetupReviewOptionContextRejectsWithoutWithConfig() {
        var result = SetupRunner.ValidateReviewOptionContextForTests(
            new[] { "--review-intent", "maintainability" },
            isSetup: true,
            withConfig: false,
            hasConfigOverride: false);
        AssertEqual(false, result.Success, "setup review option context requires with-config");
        AssertContainsText(result.Error ?? string.Empty,
            "require --with-config",
            "setup review option context requires with-config error");
    }

    private static void TestSetupReviewOptionContextRejectsConfigOverride() {
        var result = SetupRunner.ValidateReviewOptionContextForTests(
            new[] { "--review-intent", "maintainability" },
            isSetup: true,
            withConfig: true,
            hasConfigOverride: true);
        AssertEqual(false, result.Success, "setup review option context rejects config override");
        AssertContainsText(result.Error ?? string.Empty,
            "not supported when --config-json/--config-path override is used",
            "setup review option context rejects config override error");
    }

    private static void TestSetupReviewOptionContextRejectsVisionPathWithoutVisionPolicy() {
        var result = SetupRunner.ValidateReviewOptionContextForTests(
            new[] {
                "--review-loop-policy", "balanced",
                "--review-vision-path", "VISION.md"
            },
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false);
        AssertEqual(false, result.Success, "setup review option context vision path requires vision policy");
        AssertContainsText(result.Error ?? string.Empty,
            "--review-vision-path is only supported with --review-loop-policy vision",
            "setup review option context vision path requires vision policy error");
    }

    private static void TestSetupConfigRejectsInvalidReviewLoopPolicy() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--review-loop-policy", "not-a-policy"
            }), "setup invalid review loop policy");
    }

    private static void TestSetupConfigRejectsReviewOptionsWithConfigOverride() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--config-json", "{}",
                "--review-intent", "maintainability"
            }), "setup review options with config override");
    }

    private static void TestSetupConfigRejectsEmptyMergeBlockerSections() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--merge-blocker-sections", string.Empty
            }), "setup empty merge blocker sections");
    }

    private static void TestSetupConfigRejectsReviewVisionPathWithoutVisionPolicy() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--review-vision-path", "VISION.md"
            }), "setup review vision path requires vision policy");
    }

    private static void TestSetupConfigRejectsMissingReviewVisionPath() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--review-loop-policy", "vision",
                "--review-vision-path", "missing-vision-file.md"
            }), "setup review vision path missing file");
    }

    private static void TestSetupBuildConfigJsonIncludesVisionLoopPolicyDefaults() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--review-loop-policy", "vision"
        });
        AssertNotNull(content, "config json vision loop policy content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json vision loop policy root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json vision loop policy review");

        AssertEqual("maintainability", review!["intent"]?.GetValue<string>(),
            "config json vision loop policy intent");
        AssertEqual("balanced", review["strictness"]?.GetValue<string>(),
            "config json vision loop policy strictness");
        AssertEqual(false, review["mergeBlockerRequireAllSections"]?.GetValue<bool>(),
            "config json vision loop policy require all sections");
        AssertEqual(true, review["mergeBlockerRequireSectionMatch"]?.GetValue<bool>(),
            "config json vision loop policy require section match");

        var sections = review["mergeBlockerSections"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(sections, "config json vision loop policy sections");
        AssertEqual(2, sections!.Count, "config json vision loop policy sections count");
        AssertEqual("todo list", sections[0]?.GetValue<string>(),
            "config json vision loop policy first section");
        AssertEqual("critical issues", sections[1]?.GetValue<string>(),
            "config json vision loop policy second section");
    }

    private static void TestSetupBuildConfigJsonNormalizesTodoOnlyLoopPolicyAlias() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--review-loop-policy", "single_section"
        });
        AssertNotNull(content, "config json todo-only loop policy alias content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json todo-only loop policy alias root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json todo-only loop policy alias review");

        var sections = review!["mergeBlockerSections"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(sections, "config json todo-only loop policy alias sections");
        AssertEqual(1, sections!.Count, "config json todo-only loop policy alias section count");
        AssertEqual("todo list", sections[0]?.GetValue<string>(),
            "config json todo-only loop policy alias first section");
    }

    private static void TestSetupBuildConfigJsonIncludesVisionInferenceFromFile() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-review-vision-" + Guid.NewGuid().ToString("N") + ".md");
        try {
            File.WriteAllText(temp, string.Join(Environment.NewLine, new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Prioritize performance and latency.",
                "- Keep throughput and resource usage predictable.",
                string.Empty,
                "## Non-Goals",
                "- Cosmetic redesigns unrelated to throughput.",
                string.Empty,
                "## In Scope",
                "- performance optimization for hot paths and memory allocations.",
                string.Empty,
                "## Out Of Scope",
                "- unrelated UI refreshes.",
                string.Empty,
                "## Decision Principles",
                "- aligned: optimize latency and throughput.",
                "- needs-human-review: mixed trade-offs.",
                "- likely-out-of-scope: unrelated changes.",
                string.Empty
            }));

            var content = SetupRunner.BuildReviewerConfigJson(new[] {
                "--review-loop-policy", "vision",
                "--review-vision-path", temp
            });
            AssertNotNull(content, "config json vision inference content");

            var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
            AssertNotNull(root, "config json vision inference root");
            var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
            AssertNotNull(review, "config json vision inference review");
            AssertEqual("performance", review!["intent"]?.GetValue<string>(),
                "config json vision inference intent");
            AssertEqual(temp, review["visionPath"]?.GetValue<string>(),
                "config json vision inference path");
        } finally {
            try {
                if (File.Exists(temp)) {
                    File.Delete(temp);
                }
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    private static void TestSetupBuildConfigJsonMergePreservesReviewLoopSettings() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "intent": "security",
    "strictness": "strict",
    "mergeBlockerSections": ["Todo List"],
    "mergeBlockerRequireAllSections": true,
    "mergeBlockerRequireSectionMatch": true
  }
}
""";
        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--with-config", "--analysis-enabled", "true" },
            seed);
        AssertNotNull(content, "config json merge preserve review loop settings content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json merge preserve review loop settings root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json merge preserve review loop settings review");
        AssertEqual("security", review!["intent"]?.GetValue<string>(),
            "config json merge preserve review loop settings intent");
        AssertEqual("strict", review["strictness"]?.GetValue<string>(),
            "config json merge preserve review loop settings strictness");
        AssertEqual(true, review["mergeBlockerRequireAllSections"]?.GetValue<bool>(),
            "config json merge preserve review loop settings require all");
        AssertEqual(true, review["mergeBlockerRequireSectionMatch"]?.GetValue<bool>(),
            "config json merge preserve review loop settings require match");
    }

    private static void TestSetupBuildConfigJsonIncludesClaudeProviderDefaults() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--provider", "claude"
        });
        AssertNotNull(content, "config json claude defaults content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json claude defaults root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json claude defaults review");
        var anthropic = review!["anthropic"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(anthropic, "config json claude defaults anthropic block");

        AssertEqual("claude", review["provider"]?.GetValue<string>(), "config json claude defaults provider");
        AssertEqual("claude-opus-4-1", review["model"]?.GetValue<string>(), "config json claude defaults model");
        AssertEqual("ANTHROPIC_API_KEY", anthropic!["apiKeyEnv"]?.GetValue<string>(),
            "config json claude defaults api key env");
        AssertEqual(null, review["openaiTransport"], "config json claude defaults removes openai transport");
    }
#endif
}
