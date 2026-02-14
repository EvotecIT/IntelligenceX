using System;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebSetupAnalysisValidator {
    public static bool TryValidateAndNormalize(
        bool isSetup,
        bool withConfig,
        bool hasConfigOverride,
        bool? analysisEnabled,
        bool? analysisGateEnabled,
        bool? analysisRunStrict,
        string? analysisPacks,
        string? analysisExportPath,
        out bool? normalizedEnabled,
        out bool? normalizedGateEnabled,
        out bool? normalizedRunStrict,
        out string? normalizedPacks,
        out string? normalizedExportPath,
        out string? error) {
        error = null;
        normalizedEnabled = null;
        normalizedGateEnabled = null;
        normalizedRunStrict = null;
        normalizedPacks = null;
        normalizedExportPath = null;

        var hasAnyAnalysis =
            analysisEnabled.HasValue ||
            analysisGateEnabled.HasValue ||
            analysisRunStrict.HasValue ||
            !string.IsNullOrWhiteSpace(analysisPacks) ||
            !string.IsNullOrWhiteSpace(analysisExportPath);

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
            normalizedRunStrict = analysisRunStrict;
            if (!SetupAnalysisPacks.TryNormalizeCsv(analysisPacks, out normalizedPacks, out error)) {
                return false;
            }
            if (!SetupAnalysisExportPath.TryNormalize(analysisExportPath, out normalizedExportPath, out error)) {
                return false;
            }
            return true;
        }

        if (analysisGateEnabled.HasValue ||
            analysisRunStrict.HasValue ||
            !string.IsNullOrWhiteSpace(analysisPacks) ||
            !string.IsNullOrWhiteSpace(analysisExportPath)) {
            error = "analysisGateEnabled/analysisRunStrict/analysisPacks/analysisExportPath require analysisEnabled=true.";
            return false;
        }

        // Ensure no stray values accidentally override defaults.
        normalizedGateEnabled = null;
        normalizedRunStrict = null;
        normalizedPacks = null;
        normalizedExportPath = null;
        return true;
    }
}
