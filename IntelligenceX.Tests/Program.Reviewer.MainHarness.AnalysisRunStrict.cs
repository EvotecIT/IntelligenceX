namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static int RunAnalyzeRunStrictBehaviorTests() {
        var failed = 0;
        failed += Run("Analyze run disabled writes empty findings", TestAnalyzeRunDisabledWritesEmptyFindings);
        failed += Run("Analyze run non-strict allows runner failure", TestAnalyzeRunNonStrictAllowsRunnerFailure);
        failed += Run("Analyze run strict from config fails runner failure", TestAnalyzeRunStrictFromConfigFailsRunnerFailure);
        failed += Run("Analyze run strict false flag overrides config strict true",
            TestAnalyzeRunStrictFlagFalseOverridesConfigStrictTrue);
        failed += Run("Analyze run strict equals false overrides config strict true",
            TestAnalyzeRunStrictEqualsFalseOverridesConfigStrictTrue);
        failed += Run("Analyze run strict equals true overrides config strict false",
            TestAnalyzeRunStrictEqualsTrueOverridesConfigStrictFalse);
        failed += Run("Analyze run strict flag does not consume following option",
            TestAnalyzeRunStrictFlagDoesNotConsumeFollowingOption);
        failed += Run("Analyze run strict invalid explicit value fails",
            TestAnalyzeRunStrictFlagInvalidExplicitValueFails);
        failed += Run("Analyze run strict unknown option fails as unknown argument",
            TestAnalyzeRunStrictUnknownOptionFailsAsUnknownArgument);
        failed += Run("Analyze run strict keeps known option lookahead with dash-prefixed value",
            TestAnalyzeRunStrictFlagAllowsKnownOptionLookaheadWithDashValue);
        failed += Run("Analyze run strict keeps known option lookahead with framework value",
            TestAnalyzeRunStrictFlagAllowsKnownOptionLookaheadWithFrameworkValue);
        failed += Run("Analyze run pack override skips configured csharp runner failure",
            TestAnalyzeRunPacksOverrideSkipsConfiguredCsharpFailure);
        failed += Run("Analyze run missing dotnet reports unavailable command guidance",
            TestAnalyzeRunMissingDotnetReportsUnavailableCommandGuidance);
        failed += Run("Analyze run missing dotnet with framework reports unavailable command guidance",
            TestAnalyzeRunMissingDotnetWithFrameworkReportsUnavailableCommandGuidance);
        failed += Run("Analyze run missing powershell reports unavailable command guidance",
            TestAnalyzeRunMissingPowerShellReportsUnavailableCommandGuidance);
        failed += Run("Analyze run strict skips csharp runner without csharp sources",
            TestAnalyzeRunStrictSkipsCsharpRunnerWithoutCsharpSources);
        failed += Run("Analyze run invalid pack override fails", TestAnalyzeRunInvalidPackOverrideFails);
        return failed;
    }
}
#endif
