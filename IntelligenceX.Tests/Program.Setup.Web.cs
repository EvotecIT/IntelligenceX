namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestWebSetupAutodetectResponseJsonMatchesSharedContractPayload() {
        var contractCommands = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetCommandTemplates();
        var contractPaths = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        var result = new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectResult {
            Status = "warn",
            Workspace = "/tmp/workspace",
            Repo = "owner/repo",
            ContractVersion = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
            ContractFingerprint = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            CommandTemplates = contractCommands,
            RecommendedPath = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.RefreshAuthPathId,
            RecommendedReason = "Auth refresh required.",
            Paths = contractPaths,
            Checks = new[] {
                new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheck {
                    Name = "doctor",
                    Status = IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheckStatus.Warn,
                    Message = "warn"
                }
            }
        };

        var json = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupAutodetectResponseJsonForTests(result);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion,
            root.GetProperty("contractVersion").GetString(),
            "web setup autodetect response contract version");
        AssertEqual(contractCommands.AutoDetect,
            root.GetProperty("commandTemplates").GetProperty("autoDetect").GetString(),
            "web setup autodetect response auto-detect template");
        AssertEqual(contractCommands.NewSetupApply,
            root.GetProperty("commandTemplates").GetProperty("newSetupApply").GetString(),
            "web setup autodetect response setup apply template");
        AssertEqual(contractPaths.Count, root.GetProperty("paths").GetArrayLength(),
            "web setup autodetect response path count");
        AssertEqual(IntelligenceX.Setup.Onboarding.SetupOnboardingContract.NewSetupPathId,
            root.GetProperty("paths")[0].GetProperty("id").GetString(),
            "web setup autodetect response first path id");
        AssertEqual("setup",
            root.GetProperty("paths")[0].GetProperty("defaultOperation").GetString(),
            "web setup autodetect response first path default operation");
        AssertEqual(System.Text.Json.JsonValueKind.String, root.GetProperty("checks")[0].GetProperty("status").ValueKind,
            "web setup autodetect response check status type");
        AssertEqual("warn", root.GetProperty("checks")[0].GetProperty("status").GetString(),
            "web setup autodetect response check status lowercase");
    }

    private static void TestWebSetupAutodetectResponseJsonFallbacksForNullPayloads() {
        var contractPaths = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        var expectedContractVersion = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.ContractVersion;
        var expectedContractFingerprint = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true);
        var result = new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectResult {
            Status = "ok",
            Workspace = "/tmp/workspace",
            ContractVersion = string.Empty,
            ContractFingerprint = string.Empty,
            Paths = null!,
            CommandTemplates = null!,
            Checks = null!
        };

        var json = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupAutodetectResponseJsonForTests(result);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        var contractCommands = IntelligenceX.Setup.Onboarding.SetupOnboardingContract.GetCommandTemplates();

        AssertEqual(contractCommands.AutoDetect,
            root.GetProperty("commandTemplates").GetProperty("autoDetect").GetString(),
            "web setup autodetect response fallback command template");
        AssertEqual(expectedContractVersion, root.GetProperty("contractVersion").GetString(),
            "web setup autodetect response fallback contract version");
        AssertEqual(expectedContractFingerprint, root.GetProperty("contractFingerprint").GetString(),
            "web setup autodetect response fallback contract fingerprint");
        AssertEqual(contractPaths.Count, root.GetProperty("paths").GetArrayLength(),
            "web setup autodetect response fallback path count");
        AssertEqual(0, root.GetProperty("checks").GetArrayLength(),
            "web setup autodetect response fallback check count");
    }

    private static void TestWebSetupAutodetectResponseJsonRejectsUnknownCheckStatus() {
        var result = new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingAutoDetectResult {
            Status = "warn",
            Workspace = "/tmp/workspace",
            Checks = new[] {
                new IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheck {
                    Name = "doctor",
                    Status = (IntelligenceX.Cli.Setup.Onboarding.SetupOnboardingCheckStatus)999,
                    Message = "unexpected"
                }
            }
        };

        AssertThrows<ArgumentOutOfRangeException>(() =>
                IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupAutodetectResponseJsonForTests(result),
            "web setup autodetect response unknown check status");
    }

    private static void TestWebSetupBuildSetupArgsPropagatesRequestDryRun() {
        var fromRequest = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForDryRunPropagationTests(
            routeDryRun: false,
            requestDryRun: true);
        AssertEqual(true, Array.IndexOf(fromRequest, "--dry-run") >= 0, "web setup args request dry-run");

        var fromRoute = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForDryRunPropagationTests(
            routeDryRun: true,
            requestDryRun: false);
        AssertEqual(true, Array.IndexOf(fromRoute, "--dry-run") >= 0, "web setup args route dry-run");

        var none = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForDryRunPropagationTests(
            routeDryRun: false,
            requestDryRun: false);
        AssertEqual(false, Array.IndexOf(none, "--dry-run") >= 0, "web setup args no dry-run");
    }

    private static void TestWebSetupBuildSetupArgsPropagatesOpenAiAccountRouting() {
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForOpenAiAccountRoutingTests(
            openAiAccountId: "acc-primary",
            openAiAccountIds: "acc-primary,acc-backup",
            openAiAccountRotation: "round-robin",
            openAiAccountFailover: false);

        AssertEqual(true, Array.IndexOf(args, "--openai-account-id") >= 0, "web setup args openai account id flag");
        AssertEqual(true, Array.IndexOf(args, "--openai-account-ids") >= 0, "web setup args openai account ids flag");
        AssertEqual(true, Array.IndexOf(args, "--openai-account-rotation") >= 0,
            "web setup args openai account rotation flag");
        AssertEqual(true, Array.IndexOf(args, "--openai-account-failover") >= 0,
            "web setup args openai account failover flag");

        var idIndex = Array.IndexOf(args, "--openai-account-id");
        AssertEqual("acc-primary", args[idIndex + 1], "web setup args openai account id value");
        var idsIndex = Array.IndexOf(args, "--openai-account-ids");
        AssertEqual("acc-primary,acc-backup", args[idsIndex + 1], "web setup args openai account ids value");
        var rotationIndex = Array.IndexOf(args, "--openai-account-rotation");
        AssertEqual("round-robin", args[rotationIndex + 1], "web setup args openai account rotation value");
        var failoverIndex = Array.IndexOf(args, "--openai-account-failover");
        AssertEqual("false", args[failoverIndex + 1], "web setup args openai account failover value");
    }

    private static void TestWebSetupResolveWithConfigFromArgs() {
        AssertEqual(true, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo", "--with-config"), "web setup resolve with-config flag");
        AssertEqual(true, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo", "--config-path", ".intelligencex/reviewer.json"), "web setup resolve with-config config-path");
        AssertEqual(true, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo", "--config-json", "{\"review\":{}}"), "web setup resolve with-config config-json");
        AssertEqual(false, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo"), "web setup resolve with-config none");
    }

    private static void TestWebSetupOpenAiRoutingValidationRejectsConfigOverride() {
        var resultWithOverride = IntelligenceX.Cli.Setup.Web.WebApi.ValidateOpenAiAccountRoutingForTests(
            provider: "openai",
            openAiAccountId: null,
            openAiAccountIds: "acc-primary,acc-backup",
            openAiAccountRotation: "round-robin",
            openAiAccountFailover: null,
            isSetup: true,
            withConfig: true,
            hasConfigOverride: true);
        AssertEqual(false, resultWithOverride.Success, "web setup openai routing config override rejected");
        AssertContainsText(resultWithOverride.Error ?? string.Empty,
            "not supported when configJson/configPath override is used",
            "web setup openai routing config override error");

        var resultWithoutConfig = IntelligenceX.Cli.Setup.Web.WebApi.ValidateOpenAiAccountRoutingForTests(
            provider: "openai",
            openAiAccountId: null,
            openAiAccountIds: "acc-primary,acc-backup",
            openAiAccountRotation: "round-robin",
            openAiAccountFailover: null,
            isSetup: true,
            withConfig: false,
            hasConfigOverride: false);
        AssertEqual(false, resultWithoutConfig.Success, "web setup openai routing without with-config rejected");
        AssertContainsText(resultWithoutConfig.Error ?? string.Empty,
            "require withConfig=true",
            "web setup openai routing with-config error");
    }

    private static void TestWebSetupOpenAiRoutingValidationRejectsInvalidRotationWithPrimaryOnly() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateOpenAiAccountRoutingForTests(
            provider: "openai",
            openAiAccountId: "acc-primary",
            openAiAccountIds: null,
            openAiAccountRotation: "invalid-value",
            openAiAccountFailover: null,
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false);
        AssertEqual(false, result.Success, "web setup openai routing invalid rotation primary-only rejected");
        AssertContainsText(result.Error ?? string.Empty,
            "rotation must be one of",
            "web setup openai routing invalid rotation primary-only error");
    }

    private static void TestWebSetupPostApplyVerifySkipsCallbackWhenApplyFails() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            ExitSuccess = false
        };

        var verifyCalls = 0;
        var verify = IntelligenceX.Cli.Setup.Web.WebApi.ResolvePostApplyVerificationForTests(
            context,
            () => {
                verifyCalls++;
                return System.Threading.Tasks.Task.FromResult(new SetupPostApplyVerification {
                    Repo = "owner/repo",
                    Operation = "setup",
                    Passed = true
                });
            }).GetAwaiter().GetResult();

        AssertEqual(0, verifyCalls, "web setup post-apply verify callback skipped on failed apply");
        AssertEqual(true, verify.Skipped, "web setup post-apply verify skipped on failed apply");
        AssertEqual(false, verify.Passed, "web setup post-apply verify failed status on failed apply");
        AssertContainsText(verify.Note ?? string.Empty, "failed", "web setup post-apply verify failure note");
    }

    private static void TestWebSetupResolveOrgSecretVerificationContext() {
        var setupWithOrg = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            secretTarget: "org",
            secretOrg: null);
        AssertEqual(true, setupWithOrg.ExpectOrgSecret, "web setup org secret expected for org target without explicit org");
        AssertEqual(null, setupWithOrg.SecretOrg, "web setup org secret remains null when not provided");

        var updateWithOrg = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: true,
            provider: "chatgpt",
            secretTarget: "org",
            secretOrg: "EvotecIT");
        AssertEqual(true, updateWithOrg.ExpectOrgSecret, "web update-secret org secret expected for org target");
        AssertEqual("EvotecIT", updateWithOrg.SecretOrg, "web update-secret org secret value");

        var repoTarget = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            secretTarget: "repo",
            secretOrg: "EvotecIT");
        AssertEqual(false, repoTarget.ExpectOrgSecret, "web setup repo target does not expect org secret");
        AssertEqual(null, repoTarget.SecretOrg, "web setup repo target does not pass org secret");

        var nonOpenAiProvider = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: false,
            provider: "copilot",
            secretTarget: "org",
            secretOrg: "EvotecIT");
        AssertEqual(false, nonOpenAiProvider.ExpectOrgSecret, "web setup org target non-openai provider does not expect org secret");
        AssertEqual(null, nonOpenAiProvider.SecretOrg, "web setup org target non-openai provider does not pass org secret");
    }

    private static void TestWebSetupResolveOrgSecretVerificationContextPerRepo() {
        var firstRepo = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForRepoTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            repo: "ownerA/repo1",
            secretTarget: "org",
            secretOrg: null);
        AssertEqual(true, firstRepo.ExpectOrgSecret, "web setup per-repo org target expects org secret");
        AssertEqual("ownerA", firstRepo.SecretOrg, "web setup per-repo org defaults to repo owner");

        var secondRepo = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForRepoTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            repo: "ownerB/repo2",
            secretTarget: "org",
            secretOrg: null);
        AssertEqual(true, secondRepo.ExpectOrgSecret, "web setup second repo org target expects org secret");
        AssertEqual("ownerB", secondRepo.SecretOrg, "web setup second repo org defaults to repo owner");

        var explicitOrg = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForRepoTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            repo: "ownerC/repo3",
            secretTarget: "org",
            secretOrg: "SharedOrg");
        AssertEqual(true, explicitOrg.ExpectOrgSecret, "web setup explicit org target expects org secret");
        AssertEqual("SharedOrg", explicitOrg.SecretOrg, "web setup explicit org value preserved");
    }

    private static void TestWebSetupRunProcessTimeoutReturnsPromptly() {
        var command = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(command)) {
            command = "dotnet";
        }

        var timer = Stopwatch.StartNew();
        var result = IntelligenceX.Cli.Setup.Web.WebApi.RunSetupProcessForTests(
            command!,
            new[] { "--help" },
            timeoutMs: 1).GetAwaiter().GetResult();
        timer.Stop();

        AssertEqual(true, result.TimedOut, "web setup timeout flag");
        AssertEqual(124, result.ExitCode, "web setup timeout exit code");
        AssertContainsText(result.StdErr, "timed out", "web setup timeout stderr");
        if (timer.Elapsed > TimeSpan.FromSeconds(10)) {
            throw new InvalidOperationException($"Expected timeout test to return promptly, got {timer.Elapsed}.");
        }
    }
#endif
}
