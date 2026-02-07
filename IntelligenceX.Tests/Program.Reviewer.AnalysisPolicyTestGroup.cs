namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static readonly (string Name, Action Test)[] AnalysisPolicyBehaviorTests = {
        ("Analysis policy reports outcomes", TestAnalysisPolicyReportsRuleOutcomes),
        ("Analysis policy unavailable summary", TestAnalysisPolicyBuildUnavailablePolicy),
        ("Analysis policy catalog load unavailable fallback", TestAnalysisPolicyBuildsUnavailableWhenCatalogLoadFails),
        ("Analysis unavailable policy catalog fallback", TestAnalysisPolicyUnavailableUsesCatalogFallbackWhenCatalogLoadFails),
        ("Analysis policy unavailable packs normalized", TestAnalysisPolicyCatalogUnavailableNormalizesPackDisplay),
        ("Analysis policy rethrows unexpected catalog errors", TestAnalysisPolicyDoesNotSwallowUnexpectedCatalogLoadExceptions),
        ("Analysis load failure embeds policy when summary disabled", TestAnalysisLoadFailureEmbedsPolicyWhenSummaryDisabled),
        ("Analysis load failure skips output when disabled", TestAnalysisLoadFailureSkipsOutputWhenPolicyAndSummaryDisabled),
        ("Analysis policy no result files unavailable", TestAnalysisPolicyShowsUnavailableWhenNoResultFiles),
        ("Analysis policy outside-pack findings partial", TestAnalysisPolicyMarksPartialWhenOnlyOutsidePackFindingsExist),
        ("Analysis policy no enabled rules unavailable", TestAnalysisPolicyShowsUnavailableWhenNoEnabledRulesAndNoFindings),
        ("Analysis policy configurable rule preview limit", TestAnalysisPolicyRulePreviewLimitIsConfigurable),
        ("Analysis policy hidden rule preview lines", TestAnalysisPolicyRulePreviewLinesCanBeHidden),
        ("Analysis policy negative preview clamps to hidden", TestAnalysisPolicyRulePreviewLimitNegativeClampsToHidden),
        ("Analysis policy preview clamps to max", TestAnalysisPolicyRulePreviewLimitClampsToMax),
        ("Analysis policy preview truncates and id fallback", TestAnalysisPolicyEnabledRulePreviewTruncatesAndFallsBackToId),
        ("Analysis policy preview supports non-BMP unicode", TestAnalysisPolicyEnabledRulePreviewSupportsNonBmpUnicodeTitles),
        ("Analysis policy enabled rules with outside findings partial", TestAnalysisPolicyMarksPartialWhenOnlyOutsideFindingsAndEnabledRulesExist),
        ("Analysis policy null findings pass", TestAnalysisPolicyHandlesNullFindingsWhenReportExists),
        ("Analysis policy deterministic outcome ordering", TestAnalysisPolicyRuleOutcomePreviewsUseDeterministicOrdering)
    };

    private static readonly (string Name, Action Test)[] AnalysisSummaryBehaviorTests = {
        ("Analysis summary zero findings", TestAnalysisSummaryShowsZeroFindings),
        ("Analysis summary zero findings without report", TestAnalysisSummaryShowsZeroFindingsWithoutLoadReport),
        ("Analysis summary unavailable when no input files", TestAnalysisSummaryShowsUnavailableWhenNoInputFiles)
    };

    private static readonly (string Name, Action Test)[] AnalysisLoadReportBehaviorTests = {
        ("Analysis loader parses zero findings across formats", TestAnalysisLoadReportCountsParsedForZeroFindingsAcrossFormats),
        ("Analysis loader does not double count failed files", TestAnalysisLoadReportDoesNotDoubleCountFailedFiles),
        ("Analysis loader ignores empty files in parsed count", TestAnalysisLoadReportDoesNotCountEmptyFilesAsParsed),
        ("Analysis loader deduplicates resolved files", TestAnalysisLoadReportDeduplicatesResolvedFilesAcrossInputs),
        ("Analysis loader duplicate bad input failure count", TestAnalysisLoadReportCountsSingleFailureForDuplicateBadInput)
    };

    private static int RunAnalysisPolicyReportingTests() {
        var failed = 0;
        failed += RunTestGroup(AnalysisPolicyBehaviorTests);
        failed += RunTestGroup(AnalysisSummaryBehaviorTests);
        failed += RunTestGroup(AnalysisLoadReportBehaviorTests);
        return failed;
    }

    private static int RunTestGroup((string Name, Action Test)[] tests) {
        var failed = 0;
        foreach (var (name, test) in tests) {
            failed += Run(name, test);
        }
        return failed;
    }
}
#endif
