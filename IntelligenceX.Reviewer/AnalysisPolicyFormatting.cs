using IntelligenceX.Analysis;

namespace IntelligenceX.Reviewer;

internal static class AnalysisPolicyFormatting {
    internal const int MaxRulePreviewItems = AnalysisResultsSettings.DefaultPolicyRulePreviewItems;
    internal const int MaxConfigurableRulePreviewItems = AnalysisResultsSettings.MaxPolicyRulePreviewItems;
    internal const int MaxUnavailableReasonTextElements = 120;
    internal const int MaxRulePreviewTitleTextElements = 80;
    internal const string TruncatedPreviewSuffix = " (truncated)";
    internal const string TruncationEllipsis = "...";
    internal const string RulePreviewHiddenValue = "hidden (policyRulePreviewItems=0)";

    internal static int NormalizeRulePreviewItems(int value) {
        if (value < 0) {
            return 0;
        }
        if (value > MaxConfigurableRulePreviewItems) {
            return MaxConfigurableRulePreviewItems;
        }
        return value;
    }
}
