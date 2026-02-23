using System;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private static bool TryValidateReviewOptionContextForCurrentOperation(
        SetupOptions options,
        bool withConfig,
        out string? error) {
        var hasConfigOverride = !string.IsNullOrWhiteSpace(options.ConfigJson) ||
                                !string.IsNullOrWhiteSpace(options.ConfigPath);
        var isSetupOperation = !options.Cleanup && !options.UpdateSecret;
        return TryValidateReviewOptionContext(
            options,
            isSetup: isSetupOperation,
            withConfig: withConfig,
            hasConfigOverride: hasConfigOverride,
            out error);
    }

    private static bool TryValidateReviewOptionContext(
        SetupOptions options,
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        out string? error) {
        error = null;
        var hasAnyReviewOption = options.ReviewIntentSet ||
                                 options.ReviewStrictnessSet ||
                                 options.ReviewLoopPolicySet ||
                                 options.ReviewVisionPathSet ||
                                 options.MergeBlockerSectionsSet ||
                                 options.MergeBlockerRequireAllSectionsSet ||
                                 options.MergeBlockerRequireSectionMatchSet;
        if (!hasAnyReviewOption) {
            return true;
        }

        if (!isSetup) {
            error = "Review options are only supported for setup operation.";
            return false;
        }

        if (!withConfig) {
            error = "Review options require --with-config (or --config-json/--config-path).";
            return false;
        }

        if (hasConfigOverride) {
            error = "Review options are not supported when --config-json/--config-path override is used.";
            return false;
        }

        if (options.ReviewLoopPolicySet) {
            if (!SetupReviewLoopPolicy.TryNormalize(options.ReviewLoopPolicy, out var normalizedPolicy)) {
                error = $"Invalid --review-loop-policy value. Use {SetupReviewLoopPolicy.AllowedValuesMessage()}.";
                return false;
            }
            options.ReviewLoopPolicy = normalizedPolicy;
        }

        if (!options.ReviewVisionPathSet) {
            return true;
        }

        if (!options.ReviewLoopPolicySet) {
            error = "--review-vision-path requires --review-loop-policy vision.";
            return false;
        }

        if (!SetupReviewLoopPolicy.TryNormalize(options.ReviewLoopPolicy, out var loopPolicy)) {
            error = $"Invalid --review-loop-policy value. Use {SetupReviewLoopPolicy.AllowedValuesMessage()}.";
            return false;
        }
        if (!string.Equals(loopPolicy, SetupReviewLoopPolicy.Vision, StringComparison.Ordinal)) {
            error = "--review-vision-path is only supported with --review-loop-policy vision.";
            return false;
        }

        return true;
    }

    internal static (bool Success, string? Error) ValidateReviewOptionContextForTests(
        string[] args,
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride) {
        var options = SetupOptions.Parse(args);
        var success = TryValidateReviewOptionContext(
            options,
            isSetup,
            withConfig,
            hasConfigOverride,
            out var error);
        return (success, error);
    }
}
