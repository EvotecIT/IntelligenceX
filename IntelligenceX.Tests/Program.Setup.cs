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

    private static void TestSetupAutodetectJsonSerializesCheckStatusesAsLowercaseStrings() {
        var result = new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectResult {
            Status = "warn",
            Workspace = "/tmp/workspace",
            RecommendedPath = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.RefreshAuth,
            RecommendedReason = "Auth requires refresh",
            Checks = new[] {
                new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheck {
                    Name = "doctor",
                    Status = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheckStatus.Ok,
                    Message = "ok"
                },
                new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheck {
                    Name = "doctor",
                    Status = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheckStatus.Warn,
                    Message = "warn"
                },
                new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheck {
                    Name = "doctor",
                    Status = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheckStatus.Fail,
                    Message = "fail"
                }
            }
        };

        var json = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectCliRunner.SerializeForTests(result);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        AssertEqual(false, document.RootElement.TryGetProperty("Checks", out _),
            "setup autodetect json root does not use PascalCase");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
            document.RootElement.GetProperty("contractVersion").GetString(),
            "setup autodetect json contract version");
        var contractFingerprint = document.RootElement.GetProperty("contractFingerprint").GetString() ?? string.Empty;
        AssertEqual(64, contractFingerprint.Length, "setup autodetect json contract fingerprint length");
        var checks = document.RootElement.GetProperty("checks");

        AssertEqual(System.Text.Json.JsonValueKind.String, checks[0].GetProperty("status").ValueKind,
            "setup autodetect json status value kind[0]");
        AssertEqual("ok", checks[0].GetProperty("status").GetString(),
            "setup autodetect json status value[0]");
        AssertEqual("warn", checks[1].GetProperty("status").GetString(),
            "setup autodetect json status value[1]");
        AssertEqual("fail", checks[2].GetProperty("status").GetString(),
            "setup autodetect json status value[2]");
    }

    private static void TestSetupAutodetectMissingWorkspaceValueFails() {
        var (exitCode, output) = RunSetupAutodetectAndCaptureOutput(new[] { "--workspace" });
        AssertEqual(1, exitCode, "setup autodetect missing workspace exit");
        AssertContainsText(output, "Missing value for --workspace.", "setup autodetect missing workspace message");
    }

    private static void TestSetupAutodetectMissingRepoValueFails() {
        var (exitCode, output) = RunSetupAutodetectAndCaptureOutput(new[] { "--repo" });
        AssertEqual(1, exitCode, "setup autodetect missing repo exit");
        AssertContainsText(output, "Missing value for --repo.", "setup autodetect missing repo message");
    }

    private static void TestSetupAutodetectUnknownOptionFails() {
        var (exitCode, output) = RunSetupAutodetectAndCaptureOutput(new[] { "--badflag" });
        AssertEqual(1, exitCode, "setup autodetect unknown option exit");
        AssertContainsText(output, "Unknown option: --badflag", "setup autodetect unknown option message");
    }

    private static void TestSetupOnboardingContractCanonicalPaths() {
        var contractPaths = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        AssertEqual(4, contractPaths.Count, "setup contract path count with maintenance");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.NewSetupPathId, contractPaths[0].Id, "setup contract path[0] id");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.RefreshAuthPathId, contractPaths[1].Id, "setup contract path[1] id");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.CleanupPathId, contractPaths[2].Id, "setup contract path[2] id");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.MaintenancePathId, contractPaths[3].Id, "setup contract path[3] id");

        var contractPathsNoMaintenance = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetPaths(includeMaintenancePath: false);
        AssertEqual(3, contractPathsNoMaintenance.Count, "setup contract path count without maintenance");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.NewSetupPathId, contractPathsNoMaintenance[0].Id,
            "setup contract path[0] id without maintenance");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.RefreshAuthPathId, contractPathsNoMaintenance[1].Id,
            "setup contract path[1] id without maintenance");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.CleanupPathId, contractPathsNoMaintenance[2].Id,
            "setup contract path[2] id without maintenance");

        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.NewSetupPathId,
            IntelligenceX.Setup.Onboarding.SetupOnboardingContract.PathIdFromOperation(IntelligenceX.Setup.Onboarding.SetupOnboardingOperationIds.Setup),
            "setup contract map setup operation");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.RefreshAuthPathId,
            IntelligenceX.Setup.Onboarding.SetupOnboardingContract.PathIdFromOperation(IntelligenceX.Setup.Onboarding.SetupOnboardingOperationIds.UpdateSecret),
            "setup contract map update-secret operation");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.CleanupPathId,
            IntelligenceX.Setup.Onboarding.SetupOnboardingContract.PathIdFromOperation(IntelligenceX.Setup.Onboarding.SetupOnboardingOperationIds.Cleanup),
            "setup contract map cleanup operation");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.NewSetupPathId,
            IntelligenceX.Setup.Onboarding.SetupOnboardingContract.PathIdFromOperation("unknown"),
            "setup contract map unknown operation");

        var cliPaths = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.GetAll();
        AssertEqual(contractPaths.Count, cliPaths.Count, "setup cli path count matches contract");
        for (var i = 0; i < contractPaths.Count; i++) {
            AssertEqual(contractPaths[i].Id, cliPaths[i].Id, $"setup cli path[{i}] id matches contract");
            AssertEqual(contractPaths[i].DisplayName, cliPaths[i].DisplayName, $"setup cli path[{i}] display name matches contract");
            AssertEqual(contractPaths[i].Description, cliPaths[i].Description, $"setup cli path[{i}] description matches contract");
            AssertEqual(contractPaths[i].RequiresGitHubAuth, cliPaths[i].RequiresGitHubAuth,
                $"setup cli path[{i}] requires github auth matches contract");
            AssertEqual(contractPaths[i].RequiresRepoSelection, cliPaths[i].RequiresRepoSelection,
                $"setup cli path[{i}] requires repo selection matches contract");
            AssertEqual(contractPaths[i].RequiresAiAuth, cliPaths[i].RequiresAiAuth,
                $"setup cli path[{i}] requires ai auth matches contract");
            var expectedOperation = contractPaths[i].Operation switch {
                "update-secret" => SetupApplyOperation.UpdateSecret,
                "cleanup" => SetupApplyOperation.Cleanup,
                _ => SetupApplyOperation.Setup
            };
            AssertEqual(expectedOperation, cliPaths[i].DefaultOperation, $"setup cli path[{i}] operation matches contract");
            AssertSequenceEqual(contractPaths[i].Flow, cliPaths[i].Flow, $"setup cli path[{i}] flow matches contract");
        }

        var mutableFlow = contractPaths[0].Flow as string[];
        AssertNotNull(mutableFlow, "setup contract flow returns concrete array for defensive copy check");
        var originalFlowStep = mutableFlow![0];
        mutableFlow[0] = "Mutated step";
        var freshPaths = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        AssertEqual(originalFlowStep, freshPaths[0].Flow[0], "setup contract returns defensive path copies");
    }

    private static void TestSetupWizardPathIdMapsToOperation() {
        AssertEqual(IntelligenceX.Cli.Setup.Wizard.WizardOperation.Setup,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolveOperationFromPathIdForTests(
                IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.NewSetup),
            "setup wizard path new-setup maps to setup operation");
        AssertEqual(IntelligenceX.Cli.Setup.Wizard.WizardOperation.UpdateSecret,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolveOperationFromPathIdForTests(
                IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.RefreshAuth),
            "setup wizard path refresh-auth maps to update-secret operation");
        AssertEqual(IntelligenceX.Cli.Setup.Wizard.WizardOperation.Cleanup,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolveOperationFromPathIdForTests(
                IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.Cleanup),
            "setup wizard path cleanup maps to cleanup operation");
        AssertEqual(IntelligenceX.Cli.Setup.Wizard.WizardOperation.Setup,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolveOperationFromPathIdForTests("unknown-path"),
            "setup wizard unknown path falls back to setup operation");
    }

    private static void TestSetupWizardOperationMapsToPathId() {
        AssertEqual(IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.NewSetup,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolvePathIdFromOperationForTests(
                IntelligenceX.Cli.Setup.Wizard.WizardOperation.Setup),
            "setup wizard setup operation maps to new-setup path");
        AssertEqual(IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.RefreshAuth,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolvePathIdFromOperationForTests(
                IntelligenceX.Cli.Setup.Wizard.WizardOperation.UpdateSecret),
            "setup wizard update-secret operation maps to refresh-auth path");
        AssertEqual(IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.Cleanup,
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolvePathIdFromOperationForTests(
                IntelligenceX.Cli.Setup.Wizard.WizardOperation.Cleanup),
            "setup wizard cleanup operation maps to cleanup path");
    }

    private static void TestSetupWizardAutoDetectReasonNormalization() {
        AssertEqual("No recommendation details provided.",
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.NormalizeAutoDetectRecommendedReasonForTests(null),
            "setup wizard auto-detect reason normalization null");
        AssertEqual("No recommendation details provided.",
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.NormalizeAutoDetectRecommendedReasonForTests("  "),
            "setup wizard auto-detect reason normalization whitespace");
        AssertEqual("Detected existing setup.",
            IntelligenceX.Cli.Setup.Wizard.WizardRunner.NormalizeAutoDetectRecommendedReasonForTests("  Detected existing setup. "),
            "setup wizard auto-detect reason normalization trim");
    }

    private static void TestSetupOnboardingContractCommandTemplates() {
        var templates = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetCommandTemplates();
        AssertEqual("intelligencex setup autodetect --json", templates.AutoDetect, "setup contract auto-detect template");
        AssertEqual("intelligencex setup --repo owner/name --with-config --dry-run", templates.NewSetupDryRun,
            "setup contract new-setup dry-run template");
        AssertEqual("intelligencex setup --repo owner/name --with-config", templates.NewSetupApply,
            "setup contract new-setup apply template");
        AssertEqual("intelligencex setup --repo owner/name --update-secret --auth-b64 <base64> --dry-run", templates.RefreshAuthDryRun,
            "setup contract refresh-auth dry-run template");
        AssertEqual("intelligencex setup --repo owner/name --update-secret --auth-b64 <base64>", templates.RefreshAuthApply,
            "setup contract refresh-auth apply template");
        AssertEqual("intelligencex setup --repo owner/name --cleanup --dry-run", templates.CleanupDryRun,
            "setup contract cleanup dry-run template");
        AssertEqual("intelligencex setup --repo owner/name --cleanup", templates.CleanupApply,
            "setup contract cleanup apply template");
        AssertEqual("intelligencex setup web", templates.MaintenanceWizard, "setup contract maintenance wizard template");

        var fingerprintWithMaintenance = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true);
        var fingerprintWithoutMaintenance = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: false);
        AssertEqual(64, fingerprintWithMaintenance.Length, "setup contract fingerprint with maintenance length");
        AssertEqual(64, fingerprintWithoutMaintenance.Length, "setup contract fingerprint without maintenance length");
        AssertEqual(false, string.Equals(fingerprintWithMaintenance, fingerprintWithoutMaintenance, StringComparison.Ordinal),
            "setup contract fingerprint differs by maintenance mode");

        AssertEqual(true, IsLowerHex(fingerprintWithMaintenance), "setup contract fingerprint with maintenance is lowercase hex");
        AssertEqual(true, IsLowerHex(fingerprintWithoutMaintenance), "setup contract fingerprint without maintenance is lowercase hex");
    }

    private static void TestSetupOnboardingContractVerificationMatchesCanonicalValues() {
        var result = IntelligenceX.Setup.Onboarding.SetupOnboardingContractVerification.Verify(
            autodetectContractVersion: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
            autodetectContractFingerprint: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            packContractVersion: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
            packContractFingerprint: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            includeMaintenancePath: true);

        AssertEqual(true, result.IsMatch, "setup contract verification is match");
        AssertEqual(0, result.MismatchCount, "setup contract verification mismatch count");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion, result.ExpectedContractVersion,
            "setup contract verification expected version");
    }

    private static void TestSetupOnboardingContractVerificationDetectsMismatches() {
        var result = IntelligenceX.Setup.Onboarding.SetupOnboardingContractVerification.Verify(
            autodetectContractVersion: "1900-01-01.0",
            autodetectContractFingerprint: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            packContractVersion: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
            packContractFingerprint: "not-the-right-fingerprint",
            includeMaintenancePath: true);

        AssertEqual(false, result.IsMatch, "setup contract verification mismatch state");
        AssertEqual(true, result.MismatchCount >= 2, "setup contract verification mismatch count");

        var hasVersionMismatch = false;
        var hasFingerprintMismatch = false;
        for (var i = 0; i < result.Mismatches.Count; i++) {
            var mismatch = result.Mismatches[i];
            if (string.Equals(mismatch.Field, "contract_version", StringComparison.Ordinal)) {
                hasVersionMismatch = true;
            }
            if (string.Equals(mismatch.Field, "contract_fingerprint", StringComparison.Ordinal)) {
                hasFingerprintMismatch = true;
            }
        }

        AssertEqual(true, hasVersionMismatch, "setup contract verification version mismatch captured");
        AssertEqual(true, hasFingerprintMismatch, "setup contract verification fingerprint mismatch captured");
    }

    private static void TestSetupOnboardingContractVerificationRejectsMissingAutodetectMetadata() {
        AssertThrows<ArgumentException>(() =>
            IntelligenceX.Setup.Onboarding.SetupOnboardingContractVerification.Verify(
                autodetectContractVersion: "",
                autodetectContractFingerprint: "abc"),
            "setup contract verification missing autodetect version");
        AssertThrows<ArgumentException>(() =>
            IntelligenceX.Setup.Onboarding.SetupOnboardingContractVerification.Verify(
                autodetectContractVersion: IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
                autodetectContractFingerprint: ""),
            "setup contract verification missing autodetect fingerprint");
    }

    private static bool IsLowerHex(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            var isDigit = c >= '0' && c <= '9';
            var isLowerHexLetter = c >= 'a' && c <= 'f';
            if (!isDigit && !isLowerHexLetter) {
                return false;
            }
        }
        return true;
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
        AssertEqual(1, CountOccurrences(content, "# INTELLIGENCEX:BEGIN"),
            "workflow upgrade has single managed begin marker");
        AssertEqual(1, CountOccurrences(content, "# INTELLIGENCEX:END"),
            "workflow upgrade has single managed end marker");
        AssertEqual(1, CountOccurrences(content, "provider: copilot"),
            "workflow upgrade has single provider override");

        var secondPass = SetupRunner.BuildWorkflowYamlFromSeedForTests(
            new[] { "--provider", "copilot" },
            content);
        AssertEqual(content, secondPass, "workflow upgrade idempotent on second pass");
    }

    private static void TestSetupPostApplyVerifySetupPassesWithManagedWorkflowAndSecret() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/12",
            PullRequestUrl = "https://github.com/owner/repo/pull/12"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-setup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = true,
            WorkflowManaged = true,
            ConfigExists = true,
            RepoSecretLookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult.Present()
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(false, verify.Skipped, "post-apply setup verify skipped");
        AssertEqual(true, verify.Passed, "post-apply setup verify passed");
        AssertEqual("pull-request", verify.CheckedRefSource, "post-apply setup verify ref source");
        AssertEqual("intelligencex-setup/20260211", verify.CheckedRef, "post-apply setup verify ref");
    }

    private static void TestSetupPostApplyVerifyCleanupDetectsResidualConfig() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Cleanup,
            KeepSecret = false,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Cleanup complete. PR created: https://github.com/owner/repo/pull/21",
            PullRequestUrl = "https://github.com/owner/repo/pull/21"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-cleanup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = false,
            WorkflowManaged = false,
            ConfigExists = true,
            RepoSecretLookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult.Missing()
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(false, verify.Passed, "post-apply cleanup verify failed");

        var configCheck = verify.Checks.Find(check => check.Name == "Reviewer config");
        AssertNotNull(configCheck, "post-apply cleanup config check exists");
        AssertEqual(false, configCheck!.Passed, "post-apply cleanup config check failed");
    }

    private static void TestSetupPostApplyVerifySetupAllowsUnknownBranchStateWithPr() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            SkipSecret = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/13",
            PullRequestUrl = "https://github.com/owner/repo/pull/13"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = null,
            CheckRefSource = "pull-request",
            WorkflowExists = null,
            WorkflowManaged = null,
            ConfigExists = null,
            RepoSecretLookup = null
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(true, verify.Passed, "post-apply setup verify passes when PR exists and branch state is unknown");
        AssertEqual(true, verify.Checks.Exists(check => check.Name == "Workflow" && check.Skipped),
            "post-apply setup workflow check skipped when branch state unknown");
    }

    private static void TestSetupPostApplyVerifySecretLookupUnauthorizedFailsDeterministically() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.UpdateSecret,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Secret updated: INTELLIGENCEX_AUTH_B64"
        };
        var observed = new SetupPostApplyObservedState {
            RepoSecretLookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult.Unauthorized(
                "GitHub API returned 401 Unauthorized.")
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(false, verify.Passed, "post-apply unauthorized secret lookup fails");
        var secretCheck = verify.Checks.Find(check => check.Name == "Repo secret");
        AssertNotNull(secretCheck, "post-apply unauthorized secret check exists");
        AssertEqual(false, secretCheck!.Skipped, "post-apply unauthorized secret check not skipped");
        AssertEqual(false, secretCheck.Passed, "post-apply unauthorized secret check failed");
        AssertEqual("unauthorized", secretCheck.Actual, "post-apply unauthorized secret check actual");
    }

    private static void TestSetupPostApplyVerifyIncludesLatestWorkflowRunLink() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            SkipSecret = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/22",
            PullRequestUrl = "https://github.com/owner/repo/pull/22"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-setup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = true,
            WorkflowManaged = true,
            ConfigExists = true,
            LatestWorkflowRun = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunInfo(
                id: 120,
                url: "https://github.com/owner/repo/actions/runs/120",
                status: "completed",
                conclusion: "success",
                headBranch: "main",
                @event: "pull_request",
                createdAt: DateTimeOffset.Parse("2026-02-11T19:45:00Z", System.Globalization.CultureInfo.InvariantCulture))
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        var latestRun = verify.Checks.Find(check => check.Name == "Latest workflow run");
        AssertNotNull(latestRun, "post-apply latest workflow run check exists");
        AssertEqual(false, latestRun!.Skipped, "post-apply latest workflow run check not skipped");
        AssertEqual(true, latestRun.Passed, "post-apply latest workflow run check passed");
        AssertContainsText(latestRun.Note ?? string.Empty, "actions/runs/120", "post-apply latest workflow run note includes url");
    }

    private static void TestSetupPostApplyVerifyWorkflowRunLookupFailureIsNotReportedAsNone() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            SkipSecret = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/22",
            PullRequestUrl = "https://github.com/owner/repo/pull/22"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-setup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = true,
            WorkflowManaged = true,
            ConfigExists = true,
            WorkflowRunLookupStatus = "forbidden",
            WorkflowRunLookupNote = "GitHub API returned 403 Forbidden."
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        var latestRun = verify.Checks.Find(check => check.Name == "Latest workflow run");
        AssertNotNull(latestRun, "post-apply latest workflow run failure check exists");
        AssertEqual(true, latestRun!.Skipped, "post-apply latest workflow run failure check skipped");
        AssertEqual("forbidden", latestRun.Actual, "post-apply latest workflow run failure status");
        AssertContainsText(latestRun.Note ?? string.Empty, "403", "post-apply latest workflow run failure note");
    }

    private static void TestSetupPostApplyVerifyDoesNotSwallowUnexpectedWorkflowLookupExceptions() {
        using var client = CreateGitHubRepoClientForTests((_, _) => throw new NullReferenceException("boom"));
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.UpdateSecret,
            Provider = "copilot",
            ExitSuccess = true,
            Output = "Secret updated."
        };

        AssertThrows<NullReferenceException>(() =>
                SetupPostApplyVerifier.VerifyAsync(client, context).GetAwaiter().GetResult(),
            "post-apply verify unexpected workflow lookup exception");
    }

    private static void TestWizardPostApplyVerifySkipsCallbackWhenApplyFails() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            ExitSuccess = false
        };

        var verifyCalls = 0;
        var verify = IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolvePostApplyVerificationForTests(
            context,
            () => {
                verifyCalls++;
                return System.Threading.Tasks.Task.FromResult(new SetupPostApplyVerification {
                    Repo = "owner/repo",
                    Operation = "setup",
                    Passed = true
                });
            }).GetAwaiter().GetResult();

        AssertEqual(0, verifyCalls, "wizard post-apply verify callback skipped on failed apply");
        AssertEqual(true, verify.Skipped, "wizard post-apply verify skipped on failed apply");
        AssertEqual(false, verify.Passed, "wizard post-apply verify failed status on failed apply");
        AssertContainsText(verify.Note ?? string.Empty, "failed", "wizard post-apply verify failure note");
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

    private static void TestGitHubRepoClientSecretLookupMapsStatusCodes() {
        static IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult RunLookup(
            System.Net.HttpStatusCode statusCode,
            string? reasonPhrase = null) {
            using var client = CreateGitHubRepoClientForTests((_, _) => {
                var response = new System.Net.Http.HttpResponseMessage(statusCode);
                if (reasonPhrase is not null) {
                    response.ReasonPhrase = reasonPhrase;
                }
                return Task.FromResult(response);
            });
            return client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
        }

        var present = RunLookup(System.Net.HttpStatusCode.OK);
        AssertEqual("present", present.Status, "repo client secret status present");
        AssertEqual(true, present.Exists, "repo client secret exists true");
        AssertEqual(null, present.Note, "repo client secret present note");

        var missing = RunLookup(System.Net.HttpStatusCode.NotFound);
        AssertEqual("missing", missing.Status, "repo client secret status missing");
        AssertEqual(false, missing.Exists, "repo client secret exists false");
        AssertEqual(null, missing.Note, "repo client secret missing note");

        var unauthorized = RunLookup(System.Net.HttpStatusCode.Unauthorized);
        AssertEqual("unauthorized", unauthorized.Status, "repo client secret status unauthorized");
        AssertEqual(null, unauthorized.Exists, "repo client secret unauthorized exists unknown");
        AssertContainsText(unauthorized.Note ?? string.Empty, "401 Unauthorized", "repo client secret unauthorized note");

        var forbidden = RunLookup(System.Net.HttpStatusCode.Forbidden);
        AssertEqual("forbidden", forbidden.Status, "repo client secret status forbidden");
        AssertEqual(null, forbidden.Exists, "repo client secret forbidden exists unknown");
        AssertContainsText(forbidden.Note ?? string.Empty, "403 Forbidden", "repo client secret forbidden note");

        var rateLimited = RunLookup((System.Net.HttpStatusCode)429);
        AssertEqual("rate_limited", rateLimited.Status, "repo client secret status rate limited");
        AssertEqual(null, rateLimited.Exists, "repo client secret rate limited exists unknown");
        AssertContainsText(rateLimited.Note ?? string.Empty, "429 Too Many Requests", "repo client secret rate limited note");

        var unknown = RunLookup(System.Net.HttpStatusCode.InternalServerError, "Boom");
        AssertEqual("unknown", unknown.Status, "repo client secret status unknown");
        AssertEqual(null, unknown.Exists, "repo client secret unknown exists unknown");
        AssertContainsText(unknown.Note ?? string.Empty, "500 Boom", "repo client secret unknown note");
    }

    private static void TestGitHubRepoClientSecretLookupMapsClientExceptions() {
        using (var httpFailureClient = CreateGitHubRepoClientForTests((_, _) =>
                   throw new HttpRequestException("socket failed"))) {
            var httpFailure = httpFailureClient.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64")
                .GetAwaiter().GetResult();
            AssertEqual("unknown", httpFailure.Status, "repo client secret http failure status");
            AssertEqual(null, httpFailure.Exists, "repo client secret http failure exists");
            AssertContainsText(httpFailure.Note ?? string.Empty, "HTTP client error", "repo client secret http failure note");
        }

        using (var invalidOperationClient = CreateGitHubRepoClientForTests((_, _) =>
                   throw new InvalidOperationException("invalid request uri"))) {
            var invalidOperation = invalidOperationClient.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64")
                .GetAwaiter().GetResult();
            AssertEqual("unknown", invalidOperation.Status, "repo client secret invalid operation status");
            AssertEqual(null, invalidOperation.Exists, "repo client secret invalid operation exists");
            AssertContainsText(invalidOperation.Note ?? string.Empty, "configuration error", "repo client secret invalid operation note");
        }
    }

    private static void TestGitHubRepoClientSecretLookupCancellationPropagates() {
        using var client = CreateGitHubRepoClientForTests((_, _) => throw new OperationCanceledException("cancelled"));
        AssertThrows<OperationCanceledException>(() =>
                client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult(),
            "repo client secret cancellation");
    }

    private static void TestGitHubRepoClientListWorkflowRunsParsesLatestRun() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "workflow_runs": [
    {
      "id": 42,
      "html_url": "https://github.com/owner/repo/actions/runs/42",
      "status": "completed",
      "conclusion": "success",
      "head_branch": "main",
      "event": "pull_request",
      "created_at": "2026-02-11T20:00:00Z"
    }
  ]
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var lookup = client.ListWorkflowRunsAsync("owner", "repo", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(true, lookup.Success, "repo client workflow runs lookup success");
        AssertEqual("ok", lookup.Status, "repo client workflow runs lookup status");
        AssertEqual(1, lookup.Runs.Count, "repo client workflow runs count");
        AssertEqual(42L, lookup.Runs[0].Id, "repo client workflow run id");
        AssertEqual("completed", lookup.Runs[0].Status, "repo client workflow run status");
        AssertEqual("success", lookup.Runs[0].Conclusion, "repo client workflow run conclusion");
        AssertContainsText(lookup.Runs[0].Url ?? string.Empty, "actions/runs/42", "repo client workflow run url");
    }

    private static void TestGitHubRepoClientWorkflowRunLookupResultUsesDefensiveCopy() {
        var sourceRuns = new List<IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunInfo> {
            new(
                id: 7,
                url: "https://github.com/owner/repo/actions/runs/7",
                status: "completed",
                conclusion: "success",
                headBranch: "main",
                @event: "pull_request",
                createdAt: DateTimeOffset.Parse("2026-02-11T20:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
        };
        var lookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunLookupResult.Ok(sourceRuns);
        sourceRuns.Clear();

        AssertEqual("ok", lookup.Status, "repo client workflow runs defensive copy status");
        AssertEqual(true, lookup.Success, "repo client workflow runs defensive copy success");
        AssertEqual(1, lookup.Runs.Count, "repo client workflow runs defensive copy count");
        AssertEqual(false, lookup.Runs is List<IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunInfo>,
            "repo client workflow runs defensive copy list exposure");
    }

    private static void TestGitHubRepoClientListWorkflowRunsInvalidPayloadReturnsEmpty() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "workflow_runs": "invalid"
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var lookup = client.ListWorkflowRunsAsync("owner", "repo", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(false, lookup.Success, "repo client workflow runs invalid payload lookup failure");
        AssertEqual("parse_error", lookup.Status, "repo client workflow runs invalid payload status");
        AssertEqual(0, lookup.Runs.Count, "repo client workflow runs invalid payload returns empty");
    }

    private static void TestGitHubRepoClientListWorkflowRunsEncodesPathSegments() {
        string? absolutePath = null;
        using var client = CreateGitHubRepoClientForTests((request, _) => {
            absolutePath = request.RequestUri?.AbsolutePath;
            var payload = """
{
  "workflow_runs": []
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var lookup = client.ListWorkflowRunsAsync("owner+team", "repo name", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(true, lookup.Success, "repo client workflow runs path encoding lookup success");
        AssertContainsText(absolutePath ?? string.Empty, "/repos/owner%2Bteam/repo%20name/actions/workflows/",
            "repo client workflow runs owner/repo segments encoded");
        AssertContainsText(absolutePath ?? string.Empty, ".github%2Fworkflows%2Freview-intelligencex.yml",
            "repo client workflow runs workflow path encoded");
    }

    private static void TestGitHubRepoClientListWorkflowRunsMapsUnauthorized() {
        using var client = CreateGitHubRepoClientForTests((_, _) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)));

        var lookup = client.ListWorkflowRunsAsync("owner", "repo", ".github/workflows/review-intelligencex.yml", maxCount: 1)
            .GetAwaiter().GetResult();
        AssertEqual(false, lookup.Success, "repo client workflow runs unauthorized lookup failure");
        AssertEqual("unauthorized", lookup.Status, "repo client workflow runs unauthorized status");
        AssertEqual(0, lookup.Runs.Count, "repo client workflow runs unauthorized runs empty");
        AssertContainsText(lookup.Note ?? string.Empty, "401", "repo client workflow runs unauthorized note");
    }

    private static void TestGitHubRepoClientFileFetchCancellationPropagates() {
        using var client = CreateGitHubRepoClientForTests((_, _) => throw new OperationCanceledException("cancelled"));
        AssertThrows<OperationCanceledException>(() =>
                client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult(),
            "repo client file fetch cancellation");
    }

    private static void TestGitHubRepoClientFileFetchInvalidBase64ReturnsNull() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "sha": "abc123",
  "content": "@@@not-base64@@@"
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var file = client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult();
        AssertEqual(null, file, "repo client file fetch invalid base64");
    }

    private static void TestGitHubRepoClientFileFetchMissingShaReturnsNull() {
        using var client = CreateGitHubRepoClientForTests((_, _) => {
            var payload = """
{
  "content": "e30="
}
""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new System.Net.Http.StringContent(payload)
            });
        });

        var file = client.TryGetFileAsync("owner", "repo", ".intelligencex/reviewer.json", "main").GetAwaiter().GetResult();
        AssertEqual(null, file, "repo client file fetch missing sha");
    }

    private static void TestGitHubRepoClientInjectedHttpClientAppliesDefaultHeaders() {
        System.Net.Http.HttpRequestMessage? capturedRequest = null;
        using var client = CreateGitHubRepoClientForTests((request, _) => {
            capturedRequest = request;
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }, token: "injected-token");

        var result = client.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
        AssertEqual("missing", result.Status, "repo client injected headers lookup status");
        AssertNotNull(capturedRequest, "repo client injected headers captured request");
        AssertEqual("Bearer", capturedRequest!.Headers.Authorization?.Scheme, "repo client injected headers auth scheme");
        AssertEqual("injected-token", capturedRequest.Headers.Authorization?.Parameter, "repo client injected headers auth token");
        AssertEqual(true, capturedRequest.Headers.UserAgent.ToString().Contains("IntelligenceX.Cli"), "repo client injected headers user agent");
        AssertEqual(true, capturedRequest.Headers.Accept.ToString().Contains("application/vnd.github+json"), "repo client injected headers accept");
        AssertEqual(true,
            capturedRequest.Headers.TryGetValues("X-GitHub-Api-Version", out var values)
            && values.Contains("2022-11-28"),
            "repo client injected headers api version");
    }

    private static void TestGitHubRepoClientReusedInjectedHttpClientRemainsIdempotent() {
        var requests = new List<System.Net.Http.HttpRequestMessage>();
        using var http = new System.Net.Http.HttpClient(new DelegateHttpMessageHandler((request, _) => {
            requests.Add(request);
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        })) {
            BaseAddress = new Uri("https://api.github.com")
        };

        using (var first = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(http, token: "token-one")) {
            var firstResult = first.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
            AssertEqual("missing", firstResult.Status, "repo client reused injected first status");
        }

        using (var second = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(http, token: "token-two")) {
            var secondResult = second.TryRepoSecretExistsAsync("owner", "repo", "INTELLIGENCEX_AUTH_B64").GetAwaiter().GetResult();
            AssertEqual("missing", secondResult.Status, "repo client reused injected second status");
        }

        AssertEqual(true, requests.Count >= 2, "repo client reused injected requests captured");
        var lastRequest = requests[requests.Count - 1];
        AssertEqual("token-two", lastRequest.Headers.Authorization?.Parameter, "repo client reused injected latest auth token");

        var userAgentCount = 0;
        foreach (var _ in lastRequest.Headers.UserAgent) {
            userAgentCount++;
        }
        AssertEqual(1, userAgentCount, "repo client reused injected user-agent count");

        var acceptCount = 0;
        foreach (var _ in lastRequest.Headers.Accept) {
            acceptCount++;
        }
        AssertEqual(1, acceptCount, "repo client reused injected accept count");

        var versionCount = 0;
        if (lastRequest.Headers.TryGetValues("X-GitHub-Api-Version", out var apiVersions)) {
            foreach (var _ in apiVersions) {
                versionCount++;
            }
        }
        AssertEqual(1, versionCount, "repo client reused injected api version count");
    }

    private static IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient CreateGitHubRepoClientForTests(
        Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> sendAsync,
        string token = "test-token") {
        var http = new System.Net.Http.HttpClient(new DelegateHttpMessageHandler(sendAsync)) {
            BaseAddress = new Uri("https://api.github.com")
        };
        return new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient(http, token);
    }

    private static (int ExitCode, string Output) RunSetupAutodetectAndCaptureOutput(string[] args) {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectCliRunner.RunAsync(args)
                .GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private sealed class DelegateHttpMessageHandler : System.Net.Http.HttpMessageHandler {
        private readonly Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> _sendAsync;

        public DelegateHttpMessageHandler(
            Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> sendAsync) {
            _sendAsync = sendAsync;
        }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken) {
            return _sendAsync(request, cancellationToken);
        }
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
