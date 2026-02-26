namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static int RunAnalyzeToolRunnerTests() {
        var failed = 0;
        failed += Run("Analyze run PowerShell script captures engine errors", TestAnalyzeRunPowerShellScriptCapturesEngineErrors);
        failed += Run("Analyze run PowerShell strict args include fail switch", TestAnalyzeRunPowerShellStrictArgsIncludeFailSwitch);
        failed += Run("Analyze run JavaScript args include configured rules", TestAnalyzeRunJavaScriptArgsIncludeConfiguredRules);
        failed += Run("Analyze run Python args include select rule IDs", TestAnalyzeRunPythonArgsIncludeSelectRuleIds);
        failed += Run("Analyze run Python args include output file when configured",
            TestAnalyzeRunPythonArgsIncludeOutputFileWhenConfigured);
        failed += Run("Analyze run Python output-file fallback detection",
            TestAnalyzeRunPythonOutputFileFallbackDetection);
        failed += Run("Analyze run process exception classification includes expected exceptions",
            TestAnalyzeRunProcessExceptionClassificationIncludesExpectedExceptions);
        failed += Run("Analyze run process exception classification excludes unexpected exceptions",
            TestAnalyzeRunProcessExceptionClassificationExcludesUnexpectedExceptions);
        failed += Run("Analyze run external runner missing command message classification",
            TestAnalyzeRunExternalFailureMessageClassifiesMissingCommand);
        failed += Run("Analyze run external runner recognizes tool-specific unavailable markers",
            TestAnalyzeRunExternalFailureMessageRecognizesToolSpecificUnavailableMarkers);
        failed += Run("Analyze run external runner supports configured unavailable markers",
            TestAnalyzeRunExternalFailureMessageSupportsConfiguredUnavailableMarkers);
        failed += Run("Analyze run external runner configured markers refresh on environment change",
            TestAnalyzeRunExternalFailureMessageConfiguredMarkersRefreshOnEnvironmentChange);
        failed += Run("Analyze run workspace source detection skips excluded directories",
            TestAnalyzeRunWorkspaceSourceDetectionSkipsExcludedDirectories);
        failed += Run("Analyze run workspace source detection diagnostics default to zero skipped",
            TestAnalyzeRunWorkspaceSourceDetectionDiagnosticsDefaultToZeroSkipped);
        failed += Run("Analyze run workspace source detection finds powershell module files",
            TestAnalyzeRunWorkspaceSourceDetectionFindsPowerShellModuleFiles);
        failed += Run("Analyze run workspace source inventory captures multiple extensions",
            TestAnalyzeRunWorkspaceSourceInventoryCapturesMultipleExtensions);
        failed += Run("Analyze run workspace source inventory keeps tracked extensions only",
            TestAnalyzeRunWorkspaceSourceInventoryKeepsTrackedExtensionsOnly);
        failed += Run("Analyze run shared source inventory falls back when scan limit reached",
            TestAnalyzeRunSharedSourceInventoryFallsBackWhenScanLimitReached);
        failed += Run("Analyze run shared source inventory fallback detects powershell sources",
            TestAnalyzeRunSharedSourceInventoryFallbackDetectsPowerShellSources);
        failed += Run("Analyze run shared source inventory fallback detects csharp sources",
            TestAnalyzeRunSharedSourceInventoryFallbackDetectsCsharpSources);
        failed += Run("Analyze run shared source inventory fallback detects shell sources",
            TestAnalyzeRunSharedSourceInventoryFallbackDetectsShellSources);
        failed += Run("Analyze run shared source inventory fallback detects yaml sources",
            TestAnalyzeRunSharedSourceInventoryFallbackDetectsYamlSources);
        failed += Run("Analyze run javascript selectors ignore mismatched tools",
            TestAnalyzeRunJavaScriptSelectorsIgnoreMismatchedTools);
        failed += Run("Analyze run python selected rule ids ignore mismatched tools",
            TestAnalyzeRunPythonSelectedRuleIdsIgnoreMismatchedTools);
        return failed;
    }
}
#endif
