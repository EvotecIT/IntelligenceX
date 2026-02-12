namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
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
        var commandTemplates = document.RootElement.GetProperty("commandTemplates");
        AssertEqual("intelligencex setup autodetect --json",
            commandTemplates.GetProperty("autoDetect").GetString(),
            "setup autodetect json command template auto-detect");
        AssertEqual("intelligencex setup --repo owner/name --with-config",
            commandTemplates.GetProperty("newSetupApply").GetString(),
            "setup autodetect json command template setup apply");
        AssertEqual("intelligencex setup --repo owner/name --update-secret --auth-b64 <base64>",
            commandTemplates.GetProperty("refreshAuthApply").GetString(),
            "setup autodetect json command template update-secret apply");
        AssertEqual("intelligencex setup --repo owner/name --cleanup",
            commandTemplates.GetProperty("cleanupApply").GetString(),
            "setup autodetect json command template cleanup apply");
        var expectedPaths = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        var paths = document.RootElement.GetProperty("paths");
        AssertEqual(expectedPaths.Count, paths.GetArrayLength(), "setup autodetect json path count");
        for (var i = 0; i < expectedPaths.Count; i++) {
            AssertEqual(expectedPaths[i].Id,
                paths[i].GetProperty("id").GetString(),
                $"setup autodetect json path[{i}] id");
            AssertEqual(expectedPaths[i].Operation,
                paths[i].GetProperty("operation").GetString(),
                $"setup autodetect json path[{i}] operation");
        }
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

    private static void TestSetupWizardAutoDetectPromptRecommendationFallback() {
        var fallback = IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolveAutoDetectPromptRecommendationForTests(null);
        AssertEqual(IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.NewSetup, fallback.RecommendedPathId,
            "setup wizard auto-detect prompt fallback path id");
        AssertEqual("Auto-detect unavailable. Choose onboarding path manually.", fallback.RecommendedReason,
            "setup wizard auto-detect prompt fallback reason");

        var detected = IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolveAutoDetectPromptRecommendationForTests(
            new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectResult {
                RecommendedPath = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.RefreshAuth,
                RecommendedReason = "  Refresh auth required. "
            });
        AssertEqual(IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingPaths.RefreshAuth, detected.RecommendedPathId,
            "setup wizard auto-detect prompt detected path id");
        AssertEqual("Refresh auth required.", detected.RecommendedReason,
            "setup wizard auto-detect prompt detected reason normalized");
    }

    private static void TestSetupWizardAutoDetectUnavailableMessageFormatting() {
        var exception = new InvalidOperationException("doctor failed");
        var compact = IntelligenceX.Cli.Setup.Wizard.WizardRunner.FormatAutoDetectUnavailableMessageForTests(exception, verbose: false);
        AssertContainsText(compact, "Auto-detect unavailable: doctor failed.", "setup wizard auto-detect unavailable compact prefix");
        AssertContainsText(compact, "--verbose", "setup wizard auto-detect unavailable compact hint");

        var verbose = IntelligenceX.Cli.Setup.Wizard.WizardRunner.FormatAutoDetectUnavailableMessageForTests(exception, verbose: true);
        AssertContainsText(verbose, "InvalidOperationException", "setup wizard auto-detect unavailable verbose type");
        AssertContainsText(verbose, "doctor failed", "setup wizard auto-detect unavailable verbose message");

        var nullException = IntelligenceX.Cli.Setup.Wizard.WizardRunner.FormatAutoDetectUnavailableMessageForTests(null, verbose: false);
        AssertContainsText(nullException, "manual path selection", "setup wizard auto-detect unavailable null exception fallback");
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

#endif
}
