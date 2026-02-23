using System;
using System.Linq;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebSetupReviewConfigValidator {
    public static bool TryValidateAndNormalize(
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        string? reviewIntent,
        string? reviewStrictness,
        string? reviewLoopPolicy,
        string? reviewVisionPath,
        string? mergeBlockerSections,
        bool? mergeBlockerRequireAllSections,
        bool? mergeBlockerRequireSectionMatch,
        out string? normalizedReviewIntent,
        out string? normalizedReviewStrictness,
        out string? normalizedReviewLoopPolicy,
        out string? normalizedReviewVisionPath,
        out string? normalizedMergeBlockerSections,
        out bool? normalizedMergeBlockerRequireAllSections,
        out bool? normalizedMergeBlockerRequireSectionMatch,
        out string? error) {
        error = null;
        normalizedReviewIntent = null;
        normalizedReviewStrictness = null;
        normalizedReviewLoopPolicy = null;
        normalizedReviewVisionPath = null;
        normalizedMergeBlockerSections = null;
        normalizedMergeBlockerRequireAllSections = null;
        normalizedMergeBlockerRequireSectionMatch = null;

        var hasAnyReviewTweaks =
            !string.IsNullOrWhiteSpace(reviewIntent) ||
            !string.IsNullOrWhiteSpace(reviewStrictness) ||
            !string.IsNullOrWhiteSpace(reviewLoopPolicy) ||
            !string.IsNullOrWhiteSpace(reviewVisionPath) ||
            !string.IsNullOrWhiteSpace(mergeBlockerSections) ||
            mergeBlockerRequireAllSections.HasValue ||
            mergeBlockerRequireSectionMatch.HasValue;

        var reviewTweaksApply = isSetup && withConfig && !hasConfigOverride;
        if (!reviewTweaksApply) {
            if (hasAnyReviewTweaks) {
                error =
                    "Review strictness/intent/vision/merge-blocker options are only supported for setup when generating config from presets (withConfig=true and no configJson/configPath override).";
                return false;
            }
            return true;
        }

        normalizedReviewIntent = NormalizeOptional(reviewIntent);
        normalizedReviewStrictness = NormalizeOptional(reviewStrictness);
        normalizedReviewLoopPolicy = NormalizeOptional(reviewLoopPolicy);
        normalizedReviewVisionPath = NormalizeOptional(reviewVisionPath);
        if (!string.IsNullOrWhiteSpace(normalizedReviewLoopPolicy)) {
            var loopPolicy = normalizedReviewLoopPolicy.Trim().ToLowerInvariant();
            if (loopPolicy is not ("strict" or "default" or "balanced" or "lenient" or "claude" or "vision")) {
                error = "reviewLoopPolicy must be one of: strict, balanced, lenient, claude, vision.";
                return false;
            }
            normalizedReviewLoopPolicy = loopPolicy;
        }
        if (!string.IsNullOrWhiteSpace(normalizedReviewVisionPath)) {
            if (!string.Equals(normalizedReviewLoopPolicy, "vision", StringComparison.OrdinalIgnoreCase)) {
                error = "reviewVisionPath requires reviewLoopPolicy=vision.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(mergeBlockerSections)) {
            var sections = mergeBlockerSections
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sections.Length == 0) {
                error = "mergeBlockerSections requires at least one non-empty section name.";
                return false;
            }
            normalizedMergeBlockerSections = string.Join(",", sections);
        }

        normalizedMergeBlockerRequireAllSections = mergeBlockerRequireAllSections;
        normalizedMergeBlockerRequireSectionMatch = mergeBlockerRequireSectionMatch;
        return true;
    }

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
