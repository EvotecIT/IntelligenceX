namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static int RunAnalysisPolicyReportingTests() {
        var failed = 0;
        failed += Run("Analysis policy reports outcomes", TestAnalysisPolicyReportsRuleOutcomes);
        failed += Run("Analysis policy unavailable summary", TestAnalysisPolicyBuildUnavailablePolicy);
        failed += Run("Analysis policy catalog load fail fallback", TestAnalysisPolicyBuildsUnavailableWhenCatalogLoadFails);
        failed += Run("Analysis unavailable policy catalog fallback", TestAnalysisPolicyUnavailableUsesCatalogFallbackWhenCatalogLoadFails);
        failed += Run("Analysis policy unavailable packs normalized", TestAnalysisPolicyCatalogUnavailableNormalizesPackDisplay);
        failed += Run("Analysis policy rethrows unexpected catalog errors", TestAnalysisPolicyDoesNotSwallowUnexpectedCatalogLoadExceptions);
        failed += Run("Analysis load failure embeds policy when summary disabled", TestAnalysisLoadFailureEmbedsPolicyWhenSummaryDisabled);
        failed += Run("Analysis load failure skips output when disabled", TestAnalysisLoadFailureSkipsOutputWhenPolicyAndSummaryDisabled);
        failed += Run("Analysis policy no result files unavailable", TestAnalysisPolicyShowsUnavailableWhenNoResultFiles);
        failed += Run("Analysis policy outside-pack findings partial", TestAnalysisPolicyMarksPartialWhenOnlyOutsidePackFindingsExist);
        failed += Run("Analysis policy no enabled rules unavailable", TestAnalysisPolicyShowsUnavailableWhenNoEnabledRulesAndNoFindings);
        failed += Run("Analysis policy preview truncates and id fallback", TestAnalysisPolicyEnabledRulePreviewTruncatesAndFallsBackToId);
        failed += Run("Analysis policy preview supports non-BMP unicode", TestAnalysisPolicyEnabledRulePreviewSupportsNonBmpUnicodeTitles);
        failed += Run("Analysis policy enabled rules with outside findings partial", TestAnalysisPolicyMarksPartialWhenOnlyOutsideFindingsAndEnabledRulesExist);
        failed += Run("Analysis policy null findings pass", TestAnalysisPolicyHandlesNullFindingsWhenReportExists);
        failed += Run("Analysis policy deterministic outcome ordering", TestAnalysisPolicyRuleOutcomePreviewsUseDeterministicOrdering);
        failed += Run("Analysis summary zero findings", TestAnalysisSummaryShowsZeroFindings);
        failed += Run("Analysis summary zero findings without report", TestAnalysisSummaryShowsZeroFindingsWithoutLoadReport);
        failed += Run("Analysis summary unavailable when no input files", TestAnalysisSummaryShowsUnavailableWhenNoInputFiles);
        failed += Run("Analysis loader parses zero findings across formats", TestAnalysisLoadReportCountsParsedForZeroFindingsAcrossFormats);
        failed += Run("Analysis loader does not double count failed files", TestAnalysisLoadReportDoesNotDoubleCountFailedFiles);
        failed += Run("Analysis loader ignores empty files in parsed count", TestAnalysisLoadReportDoesNotCountEmptyFilesAsParsed);
        failed += Run("Analysis loader deduplicates resolved files", TestAnalysisLoadReportDeduplicatesResolvedFilesAcrossInputs);
        failed += Run("Analysis loader duplicate bad input failure count", TestAnalysisLoadReportCountsSingleFailureForDuplicateBadInput);
        return failed;
    }
}
#endif
