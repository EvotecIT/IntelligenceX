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
            AnalysisPacks = "all-100",
            AnalysisExportPath = ".intelligencex/analyzers"
        };

        var args = SetupArgsBuilder.FromPlan(plan);
        AssertSequenceEqual(new[] {
            "--repo", "owner/repo",
            "--analysis-enabled", "false"
        }, args, "setup args analysis disabled");
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
        var content = SetupRunner.BuildReviewerConfigJson(new[] { "--analysis-gate", "true" });
        AssertNotNull(content, "config json content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json root");

        var analysis = root!["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "analysis object");
        AssertEqual(true, analysis!["enabled"]?.GetValue<bool>(), "analysis.enabled inferred");

        var gate = analysis["gate"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(gate, "analysis.gate");
        AssertEqual(true, gate!["enabled"]?.GetValue<bool>(), "analysis.gate.enabled");
    }

    private static void TestSetupBuildConfigJsonMergePreservesReviewSettingsWhenEnablingAnalysis() {
        var seed = """
{
  "review": {
    "provider": "openai",
    "openaiTransport": "native",
    "model": "gpt-5-mini",
    "profile": "security",
    "mode": "summary",
    "commentMode": "sticky",
    "includeIssueComments": false,
    "includeReviewComments": true,
    "includeRelatedPullRequests": false,
    "progressUpdates": false,
    "diagnostics": false,
    "preflight": true,
    "preflightTimeoutSeconds": 30,
    "customReviewFlag": "keep-me"
  }
}
""";

        var content = SetupRunner.BuildReviewerConfigJsonFromSeedForTests(
            new[] { "--analysis-enabled", "true", "--analysis-gate", "true" },
            seed);
        AssertNotNull(content, "config json merge content");

        var root = System.Text.Json.Nodes.JsonNode.Parse(content) as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(root, "config json merge root");

        var review = root!["review"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(review, "config json merge review");
        AssertEqual("keep-me", review!["customReviewFlag"]?.GetValue<string>(), "config json merge keeps custom review key");
        AssertEqual("security", review["profile"]?.GetValue<string>(), "config json merge keeps existing profile");

        var analysis = root["analysis"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(analysis, "config json merge analysis object");
        AssertEqual(true, analysis!["enabled"]?.GetValue<bool>(), "config json merge analysis.enabled");

        var gate = analysis["gate"] as System.Text.Json.Nodes.JsonObject;
        AssertNotNull(gate, "config json merge analysis.gate");
        AssertEqual(true, gate!["enabled"]?.GetValue<bool>(), "config json merge analysis.gate.enabled");

        var packsNode = analysis["packs"] as System.Text.Json.Nodes.JsonArray;
        AssertNotNull(packsNode, "config json merge analysis.packs");
        AssertEqual(true, packsNode!.Count > 0, "config json merge analysis.packs has values");
    }

    private static void TestSetupWorkflowUpgradePreservesCustomSectionsOutsideManagedBlock() {
        var seed = """
name: IntelligenceX Review

on:
  pull_request:
    types: [opened, synchronize, reopened, ready_for_review]

jobs:
  custom_pre:
    runs-on: ubuntu-latest
    steps:
      - run: echo pre
  # INTELLIGENCEX:BEGIN
  review:
    uses: evotecit/github-actions/.github/workflows/review-intelligencex.yml@master
    with:
      provider: openai
      model: gpt-5.3-codex
  # INTELLIGENCEX:END
  custom_post:
    runs-on: ubuntu-latest
    steps:
      - run: echo post
""";

        var content = SetupRunner.BuildWorkflowYamlFromSeedForTests(
            new[] { "--provider", "copilot" },
            seed);

        AssertContainsText(content, "custom_pre:", "workflow upgrade keeps custom_pre");
        AssertContainsText(content, "custom_post:", "workflow upgrade keeps custom_post");
        AssertContainsText(content, "provider: copilot", "workflow upgrade updates managed provider");
        AssertContainsText(content, "# INTELLIGENCEX:BEGIN", "workflow upgrade keeps managed begin marker");
        AssertContainsText(content, "# INTELLIGENCEX:END", "workflow upgrade keeps managed end marker");
    }

    private static void TestGitHubRepoDetectorParsesRemoteUrls() {
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("https://github.com/owner/repo.git"), "https git");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("https://github.com/owner/repo"), "https no git");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("git@github.com:owner/repo.git"), "ssh scp");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("ssh://git@github.com/owner/repo.git"), "ssh url");
        AssertEqual("owner/repo", GitHubRepoDetector.ParseRepoFromRemoteUrl("ssh://git@github.mycorp.local/owner/repo.git"), "ssh ghe");
        AssertEqual(null, GitHubRepoDetector.ParseRepoFromRemoteUrl("not a url"), "invalid url");
    }

    private static void TestGitHubRepoDetectorParsesGitConfigRemoteSection() {
        var config = """
[core]
    repositoryformatversion = 0
    url = SHOULD_NOT_MATCH
[remote "origin"]
    fetch = +refs/heads/*:refs/remotes/origin/*
    url = git@github.com:EvotecIT/IntelligenceX.git
[branch "main"]
    remote = origin
    merge = refs/heads/main
    url = ALSO_SHOULD_NOT_MATCH
[remote "upstream"]
    url = https://github.com/other/repo.git
""";

        AssertEqual("git@github.com:EvotecIT/IntelligenceX.git",
            GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "origin"),
            "origin url");
        AssertEqual("https://github.com/other/repo.git",
            GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "upstream"),
            "upstream url");
        AssertEqual(null, GitHubRepoDetector.TryReadRemoteUrlFromGitConfigText(config, "missing"), "missing remote");
    }

    private static void TestGitHubSecretsRejectEmptyValue() {
        using var client = new GitHubSecretsClient("token");
        AssertThrows<InvalidOperationException>(() =>
            client.SetRepoSecretAsync("owner", "repo", "SECRET_NAME", "").GetAwaiter().GetResult(),
            "repo secret empty");
        AssertThrows<InvalidOperationException>(() =>
            client.SetOrgSecretAsync("org", "SECRET_NAME", " ").GetAwaiter().GetResult(),
            "org secret empty");
    }

    private static void TestReleaseReviewerEnvToken() {
        var previous = Environment.GetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN", "token-value");
            var options = new ReleaseReviewerOptions();
            ReleaseReviewerOptions.ApplyEnvDefaults(options);
            AssertEqual("token-value", options.Token, "reviewer token");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_REVIEWER_TOKEN", previous);
        }
    }
#endif
}
