using System;
using IntelligenceX.Cli.Setup;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {

    internal static string[] BuildSetupArgsForDryRunPropagationTests(bool routeDryRun, bool requestDryRun) {
        var request = new SetupRequest {
            Repo = "owner/repo",
            GitHubToken = "token",
            DryRun = requestDryRun
        };
        return BuildSetupArgsForRepo(request, routeDryRun, "owner/repo");
    }

    internal static string[] BuildSetupArgsForOpenAiAccountRoutingTests(
        string? openAiAccountId,
        string? openAiAccountIds,
        string? openAiAccountRotation,
        bool? openAiAccountFailover) {
        var request = new SetupRequest {
            Repo = "owner/repo",
            GitHubToken = "token",
            WithConfig = true,
            Provider = "openai",
            OpenAIAccountId = openAiAccountId,
            OpenAIAccountIds = openAiAccountIds,
            OpenAIAccountRotation = openAiAccountRotation,
            OpenAIAccountFailover = openAiAccountFailover
        };
        return BuildSetupArgsForRepo(request, routeDryRun: false, "owner/repo");
    }

    internal static string[] BuildSetupArgsForOpenAiModelTests(string? openAiModel) {
        var request = new SetupRequest {
            Repo = "owner/repo",
            GitHubToken = "token",
            Provider = "openai",
            OpenAIModel = openAiModel
        };
        return BuildSetupArgsForRepo(request, routeDryRun: false, "owner/repo");
    }

    internal static string[] BuildSetupArgsForAnalysisRunStrictTests(
        bool? analysisEnabled,
        bool? analysisRunStrict,
        bool withConfig = true,
        bool hasConfigOverride = false) {
        var request = new SetupRequest {
            Repo = "owner/repo",
            GitHubToken = "token",
            WithConfig = withConfig,
            AnalysisEnabled = analysisEnabled,
            AnalysisRunStrict = analysisRunStrict,
            ConfigJson = hasConfigOverride ? "{}" : null
        };
        return BuildSetupArgsForRepo(request, routeDryRun: false, "owner/repo");
    }

    internal static string[] BuildSetupArgsForReviewConfigTweaksTests(
        string? reviewIntent,
        string? reviewStrictness,
        string? reviewLoopPolicy,
        string? reviewVisionPath,
        string? mergeBlockerSections,
        bool? mergeBlockerRequireAllSections,
        bool? mergeBlockerRequireSectionMatch,
        bool withConfig = true,
        bool hasConfigOverride = false) {
        var request = new SetupRequest {
            Repo = "owner/repo",
            GitHubToken = "token",
            WithConfig = withConfig,
            ReviewIntent = reviewIntent,
            ReviewStrictness = reviewStrictness,
            ReviewLoopPolicy = reviewLoopPolicy,
            ReviewVisionPath = reviewVisionPath,
            MergeBlockerSections = mergeBlockerSections,
            MergeBlockerRequireAllSections = mergeBlockerRequireAllSections,
            MergeBlockerRequireSectionMatch = mergeBlockerRequireSectionMatch,
            ConfigJson = hasConfigOverride ? "{}" : null
        };
        return BuildSetupArgsForRepo(request, routeDryRun: false, "owner/repo");
    }

    internal static string[] BuildSetupArgsForTriageBootstrapTests(bool triageBootstrap) {
        var request = new SetupRequest {
            Repo = "owner/repo",
            GitHubToken = "token",
            TriageBootstrap = triageBootstrap
        };
        return BuildSetupArgsForRepo(request, routeDryRun: false, "owner/repo");
    }

    internal static bool ResolveWithConfigFromArgsForTests(params string[] args) {
        return ResolveWithConfigFromArgs(args);
    }

    internal static (bool Success, string? Error) ValidateOpenAiAccountRoutingForTests(
        string provider,
        string? openAiAccountId,
        string? openAiAccountIds,
        string? openAiAccountRotation,
        bool? openAiAccountFailover,
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride) {
        var request = new SetupRequest {
            Provider = provider,
            OpenAIAccountId = openAiAccountId,
            OpenAIAccountIds = openAiAccountIds,
            OpenAIAccountRotation = openAiAccountRotation,
            OpenAIAccountFailover = openAiAccountFailover
        };
        var success = TryValidateAndNormalizeOpenAiAccountRouting(
            request,
            isSetup,
            withConfig,
            hasConfigOverride,
            out var error);
        return (success, error);
    }

    internal static (
        bool Success,
        bool? NormalizedEnabled,
        bool? NormalizedGateEnabled,
        bool? NormalizedRunStrict,
        string? NormalizedPacks,
        string? NormalizedExportPath,
        string? Error) ValidateAnalysisForTests(
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        bool? analysisEnabled,
        bool? analysisGateEnabled,
        bool? analysisRunStrict,
        string? analysisPacks,
        string? analysisExportPath) {
        var success = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: withConfig,
            hasConfigOverride: hasConfigOverride,
            analysisEnabled: analysisEnabled,
            analysisGateEnabled: analysisGateEnabled,
            analysisRunStrict: analysisRunStrict,
            analysisPacks: analysisPacks,
            analysisExportPath: analysisExportPath,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGateEnabled,
            normalizedRunStrict: out var normalizedRunStrict,
            normalizedPacks: out var normalizedPacks,
            normalizedExportPath: out var normalizedExportPath,
            error: out var error);
        return (
            success,
            normalizedEnabled,
            normalizedGateEnabled,
            normalizedRunStrict,
            normalizedPacks,
            normalizedExportPath,
            error);
    }

    internal static (
        bool Success,
        string? NormalizedReviewIntent,
        string? NormalizedReviewStrictness,
        string? NormalizedReviewLoopPolicy,
        string? NormalizedReviewVisionPath,
        string? NormalizedMergeBlockerSections,
        bool? NormalizedMergeBlockerRequireAllSections,
        bool? NormalizedMergeBlockerRequireSectionMatch,
        string? Error) ValidateReviewConfigForTests(
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        string? reviewIntent,
        string? reviewStrictness,
        string? reviewLoopPolicy,
        string? reviewVisionPath,
        string? mergeBlockerSections,
        bool? mergeBlockerRequireAllSections,
        bool? mergeBlockerRequireSectionMatch) {
        var success = WebSetupReviewConfigValidator.TryValidateAndNormalize(
            isSetup: isSetup,
            withConfig: withConfig,
            hasConfigOverride: hasConfigOverride,
            reviewIntent: reviewIntent,
            reviewStrictness: reviewStrictness,
            reviewLoopPolicy: reviewLoopPolicy,
            reviewVisionPath: reviewVisionPath,
            mergeBlockerSections: mergeBlockerSections,
            mergeBlockerRequireAllSections: mergeBlockerRequireAllSections,
            mergeBlockerRequireSectionMatch: mergeBlockerRequireSectionMatch,
            normalizedReviewIntent: out var normalizedReviewIntent,
            normalizedReviewStrictness: out var normalizedReviewStrictness,
            normalizedReviewLoopPolicy: out var normalizedReviewLoopPolicy,
            normalizedReviewVisionPath: out var normalizedReviewVisionPath,
            normalizedMergeBlockerSections: out var normalizedMergeBlockerSections,
            normalizedMergeBlockerRequireAllSections: out var normalizedMergeBlockerRequireAllSections,
            normalizedMergeBlockerRequireSectionMatch: out var normalizedMergeBlockerRequireSectionMatch,
            error: out var error);
        return (
            success,
            normalizedReviewIntent,
            normalizedReviewStrictness,
            normalizedReviewLoopPolicy,
            normalizedReviewVisionPath,
            normalizedMergeBlockerSections,
            normalizedMergeBlockerRequireAllSections,
            normalizedMergeBlockerRequireSectionMatch,
            error);
    }

    internal static (bool ExpectOrgSecret, string? SecretOrg) ResolveOrgSecretVerificationContextForTests(
        bool cleanup,
        bool updateSecret,
        string provider,
        string? secretTarget,
        string? secretOrg) {
        var operation = cleanup
            ? SetupApplyOperation.Cleanup
            : updateSecret
                ? SetupApplyOperation.UpdateSecret
                : SetupApplyOperation.Setup;
        return ResolveOrgSecretVerificationContext(operation, provider, secretTarget, secretOrg);
    }

    internal static (bool ExpectOrgSecret, string? SecretOrg) ResolveOrgSecretVerificationContextForRepoTests(
        bool cleanup,
        bool updateSecret,
        string provider,
        string repo,
        string? secretTarget,
        string? secretOrg) {
        var operation = cleanup
            ? SetupApplyOperation.Cleanup
            : updateSecret
                ? SetupApplyOperation.UpdateSecret
                : SetupApplyOperation.Setup;
        var resolvedSecretOrg = ResolveSecretOrgForRepo(repo, secretOrg);
        return ResolveOrgSecretVerificationContext(operation, provider, secretTarget, resolvedSecretOrg);
    }
}
