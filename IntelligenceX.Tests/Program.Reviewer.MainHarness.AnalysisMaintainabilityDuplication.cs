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
        failed += Run("Analyze run duplication default scope includes shell and yaml",
            TestAnalyzeRunInternalDuplicationDefaultScopeIncludesShellAndYaml);
        failed += Run("Analyze run duplication language threshold sh",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesShellExtension);
        failed += Run("Analyze run duplication language threshold yml",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesYamlExtension);
        failed += Run("Analyze run duplication language threshold bash alias",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesShellAliasAndBashExtension);
        failed += Run("Analyze run duplication language threshold zsh alias",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesShellAliasAndZshExtension);
        failed += Run("Analyze run duplication language threshold yaml alias",
            TestAnalyzeRunInternalDuplicationLanguageSpecificThresholdUsesYamlAliasAndYamlExtension);
        failed += Run("Analyze run duplication ignores shell shebang and comments",
            TestAnalyzeRunInternalDuplicationIgnoresShellShebangAndCommentOnlyLines);
        failed += Run("Analyze run duplication ignores yaml comments",
            TestAnalyzeRunInternalDuplicationIgnoresYamlCommentOnlyLines);
        failed += Run("Analyze run duplication shell hash in parameter expansion",
            TestAnalyzeRunInternalDuplicationShellHashInParameterExpansionDoesNotTriggerCommentStripping);
        failed += Run("Analyze run duplication shell hash in double prefix removal",
            TestAnalyzeRunInternalDuplicationShellHashInDoublePrefixRemovalDoesNotTriggerCommentStripping);
        failed += Run("Analyze run duplication shell hash in arithmetic expression",
            TestAnalyzeRunInternalDuplicationShellHashInArithmeticDoesNotTriggerCommentStripping);
        failed += Run("Analyze run duplication shell word-internal hash",
            TestAnalyzeRunInternalDuplicationShellWordInternalHashDoesNotTriggerCommentStripping);
        failed += Run("Analyze run duplication shell escaped hash",
            TestAnalyzeRunInternalDuplicationShellEscapedHashDoesNotTriggerCommentStripping);
        failed += Run("Analyze run duplication yaml escaped single quote hash",
            TestAnalyzeRunInternalDuplicationYamlEscapedSingleQuoteHashDoesNotTriggerCommentStripping);
        failed += Run("Analyze run duplication language-only tag activates rule",
            TestAnalyzeRunInternalDuplicationLanguageSpecificTagOnlyActivatesRule);
        failed += Run("Duplication metrics store modern extension language inference",
            TestDuplicationMetricsStoreInfersLanguageForModernExtensions);
        return failed;
    }
}
#endif
