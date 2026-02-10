using System;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebSetupAnalysisValidator {
    public static bool TryValidateAndNormalize(
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        bool? analysisEnabled,
        bool? analysisGateEnabled,
        string? analysisPacks,
        out bool? normalizedEnabled,
        out bool? normalizedGateEnabled,
        out string? normalizedPacks,
        out string? error) {
        error = null;
        normalizedEnabled = null;
        normalizedGateEnabled = null;
        normalizedPacks = null;

        var hasAnyAnalysis =
            analysisEnabled.HasValue ||
            analysisGateEnabled.HasValue ||
            !string.IsNullOrWhiteSpace(analysisPacks);

        var analysisApplies = isSetup && withConfig && !hasConfigOverride;
        if (!analysisApplies) {
            if (hasAnyAnalysis) {
                error = "Static analysis options are only supported for setup when generating config from presets (withConfig=true and no configJson/configPath override).";
                return false;
            }
            return true;
        }

        normalizedEnabled = analysisEnabled;
        if (analysisEnabled == true) {
            normalizedGateEnabled = analysisGateEnabled;
            if (!SetupAnalysisPacks.TryNormalizeCsv(analysisPacks, out normalizedPacks, out error)) {
                return false;
            }
            return true;
        }

        if (analysisGateEnabled.HasValue || !string.IsNullOrWhiteSpace(analysisPacks)) {
            error = "analysisGateEnabled/analysisPacks require analysisEnabled=true.";
            return false;
        }

        // Ensure no stray values accidentally override defaults.
        normalizedGateEnabled = null;
        normalizedPacks = null;
        return true;
    }
}

