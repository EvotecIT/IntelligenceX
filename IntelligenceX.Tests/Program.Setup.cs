namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestSetupArgsRejectSkipUpdate() {
        var plan = new SetupPlan("owner/repo") {
            SkipSecret = true,
            UpdateSecret = true
        };
        AssertThrows<InvalidOperationException>(() => SetupArgsBuilder.FromPlan(plan), "skip+update");
    }

    private static void TestSetupArgsIncludeAnalysisOptions() {
        var plan = new SetupPlan("owner/repo") {
            AnalysisEnabled = true,
            AnalysisGateEnabled = true,
            AnalysisPacks = "all-50,all-100"
        };
        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--analysis-enabled", "true",
            "--analysis-gate", "true",
            "--analysis-packs", "all-50,all-100"
        }, args, "setup args analysis");
    }

    private static void TestSetupArgsIncludeTriageBootstrap() {
        var plan = new SetupPlan("owner/repo") {
            TriageBootstrap = true
        };
        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--triage-bootstrap"
        }, args, "setup args triage bootstrap");
    }

    private static void TestSetupTriageControlIssueProvisionDecision() {
        AssertEqual(true, SetupRunner.ShouldProvisionTriageControlIssue(null),
            "triage control issue should be provisioned when variable missing");
        AssertEqual(true, SetupRunner.ShouldProvisionTriageControlIssue(string.Empty),
            "triage control issue should be provisioned when variable empty");
        AssertEqual(true, SetupRunner.ShouldProvisionTriageControlIssue("abc"),
            "triage control issue should be provisioned when variable non-numeric");
        AssertEqual(true, SetupRunner.ShouldProvisionTriageControlIssue("0"),
            "triage control issue should be provisioned when variable non-positive");
        AssertEqual(false, SetupRunner.ShouldProvisionTriageControlIssue("42"),
            "triage control issue should not be provisioned when variable valid");
    }

    private static void TestSetupProjectViewApplyIssueProvisionDecision() {
        AssertEqual(false, SetupRunner.ShouldProvisionProjectViewApplyIssue(
                issueVariableValue: "14",
                missingViews: 3,
                directCreateSupported: false),
            "project view issue should not be provisioned when variable is valid");

        AssertEqual(false, SetupRunner.ShouldProvisionProjectViewApplyIssue(
                issueVariableValue: null,
                missingViews: 0,
                directCreateSupported: false),
            "project view issue should not be provisioned when no views are missing");

        AssertEqual(false, SetupRunner.ShouldProvisionProjectViewApplyIssue(
                issueVariableValue: null,
                missingViews: 2,
                directCreateSupported: true),
            "project view issue should not be provisioned when direct create is supported");

        AssertEqual(true, SetupRunner.ShouldProvisionProjectViewApplyIssue(
                issueVariableValue: null,
                missingViews: 2,
                directCreateSupported: false),
            "project view issue should be provisioned when missing views and variable absent");

        AssertEqual(true, SetupRunner.ShouldProvisionProjectViewApplyIssue(
                issueVariableValue: "oops",
                missingViews: 1,
                directCreateSupported: false),
            "project view issue should be provisioned when variable is invalid");
    }

    private static void TestSetupTriageBootstrapLinksCommentIncludesAssistiveIssueLinks() {
        var comment = SetupRunner.BuildTriageBootstrapLinksComment(
            repoFullName: "owner/repo",
            projectOwner: "owner",
            projectNumber: 123,
            controlIssueNumber: 11,
            viewApplyIssueNumber: 22,
            missingViews: 3,
            directCreateSupported: false,
            labelsCreatedCount: 4,
            labelsTotalCount: 24,
            labelsEnsureFailed: false);

        AssertContainsText(comment, "intelligencex:triage-bootstrap-links", "bootstrap links marker");
        AssertContainsText(comment, "https://github.com/owner/repo/issues/11", "control issue link present");
        AssertContainsText(comment, "https://github.com/owner/repo/issues/22", "view apply issue link present");
        AssertContainsText(comment, "IX labels: ensured (24 tracked, 4 created this run).", "labels ensured summary present");
        AssertContainsText(comment, "Maintainer Entry Point", "maintainer entry point section present");
    }

    private static void TestSetupTriageBootstrapLinksCommentHandlesMissingViewIssue() {
        var comment = SetupRunner.BuildTriageBootstrapLinksComment(
            repoFullName: "owner/repo",
            projectOwner: "owner",
            projectNumber: 123,
            controlIssueNumber: 11,
            viewApplyIssueNumber: null,
            missingViews: 2,
            directCreateSupported: false,
            labelsCreatedCount: 0,
            labelsTotalCount: 24,
            labelsEnsureFailed: false);

        AssertContainsText(comment, "auto-provision failed", "missing view issue fallback text present");
        AssertContainsText(comment, "project-view-apply --create-issue", "manual recovery command hint present");
    }

    private static void TestSetupTriageBootstrapLinksCommentHandlesLabelEnsureFailure() {
        var comment = SetupRunner.BuildTriageBootstrapLinksComment(
            repoFullName: "owner/repo",
            projectOwner: "owner",
            projectNumber: 123,
            controlIssueNumber: 11,
            viewApplyIssueNumber: null,
            missingViews: 0,
            directCreateSupported: true,
            labelsCreatedCount: 0,
            labelsTotalCount: 24,
            labelsEnsureFailed: true);

        AssertContainsText(comment, "IX labels: ensure failed", "label failure summary present");
        AssertContainsText(comment, "project-init --repo owner/repo --owner owner --project 123 --ensure-labels --no-link-repo --no-ensure-default-views",
            "label recovery command hint present");
    }

    private static void TestSetupArgsIncludeAnalysisRunStrictOption() {
        var plan = new SetupPlan("owner/repo") {
            AnalysisEnabled = true,
            AnalysisRunStrict = true
        };
        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--analysis-enabled", "true",
            "--analysis-run-strict", "true"
        }, args, "setup args analysis run strict");
    }

    private static void TestSetupArgsIncludeAnalysisExportPath() {
        var plan = new SetupPlan("owner/repo") {
            AnalysisEnabled = true,
            AnalysisExportPath = ".intelligencex/analyzers"
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--analysis-enabled", "true",
            "--analysis-export-path", ".intelligencex/analyzers"
        }, args, "setup args analysis export path");
    }

    private static void TestSetupArgsDisableAnalysisOmitsGateAndPacks() {
        var plan = new SetupPlan("owner/repo") {
            AnalysisEnabled = false,
            AnalysisGateEnabled = true,
            AnalysisRunStrict = true,
            AnalysisPacks = "all-100",
            AnalysisExportPath = ".intelligencex/analyzers"
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--analysis-enabled", "false"
        }, args, "setup args analysis disabled");
    }

    private static void TestSetupArgsIncludeOpenAiAccountRouting() {
        var plan = new SetupPlan("owner/repo") {
            Provider = "openai",
            OpenAIAccountId = "acc-primary",
            OpenAIAccountIds = "acc-primary,acc-backup",
            OpenAIAccountRotation = "round-robin",
            OpenAIAccountFailover = false
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--provider", "openai",
            "--openai-account-id", "acc-primary",
            "--openai-account-ids", "acc-primary,acc-backup",
            "--openai-account-rotation", "round-robin",
            "--openai-account-failover", "false"
        }, args, "setup args openai account routing");
    }

    private static void TestSetupArgsIncludeOpenAiAccountRoutingWithPrimaryOnly() {
        var plan = new SetupPlan("owner/repo") {
            Provider = "openai",
            OpenAIAccountId = "acc-primary",
            OpenAIAccountRotation = "round-robin",
            OpenAIAccountFailover = false
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--provider", "openai",
            "--openai-account-id", "acc-primary",
            "--openai-account-rotation", "round-robin",
            "--openai-account-failover", "false"
        }, args, "setup args openai account routing primary only");
    }

    private static void TestSetupArgsIncludeOpenAiModel() {
        var plan = new SetupPlan("owner/repo") {
            Provider = "openai",
            OpenAIModel = "gpt-5.4/fast"
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--provider", "openai",
            "--model", "gpt-5.4/fast"
        }, args, "setup args openai model");
    }

    private static void TestSetupProviderCatalogReturnsRecommendedOpenAiModels() {
        var models = IntelligenceX.Cli.Setup.SetupProviderCatalog.GetRecommendedModels("openai");
        AssertEqual(true, models.Count >= 3, "setup provider catalog openai model count");
        AssertEqual("gpt-5.5", models[0], "setup provider catalog openai default first");
        AssertEqual(true, models.Contains("gpt-5.5/fast", StringComparer.OrdinalIgnoreCase),
            "setup provider catalog openai fast present");
        AssertEqual(true, models.Contains("gpt-5-mini", StringComparer.OrdinalIgnoreCase),
            "setup provider catalog openai mini present");
    }

    private static void TestSetupProviderCatalogReturnsRecommendedClaudeModels() {
        var models = IntelligenceX.Cli.Setup.SetupProviderCatalog.GetRecommendedModels("claude");
        AssertEqual(3, models.Count, "setup provider catalog claude model count");
        AssertEqual("claude-opus-4-1", models[0], "setup provider catalog claude default first");
        AssertEqual(true, models.Contains("claude-sonnet-4-5", StringComparer.OrdinalIgnoreCase),
            "setup provider catalog claude sonnet present");
        AssertEqual(true, models.Contains("claude-haiku-4-5", StringComparer.OrdinalIgnoreCase),
            "setup provider catalog claude haiku present");
    }

    private static void TestSetupProviderCatalogDescribesRecommendedModelProfiles() {
        var openAiProfile = IntelligenceX.Cli.Setup.SetupProviderCatalog.TryGetRecommendedModelProfile("openai", "gpt-5.5");
        AssertEqual(true, openAiProfile.HasValue, "setup provider catalog openai profile exists");
        AssertContainsText(openAiProfile?.Summary ?? string.Empty, "default quality", "setup provider catalog openai profile summary");
        AssertEqual(true, openAiProfile?.IsRecommendedDefault ?? false, "setup provider catalog openai default flag");
        AssertEqual("OpenAI default review", openAiProfile?.ProfileLabel ?? string.Empty, "setup provider catalog openai profile label");

        var claudeProfile = IntelligenceX.Cli.Setup.SetupProviderCatalog.TryGetRecommendedModelProfile("claude", "claude-sonnet-4-5");
        AssertEqual(true, claudeProfile.HasValue, "setup provider catalog claude profile exists");
        AssertContainsText(claudeProfile?.Summary ?? string.Empty, "Balanced Claude option", "setup provider catalog claude profile summary");
        AssertEqual("claude-balanced-review", claudeProfile?.ProfileId ?? string.Empty, "setup provider catalog claude profile id");

        var missing = IntelligenceX.Cli.Setup.SetupProviderCatalog.TryGetRecommendedModelProfile("claude", "claude-custom-test");
        AssertEqual(false, missing.HasValue, "setup provider catalog missing profile");

        var byId = IntelligenceX.Cli.Setup.SetupProviderCatalog.TryGetRecommendedModelProfileById("claude", "claude-fast-review");
        AssertEqual(true, byId.HasValue, "setup provider catalog profile by id exists");
        AssertEqual("claude-haiku-4-5", byId?.ModelId ?? string.Empty, "setup provider catalog profile by id model");
    }

    private static void TestSetupArgsIncludeClaudeModelAndApiKey() {
        var plan = new SetupPlan("owner/repo") {
            Provider = "claude",
            OpenAIModel = "claude-sonnet-4-20250514",
            AnthropicApiKey = "sk-ant-test"
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--anthropic-api-key", "sk-ant-test",
            "--provider", "claude",
            "--model", "claude-sonnet-4-20250514"
        }, args, "setup args claude model and api key");
    }

    private static void TestSetupConfigRejectsInvalidOpenAiAccountRotation() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--provider", "openai",
                "--openai-account-ids", "acc-primary,acc-backup",
                "--openai-account-rotation", "invalid-value"
            }), "setup invalid openai account rotation");
    }

    private static void TestSetupConfigRejectsAnalysisStrictWithoutAnalysisEnabled() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--with-config",
                "--analysis-run-strict", "true"
            }), "setup analysis strict requires analysis enabled");
    }

    private static void TestSetupConfigRejectsAnalysisOptionsWithConfigOverride() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--with-config",
                "--config-json", "{}",
                "--analysis-enabled", "true"
            }), "setup analysis options with config override");
    }

    private static void TestSetupConfigRejectsAnalysisOptionsWithoutWithConfig() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--analysis-enabled", "true"
            }), "setup analysis options without with-config");
    }

    private static void TestSetupConfigRejectsInvalidAnalysisPackId() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--with-config",
                "--analysis-enabled", "true",
                "--analysis-packs", "all-50,--force"
            }), "setup invalid analysis pack id");
    }

    private static void TestSetupConfigMergeRejectsInvalidOpenAiAccountRotationFromSnapshot() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiAccountId": "acc-primary",
    "openaiAccountRotation": "invalid-value"
  }
}
""";
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
                new[] { "--with-config", "--analysis-enabled", "true" },
                seed), "setup merge invalid openai account rotation from snapshot");
    }

    private static void TestSetupAnalysisExportPathNormalization() {
        var ok = SetupAnalysisExportPath.TryNormalize(" .intelligencex\\analyzers ", out var normalized, out var error);
        AssertEqual(true, ok, "analysis export path normalized ok");
        AssertEqual(null, error, "analysis export path normalized error");
        AssertEqual(".intelligencex/analyzers", normalized, "analysis export path normalized value");

        var invalid = SetupAnalysisExportPath.TryNormalize("../outside", out _, out var invalidError);
        AssertEqual(false, invalid, "analysis export path rejects parent");
        AssertContainsText(invalidError ?? string.Empty, "analysisExportPath", "analysis export path invalid message");
    }

    private static void TestSetupAnalysisExportPathCombineRejectsRootedFileName() {
        var combined = SetupAnalysisExportPath.Combine(".intelligencex/analyzers", ".editorconfig");
        AssertEqual(".intelligencex/analyzers/.editorconfig", combined, "analysis export path combine valid");

        AssertThrows<ArgumentException>(() =>
            SetupAnalysisExportPath.Combine(".intelligencex/analyzers", "/.editorconfig"), "analysis export path combine rooted");
        AssertThrows<ArgumentException>(() =>
            SetupAnalysisExportPath.Combine(".intelligencex/analyzers", ".."), "analysis export path combine parent");
        AssertThrows<ArgumentException>(() =>
            SetupAnalysisExportPath.Combine(".intelligencex/analyzers", "nested/file"), "analysis export path combine separators");
        AssertThrows<ArgumentException>(() =>
            SetupAnalysisExportPath.Combine(".intelligencex/analyzers", "..%2f.editorconfig"), "analysis export path combine encoded traversal");
        AssertThrows<ArgumentException>(() =>
            SetupAnalysisExportPath.Combine(".intelligencex/analyzers", "name."), "analysis export path combine trailing dot");
    }

    private static void TestSetupAnalysisExportCatalogPrereqValidation() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-setup-export-prereq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            var ok = SetupRunner.ValidateLocalAnalysisCatalogForTests(temp, out var error);
            AssertEqual(false, ok, "analysis export prereq missing dirs");
            AssertContainsText(error ?? string.Empty, "Analysis/Catalog/rules", "analysis export prereq missing dirs message");

            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "csharp");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            File.WriteAllText(Path.Combine(rulesDir, "CA0001.json"), "{}");
            File.WriteAllText(Path.Combine(packsDir, "all-50.json"), "{}");

            ok = SetupRunner.ValidateLocalAnalysisCatalogForTests(temp, out error);
            AssertEqual(true, ok, "analysis export prereq valid dirs");
            AssertEqual(string.Empty, error, "analysis export prereq valid message");
        } finally {
            try {
                Directory.Delete(temp, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    private static void TestSetupAnalysisExportDuplicateTargetDetection() {
        var duplicate = SetupAnalysisExportPath.FindFirstDuplicatePath(new[] {
            ".intelligencex/analyzers/.editorconfig",
            ".intelligencex/analyzers/PSScriptAnalyzerSettings.psd1",
            ".intelligencex/analyzers/.editorconfig"
        });
        AssertEqual(".intelligencex/analyzers/.editorconfig", duplicate, "analysis export duplicate detection");

        var mixedSeparatorAndCaseDuplicate = SetupAnalysisExportPath.FindFirstDuplicatePath(new[] {
            ".intelligencex\\analyzers\\.editorconfig",
            ".intelligencex/analyzers/.EDITORCONFIG"
        });
        AssertEqual(".intelligencex/analyzers/.editorconfig", mixedSeparatorAndCaseDuplicate,
            "analysis export duplicate detection mixed separators and case");

        var none = SetupAnalysisExportPath.FindFirstDuplicatePath(new[] {
            ".intelligencex/analyzers/.editorconfig",
            ".intelligencex/analyzers/PSScriptAnalyzerSettings.psd1"
        });
        AssertEqual(null, none, "analysis export duplicate detection none");
    }

    private static void TestSetupAnalysisDisableWritesFalse() {
        var root = new System.Text.Json.Nodes.JsonObject();
        SetupAnalysisConfig.Apply(
            root,
            enabledSet: true, enabled: false,
            gateEnabledSet: false, gateEnabled: false,
            packsSet: false, packs: Array.Empty<string>());

        var analysis = root["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "analysis root");
        AssertEqual(false, analysis!["enabled"]?.GetValue<bool>(), "analysis.enabled");
    }

    private static void TestSetupAnalysisDefaultsPacksToAll50() {
        var root = new System.Text.Json.Nodes.JsonObject();
        SetupAnalysisConfig.Apply(
            root,
            enabledSet: true, enabled: true,
            gateEnabledSet: false, gateEnabled: false,
            packsSet: true, packs: Array.Empty<string>());

        var analysis = root["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "analysis root");

        var packsNode = analysis!["packs"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(packsNode, "analysis.packs");

        var packs = new List<string>();
        foreach (var item in packsNode!) {
            var value = item?.GetValue<string>();
            if (!string.IsNullOrEmpty(value)) {
                packs.Add(value);
            }
        }

        AssertSequenceEqual(new[] { "all-50" }, packs, "analysis.packs default");
    }

    private static void TestSetupBuildConfigJsonHonorsAnalysisGateOnNewConfig() {
        AssertThrows<InvalidOperationException>(() =>
            SetupRunner.BuildReviewerConfigJson(new[] {
                "--with-config",
                "--analysis-gate", "true"
            }), "config json analysis gate requires analysis enabled");

        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--with-config",
            "--analysis-enabled", "true",
            "--analysis-gate", "true"
        });
        AssertNotNull(content, "config json content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json root");

        var analysis = root!["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "analysis object");
        AssertEqual(true, analysis!["enabled"]?.GetValue<bool>(), "analysis.enabled");

        var gate = analysis["gate"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(gate, "analysis.gate");
        AssertEqual(true, gate!["enabled"]?.GetValue<bool>(), "analysis.gate.enabled");
    }

    private static void TestSetupBuildConfigJsonIncludesAnalysisRunStrict() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--with-config",
            "--analysis-enabled", "true",
            "--analysis-run-strict", "true"
        });
        AssertNotNull(content, "config json analysis run strict content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json analysis run strict root");
        var analysis = root!["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "analysis object");
        var run = analysis!["run"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(run, "analysis.run object");
        AssertEqual(true, run!["strict"]?.GetValue<bool>(), "analysis.run.strict value");
        var results = analysis["results"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(results, "analysis.results object");
        AssertEqual(0, results!["maxInline"]?.GetValue<int>(), "analysis.results.maxInline default");
    }

    private static void TestSetupBuildConfigJsonIncludesOpenAiAccountRouting() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--openai-account-id", "acc-primary",
            "--openai-account-ids", "acc-primary,acc-backup",
            "--openai-account-rotation", "round-robin",
            "--openai-account-failover", "false"
        });
        AssertNotNull(content, "config json openai account routing content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json openai account routing root");

        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json openai account routing review");
        AssertEqual("acc-primary", review!["openaiAccountId"]?.GetValue<string>(),
            "config json openai account routing primary account");
        AssertEqual("round-robin", review["openaiAccountRotation"]?.GetValue<string>(),
            "config json openai account routing rotation");
        AssertEqual(false, review["openaiAccountFailover"]?.GetValue<bool>(),
            "config json openai account routing failover");

        var ids = review["openaiAccountIds"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(ids, "config json openai account routing account ids");
        AssertEqual(2, ids!.Count, "config json openai account routing account ids count");
        AssertEqual("acc-primary", ids[0]?.GetValue<string>(), "config json openai account routing first id");
        AssertEqual("acc-backup", ids[1]?.GetValue<string>(), "config json openai account routing second id");
    }

    private static void TestSetupBuildConfigJsonIncludesReviewerRuntimePolicyDefaults() {
        var content = SetupRunner.BuildReviewerConfigJson(new[] {
            "--with-config"
        });
        AssertNotNull(content, "config json runtime policy content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json runtime policy root");
        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json runtime policy review");

        AssertEqual("pr-base", review!["reviewDiffRange"]?.GetValue<string>(), "config json review diff range");
        AssertEqual(true, review["summaryStability"]?.GetValue<bool>(), "config json summary stability");
        AssertEqual(true, review["includeReviewThreads"]?.GetValue<bool>(), "config json include review threads");
        AssertEqual(true, review["reviewThreadsIncludeBots"]?.GetValue<bool>(), "config json review threads include bots");
        AssertEqual(25, review["reviewThreadsMax"]?.GetValue<int>(), "config json review threads max");
        AssertEqual(25, review["reviewThreadsAutoResolveMax"]?.GetValue<int>(), "config json auto-resolve max");
        AssertEqual("pr-base", review["reviewThreadsAutoResolveDiffRange"]?.GetValue<string>(),
            "config json auto-resolve diff range");
        AssertEqual(true, review["reviewThreadsAutoResolveSweepNoBlockers"]?.GetValue<bool>(),
            "config json sweep no blockers");
        AssertEqual(true, review["reviewThreadsAutoResolveAIReply"]?.GetValue<bool>(),
            "config json auto-resolve ai reply");
        AssertEqual(true, review["reviewUsageSummary"]?.GetValue<bool>(), "config json usage summary");
        AssertEqual(true, review["reviewUsageBudgetGuard"]?.GetValue<bool>(), "config json usage budget guard");
        AssertEqual(true, review["reviewUsageBudgetAllowCredits"]?.GetValue<bool>(),
            "config json usage budget credits");
        AssertEqual(true, review["reviewUsageBudgetAllowWeeklyLimit"]?.GetValue<bool>(),
            "config json usage budget weekly");
    }

#endif
}
