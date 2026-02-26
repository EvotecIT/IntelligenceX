namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static int RunAnalysisMaintainabilityDuplicationTests() {
        var failed = 0;
        failed += Run("Analyze run internal duplication threshold",
            TestAnalyzeRunInternalDuplicationRuleRespectsThreshold);
        failed += Run("Analyze run internal duplication malformed tags warn",
            TestAnalyzeRunInternalDuplicationRuleWarnsOnMalformedTags);
        failed += Run("Analyze run internal duplication tokenized javascript",
            TestAnalyzeRunInternalDuplicationTokenizesJavaScript);
        failed += Run("Analyze run internal duplication tokenized mts",
            TestAnalyzeRunInternalDuplicationTokenizesTypeScriptModuleExtension);
        failed += Run("Analyze run internal duplication ignores javascript imports",
            TestAnalyzeRunInternalDuplicationIgnoresJavaScriptImports);
        failed += Run("Analyze run internal duplication ignores PowerShell using statements",
            TestAnalyzeRunInternalDuplicationIgnoresPowerShellUsingStatements);
        failed += Run("Analyze run internal duplication tokenized python",
            TestAnalyzeRunInternalDuplicationTokenizesPython);
        failed += Run("Analyze run internal duplication tokenized pyi",
            TestAnalyzeRunInternalDuplicationTokenizesPythonStubExtension);
        failed += Run("Analyze run internal duplication ignores python imports",
            TestAnalyzeRunInternalDuplicationIgnoresPythonImports);
        failed += Run("Analyze run internal duplication python triple-quote comment handling",
            TestAnalyzeRunInternalDuplicationPythonTripleQuoteCommentHandling);
        failed += Run("Analyze run include-ext is per-rule",
            TestAnalyzeRunInternalMaintainabilityIncludeExtIsPerRule);
        failed += Run("Analyze run duplication language threshold",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThreshold);
        failed += Run("Analyze run duplication language threshold mts",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesTypeScriptModuleExtension);
        failed += Run("Analyze run duplication language threshold pyi",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesPythonStubExtension);
        failed += Run("Analyze run duplication language-only tag activates rule",
            TestAnalyzeRunInternalDuplicationLanguageSpecificTagOnlyActivatesRule);
        failed += Run("Duplication metrics store modern extension language inference",
            TestDuplicationMetricsStoreInfersLanguageForModernExtensions);
        return failed;
    }
}
#endif
