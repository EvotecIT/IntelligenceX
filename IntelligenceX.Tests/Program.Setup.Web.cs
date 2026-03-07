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

    private static void TestWebSetupBuildSetupArgsPropagatesOpenAiAccountRoutingWithPrimaryOnly() {
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForOpenAiAccountRoutingTests(
            openAiAccountId: "acc-primary",
            openAiAccountIds: null,
            openAiAccountRotation: "round-robin",
            openAiAccountFailover: false);

        AssertEqual(true, Array.IndexOf(args, "--openai-account-id") >= 0, "web setup args openai primary-only id flag");
        AssertEqual(false, Array.IndexOf(args, "--openai-account-ids") >= 0, "web setup args openai primary-only ids flag");
        AssertEqual(true, Array.IndexOf(args, "--openai-account-rotation") >= 0,
            "web setup args openai primary-only rotation flag");
        AssertEqual(true, Array.IndexOf(args, "--openai-account-failover") >= 0,
            "web setup args openai primary-only failover flag");

        var idIndex = Array.IndexOf(args, "--openai-account-id");
        AssertEqual("acc-primary", args[idIndex + 1], "web setup args openai primary-only id value");
        var rotationIndex = Array.IndexOf(args, "--openai-account-rotation");
        AssertEqual("round-robin", args[rotationIndex + 1], "web setup args openai primary-only rotation value");
        var failoverIndex = Array.IndexOf(args, "--openai-account-failover");
        AssertEqual("false", args[failoverIndex + 1], "web setup args openai primary-only failover value");
    }

    private static void TestWebSetupBuildSetupArgsPropagatesOpenAiModel() {
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForOpenAiModelTests("gpt-5.4/fast");
        var modelIndex = Array.IndexOf(args, "--openai-model");
        AssertEqual(true, modelIndex >= 0, "web setup args openai model flag");
        AssertEqual("gpt-5.4/fast", args[modelIndex + 1], "web setup args openai model value");
    }

    private static void TestWebSetupBuildSetupArgsPropagatesAnalysisRunStrict() {
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForAnalysisRunStrictTests(
            analysisEnabled: true,
            analysisRunStrict: true);
        AssertEqual(true, Array.IndexOf(args, "--analysis-enabled") >= 0, "web setup args analysis enabled flag");
        AssertEqual(true, Array.IndexOf(args, "--analysis-run-strict") >= 0,
            "web setup args analysis run strict flag");
        var runStrictIndex = Array.IndexOf(args, "--analysis-run-strict");
        AssertEqual("true", args[runStrictIndex + 1], "web setup args analysis run strict value");

        var disabledArgs = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForAnalysisRunStrictTests(
            analysisEnabled: false,
            analysisRunStrict: true);
        AssertEqual(false, Array.IndexOf(disabledArgs, "--analysis-run-strict") >= 0,
            "web setup args analysis run strict omitted when analysis disabled");

        var overrideArgs = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForAnalysisRunStrictTests(
            analysisEnabled: true,
            analysisRunStrict: true,
            withConfig: true,
            hasConfigOverride: true);
        AssertEqual(false, Array.IndexOf(overrideArgs, "--analysis-run-strict") >= 0,
            "web setup args analysis run strict omitted for config override");
    }

    private static void TestWebSetupBuildSetupArgsPropagatesReviewConfigTweaks() {
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForReviewConfigTweaksTests(
            reviewIntent: "maintainability",
            reviewStrictness: "strict",
            reviewLoopPolicy: "vision",
            reviewVisionPath: "VISION.md",
            mergeBlockerSections: "Todo List,Critical Issues",
            mergeBlockerRequireAllSections: false,
            mergeBlockerRequireSectionMatch: false);

        AssertEqual(true, Array.IndexOf(args, "--review-intent") >= 0,
            "web setup args review intent flag");
        AssertEqual(true, Array.IndexOf(args, "--review-strictness") >= 0,
            "web setup args review strictness flag");
        AssertEqual(true, Array.IndexOf(args, "--review-loop-policy") >= 0,
            "web setup args review loop policy flag");
        AssertEqual(true, Array.IndexOf(args, "--review-vision-path") >= 0,
            "web setup args review vision path flag");
        AssertEqual(true, Array.IndexOf(args, "--merge-blocker-sections") >= 0,
            "web setup args merge blocker sections flag");
        AssertEqual(true, Array.IndexOf(args, "--merge-blocker-require-all-sections") >= 0,
            "web setup args merge blocker require all sections flag");
        AssertEqual(true, Array.IndexOf(args, "--merge-blocker-require-section-match") >= 0,
            "web setup args merge blocker require section match flag");

        var intentIndex = Array.IndexOf(args, "--review-intent");
        var strictnessIndex = Array.IndexOf(args, "--review-strictness");
        var policyIndex = Array.IndexOf(args, "--review-loop-policy");
        var visionPathIndex = Array.IndexOf(args, "--review-vision-path");
        var sectionsIndex = Array.IndexOf(args, "--merge-blocker-sections");
        var requireAllIndex = Array.IndexOf(args, "--merge-blocker-require-all-sections");
        var requireMatchIndex = Array.IndexOf(args, "--merge-blocker-require-section-match");
        AssertEqual("maintainability", args[intentIndex + 1], "web setup args review intent value");
        AssertEqual("strict", args[strictnessIndex + 1], "web setup args review strictness value");
        AssertEqual("vision", args[policyIndex + 1], "web setup args review loop policy value");
        AssertEqual("VISION.md", args[visionPathIndex + 1], "web setup args review vision path value");
        AssertEqual("Todo List,Critical Issues", args[sectionsIndex + 1],
            "web setup args merge blocker sections value");
        AssertEqual("false", args[requireAllIndex + 1],
            "web setup args merge blocker require all sections value");
        AssertEqual("false", args[requireMatchIndex + 1],
            "web setup args merge blocker require section match value");
    }

    private static void TestWebSetupBuildSetupArgsOmitsMergeBlockerBooleansWhenUnset() {
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForReviewConfigTweaksTests(
            reviewIntent: null,
            reviewStrictness: null,
            reviewLoopPolicy: "balanced",
            reviewVisionPath: null,
            mergeBlockerSections: "Todo List,Critical Issues",
            mergeBlockerRequireAllSections: null,
            mergeBlockerRequireSectionMatch: null);

        AssertEqual(true, Array.IndexOf(args, "--review-loop-policy") >= 0,
            "web setup args omit merge blocker booleans loop policy flag");
        AssertEqual(true, Array.IndexOf(args, "--merge-blocker-sections") >= 0,
            "web setup args omit merge blocker booleans sections flag");
        AssertEqual(false, Array.IndexOf(args, "--merge-blocker-require-all-sections") >= 0,
            "web setup args omit merge blocker booleans require all sections flag");
        AssertEqual(false, Array.IndexOf(args, "--merge-blocker-require-section-match") >= 0,
            "web setup args omit merge blocker booleans require section match flag");
    }

    private static void TestWebSetupBuildSetupArgsPropagatesTriageBootstrap() {
        var enabled = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForTriageBootstrapTests(triageBootstrap: true);
        AssertEqual(true, Array.IndexOf(enabled, "--triage-bootstrap") >= 0,
            "web setup args triage bootstrap enabled");

        var disabled = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForTriageBootstrapTests(triageBootstrap: false);
        AssertEqual(false, Array.IndexOf(disabled, "--triage-bootstrap") >= 0,
            "web setup args triage bootstrap disabled");
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

    private static void TestWebSetupAnalysisValidationNormalizesRunStrict() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateAnalysisForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: true,
            analysisRunStrict: true,
            analysisPacks: "all-50",
            analysisExportPath: ".intelligencex/analyzers");
        AssertEqual(true, result.Success, "web setup analysis validation success");
        AssertEqual(true, result.NormalizedEnabled, "web setup analysis validation normalized enabled");
        AssertEqual(true, result.NormalizedGateEnabled, "web setup analysis validation normalized gate");
        AssertEqual(true, result.NormalizedRunStrict, "web setup analysis validation normalized run strict");
        AssertEqual("all-50", result.NormalizedPacks, "web setup analysis validation normalized packs");
        AssertEqual(".intelligencex/analyzers", result.NormalizedExportPath,
            "web setup analysis validation normalized export path");
    }

    private static void TestWebSetupAnalysisValidationRejectsRunStrictWithoutAnalysisEnabled() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateAnalysisForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: false,
            analysisGateEnabled: null,
            analysisRunStrict: true,
            analysisPacks: null,
            analysisExportPath: null);
        AssertEqual(false, result.Success, "web setup analysis validation run strict requires analysis enabled");
        AssertContainsText(result.Error ?? string.Empty,
            "analysisGateEnabled/analysisRunStrict/analysisPacks/analysisExportPath require analysisEnabled=true",
            "web setup analysis validation run strict requires analysis enabled error");
    }

    private static void TestWebSetupAnalysisValidationRejectsRunStrictOutsidePresetGeneration() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateAnalysisForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: true,
            analysisEnabled: null,
            analysisGateEnabled: null,
            analysisRunStrict: true,
            analysisPacks: null,
            analysisExportPath: null);
        AssertEqual(false, result.Success, "web setup analysis validation run strict rejected for config override");
        AssertContainsText(result.Error ?? string.Empty,
            "only supported for setup when generating config from presets",
            "web setup analysis validation run strict config override error");
    }

    private static void TestWebSetupReviewConfigValidationNormalizesLoopPolicy() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateReviewConfigForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            reviewIntent: " maintainability ",
            reviewStrictness: " strict ",
            reviewLoopPolicy: "Vision",
            reviewVisionPath: " VISION.md ",
            mergeBlockerSections: "Todo List, Critical Issues,Todo List",
            mergeBlockerRequireAllSections: false,
            mergeBlockerRequireSectionMatch: true);
        AssertEqual(true, result.Success, "web setup review config validation success");
        AssertEqual("maintainability", result.NormalizedReviewIntent,
            "web setup review config validation normalized intent");
        AssertEqual("strict", result.NormalizedReviewStrictness,
            "web setup review config validation normalized strictness");
        AssertEqual("vision", result.NormalizedReviewLoopPolicy,
            "web setup review config validation normalized loop policy");
        AssertEqual("VISION.md", result.NormalizedReviewVisionPath,
            "web setup review config validation normalized vision path");
        AssertEqual("Todo List,Critical Issues", result.NormalizedMergeBlockerSections,
            "web setup review config validation normalized sections");
        AssertEqual(false, result.NormalizedMergeBlockerRequireAllSections,
            "web setup review config validation normalized require all");
        AssertEqual(true, result.NormalizedMergeBlockerRequireSectionMatch,
            "web setup review config validation normalized require match");
    }

    private static void TestWebSetupReviewConfigValidationNormalizesTodoOnlyLoopPolicy() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateReviewConfigForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            reviewIntent: null,
            reviewStrictness: null,
            reviewLoopPolicy: "single_section",
            reviewVisionPath: null,
            mergeBlockerSections: null,
            mergeBlockerRequireAllSections: null,
            mergeBlockerRequireSectionMatch: null);
        AssertEqual(true, result.Success, "web setup review config validation todo-only loop policy success");
        AssertEqual("todo-only", result.NormalizedReviewLoopPolicy,
            "web setup review config validation normalized todo-only loop policy");
        AssertEqual(null, result.NormalizedMergeBlockerRequireAllSections,
            "web setup review config validation todo-only loop policy keep require all unset");
        AssertEqual(null, result.NormalizedMergeBlockerRequireSectionMatch,
            "web setup review config validation todo-only loop policy keep require section match unset");
    }

    private static void TestWebSetupReviewConfigValidationRejectsOutsidePresetGeneration() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateReviewConfigForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: true,
            reviewIntent: null,
            reviewStrictness: null,
            reviewLoopPolicy: "vision",
            reviewVisionPath: null,
            mergeBlockerSections: null,
            mergeBlockerRequireAllSections: null,
            mergeBlockerRequireSectionMatch: null);
        AssertEqual(false, result.Success,
            "web setup review config validation rejects config override");
        AssertContainsText(result.Error ?? string.Empty,
            "only supported for setup when generating config from presets",
            "web setup review config validation outside preset generation error");
    }

    private static void TestWebSetupReviewConfigValidationRejectsInvalidLoopPolicy() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateReviewConfigForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            reviewIntent: null,
            reviewStrictness: null,
            reviewLoopPolicy: "invalid",
            reviewVisionPath: null,
            mergeBlockerSections: null,
            mergeBlockerRequireAllSections: null,
            mergeBlockerRequireSectionMatch: null);
        AssertEqual(false, result.Success, "web setup review config validation invalid loop policy rejected");
        AssertContainsText(result.Error ?? string.Empty,
            "must be one of",
            "web setup review config validation invalid loop policy error");
    }

    private static void TestWebSetupReviewConfigValidationRejectsVisionPathWithoutVisionPolicy() {
        var result = IntelligenceX.Cli.Setup.Web.WebApi.ValidateReviewConfigForTests(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            reviewIntent: null,
            reviewStrictness: null,
            reviewLoopPolicy: "balanced",
            reviewVisionPath: "VISION.md",
            mergeBlockerSections: null,
            mergeBlockerRequireAllSections: null,
            mergeBlockerRequireSectionMatch: null);
        AssertEqual(false, result.Success, "web setup review config validation vision path requires vision policy");
        AssertContainsText(result.Error ?? string.Empty,
            "requires reviewLoopPolicy=vision",
            "web setup review config validation vision path policy error");
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
